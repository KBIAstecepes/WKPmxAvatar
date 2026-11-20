using System;
using System.IO;
using System.Text;

namespace WKPmxAvatar
{
    // Plain data only: the same parser can be tested without loading Unity.
    public sealed class PmxData
    {
        public struct V3 { public float X, Y, Z; }
        public sealed class Vertex
        {
            public V3 Position, Normal;
            public float U, V;
            public int[] Bones = new int[4];
            public float[] Weights = new float[4];
        }
        public sealed class Material
        {
            public string Name;
            public float R, G, B, A;
            public byte Flags;
            public int Texture, IndexCount;
        }
        public sealed class Bone
        {
            public string Name, English;
            public V3 Position;
            public int Parent, InheritParent = -1;
            public float InheritWeight;
            public ushort Flags;
        }
        public string Name;
        public Vertex[] Vertices;
        public int[] Indices;
        public string[] Textures;
        public Material[] Materials;
        public Bone[] Bones;
        public int ApproximateWeights;
    }

    public sealed class PmxReader : IDisposable
    {
        readonly BinaryReader r;
        Encoding encoding;
        int vertexSize, textureSize, boneSize, additionalUV;
        PmxReader(Stream stream) { r = new BinaryReader(stream, Encoding.UTF8, true); }
        public void Dispose() => r.Dispose();
        int Count(int max = 2000000)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > max) throw new InvalidDataException("PMX count out of range: " + n);
            return n;
        }
        byte[] Bytes(int n)
        {
            var b = r.ReadBytes(n);
            if (b.Length != n) throw new EndOfStreamException();
            return b;
        }
        string Text() => encoding.GetString(Bytes(Count(16 * 1024 * 1024)));
        float F()
        {
            float f = r.ReadSingle();
            if (float.IsNaN(f) || float.IsInfinity(f)) throw new InvalidDataException("Non-finite PMX number");
            return f;
        }
        PmxData.V3 V3() => new PmxData.V3 { X = F(), Y = F(), Z = F() };
        void Floats(int n) { for (int i = 0; i < n; i++) F(); }
        int Index(int size, bool unsigned = false)
        {
            switch (size)
            {
                case 1: return unsigned ? r.ReadByte() : r.ReadSByte();
                case 2: return unsigned ? r.ReadUInt16() : r.ReadInt16();
                case 4: return r.ReadInt32();
                default: throw new InvalidDataException("Invalid PMX index size");
            }
        }
        public static PmxData Read(Stream stream)
        {
            using (var reader = new PmxReader(stream)) return reader.Parse();
        }
        PmxData Parse()
        {
            if (Encoding.ASCII.GetString(Bytes(4)) != "PMX ") throw new InvalidDataException("Not PMX");
            float version = F();
            if (Math.Abs(version - 2f) > 0.001f && Math.Abs(version - 2.1f) > 0.001f)
                throw new InvalidDataException("Only PMX 2.0/2.1 supported");
            byte[] h = Bytes(r.ReadByte());
            if (h.Length < 8 || h[0] > 1 || h[1] > 4) throw new InvalidDataException("Invalid PMX header");
            for (int i = 2; i < 8; i++) if (h[i] != 1 && h[i] != 2 && h[i] != 4)
                throw new InvalidDataException("Invalid PMX index size");
            encoding = h[0] == 0 ? Encoding.Unicode : new UTF8Encoding(false, true);
            additionalUV = h[1]; vertexSize = h[2]; textureSize = h[3]; boneSize = h[5];
            var data = new PmxData { Name = Text() }; Text(); Text(); Text();
            data.Vertices = new PmxData.Vertex[Count()];
            foreach (int i in Range(data.Vertices.Length))
            {
                var v = new PmxData.Vertex { Position = V3(), Normal = V3(), U = F(), V = F() };
                Floats(additionalUV * 4);
                byte deform = r.ReadByte();
                switch (deform)
                {
                    case 0: v.Bones[0] = Index(boneSize); v.Weights[0] = 1; break;
                    case 1:
                    case 3:
                        v.Bones[0] = Index(boneSize); v.Bones[1] = Index(boneSize);
                        v.Weights[0] = F(); v.Weights[1] = 1 - v.Weights[0];
                        if (deform == 3) { Floats(9); data.ApproximateWeights++; }
                        break;
                    case 2:
                    case 4:
                        for (int b = 0; b < 4; b++) v.Bones[b] = Index(boneSize);
                        for (int b = 0; b < 4; b++) v.Weights[b] = F();
                        if (deform == 4) data.ApproximateWeights++;
                        break;
                    default: throw new InvalidDataException("Unknown PMX deformation");
                }
                F(); data.Vertices[i] = v;
            }
            data.Indices = new int[Count(12000000)];
            if (data.Indices.Length % 3 != 0) throw new InvalidDataException("Invalid triangle count");
            for (int i = 0; i < data.Indices.Length; i++)
            {
                int index = Index(vertexSize, true);
                if (index < 0 || index >= data.Vertices.Length) throw new InvalidDataException("Invalid vertex index");
                data.Indices[i] = index;
            }
            data.Textures = new string[Count(10000)];
            for (int i = 0; i < data.Textures.Length; i++) data.Textures[i] = Text();
            data.Materials = new PmxData.Material[Count(10000)];
            long total = 0;
            for (int i = 0; i < data.Materials.Length; i++)
            {
                var m = new PmxData.Material { Name = Text() }; Text();
                m.R = F(); m.G = F(); m.B = F(); m.A = F();
                Floats(7); m.Flags = r.ReadByte(); Floats(5);
                m.Texture = Index(textureSize); Index(textureSize); r.ReadByte();
                if (m.Texture < -1 || m.Texture >= data.Textures.Length) throw new InvalidDataException("Invalid texture index");
                byte toon = r.ReadByte();
                if (toon == 0) Index(textureSize);
                else if (toon == 1) r.ReadByte();
                else throw new InvalidDataException("Invalid toon mode");
                Text(); m.IndexCount = Count(12000000);
                if (m.IndexCount % 3 != 0) throw new InvalidDataException("Invalid material triangles");
                total += m.IndexCount; data.Materials[i] = m;
            }
            if (total != data.Indices.Length) throw new InvalidDataException("Material triangle count mismatch");
            data.Bones = new PmxData.Bone[Count(20000)];
            for (int i = 0; i < data.Bones.Length; i++)
            {
                var b = new PmxData.Bone { Name = Text(), English = Text(), Position = V3(), Parent = Index(boneSize) };
                r.ReadInt32(); ushort flags = r.ReadUInt16();
                b.Flags = flags;
                if ((flags & 1) != 0) Index(boneSize); else Floats(3);
                if ((flags & 0x300) != 0) { b.InheritParent = Index(boneSize); b.InheritWeight = F(); }
                if ((flags & 0x400) != 0) Floats(3);
                if ((flags & 0x800) != 0) Floats(6);
                if ((flags & 0x2000) != 0) r.ReadInt32();
                if ((flags & 0x20) != 0)
                {
                    Index(boneSize); r.ReadInt32(); F();
                    int links = Count(20000);
                    for (int j = 0; j < links; j++) { Index(boneSize); if (r.ReadByte() != 0) Floats(6); }
                }
                data.Bones[i] = b;
            }
            // Reject cycles before constructing Unity's Transform hierarchy.
            for (int i = 0; i < data.Bones.Length; i++)
            {
                if (data.Bones[i].InheritParent < -1 || data.Bones[i].InheritParent >= data.Bones.Length || data.Bones[i].InheritParent == i)
                    throw new InvalidDataException("Invalid inherit bone index");
                int p = i, steps = 0;
                while (p != -1)
                {
                    if (p < 0 || p >= data.Bones.Length || ++steps > data.Bones.Length)
                        throw new InvalidDataException("Invalid/cyclic PMX bone hierarchy");
                    p = data.Bones[p].Parent;
                }
            }
            foreach (var v in data.Vertices)
            {
                float sum = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (v.Weights[i] < 0 || v.Weights[i] > 1 ||
                        (v.Weights[i] > 0 && (v.Bones[i] < 0 || v.Bones[i] >= data.Bones.Length)))
                        throw new InvalidDataException("Invalid bone weight");
                    if (v.Weights[i] == 0) v.Bones[i] = 0;
                    sum += v.Weights[i];
                }
                if (sum < 0.00001f) throw new InvalidDataException("Zero vertex weight");
                for (int i = 0; i < 4; i++) v.Weights[i] /= sum;
            }
            if (data.Vertices.Length == 0 || data.Bones.Length == 0) throw new InvalidDataException("Empty PMX model");
            return data; // Morphs and MMD physics are intentionally not evaluated.
        }
        static System.Collections.Generic.IEnumerable<int> Range(int n) { for (int i = 0; i < n; i++) yield return i; }
    }
}
