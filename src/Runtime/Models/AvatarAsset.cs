using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WKPmxAvatar
{
    internal sealed class AvatarAsset : IDisposable
    {
        readonly List<Object> owned = new List<Object>();
        internal GameObject Template;
        internal readonly List<string> Warnings = new List<string>();
        Material[] avatarMaterials;
        Texture2D[] maps;
        Color[] tints;
        bool[] textureAlpha;
        internal static Vector3 Vec(PmxData.V3 v) => new Vector3(v.X, v.Y, v.Z);
        internal static AvatarAsset Load(string path, float height)
        {
            var asset = new AvatarAsset();
            try { asset.Build(path, height); return asset; }
            catch { asset.Dispose(); throw; }
        }
        void Build(string path, float height)
        {
            PmxData d;
            using (var s = File.OpenRead(path)) d = PmxReader.Read(s);
            Template = new GameObject("PMX_" + d.Name);
            Template.SetActive(false);
            Object.DontDestroyOnLoad(Template);
            var bones = new Transform[d.Bones.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in d.Vertices) { minY = Mathf.Min(minY, v.Position.Y); maxY = Mathf.Max(maxY, v.Position.Y); }
            if (maxY - minY < 0.001f) throw new InvalidDataException("PMX has no height");
            float scale = height / (maxY - minY);
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject(d.Bones[i].Name).transform;
                bones[i].SetParent(Template.transform, false);
                bones[i].localPosition = (Vec(d.Bones[i].Position) - Vector3.up * minY) * scale;
            }
            for (int i = 0; i < bones.Length; i++)
                if (d.Bones[i].Parent >= 0) bones[i].SetParent(bones[d.Bones[i].Parent], true);
            bool doubleSided = Array.Exists(d.Materials, m => (m.Flags & 1) != 0 && m.A > 0.001f);
            int vertexCount = d.Vertices.Length;
            var positions = new Vector3[vertexCount * (doubleSided ? 2 : 1)];
            var normals = new Vector3[positions.Length];
            var uv = new Vector2[positions.Length];
            var weights = new BoneWeight[positions.Length];
            for (int i = 0; i < vertexCount; i++)
            {
                var v = d.Vertices[i];
                positions[i] = (Vec(v.Position) - Vector3.up * minY) * scale;
                normals[i] = Vec(v.Normal); uv[i] = new Vector2(v.U, 1 - v.V);
                weights[i] = new BoneWeight {
                    boneIndex0 = v.Bones[0], boneIndex1 = v.Bones[1], boneIndex2 = v.Bones[2], boneIndex3 = v.Bones[3],
                    weight0 = v.Weights[0], weight1 = v.Weights[1], weight2 = v.Weights[2], weight3 = v.Weights[3] };
                if (doubleSided)
                {
                    int back = i + vertexCount;
                    positions[back] = positions[i]; normals[back] = -normals[i];
                    uv[back] = uv[i]; weights[back] = weights[i];
                }
            }
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bind.Length; i++) bind[i] = bones[i].worldToLocalMatrix * Template.transform.localToWorldMatrix;
            var mesh = new Mesh { name = d.Name, indexFormat = IndexFormat.UInt32 };
            owned.Add(mesh);
            mesh.vertices = positions; mesh.normals = normals; mesh.uv = uv;
            mesh.boneWeights = weights; mesh.bindposes = bind; mesh.subMeshCount = d.Materials.Length;
            int offset = 0;
            for (int i = 0; i < d.Materials.Length; i++)
            {
                int n = d.Materials[i].IndexCount;
                var indices = new int[n]; Array.Copy(d.Indices, offset, indices, 0, n);
                // PMX and Unity both use left-handed coordinates; Unity uses clockwise faces.
                mesh.SetTriangles(d.Materials[i].A <= 0.001f ? Array.Empty<int>() :
                    MaterialPolicy.Faces(indices, (d.Materials[i].Flags & 1) != 0, vertexCount), i, false); offset += n;
            }
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Shader shader = SelectShader(Plugin.Instance.UnlitTextures.Value);
            var textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            var alphaCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var materials = new Material[d.Materials.Length];
            avatarMaterials = materials; maps = new Texture2D[materials.Length]; tints = new Color[materials.Length]; textureAlpha = new bool[materials.Length];
            string folder = Path.GetFullPath(Path.GetDirectoryName(path)) + Path.DirectorySeparatorChar;
            for (int i = 0; i < materials.Length; i++)
            {
                var m = d.Materials[i];
                var mat = new Material(shader) { name = m.Name };
                owned.Add(mat); materials[i] = mat;
                tints[i] = new Color(m.R, m.G, m.B, m.A);
                if (m.Texture < 0) continue;
                string texturePath = Path.GetFullPath(Path.Combine(folder, d.Textures[m.Texture].Replace('\\', Path.DirectorySeparatorChar)));
                if (!texturePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Texture path escapes model folder: " + d.Textures[m.Texture]);
                if (!textureCache.TryGetValue(texturePath, out var texture))
                {
                    string ext = Path.GetExtension(texturePath).ToLowerInvariant();
                    if (!File.Exists(texturePath) || (ext != ".png" && ext != ".jpg" && ext != ".jpeg"))
                    { Warnings.Add("Missing/unsupported texture: " + texturePath); continue; }
                    if (new FileInfo(texturePath).Length > 128 * 1024 * 1024) throw new InvalidDataException("Texture file too large");
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    owned.Add(texture);
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(texturePath), false))
                        throw new InvalidDataException("Cannot decode texture: " + texturePath);
                    texture.name = Path.GetFileName(texturePath); textureCache[texturePath] = texture;
                    bool hasAlpha = false;
                    foreach (var pixel in texture.GetPixels32())
                        if (MaterialPolicy.HasCoverageAlpha(pixel.a)) { hasAlpha = true; break; }
                    alphaCache[texturePath] = hasAlpha;
                    texture.Apply(true, true);
                }
                maps[i] = texture;
                textureAlpha[i] = alphaCache[texturePath];
                Plugin.Instance.InfoLog("PMX material " + m.Name + " texture=" + texture.name + " tint=" + tints[i]);
            }
            int tailTriangles=TailMaterialSplit.Apply(d,mesh,ref materials,ref maps,ref tints,ref textureAlpha,owned);avatarMaterials=materials;
            if(tailTriangles>0)Plugin.Instance.InfoLog("Tail PMX fallback separated: "+tailTriangles+" triangles");
            ApplyRendering(Plugin.Instance.UnlitTextures.Value);
            var renderer = Template.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh; renderer.bones = bones; renderer.sharedMaterials = materials;
            renderer.rootBone = Template.transform; renderer.quality = SkinQuality.Bone4;
            renderer.localBounds = new Bounds(Vector3.up * height * 0.5f, Vector3.one * height * 3);
            bool rossi=Array.Exists(d.Textures,t=>t.IndexOf("T_actor_wulfa_cloth_01_D",StringComparison.OrdinalIgnoreCase)>=0)
                && Array.Exists(d.Textures,t=>t.IndexOf("T_actor_wulfa_body_01_D",StringComparison.OrdinalIgnoreCase)>=0);
            if(rossi)
            {
                var resources=Plugin.Instance.RenderingResources;
                if(resources.TryCreateMaterials(materials,maps,out var experimental,out var mapped,out var error))
                {
                    Plugin.Instance.InfoLog("Experimental Rossi materials prepared: "+mapped.Length+" submeshes; graphics="+SystemInfo.graphicsDeviceType);
                    foreach(int index in mapped)owned.Add(experimental[index]);
                    var binding=Template.AddComponent<AvatarAppearanceBinding>();binding.Target=renderer;binding.Baseline=materials;binding.Experimental=experimental;binding.MappedSlots=mapped;
                    int faceIndex=Array.FindIndex(d.Materials,m=>m.Name=="面");int headIndex=Array.FindIndex(d.Bones,b=>b.Name=="頭");
                    if(faceIndex>=0 && headIndex>=0)
                    {
                        var frame=Template.AddComponent<FaceFrameBinding>();frame.Target=renderer;frame.Head=bones[headIndex];frame.MaterialIndex=faceIndex;frame.enabled=false;binding.Face=frame;
                    }
                }
                else Warnings.Add("Experimental materials unavailable; base textures retained: "+error);
            }
            var rig = Template.AddComponent<AvatarMotion>();
            rig.Bones = bones;
            rig.GrantSources = new int[bones.Length]; rig.GrantWeights = new float[bones.Length]; rig.GrantFlags = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            { rig.GrantSources[i] = d.Bones[i].InheritParent; rig.GrantWeights[i] = d.Bones[i].InheritWeight; rig.GrantFlags[i] = d.Bones[i].Flags; }
            // PMX local axes are identity at rest. Bone names allow common MMD rigs without Humanoid conversion.
            if (d.ApproximateWeights > 0) Warnings.Add(d.ApproximateWeights + " SDEF/QDEF vertices approximated using linear skinning");
        }
        static Shader SelectShader(bool unlit)
        {
            // Sprite/UI shaders do not write depth and cannot render a layered 3D avatar.
            bool srp = GraphicsSettings.currentRenderPipeline != null;
            string[] candidates = srp
                ? (unlit ? new[] { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Lit" }
                         : new[] { "Universal Render Pipeline/Lit" })
                : new[] { "Standard" };
            foreach (string name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null && shader.isSupported) return shader;
            }
            throw new InvalidOperationException("No supported depth-writing 3D avatar shader found for this render pipeline");
        }
        static void SetFloat(Material mat, string property, float value)
        {
            if (mat.HasProperty(property)) mat.SetFloat(property, value);
        }
        internal void ApplyRendering(bool unlit)
        {
            var shader = SelectShader(unlit);
            Plugin.Instance.InfoLog("PMX rendering=" + (unlit ? "Texture" : "Lit") + " shader=" + shader.name + " pipeline=" + (GraphicsSettings.currentRenderPipeline == null ? "Built-in" : GraphicsSettings.currentRenderPipeline.GetType().Name));
            for (int i = 0; i < avatarMaterials.Length; i++)
            {
                var surface = MaterialPolicy.Classify(tints[i].a, textureAlpha[i]);
                bool transparent = surface == MaterialSurface.Transparent;
                bool cutout = surface == MaterialSurface.Cutout;
                var mat = avatarMaterials[i]; mat.shader = shader;
                mat.shaderKeywords = Array.Empty<string>();
                bool emissionOnly = unlit && (shader.name == "Standard" || shader.name.EndsWith("/Lit"));
                // Emission supplies the texture color; black diffuse prevents lighting it twice.
                Color tint = tints[i];
                Color baseColor = emissionOnly ? new Color(0, 0, 0, tint.a) : tint;
                foreach (string p in new[] { "_Color", "_BaseColor" }) if (mat.HasProperty(p)) mat.SetColor(p, baseColor);
                foreach (string p in new[] { "_MainTex", "_BaseMap" }) if (mat.HasProperty(p)) mat.SetTexture(p, maps[i]);
                // Double-sided PMX faces have explicit reversed copies with inverted normals.
                SetFloat(mat, "_Cull", 2);
                SetFloat(mat, "_Metallic", 0);
                SetFloat(mat, "_Smoothness", unlit ? 0 : 0.15f);
                SetFloat(mat, "_Glossiness", unlit ? 0 : 0.15f);
                SetFloat(mat, "_SpecularHighlights", unlit ? 0 : 1);
                SetFloat(mat, "_GlossyReflections", unlit ? 0 : 1);
                SetFloat(mat, "_EnvironmentReflections", unlit ? 0 : 1);
                if (unlit) { mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
                SetFloat(mat, "_Cutoff", 0.15f);
                SetFloat(mat, "_AlphaClip", cutout ? 1 : 0);
                SetFloat(mat, "_Mode", transparent ? 2 : cutout ? 1 : 0);
                SetFloat(mat, "_Surface", transparent ? 1 : 0);
                SetFloat(mat, "_Blend", 0);
                SetFloat(mat, "_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
                SetFloat(mat, "_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                SetFloat(mat, "_SrcBlendAlpha", (float)BlendMode.One);
                SetFloat(mat, "_DstBlendAlpha", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
                SetFloat(mat, "_ZWrite", transparent ? 0 : 1);
                if (transparent)
                {
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    if (shader.name.StartsWith("Universal")) mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
                if (cutout) mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
                mat.renderQueue = (int)(transparent ? RenderQueue.Transparent : cutout ? RenderQueue.AlphaTest : RenderQueue.Geometry);
                mat.SetShaderPassEnabled("ShadowCaster", !transparent && surface != MaterialSurface.Hidden);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionOnly ? new Color(tint.r, tint.g, tint.b, 1) : Color.black);
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", maps[i]);
                if (emissionOnly) { mat.EnableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
                Plugin.Instance.InfoLog("PMX surface " + mat.name + "=" + surface + " depthWrite=" + !transparent + " textureAlpha=" + textureAlpha[i]);
            }
        }
        public void Dispose()
        {
            if (Template != null) Object.Destroy(Template);
            foreach (var item in owned) if (item != null) Object.Destroy(item);
            owned.Clear();
        }
    }
}
