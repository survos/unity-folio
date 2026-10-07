using System;
using UnityEngine;

namespace Survos.Folio
{
    /// ThumbHash decoding (https://evanw.github.io/thumbhash/, port of the reference
    /// thumbHashToRGBA): a ~25-byte hash becomes a blurred image of at most 32 px,
    /// with the source's approximate aspect ratio.
    public static class ThumbHash
    {
        /// Decode to RGBA, rows top to bottom.
        public static (int w, int h, byte[] rgba) ToRgba(byte[] hash)
        {
            if (hash == null || hash.Length < 5) throw new ArgumentException("Not a ThumbHash.", nameof(hash));
            int header24 = hash[0] | (hash[1] << 8) | (hash[2] << 16);
            int header16 = hash[3] | (hash[4] << 8);
            float lDc = (header24 & 63) / 63f;
            float pDc = ((header24 >> 6) & 63) / 31.5f - 1;
            float qDc = ((header24 >> 12) & 63) / 31.5f - 1;
            float lScale = ((header24 >> 18) & 31) / 31f;
            bool hasAlpha = (header24 >> 23) != 0;
            float pScale = ((header16 >> 3) & 63) / 63f;
            float qScale = ((header16 >> 9) & 63) / 63f;
            bool isLandscape = (header16 >> 15) != 0;
            int lx = Math.Max(3, isLandscape ? hasAlpha ? 5 : 7 : header16 & 7);
            int ly = Math.Max(3, isLandscape ? header16 & 7 : hasAlpha ? 5 : 7);
            float aDc = hasAlpha ? (hash[5] & 15) / 15f : 1;
            float aScale = (hash[5] >> 4) / 15f;

            // The varying factors (saturation boosted 1.25x to compensate for quantisation).
            int acStart = hasAlpha ? 6 : 5;
            int acIndex = 0;
            float[] Channel(int nx, int ny, float scale)
            {
                var ac = new System.Collections.Generic.List<float>();
                for (int cy = 0; cy < ny; cy++)
                for (int cx = cy > 0 ? 0 : 1; cx * ny < nx * (ny - cy); cx++)
                {
                    int b = acStart + (acIndex >> 1);
                    int nibble = b < hash.Length ? (hash[b] >> ((acIndex & 1) << 2)) & 15 : 0;
                    acIndex++;
                    ac.Add((nibble / 7.5f - 1) * scale);
                }
                return ac.ToArray();
            }
            var lAc = Channel(lx, ly, lScale);
            var pAc = Channel(3, 3, pScale * 1.25f);
            var qAc = Channel(3, 3, qScale * 1.25f);
            var aAc = hasAlpha ? Channel(5, 5, aScale) : null;

            float ratio = AspectRatio(hash);
            int w = (int)Math.Round(ratio > 1 ? 32 : 32 * ratio);
            int h = (int)Math.Round(ratio > 1 ? 32 / ratio : 32);
            var rgba = new byte[w * h * 4];
            var fx = new float[Math.Max(lx, hasAlpha ? 5 : 3)];
            var fy = new float[Math.Max(ly, hasAlpha ? 5 : 3)];
            for (int y = 0, i = 0; y < h; y++)
            for (int x = 0; x < w; x++, i += 4)
            {
                float l = lDc, p = pDc, q = qDc, a = aDc;
                for (int cx = 0; cx < fx.Length; cx++) fx[cx] = (float)Math.Cos(Math.PI / w * (x + 0.5) * cx);
                for (int cy = 0; cy < fy.Length; cy++) fy[cy] = (float)Math.Cos(Math.PI / h * (y + 0.5) * cy);

                for (int cy = 0, j = 0; cy < ly; cy++)
                {
                    float fy2 = fy[cy] * 2;
                    for (int cx = cy > 0 ? 0 : 1; cx * ly < lx * (ly - cy); cx++, j++) l += lAc[j] * fx[cx] * fy2;
                }
                for (int cy = 0, j = 0; cy < 3; cy++)
                {
                    float fy2 = fy[cy] * 2;
                    for (int cx = cy > 0 ? 0 : 1; cx < 3 - cy; cx++, j++)
                    {
                        float f = fx[cx] * fy2;
                        p += pAc[j] * f;
                        q += qAc[j] * f;
                    }
                }
                if (aAc != null)
                    for (int cy = 0, j = 0; cy < 5; cy++)
                    {
                        float fy2 = fy[cy] * 2;
                        for (int cx = cy > 0 ? 0 : 1; cx < 5 - cy; cx++, j++) a += aAc[j] * fx[cx] * fy2;
                    }

                float bl = l - 2f / 3f * p;
                float r = (3 * l - bl + q) / 2;
                float g = r - q;
                rgba[i] = Byte(r);
                rgba[i + 1] = Byte(g);
                rgba[i + 2] = Byte(bl);
                rgba[i + 3] = Byte(a);
            }
            return (w, h, rgba);
        }

        // Same as assigning to a Uint8Array: clamp, then truncate.
        static byte Byte(float v) => (byte)Math.Max(0, 255 * Math.Min(1, v));

        /// Width / height of the source, approximately.
        public static float AspectRatio(byte[] hash)
        {
            int header = hash[3];
            bool hasAlpha = (hash[2] & 0x80) != 0;
            bool isLandscape = (hash[4] & 0x80) != 0;
            int lx = isLandscape ? hasAlpha ? 5 : 7 : header & 7;
            int ly = isLandscape ? header & 7 : hasAlpha ? 5 : 7;
            return (float)lx / ly;
        }

        /// A small bilinear-filtered texture, or null for a missing or malformed hash.
        public static Texture2D ToTexture(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                var (w, h, rgba) = ToRgba(Convert.FromBase64String(base64));
                // Unity's texture rows run bottom to top.
                var flipped = new byte[rgba.Length];
                int stride = w * 4;
                for (int y = 0; y < h; y++) Buffer.BlockCopy(rgba, y * stride, flipped, (h - 1 - y) * stride, stride);
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ThumbHash" };
                texture.LoadRawTextureData(flipped);
                texture.Apply(false, true);
                return texture;
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
            catch (IndexOutOfRangeException) { return null; }
        }

        /// "#rrggbb" as a colour, or null.
        public static Color? ParseColor(string hex) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : null;
    }
}
