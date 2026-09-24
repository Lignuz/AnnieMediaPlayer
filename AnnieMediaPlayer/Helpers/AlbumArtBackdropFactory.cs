using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    // 앨범 표지를 작게 축소한 뒤 흐리게 만들어 창 크기와 무관하게 재사용합니다.
    internal static class AlbumArtBackdropFactory
    {
        private const int MaxDimension = 256;

        public static BitmapSource Create(BitmapSource artwork, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scale = Math.Min(1d, MaxDimension / (double)Math.Max(artwork.PixelWidth, artwork.PixelHeight));
            BitmapSource source = scale < 1
                ? new TransformedBitmap(artwork, new ScaleTransform(scale, scale))
                : artwork;
            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            var scratch = new byte[pixels.Length];
            converted.CopyPixels(pixels, stride, 0);

            var radius = Math.Max(2, (int)Math.Round(Math.Min(width, height) * 0.06));
            for (var pass = 0; pass < 2; pass++)
            {
                BlurHorizontal(pixels, scratch, width, height, radius, cancellationToken);
                BlurVertical(scratch, pixels, width, height, radius, cancellationToken);
            }

            var backdrop = BitmapSource.Create(width, height, 96, 96,
                PixelFormats.Pbgra32, null, pixels, stride);
            backdrop.Freeze();
            return backdrop;
        }

        private static void BlurHorizontal(byte[] input, byte[] output, int width, int height,
            int radius, CancellationToken cancellationToken)
        {
            var stride = width * 4;
            var sums = new int[4];
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = y * stride;
                Array.Clear(sums);
                for (var x = 0; x <= Math.Min(radius, width - 1); x++)
                    AddPixel(input, row + x * 4, sums, 1);

                for (var x = 0; x < width; x++)
                {
                    var count = Math.Min(width - 1, x + radius) - Math.Max(0, x - radius) + 1;
                    var offset = row + x * 4;
                    for (var channel = 0; channel < 4; channel++)
                        output[offset + channel] = (byte)(sums[channel] / count);

                    if (x - radius >= 0)
                        AddPixel(input, row + (x - radius) * 4, sums, -1);
                    if (x + radius + 1 < width)
                        AddPixel(input, row + (x + radius + 1) * 4, sums, 1);
                }
            }
        }

        private static void BlurVertical(byte[] input, byte[] output, int width, int height,
            int radius, CancellationToken cancellationToken)
        {
            var stride = width * 4;
            var sums = new int[4];
            for (var x = 0; x < width; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var column = x * 4;
                Array.Clear(sums);
                for (var y = 0; y <= Math.Min(radius, height - 1); y++)
                    AddPixel(input, y * stride + column, sums, 1);

                for (var y = 0; y < height; y++)
                {
                    var count = Math.Min(height - 1, y + radius) - Math.Max(0, y - radius) + 1;
                    var offset = y * stride + column;
                    for (var channel = 0; channel < 4; channel++)
                        output[offset + channel] = (byte)(sums[channel] / count);

                    if (y - radius >= 0)
                        AddPixel(input, (y - radius) * stride + column, sums, -1);
                    if (y + radius + 1 < height)
                        AddPixel(input, (y + radius + 1) * stride + column, sums, 1);
                }
            }
        }

        private static void AddPixel(byte[] pixels, int offset, int[] sums, int sign)
        {
            for (var channel = 0; channel < 4; channel++)
                sums[channel] += pixels[offset + channel] * sign;
        }
    }
}
