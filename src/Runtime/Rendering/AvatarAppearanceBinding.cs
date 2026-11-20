using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WKPmxAvatar
{
    public static class AppearanceOptions
    {
        public static bool Enabled, SceneLighting=true, EyeFaceContext=true, EyeShadowFeather=true, EyeCoherence=true;
        public static float AmbientFloor=0.12f, MinimumVisibility=0.28f, Exposure=1, Brightness=0.8f;
    }
    [DefaultExecutionOrder(10900)]
    public sealed class AvatarAppearanceBinding : MonoBehaviour
    {
        public Renderer Target;
        public Material[] Baseline, Experimental;
        public int[] MappedSlots;
        public FaceFrameBinding Face;
        MaterialPropertyBlock block;
        Color[] probeColors;
        Vector4[] ambientFaces;
        List<ReflectionProbeBlendInfo> reflectionProbes;
        static readonly Vector3[] ProbeDirections={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
        bool applied;
        float nextSample;
        static Light[] lights;
        static float nextLightScan;
        int irisIndex=-1;Mesh contextMesh;bool contextReady;
        int shadowIndex=-1;Mesh shadowMesh;bool shadowReady;
        Material[] referenceMaterials,referenceSource;bool coordinatedWhiteApplied;
        static readonly Vector4 ReferenceDirection=new Vector4(-0.35f,0.7f,-0.6f,0);
        public bool UsingExperimental=>applied;
        public void Refresh(bool force=false)
        {
            if(Target==null || Baseline==null) return;
            bool enabled=AppearanceOptions.Enabled && Experimental!=null && Experimental.Length==Baseline.Length && MappedSlots!=null && MappedSlots.Length==EndfieldResources.ExpectedMappedCount;
            bool coordinatedWhite=AppearanceOptions.SceneLighting&&AppearanceOptions.EyeCoherence;
            if (force || applied != enabled || coordinatedWhiteApplied!=coordinatedWhite)
            {
                if (applied && !enabled) ClearSceneInputs();
                if(enabled&&!coordinatedWhite)
                {
                    if(referenceMaterials==null||referenceSource!=Experimental)
                    {
                        referenceSource=Experimental;referenceMaterials=(Material[])Experimental.Clone();
                        int white=System.Array.FindIndex(Baseline,m=>m.name=="目白");if(white>=0)referenceMaterials[white]=Baseline[white];
                    }
                    Target.sharedMaterials=referenceMaterials;
                }
                else Target.sharedMaterials = enabled ? Experimental : Baseline;
                coordinatedWhiteApplied=coordinatedWhite;
                applied = enabled;
                if (Face != null) { Face.enabled = enabled; if (!enabled) Face.Clear(); }
                if (!enabled) RestoreFallbackInputs();
                nextSample = 0;
            }
            if(!applied || (!force && Time.unscaledTime<nextSample)) return;
            nextSample=Time.unscaledTime+0.1f;
            Vector3 direction=ReferenceDirection; Color direct=new Color(1,0.97f,0.93f,1),ambient=new Color(0.3f,0.33f,0.4f,1);
            if(AppearanceOptions.SceneLighting) SampleScene(out direction,out direct,out ambient);
            else {direction=(Face!=null&&Face.Head!=null?Face.Head.rotation:Target.transform.rotation)*(Vector3)ReferenceDirection;if(Face!=null)Face.Refresh();}
            float exposure=Mathf.Clamp(AppearanceOptions.Exposure,0.1f,4)*Mathf.Clamp(AppearanceOptions.Brightness,0.4f,1.2f);
            if(block==null)block=new MaterialPropertyBlock();
            bool scene = AppearanceOptions.SceneLighting;
            bool link=scene&&AppearanceOptions.EyeFaceContext&&PrepareEyeContext();
            bool feather=scene&&AppearanceOptions.EyeShadowFeather&&PrepareEyeShadow();
            if(link)Face.Refresh();
            Texture reflection = null;
            Vector4 decode = Vector4.zero, reflectionParameters = Vector4.zero;
            if (scene)
            {
                if (ambientFaces == null) ambientFaces = new Vector4[6];
                for (int i = 0; i < ambientFaces.Length; i++) ambientFaces[i] = (Vector4)(probeColors[i] * exposure);
                reflection = SceneReflection(out decode, out reflectionParameters, exposure);
            }
            foreach(int index in MappedSlots)
            {
                Target.GetPropertyBlock(block,index); block.SetVector("_ReferenceLightDirection",direction);
                var material = Experimental[index];
                if(material.HasProperty("_ShadowFeatherEnabled"))block.SetFloat("_ShadowFeatherEnabled",feather&&index==shadowIndex?1:0);
                if(material.HasProperty("_LinearShadowBlendEnabled"))block.SetFloat("_LinearShadowBlendEnabled",scene&&AppearanceOptions.EyeCoherence?1:0);
                if(material.HasProperty("_EyeDiffuseContextBlend"))block.SetFloat("_EyeDiffuseContextBlend",scene&&AppearanceOptions.EyeCoherence&&Baseline[index].name=="目"?1:0);
                if(material.HasProperty("_EyeFaceContextEnabled"))block.SetFloat("_EyeFaceContextEnabled",link&&index==irisIndex?1:0);
                if(material.HasProperty("_EyeReflectionAdaptEnabled"))block.SetFloat("_EyeReflectionAdaptEnabled",scene&&Baseline[index].name=="目"?1:0);
                bool directional = material.HasProperty("_SceneAmbientEnabled");
                if (directional)
                {
                    block.SetVector("_ReferenceLightColor", (Vector4)(direct * exposure));
                    block.SetVector("_ReferenceAmbient", (Vector4)(ambient * exposure));
                    block.SetFloat("_SceneAmbientEnabled", scene ? 1 : 0);
                    if (scene) block.SetVectorArray("_SceneAmbientFaces", ambientFaces);
                    if (material.HasProperty("_MinimumVisibilityColor"))
                        block.SetVector("_MinimumVisibilityColor", scene ? CoolVisibilityFloor() : Vector4.zero);
                }
                else
                {
                    block.SetColor("_ReferenceLightColor",direct*exposure);
                    block.SetColor("_ReferenceAmbient",ambient*exposure);
                }
                if (material.HasProperty("_SceneReflectionParams"))
                    SetReflection(material, reflection, decode, reflectionParameters);
                Target.SetPropertyBlock(block,index);
            }
            // The separated PMX tail still uses its original fallback material.
            // Keep that material visible when scene exposure is reduced too.
            float tailExposure = scene ? Mathf.Max(exposure, Mathf.Clamp01(AppearanceOptions.MinimumVisibility)) : exposure;
            Color eyeWhite = scene ? EyeLayerLight(direction,direct,ambient,exposure,false) : Color.white;
            Color eyeHighlight = scene ? EyeLayerLight(direction,direct,ambient,exposure,true) : Color.white;
            for(int i=0;i<Baseline.Length;i++)
            {
                var material=Baseline[i]; string name=material.name;
                if(name.StartsWith("Tail_"))
                {
                    Target.GetPropertyBlock(block,i);
                    if(material.HasProperty("_EmissionColor"))block.SetColor("_EmissionColor",ScaleRgb(material.GetColor("_EmissionColor"),tailExposure));
                    if(material.HasProperty("_Color"))block.SetColor("_Color",ScaleRgb(material.GetColor("_Color"),tailExposure));
                    Target.SetPropertyBlock(block,i);
                }
                else if(IsEyeLayer(name) && material.HasProperty("_EmissionColor"))
                {
                    // PMX sclera and authored catchlight are separate fallback submeshes.
                    // Keep their textures and coverage; their emission was otherwise constant.
                    Color tint=name=="目HL" ? eyeHighlight : eyeWhite;
                    Color original=material.GetColor("_EmissionColor");
                    Target.GetPropertyBlock(block,i);
                    block.SetColor("_EmissionColor",new Color(original.r*tint.r,original.g*tint.g,original.b*tint.b,original.a));
                    Target.SetPropertyBlock(block,i);
                }
            }
        }
        static bool IsEyeLayer(string name)=>name=="目白" || name=="目HL";
        bool PrepareEyeShadow()
        {
            Mesh mesh=Target is SkinnedMeshRenderer skinned?skinned.sharedMesh:Target.GetComponent<MeshFilter>()?.sharedMesh;
            if(mesh==null)return false;
            if(shadowMesh!=mesh)
            {
                shadowMesh=mesh;shadowIndex=System.Array.FindIndex(Baseline,m=>m.name=="目影");
                shadowReady=global::EyeShadowFeather.EnsurePrepared(mesh,shadowIndex);
            }
            return shadowReady;
        }
        bool PrepareEyeContext()
        {
            if(Face==null||Face.Head==null)return false;
            Mesh mesh=Target is SkinnedMeshRenderer skinned?skinned.sharedMesh:Target.GetComponent<MeshFilter>()?.sharedMesh;
            if(mesh==null)return false;
            if(contextMesh!=mesh)
            {
                contextMesh=mesh;irisIndex=System.Array.FindIndex(Baseline,m=>m.name=="目");
                int sclera=System.Array.FindIndex(Baseline,m=>m.name=="目白");
                Face.AdditionalMaterialIndices=new[]{irisIndex,sclera};
                contextReady=irisIndex>=0&&sclera>=0&&EyeFaceUvTransfer.EnsurePrepared(mesh,irisIndex,sclera);
                if(contextReady)
                {
                    var face=Experimental[Face.MaterialIndex];var iris=Experimental[irisIndex];
                    iris.SetTexture("_SDFLightmap",face.GetTexture("_SDFLightmap"));iris.SetTexture("_SDFMask",face.GetTexture("_SDFMask"));
                }
            }
            return contextReady;
        }
        static float EyeLuminance(Color color)=>color.r*0.2126729f+color.g*0.7151522f+color.b*0.072175f;
        Color EyeLayerLight(Vector3 direction,Color direct,Color ambient,float exposure,bool highlight)
        {
            Vector3 forward=Face!=null && Face.Head!=null ? Face.Head.rotation*Face.LocalForward : Target.transform.rotation*Vector3.back;
            float facing=Mathf.Clamp01(Vector3.Dot(forward.normalized,direction.normalized));
            float gain=highlight ? facing*facing*facing*facing*0.7f : Mathf.Lerp(0.35f,0.7f,facing);
            Color light=(ambient*(highlight?0:0.6f)+direct*gain)*exposure;
            float luminance=EyeLuminance(light);
            light=Color.Lerp(new Color(luminance,luminance,luminance,1),light,0.35f);
            float peak=Mathf.Max(light.r,Mathf.Max(light.g,light.b));
            if(peak>0.6f){float excess=peak-0.6f;light*=(0.6f+0.4f*excess/(excess+0.4f))/peak;}
            // Catchlights fade with available illumination; sclera keeps a modest floor.
            float strength=Mathf.Clamp01(AppearanceOptions.MinimumVisibility)*0.9f*(highlight?0.03f:0.55f);
            Color minimum=new Color(0.64f*strength,0.78f*strength,strength,1);
            float missing=Mathf.Clamp01(1-EyeLuminance(light)/Mathf.Max(1e-6f,EyeLuminance(minimum)));
            light+=minimum*missing;
            return QualitySettings.activeColorSpace==ColorSpace.Gamma ? light.gamma : light;
        }
        static Color ScaleRgb(Color color,float amount)=>new Color(color.r*amount,color.g*amount,color.b*amount,color.a);
        static Vector4 CoolVisibilityFloor()
        {
            float strength = Mathf.Clamp01(AppearanceOptions.MinimumVisibility);
            // Linear cool fill is independent of scene exposure.
            return new Vector4(0.64f * strength, 0.78f * strength, strength, 0);
        }
        void SampleScene(out Vector3 direction,out Color direct,out Color ambient)
        {
            Vector3 position=Face!=null && Face.Head!=null?Face.Head.position:Target.bounds.center;
            if (probeColors == null) probeColors = new Color[6];
            if(LightmapSettings.lightProbes!=null && LightmapSettings.lightProbes.count>0)
            {
                LightProbes.GetInterpolatedProbe(position,Target,out var probe);
                probe.Evaluate(ProbeDirections,probeColors);
            }
            else if (RenderSettings.ambientMode == AmbientMode.Skybox)
            {
                var sceneProbe = RenderSettings.ambientProbe;
                sceneProbe.Evaluate(ProbeDirections, probeColors);
            }
            else
            {
                bool flat = RenderSettings.ambientMode == AmbientMode.Flat;
                for (int i = 0; i < probeColors.Length; i++)
                {
                    var color = flat ? RenderSettings.ambientLight : i == 2 ? RenderSettings.ambientSkyColor :
                        i == 3 ? RenderSettings.ambientGroundColor : RenderSettings.ambientEquatorColor;
                    probeColors[i] = color.linear * RenderSettings.ambientIntensity;
                }
            }
            float floor=Mathf.Clamp01(AppearanceOptions.AmbientFloor);
            ambient=Color.black;
            for (int i = 0; i < probeColors.Length; i++)
            {
                var color = probeColors[i];
                float r = Mathf.Max(0, color.r), g = Mathf.Max(0, color.g), b = Mathf.Max(0, color.b);
                float lift = Mathf.Max(0, floor - Mathf.Max(r, Mathf.Max(g, b)));
                probeColors[i] = new Color(r + lift, g + lift, b + lift, 1);
                ambient += probeColors[i] / probeColors.Length;
            }
            if(lights==null || Time.unscaledTime>=nextLightScan)
            {lights=Object.FindObjectsByType<Light>(FindObjectsSortMode.None);nextLightScan=Time.unscaledTime+1;}
            direction=ReferenceDirection;direct=Color.black;float best=0;
            foreach(var light in lights)
            {
                if(light==null || !light.isActiveAndEnabled || (light.cullingMask&(1<<Target.gameObject.layer))==0 || light.intensity<=0 || (light.bakingOutput.isBaked && light.bakingOutput.lightmapBakeType==LightmapBakeType.Baked)) continue;
                Vector3 toward;float attenuation=1;
                if(light.type==LightType.Directional) toward=-light.transform.forward;
                else if(light.type==LightType.Point || light.type==LightType.Spot)
                {
                    toward=light.transform.position-position;float distance=toward.magnitude;
                    if(distance>=light.range || light.range<=0)continue;
                    attenuation=Mathf.Pow(1-distance*distance/(light.range*light.range),2);
                    if(light.type==LightType.Spot)
                    {
                        float outer=Mathf.Cos(light.spotAngle*0.5f*Mathf.Deg2Rad),inner=Mathf.Cos(light.innerSpotAngle*0.5f*Mathf.Deg2Rad);
                        attenuation*=Mathf.Clamp01((Vector3.Dot(light.transform.forward,-toward.normalized)-outer)/Mathf.Max(0.001f,inner-outer));
                    }
                }
                else continue;
                Color color=light.color.linear*Mathf.Min(16,light.intensity)*attenuation;
                float strength=Mathf.Max(color.r,Mathf.Max(color.g,color.b));
                if(strength>best) {best=strength;direction=toward.sqrMagnitude>1e-8f?toward.normalized:(Vector3)ReferenceDirection;direct=color;}
            }
        }
        Texture SceneReflection(out Vector4 decode, out Vector4 parameters, float exposure)
        {
            decode = Vector4.zero;
            parameters = Vector4.zero;
            if (Target.reflectionProbeUsage == ReflectionProbeUsage.Off) return null;
            if (reflectionProbes == null) reflectionProbes = new List<ReflectionProbeBlendInfo>(4);
            reflectionProbes.Clear();
            Target.GetClosestReflectionProbes(reflectionProbes);
            ReflectionProbe bestProbe = null;
            float weight = 0;
            foreach (var entry in reflectionProbes)
            {
                if (entry.weight <= weight || entry.probe == null || !entry.probe.isActiveAndEnabled ||
                    !IsCube(entry.probe.texture)) continue;
                weight = entry.weight;
                bestProbe = entry.probe;
            }
            Texture texture;
            float gain;
            if (bestProbe != null)
            {
                texture = bestProbe.texture;
                decode = bestProbe.textureHDRDecodeValues;
                gain = bestProbe.intensity;
            }
            else
            {
                if (RenderSettings.customReflectionTexture == null && RenderSettings.skybox == null) return null;
                texture = ReflectionProbe.defaultTexture;
                decode = ReflectionProbe.defaultTextureHDRDecodeValues;
                weight = 1;
                gain = RenderSettings.reflectionIntensity;
            }
            if (!IsCube(texture) || decode.x <= 0) return null;
            parameters = new Vector4(Mathf.Clamp01(weight) * 0.7f, Mathf.Max(0, gain) * exposure,
                Mathf.Max(0, texture.mipmapCount - 1), texture.isDataSRGB ? 1 : 0);
            return texture;
        }
        static bool IsCube(Texture texture) => texture != null && texture.dimension == TextureDimension.Cube;
        void SetReflection(Material material, Texture texture, Vector4 decode, Vector4 parameters)
        {
            var cube = texture != null ? texture : material.GetTexture("_EnvironmentCube");
            if (cube != null) block.SetTexture("_SceneReflectionCube", cube);
            block.SetVector("_SceneReflectionDecode", decode);
            block.SetVector("_SceneReflectionParams", parameters);
        }
        void OnEnable(){Refresh(true);}
        void LateUpdate(){Refresh();}
        void RestoreFallbackInputs()
        {
            if (block == null) return;
            for (int i = 0; i < Baseline.Length; i++)
            {
                var material = Baseline[i];
                if (!material.name.StartsWith("Tail_") && !IsEyeLayer(material.name)) continue;
                Target.GetPropertyBlock(block, i);
                if (material.HasProperty("_EmissionColor")) block.SetColor("_EmissionColor", material.GetColor("_EmissionColor"));
                if (material.HasProperty("_Color")) block.SetColor("_Color", material.GetColor("_Color"));
                Target.SetPropertyBlock(block, i);
            }
        }
        void ClearSceneInputs()
        {
            if (block == null) return;
            foreach (int index in MappedSlots)
            {
                var material = Experimental[index];
                Target.GetPropertyBlock(block, index);
                if (material.HasProperty("_SceneAmbientEnabled")) block.SetFloat("_SceneAmbientEnabled", 0);
                if (material.HasProperty("_EyeFaceContextEnabled")) block.SetFloat("_EyeFaceContextEnabled", 0);
                if (material.HasProperty("_EyeReflectionAdaptEnabled")) block.SetFloat("_EyeReflectionAdaptEnabled", 0);
                if (material.HasProperty("_ShadowFeatherEnabled")) block.SetFloat("_ShadowFeatherEnabled", 0);
                if (material.HasProperty("_LinearShadowBlendEnabled")) block.SetFloat("_LinearShadowBlendEnabled", 0);
                if (material.HasProperty("_EyeDiffuseContextBlend")) block.SetFloat("_EyeDiffuseContextBlend", 0);
                if (material.HasProperty("_MinimumVisibilityColor")) block.SetVector("_MinimumVisibilityColor", Vector4.zero);
                if (material.HasProperty("_SceneReflectionParams")) SetReflection(material, null, Vector4.zero, Vector4.zero);
                Target.SetPropertyBlock(block, index);
            }
            reflectionProbes?.Clear();
        }
        void OnDisable()
        {
            if (Face != null) { Face.enabled = false; Face.Clear(); }
            if (Target != null && Baseline != null)
            {
                if (applied) ClearSceneInputs();
                RestoreFallbackInputs();
                Target.sharedMaterials = Baseline;
            }
            applied = false;
        }
    }
}
