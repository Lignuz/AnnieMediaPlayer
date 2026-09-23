using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    // 커버 이미지에서 대표색을 추출합니다.
    // 커버를 48x48 로 영역 평균 축소 → OKLab 공간 k-means(5) → 채도가 높고 명도가 적당하며 면적이 있는 색을 고릅니다.
    // 단순 평균색은 여러 색이 섞인 커버에서 탁해지므로 시각화 팔레트에는 이 색을 사용합니다.
    internal static class CoverColorExtractor
    {
        private const int SampleSize = 48;
        private const int ClusterCount = 5;

        private readonly record struct Lab(float L, float A, float B);

        public static Color? GetVibrantColor(BitmapSource? source)
        {
            if (source == null)
                return null;

            var bgra = SampleBgra(source, SampleSize, SampleSize);
            var pixels = new List<Lab>(SampleSize * SampleSize);
            var weights = new List<float>(SampleSize * SampleSize);
            for (var i = 0; i < bgra.Length; i += 4)
            {
                var alpha = bgra[i + 3];
                if (alpha < 16)
                    continue; // 투명 픽셀 제외

                pixels.Add(ToOklab(bgra[i + 2] / 255f, bgra[i + 1] / 255f, bgra[i] / 255f));
                weights.Add(alpha / 255f);
            }

            if (pixels.Count == 0)
                return null;

            // k-means (결정적 초기화: 평균에 가장 가까운 점 → 이후 가장 먼 점)
            float weightSum = 0, meanL = 0, meanA = 0, meanB = 0;
            for (var i = 0; i < pixels.Count; i++)
            {
                meanL += pixels[i].L * weights[i];
                meanA += pixels[i].A * weights[i];
                meanB += pixels[i].B * weights[i];
                weightSum += weights[i];
            }

            var mean = new Lab(meanL / weightSum, meanA / weightSum, meanB / weightSum);
            var centers = new List<Lab>();
            var first = 0;
            for (var i = 1; i < pixels.Count; i++)
            {
                if (Dist2(pixels[i], mean) < Dist2(pixels[first], mean))
                    first = i;
            }
            centers.Add(pixels[first]);

            var nearest = new float[pixels.Count];
            Array.Fill(nearest, 1e9f);
            while (centers.Count < ClusterCount)
            {
                var far = 0;
                var farDistance = -1f;
                for (var i = 0; i < pixels.Count; i++)
                {
                    nearest[i] = Math.Min(nearest[i], Dist2(pixels[i], centers[^1]));
                    if (nearest[i] > farDistance)
                    {
                        farDistance = nearest[i];
                        far = i;
                    }
                }

                if (farDistance < 1e-5f)
                    break; // 색이 K 개보다 적음
                centers.Add(pixels[far]);
            }

            var share = new float[centers.Count];
            for (var iteration = 0; iteration < 16; iteration++)
            {
                var accL = new float[centers.Count];
                var accA = new float[centers.Count];
                var accB = new float[centers.Count];
                Array.Clear(share);
                for (var i = 0; i < pixels.Count; i++)
                {
                    var best = 0;
                    for (var k = 1; k < centers.Count; k++)
                    {
                        if (Dist2(pixels[i], centers[k]) < Dist2(pixels[i], centers[best]))
                            best = k;
                    }

                    accL[best] += pixels[i].L * weights[i];
                    accA[best] += pixels[i].A * weights[i];
                    accB[best] += pixels[i].B * weights[i];
                    share[best] += weights[i];
                }

                for (var k = 0; k < centers.Count; k++)
                {
                    if (share[k] > 0)
                        centers[k] = new Lab(accL[k] / share[k], accA[k] / share[k], accB[k] / share[k]);
                }
            }

            // 비슷한 중심 병합
            var clusters = new List<(Lab Color, float Share)>();
            for (var k = 0; k < centers.Count; k++)
            {
                if (share[k] <= 0)
                    continue;

                var merged = false;
                for (var c = 0; c < clusters.Count; c++)
                {
                    if (Dist2(clusters[c].Color, centers[k]) < 0.03f * 0.03f)
                    {
                        var (color, total) = clusters[c];
                        var t = share[k] / (total + share[k]);
                        clusters[c] = (new Lab(color.L + (centers[k].L - color.L) * t,
                                               color.A + (centers[k].A - color.A) * t,
                                               color.B + (centers[k].B - color.B) * t), total + share[k]);
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                    clusters.Add((centers[k], share[k]));
            }

            clusters.Sort((x, y) => y.Share.CompareTo(x.Share));

            // vibrant: 채도 높고, 명도가 너무 어둡거나 밝지 않고, 어느 정도 면적이 있는 색
            var bestScore = -1f;
            var vibrant = clusters[0].Color;
            foreach (var (color, total) in clusters)
            {
                var chroma = MathF.Sqrt(color.A * color.A + color.B * color.B);
                var score = chroma * (1f - Math.Min(1f, MathF.Abs(color.L - 0.68f) * 1.4f)) * MathF.Sqrt(0.15f + total / weightSum);
                if (score > bestScore)
                {
                    bestScore = score;
                    vibrant = color;
                }
            }

            return FromOklab(vibrant);
        }

        // 이미지를 width x height 로 영역 평균(박스 필터) 축소한 BGRA(스트레이트 알파) 바이트를 반환합니다.
        public static byte[] SampleBgra(BitmapSource source, int width, int height)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int sourceWidth = converted.PixelWidth, sourceHeight = converted.PixelHeight;
            var stride = sourceWidth * 4;
            var pixels = new byte[stride * sourceHeight];
            converted.CopyPixels(pixels, stride, 0);

            var result = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var y0 = y * sourceHeight / height;
                var y1 = Math.Max(y0 + 1, (y + 1) * sourceHeight / height);
                for (var x = 0; x < width; x++)
                {
                    var x0 = x * sourceWidth / width;
                    var x1 = Math.Max(x0 + 1, (x + 1) * sourceWidth / width);
                    double b = 0, g = 0, r = 0, a = 0;
                    var count = 0;
                    for (var sy = y0; sy < y1; sy++)
                    {
                        var row = sy * stride;
                        for (var sx = x0; sx < x1; sx++)
                        {
                            var i = row + sx * 4;
                            double alpha = pixels[i + 3];
                            b += pixels[i] * alpha;
                            g += pixels[i + 1] * alpha;
                            r += pixels[i + 2] * alpha;
                            a += alpha;
                            count++;
                        }
                    }

                    var o = (y * width + x) * 4;
                    if (a > 0)
                    {
                        result[o] = (byte)Math.Round(b / a);
                        result[o + 1] = (byte)Math.Round(g / a);
                        result[o + 2] = (byte)Math.Round(r / a);
                    }
                    result[o + 3] = (byte)Math.Round(a / count);
                }
            }

            return result;
        }

        private static float Dist2(Lab x, Lab y)
        {
            float dL = x.L - y.L, da = x.A - y.A, db = x.B - y.B;
            return dL * dL + da * da + db * db;
        }

        private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        private static float ToGamma(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

        private static Lab ToOklab(float r, float g, float b)
        {
            r = ToLinear(r);
            g = ToLinear(g);
            b = ToLinear(b);
            var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
            return new Lab(0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                           1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                           0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        private static Color FromOklab(Lab c)
        {
            var l_ = c.L + 0.3963377774f * c.A + 0.2158037573f * c.B;
            var m_ = c.L - 0.1055613458f * c.A - 0.0638541728f * c.B;
            var s_ = c.L - 0.0894841775f * c.A - 1.2914855480f * c.B;
            float l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
            var r = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            var g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            var b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;
            static byte To8(float v) => (byte)MathF.Round(Math.Clamp(ToGamma(Math.Clamp(v, 0f, 1f)), 0f, 1f) * 255f);
            return Color.FromRgb(To8(r), To8(g), To8(b));
        }
    }
}
