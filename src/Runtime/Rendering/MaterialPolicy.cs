namespace WKPmxAvatar
{
    internal enum MaterialSurface { Hidden, Opaque, Cutout, Transparent }

    internal static class MaterialPolicy
    {
        internal static MaterialSurface Classify(float opacity, bool textureHasAlpha)
        {
            if (opacity <= 0.001f) return MaterialSurface.Hidden;
            if (opacity < 0.99f) return MaterialSurface.Transparent;
            return textureHasAlpha ? MaterialSurface.Cutout : MaterialSurface.Opaque;
        }

        // A byte of 254 can be an exporter rounding artifact. Keep opaque maps opaque.
        internal static bool HasCoverageAlpha(byte alpha) => alpha < 254;

        internal static int[] Faces(int[] source, bool doubleSided, int vertexCount)
        {
            if (!doubleSided) return source;
            var result = new int[source.Length * 2];
            System.Array.Copy(source, result, source.Length);
            for (int i = 0; i < source.Length; i += 3)
            {
                result[source.Length + i] = source[i] + vertexCount;
                result[source.Length + i + 1] = source[i + 2] + vertexCount;
                result[source.Length + i + 2] = source[i + 1] + vertexCount;
            }
            return result;
        }
    }
}
