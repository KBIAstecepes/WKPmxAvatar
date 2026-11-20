using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using Newtonsoft.Json;

namespace WKPmxAvatar
{
    // Shared read-only resources; materials remain owned by the individual avatar asset.
    public sealed class EndfieldResources : IDisposable
    {
        public const int ExpectedMappedCount=12;
        [Serializable] public class Profile { public MaterialInfo[] materials; public TextureInfo[] textures; }
        [Serializable] public class TextureInfo { public string name,file; public int width,height,unityFormat,unityColorSpace,mips; public TextureSettings settings; }
        [Serializable] public class TextureSettings { public int m_FilterMode,m_WrapU,m_WrapV,m_Aniso; public float m_MipBias; }
        [Serializable] public class Slot { public string slot,texture; public Vector2 scale,offset; }
        [Serializable] public class Floats
        {
            public float _BumpScale,_Metallic,_Specular,_Smoothness,_EmissionBrightness,_SpecRampIridescentMode;
            public float _SpecBumpScale,_UseShadowLutTex,_ShadowColorBrightness,_ShadowColorSaturation;
            public float _AnisotropyValue,_AnisotropyValue2,_AnisotropyDirX,_AnisotropyIntensity,_AnisotropyEdgeFade,_AnisotropyRange2;
            public float _SpecularLine,_LineValue,_LineRange,_LineIntensity,_LineSaturation,_MatcapNormalScale,_UseMatcap;
            public float _FaceHighlightMap,_FaceRimOffScale,_SkinRimOffScale;
        }
        [Serializable] public class Colors { public Color _BaseColor,_EmissionColor,_AnisotropyColor2,_MatcapColor,_EyeScatteringColor,_SDFRimColor,_HighlightMapVector; }
        [Serializable] public class MaterialInfo { public string name; public string[] keywords; public Slot[] textures; public Floats floats; public Colors colors; }
        readonly string folder;
        readonly List<Object> owned=new List<Object>();
        readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
        Profile profile; AssetBundle bundle; Shader cloth,surface,eyeShadow; Cubemap environment;
        bool failed;
        public string Status { get; private set; }="Not loaded";
        public EndfieldResources(string root) { folder=Path.GetFullPath(root)+Path.DirectorySeparatorChar; }
        string Inside(string relative)
        {
            if(string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Resource path must be relative");
            string full=Path.GetFullPath(Path.Combine(folder,relative));
            if(!full.StartsWith(folder,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Resource path escapes rendering folder");
            return full;
        }
        public bool EnsureLoaded()
        {
            if(bundle!=null) return true;
            if(failed) return false;
            try
            {
                if(GraphicsSettings.currentRenderPipeline!=null) throw new NotSupportedException("Experimental shaders require Built-in rendering");
                string file=Inside("rossi-original-materials.json");
                if(new FileInfo(file).Length>4*1024*1024) throw new InvalidDataException("Material profile too large");
                // BepInEx-loaded DTOs are outside the game's build-time Unity serialization metadata.
                // Use the game's existing managed JSON parser, not Unity's native JsonUtility.
                profile=JsonConvert.DeserializeObject<Profile>(File.ReadAllText(file),new JsonSerializerSettings {
                    TypeNameHandling=TypeNameHandling.None,MetadataPropertyHandling=MetadataPropertyHandling.Ignore,MaxDepth=64 });
                if(profile?.materials==null || profile.textures==null || profile.materials.Length<1 || profile.materials.Length>64 || profile.textures.Length<1 || profile.textures.Length>128)
                    throw new InvalidDataException("Invalid material profile (materials="+(profile?.materials?.Length.ToString()??"null")+", textures="+(profile?.textures?.Length.ToString()??"null")+")");
                string bundleFile=Inside("rossi-cloth-prototype.bundle");
                if(new FileInfo(bundleFile).Length>64*1024*1024) throw new InvalidDataException("Shader bundle too large");
                bundle=AssetBundle.LoadFromFile(bundleFile);
                if(bundle==null) throw new InvalidDataException("Shader bundle could not load");
                cloth=bundle.LoadAsset<Shader>("Assets/Shaders/RossiCloth.shader");
                surface=bundle.LoadAsset<Shader>("Assets/Shaders/RossiSurface.shader");
                eyeShadow=bundle.LoadAsset<Shader>("Assets/Shaders/RossiEyeShadow.shader");
                if(eyeShadow==null||!eyeShadow.isSupported)throw new NotSupportedException("Eye shadow shader unsupported");
                if(cloth==null || surface==null || !cloth.isSupported || !surface.isSupported) throw new NotSupportedException("Experimental shader unsupported");
                var keepers=bundle.LoadAllAssets<Material>(); var combinations=new HashSet<string>();
                foreach(var keeper in keepers) { var keys=keeper.shaderKeywords; Array.Sort(keys); combinations.Add(string.Join(" ",keys)); }
                if(keepers.Length!=32 || combinations.Count!=32) throw new InvalidDataException("Incomplete cloth shader variants");
                environment=StudioEnvironment(); owned.Add(environment);
                Status="Ready (experimental)"; return true;
            }
            catch(Exception ex) { Dispose(); failed=true; Status="Base textures: "+ex.Message; return false; }
        }
        Texture2D Texture(string name)
        {
            if(textures.TryGetValue(name,out var cached)) return cached;
            var info=Array.Find(profile.textures,t=>t.name==name);
            if(info==null || info.settings==null || info.width<1 || info.height<1 || info.width>8192 || info.height>8192 || info.mips<1 || info.mips>15)
                throw new InvalidDataException("Invalid texture metadata: "+name);
            if(info.unityFormat!=4 && info.unityFormat!=25 && info.unityFormat!=27) throw new NotSupportedException("Texture format: "+info.unityFormat);
            if(!SystemInfo.SupportsTextureFormat((TextureFormat)info.unityFormat)) throw new NotSupportedException("GPU texture format: "+info.unityFormat);
            string path=Inside(info.file);
            if(new FileInfo(path).Length>128*1024*1024) throw new InvalidDataException("Texture file too large");
            byte[] bytes=File.ReadAllBytes(path);
            var texture=new Texture2D(info.width,info.height,(TextureFormat)info.unityFormat,info.mips,info.unityColorSpace==0);
            owned.Add(texture);
            if(info.file.EndsWith(".dds",StringComparison.OrdinalIgnoreCase))
            {
                if(bytes.Length<148 || BitConverter.ToUInt32(bytes,0)!=0x20534444 || BitConverter.ToInt32(bytes,12)!=info.height || BitConverter.ToInt32(bytes,16)!=info.width || BitConverter.ToInt32(bytes,28)!=info.mips || BitConverter.ToUInt32(bytes,84)!=0x30315844)
                    throw new InvalidDataException("DDS header mismatch: "+name);
                int dxgi=BitConverter.ToInt32(bytes,128);
                if(dxgi!=(info.unityFormat==25?98:83)) throw new InvalidDataException("DDS format mismatch: "+name);
                long expected=0; int w=info.width,h=info.height;
                for(int mip=0;mip<info.mips;mip++) {expected+=(long)Math.Max(1,(w+3)/4)*Math.Max(1,(h+3)/4)*16;w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
                if(bytes.Length!=148+expected) throw new InvalidDataException("DDS mip payload mismatch: "+name);
                var payload=new byte[bytes.Length-148]; Array.Copy(bytes,148,payload,0,payload.Length); texture.LoadRawTextureData(payload); texture.Apply(false,true);
            }
            else if(!info.file.EndsWith(".png",StringComparison.OrdinalIgnoreCase) || !ImageConversion.LoadImage(texture,bytes,true) || texture.width!=info.width || texture.height!=info.height)
                throw new InvalidDataException("PNG metadata mismatch: "+name);
            texture.name=name; texture.filterMode=(FilterMode)info.settings.m_FilterMode;
            texture.wrapModeU=(TextureWrapMode)info.settings.m_WrapU; texture.wrapModeV=(TextureWrapMode)info.settings.m_WrapV;
            texture.anisoLevel=info.settings.m_Aniso; texture.mipMapBias=info.settings.m_MipBias;
            textures.Add(name,texture); return texture;
        }
        public bool TryCreateMaterials(Material[] baseline,Texture2D[] pmxMaps,out Material[] result,out int[] mapped,out string error)
        {
            result=baseline; mapped=Array.Empty<int>(); error=null;
            if(!EnsureLoaded()) {error=Status;return false;}
            var created=new List<Material>(); var slots=new List<int>();
            try
            {
                var next=(Material[])baseline.Clone();
                for(int i=0;i<next.Length;i++)
                {
                    string name=baseline[i].name;
                    if(name=="目影")
                    {
                        if(!baseline[i].HasProperty("_Color")||!baseline[i].HasProperty("_EmissionColor")||baseline[i].GetColor("_EmissionColor").maxColorComponent>0)throw new InvalidDataException("Eye overlay is not the expected black PMX mask");
                        var overlay=new Material(eyeShadow){name=name+"_PmxFeather",renderQueue=baseline[i].renderQueue};created.Add(overlay);
                        if(!overlay.HasProperty("_LinearShadowBlendEnabled"))throw new InvalidDataException("Eye transmission requires matching shader bundle");
                        overlay.SetFloat("_Opacity",baseline[i].GetColor("_Color").a);
                        next[i]=overlay;slots.Add(i);continue;
                    }
                    string suffix=name=="Cloth1"?"cloth_01":name=="Cloth2"?"cloth_02":name=="Cloth3"?"cloth_03":name=="面"||name=="口内"||name=="目白"?"face_01":name=="睫眉"?"eyebrow_01":name=="肌"?"body_01":name=="发1"?"hair_01":name=="发Alpha"?"hair_02":name=="目"?"iris_01":null;
                    if(suffix==null) continue;
                    var info=Array.Find(profile.materials,m=>m.name=="M_actor_wulfa_"+suffix);
                    if(info?.floats==null || info.colors==null || info.textures==null || info.keywords==null) throw new InvalidDataException("Missing material: "+suffix);
                    bool isCloth=name.StartsWith("Cloth",StringComparison.Ordinal);
                    var material=new Material(isCloth?cloth:surface) {name=name+"_Endfield"}; created.Add(material);
                    foreach(var slot in info.textures) if(material.HasProperty(slot.slot)) {material.SetTexture(slot.slot,Texture(slot.texture));material.SetTextureScale(slot.slot,slot.scale);material.SetTextureOffset(slot.slot,slot.offset);}
                    foreach(var field in typeof(Floats).GetFields()) if(material.HasProperty(field.Name)) material.SetFloat(field.Name,(float)field.GetValue(info.floats));
                    foreach(var field in typeof(Colors).GetFields()) if(material.HasProperty(field.Name)) material.SetColor(field.Name,(Color)field.GetValue(info.colors));
                    if(isCloth)
                    {
                        string[] from={"_NORMALMAP","_METALLICSPECGLOSSMAP","_DIFF_RAMP_ON","_SPEC_RAMP_ON","_EMISSION"};
                        string[] to={"WK_NORMAL","WK_PBR_MAP","WK_DIFF_RAMP","WK_SPEC_RAMP","WK_EMISSION"};
                        for(int j=0;j<from.Length;j++) if(Array.IndexOf(info.keywords,from[j])>=0) material.EnableKeyword(to[j]);
                        material.SetTexture("_EnvironmentCube",environment);
                    }
                    else
                    {
                        material.SetFloat("_Region",name=="面"||name=="目白"?1:name.StartsWith("发",StringComparison.Ordinal)?2:name=="目"?3:name=="睫眉"?4:name=="口内"?5:0);
                        material.SetFloat("_NormalEnabled",Array.IndexOf(info.keywords,"_NORMALMAP")>=0?1:0);
                        material.SetFloat("_SplitNormals",Array.IndexOf(info.keywords,"_SPECULAR_NORMALMAP")>=0?1:0);
                        material.SetFloat("_AlphaClipThreshold",name=="发Alpha"?0.15f:0);
                        if(name=="肌" || name=="目" || name=="睫眉" || name=="口内" || name=="目白") material.SetTexture("_BaseMap",pmxMaps[i]);
                        if(name=="目白")material.SetFloat("_Specular",0);
                        if(name=="目")
                        {
                            if(!material.HasProperty("_EyeReflectionAdaptEnabled"))throw new InvalidDataException("Iris reflection calibration requires the matching shader bundle");
                            // Original MatcapColor is retained. This is a separate PMX gain,
                            // selected by controlled RGB/alpha-tint decomposition, not a native value.
                            material.SetFloat("_EyeReflectionRgbWeight",1);
                            material.SetFloat("_EyeReflectionTintWeight",.25f);
                            material.SetFloat("_EyeDiffuseContextBlend",1);
                        }
                        // Mouth is a PMX-specific interior mesh, not an exterior face SDF.
                        // Neither accessory layer may inherit iris curvature or reflection.
                        if(name=="睫眉" || name=="口内")
                        {
                            material.SetFloat("_FaceSdfEnabled",0);
                            material.SetFloat("_UseMatcap",0);
                            material.SetFloat("_EyeScatteringEnabled",0);
                            material.SetFloat("_EyeFaceContextEnabled",0);
                            material.SetFloat("_Specular",0);
                        }
                    }
                    next[i]=material; slots.Add(i);
                }
                if(slots.Count!=ExpectedMappedCount) throw new InvalidDataException("Rossi requires twelve mapped submeshes, including coordinated sclera");
                result=next; mapped=slots.ToArray(); Status="Experimental materials ready: "+slots.Count+" submeshes";return true;
            }
            catch(Exception ex) {foreach(var material in created) Destroy(material);error=ex.Message;Status="Base textures: "+error;return false;}
        }
        public void Retry() {if(bundle==null) {failed=false;Status="Not loaded";}}
        public void Dispose()
        {
            foreach(var obj in owned) Destroy(obj); owned.Clear(); textures.Clear();
            if(bundle!=null) bundle.Unload(true); bundle=null; cloth=null;surface=null;eyeShadow=null;profile=null;environment=null;
        }
        static void Destroy(Object obj) {if(obj==null)return;if(Application.isPlaying)Object.Destroy(obj);else Object.DestroyImmediate(obj);}
        static Cubemap StudioEnvironment()
        {
            const int size=64; var cube=new Cubemap(size,TextureFormat.RGBAHalf,true);cube.name="Rossi fallback studio reflection";
            for(int face=0;face<6;face++)
            {
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                {
                    float u=2*(x+0.5f)/size-1,v=2*(y+0.5f)/size-1;
                    Vector3 d=face==0?new Vector3(1,-v,-u):face==1?new Vector3(-1,-v,u):face==2?new Vector3(u,1,v):face==3?new Vector3(u,-1,-v):face==4?new Vector3(u,-v,1):new Vector3(-u,-v,-1);d.Normalize();
                    Color c=Color.Lerp(new Color(0.32f,0.30f,0.28f,1),new Color(0.85f,0.95f,1.15f,1),Mathf.Clamp01(d.y*0.5f+0.5f));
                    pixels[y*size+x]=c+new Color(2.5f,2.4f,2.2f,0)*Mathf.Pow(Mathf.Max(0,Vector3.Dot(d,new Vector3(-0.5f,0.6f,-0.6f).normalized)),24);
                }
                cube.SetPixels(pixels,(CubemapFace)face);
            }
            cube.Apply(true,true);return cube;
        }
    }
}
