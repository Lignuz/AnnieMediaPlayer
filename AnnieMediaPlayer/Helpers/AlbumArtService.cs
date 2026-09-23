using FFmpeg.AutoGen;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Unosquare.FFME;

using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingGraphicsPath = System.Drawing.Drawing2D.GraphicsPath;
using DrawingLinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using DrawingPathGradientBrush = System.Drawing.Drawing2D.PathGradientBrush;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSizeF = System.Drawing.SizeF;
using DrawingStringFormat = System.Drawing.StringFormat;
using DrawingStringFormatFlags = System.Drawing.StringFormatFlags;
using DrawingStringTrimming = System.Drawing.StringTrimming;
using DrawingSolidBrush = System.Drawing.SolidBrush;

namespace AnnieMediaPlayer
{
    public enum AlbumArtSource
    {
        None,
        Embedded,
        Folder,
        ShellThumbnail,
        Generated
    }

    public sealed record AlbumArtResult(BitmapSource? Image, AlbumArtSource Source, string Detail, TimeSpan? Duration = null);

    /// <summary>
    /// 로컬 미디어에서 앨범 이미지를 찾습니다.
    /// 온라인 조회는 포함하지 않으며, 결과 이미지는 모두 Freeze 된 상태로 반환합니다.
    /// </summary>
    public sealed class AlbumArtService : IDisposable
    {
        // 재생목록 항목이 한꺼번에 요청되므로 파일 열기·디코딩 작업 수를 제한합니다.
        private static readonly int MaxConcurrentLoads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        // 캐시된 이미지의 대략적인 메모리 한도입니다. 초과하면 오래 사용하지 않은 항목부터 제거합니다.
        private const long CacheByteLimit = 64L * 1024 * 1024;

        private sealed record CacheEntry(string Key, AlbumArtResult Result, long Bytes);

        private readonly SemaphoreSlim _loadGate = new(MaxConcurrentLoads, MaxConcurrentLoads);
        private readonly object _cacheSync = new();
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache = new();
        private readonly LinkedList<CacheEntry> _cacheOrder = new(); // 앞쪽이 최근 사용 항목
        private long _cacheBytes;
        private int _disposed;

        public async Task<AlbumArtResult> LoadAsync(string filePath, int maxSide = 96, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            maxSide = Math.Clamp(maxSide, 32, 1024);

            var fullPath = Path.GetFullPath(filePath);
            var key = CreateCacheKey(fullPath, maxSide);
            if (TryGetCached(key, out var cached))
                return cached;

            await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 대기하는 동안 같은 파일의 결과가 캐시에 들어왔을 수 있습니다.
                if (TryGetCached(key, out cached))
                    return cached;

                var result = await Task.Run(() => LoadCore(fullPath, maxSide, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                AddToCache(key, result);

                return result;
            }
            finally
            {
                _loadGate.Release();
            }
        }

        public void ClearCache()
        {
            lock (_cacheSync)
            {
                _cache.Clear();
                _cacheOrder.Clear();
                _cacheBytes = 0;
            }
        }

        private bool TryGetCached(string key, out AlbumArtResult result)
        {
            lock (_cacheSync)
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _cacheOrder.Remove(node);
                    _cacheOrder.AddFirst(node);
                    result = node.Value.Result;
                    return true;
                }
            }

            result = null!;
            return false;
        }

        private void AddToCache(string key, AlbumArtResult result)
        {
            var image = result.Image;
            var bytes = image is null ? 0 : (long)image.PixelWidth * image.PixelHeight * 4;

            lock (_cacheSync)
            {
                // Dispose 가 캐시를 비운 뒤 늦게 끝난 로딩 결과가 다시 들어가지 않도록 잠금 안에서 확인합니다.
                if (Volatile.Read(ref _disposed) != 0)
                    return;

                if (_cache.Remove(key, out var existing))
                {
                    _cacheOrder.Remove(existing);
                    _cacheBytes -= existing.Value.Bytes;
                }

                _cache[key] = _cacheOrder.AddFirst(new CacheEntry(key, result, bytes));
                _cacheBytes += bytes;

                while (_cacheBytes > CacheByteLimit && _cacheOrder.Last is { } oldest && oldest != _cacheOrder.First)
                {
                    _cacheOrder.RemoveLast();
                    _cache.Remove(oldest.Value.Key);
                    _cacheBytes -= oldest.Value.Bytes;
                }
            }
        }

        private static string CreateCacheKey(string filePath, int maxSide)
        {
            try
            {
                var info = new FileInfo(filePath);
                return $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{maxSide}";
            }
            catch (IOException)
            {
                return $"{filePath}|{maxSide}";
            }
            catch (UnauthorizedAccessException)
            {
                return $"{filePath}|{maxSide}";
            }
        }

        private static AlbumArtResult LoadCore(string filePath, int maxSide, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedded = TryEmbeddedArt(filePath, out var duration, cancellationToken);

            try
            {
                if (embedded is not null)
                {
                    var image = DecodeImage(embedded, maxSide);
                    if (image is not null)
                        return new AlbumArtResult(image, AlbumArtSource.Embedded, "파일에 내장된 앨범 이미지", duration);
                }

                cancellationToken.ThrowIfCancellationRequested();
                foreach (var candidate in EnumerateFolderCandidates(filePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var image = DecodeImage(File.ReadAllBytes(candidate), maxSide);
                        if (image is not null)
                            return new AlbumArtResult(image, AlbumArtSource.Folder, $"폴더 이미지: {Path.GetFileName(candidate)}", duration);
                    }
                    catch (IOException)
                    {
                        // 손상되었거나 사용 중인 후보는 다음 후보로 넘어갑니다.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 접근할 수 없는 후보는 다음 후보로 넘어갑니다.
                    }
                    catch (ArgumentException)
                    {
                        // 지원하지 않는 이미지 형식은 다음 후보로 넘어갑니다.
                    }
                    catch (NotSupportedException)
                    {
                        // 지원하지 않는 이미지 코덱은 다음 후보로 넘어갑니다.
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var shellImage = TryShellThumbnail(filePath, maxSide, cancellationToken);
                if (shellImage is not null)
                    return new AlbumArtResult(shellImage, AlbumArtSource.ShellThumbnail, "Windows 셸 썸네일", duration);

                cancellationToken.ThrowIfCancellationRequested();
                var generated = CreateGeneratedCover(filePath, maxSide);
                return new AlbumArtResult(generated, generated is null ? AlbumArtSource.None : AlbumArtSource.Generated,
                    generated is null ? "앨범 이미지를 찾지 못함" : "앨범 이미지 없음 · 생성형 커버", duration);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Album art load failed: {filePath} - {ex}");
                var generated = CreateGeneratedCover(filePath, maxSide);
                return new AlbumArtResult(generated, generated is null ? AlbumArtSource.None : AlbumArtSource.Generated,
                    generated is null ? "앨범 이미지 처리 실패" : "앨범 이미지 처리 실패 · 생성형 커버", duration);
            }
        }

        private static IEnumerable<string> EnumerateFolderCandidates(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
                yield break;

            var names = new[] { "cover", "folder", "front", "album", "albumart" };
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            foreach (var name in names)
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }

        private static unsafe byte[]? TryEmbeddedArt(string filePath, out TimeSpan? duration, CancellationToken cancellationToken)
        {
            duration = null;
            try
            {
                if (!Library.IsInitialized)
                {
                    Library.FFmpegDirectory = "ffmpeg";
                    if (!Library.LoadFFmpeg())
                        return null;
                }

                AVFormatContext* formatContext = null;
                if (ffmpeg.avformat_open_input(&formatContext, filePath, null, null) < 0 || formatContext == null)
                    return null;

                try
                {
                    if (ffmpeg.avformat_find_stream_info(formatContext, null) < 0)
                        return null;

                    if (formatContext->duration > 0 && formatContext->duration != ffmpeg.AV_NOPTS_VALUE)
                    {
                        var durationSeconds = (double)formatContext->duration / ffmpeg.AV_TIME_BASE;
                        if (double.IsFinite(durationSeconds) && durationSeconds > 0)
                            duration = TimeSpan.FromSeconds(durationSeconds);
                    }

                    for (uint index = 0; index < formatContext->nb_streams; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var stream = formatContext->streams[index];
                        if (stream == null || (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                            continue;

                        var packet = &stream->attached_pic;
                        if (packet->data == null || packet->size <= 0)
                            continue;

                        var data = new byte[packet->size];
                        Marshal.Copy((IntPtr)packet->data, data, 0, packet->size);
                        return data;
                    }
                }
                finally
                {
                    ffmpeg.avformat_close_input(&formatContext);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Embedded album art read failed: {filePath} - {ex.Message}");
            }

            return null;
        }

        private static BitmapSource? DecodeImage(byte[] data, int maxSide)
        {
            if (data.Length == 0)
                return null;

            using var stream = new MemoryStream(data, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return null;

            var frame = decoder.Frames[0];
            var largestSide = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (largestSide <= maxSide)
            {
                frame.Freeze();
                return frame;
            }

            var scale = (double)maxSide / largestSide;
            var transform = new ScaleTransform(scale, scale);
            transform.Freeze();
            var resized = new TransformedBitmap(frame, transform);
            resized.Freeze();
            return resized;
        }

        private static BitmapSource? TryShellThumbnail(string filePath, int maxSide, CancellationToken cancellationToken)
        {
            BitmapSource? result = null;
            Exception? failure = null;
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                var initialized = false;
                IntPtr bitmap = IntPtr.Zero;
                IShellItemImageFactory? factory = null;
                try
                {
                    var hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
                    initialized = hr >= 0;
                    if (hr < 0)
                        return;

                    var iid = typeof(IShellItemImageFactory).GUID;
                    if (SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out factory) < 0 || factory is null)
                        return;

                    var size = new ShellSize { cx = maxSide, cy = maxSide };
                    const ShellImageFlags flags = ShellImageFlags.ThumbnailOnly | ShellImageFlags.BiggerSizeOk;
                    if (factory.GetImage(size, flags, out bitmap) < 0 || bitmap == IntPtr.Zero)
                        return;

                    result = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    result.Freeze();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    if (bitmap != IntPtr.Zero)
                        DeleteObject(bitmap);
                    if (factory is not null)
                        Marshal.FinalReleaseComObject(factory);
                    if (initialized)
                        CoUninitialize();
                    completed.TrySetResult(true);
                }
            })
            {
                IsBackground = true,
                Name = "AnnieMediaPlayer Album Art Shell Thumbnail"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            while (!completed.Task.Wait(25))
                cancellationToken.ThrowIfCancellationRequested();

            if (failure is not null)
                PlayerDiagnostics.Write($"Shell thumbnail read failed: {filePath} - {failure.Message}");
            return result;
        }

        private static BitmapSource? CreateGeneratedCover(string filePath, int size)
        {
            try
            {
                var metadata = ReadGeneratedCoverMetadata(filePath);
                var paletteKey = $"{(string.IsNullOrWhiteSpace(metadata.AlbumArtist) ? metadata.Artist : metadata.AlbumArtist)}|{metadata.Album}";
                var hash = Fnv1a(paletteKey);
                var hue = hash % 360;
                var spread = 30 + (hash >> 9) % 60;
                var first = HslToColor(hue, 0.65f, 0.45f);
                var second = HslToColor(hue + spread, 0.70f, 0.24f);

                using var bitmap = new DrawingBitmap(size, size, DrawingPixelFormat.Format32bppArgb);
                using var graphics = DrawingGraphics.FromImage(bitmap);
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // ClearType은 투명 비트맵의 RGB 채널에 색 프린지를 남길 수 있으므로
                // WPF Image로 표시되는 생성 커버에는 회색조 안티앨리어싱을 사용합니다.
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.TextContrast = 0;
                graphics.Clear(DrawingColor.FromArgb(255, 18, 12, 16));

                var cardPadding = Math.Max(8f, size * 0.035f);
                var card = new DrawingRectangleF(cardPadding, cardPadding,
                    size - cardPadding * 2, size - cardPadding * 2);
                var radius = Math.Max(10f, size * 0.035f);

                using (var cardPath = CreateRoundedRectangle(card, radius))
                {
                    graphics.SetClip(cardPath);
                    using (var background = new DrawingLinearGradientBrush(card, first, second, 35f))
                        graphics.FillRectangle(background, card);

                    for (var index = 0; index < 4; index++)
                    {
                        var seed = hash * (2654435761u + (uint)index * 40503u);
                        var x = card.Left + card.Width * (0.10f + ((seed & 0xff) / 255f) * 0.80f);
                        var y = card.Top + card.Height * (0.10f + (((seed >> 8) & 0xff) / 255f) * 0.72f);
                        var radiusX = card.Width * (0.24f + (((seed >> 16) & 0xff) / 255f) * 0.22f);
                        var radiusY = radiusX * (0.75f + (((seed >> 24) & 0xff) / 255f) * 0.35f);
                        var blobColor = HslToColor(hue + spread * (index - 1.5f) * 1.3f, 0.80f, 0.60f);

                        using var blobPath = new DrawingGraphicsPath();
                        blobPath.AddEllipse(x - radiusX, y - radiusY, radiusX * 2, radiusY * 2);
                        using var blob = new DrawingPathGradientBrush(blobPath)
                        {
                            CenterColor = DrawingColor.FromArgb(92, blobColor),
                            SurroundColors = new[] { DrawingColor.FromArgb(0, blobColor) }
                        };
                        graphics.FillPath(blob, blobPath);
                    }

                    // 카드 전체에 투명한 시작점을 포함한 그라디언트를 겹쳐 그립니다.
                    // 별도 사각형의 시작 경계가 남지 않도록 하단 스크림의 경계선을 제거합니다.
                    using (var scrim = new DrawingLinearGradientBrush(card,
                               DrawingColor.FromArgb(0, 0, 0, 0), DrawingColor.FromArgb(205, 0, 0, 0), 90f))
                    {
                        scrim.InterpolationColors = new ColorBlend
                        {
                            Positions = new[] { 0.0f, 0.48f, 0.62f, 1.0f },
                            Colors = new[]
                            {
                                DrawingColor.FromArgb(0, 0, 0, 0),
                                DrawingColor.FromArgb(0, 0, 0, 0),
                                DrawingColor.FromArgb(85, 0, 0, 0),
                                DrawingColor.FromArgb(205, 0, 0, 0)
                            }
                        };
                        graphics.FillPath(scrim, cardPath);
                    }

                    graphics.ResetClip();

                    using var outline = new System.Drawing.Pen(DrawingColor.FromArgb(34, 255, 255, 255), Math.Max(1f, size / 320f));
                    graphics.DrawPath(outline, cardPath);
                }

                var contentPadding = card.Width * 0.075f;
                var textLeft = card.Left + contentPadding;
                var textWidth = card.Width - contentPadding * 2;
                var initials = GetInitials(string.IsNullOrWhiteSpace(metadata.Album) ? metadata.Title : metadata.Album);
                var initialsRect = new DrawingRectangleF(textLeft, card.Top + card.Height * 0.10f,
                    textWidth, card.Height * 0.31f);
                var titleRect = new DrawingRectangleF(textLeft, card.Top + card.Height * 0.76f,
                    textWidth, card.Height * 0.105f);
                var artistRect = new DrawingRectangleF(textLeft, card.Top + card.Height * 0.865f,
                    textWidth, card.Height * 0.075f);

                using var whiteBrush = new DrawingSolidBrush(DrawingColor.FromArgb(248, 255, 255, 255));
                using var softBrush = new DrawingSolidBrush(DrawingColor.FromArgb(210, 255, 255, 255));
                using var initialsFont = new DrawingFont("Segoe UI", Math.Max(18f, card.Width * 0.25f),
                    DrawingFontStyle.Bold, DrawingGraphicsUnit.Pixel);
                using var titleFont = CreateFittingFont(graphics, metadata.Title, textWidth,
                    Math.Max(14f, card.Width * 0.072f), Math.Max(10f, card.Width * 0.045f), DrawingFontStyle.Bold);
                using var artistFont = CreateFittingFont(graphics, metadata.Artist, textWidth,
                    Math.Max(11f, card.Width * 0.045f), Math.Max(9f, card.Width * 0.032f), DrawingFontStyle.Regular);
                using var textFormat = CreateSingleLineTextFormat();

                graphics.DrawString(initials, initialsFont, whiteBrush, initialsRect, textFormat);
                graphics.DrawString(metadata.Title, titleFont, whiteBrush, titleRect, textFormat);
                graphics.DrawString(string.IsNullOrWhiteSpace(metadata.Artist) ? "Unknown Artist" : metadata.Artist,
                    artistFont, softBrush, artistRect, textFormat);

                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                return DecodeImage(stream.ToArray(), size);
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Generated album art creation failed: {filePath} - {ex.Message}");
                return null;
            }
        }

        private sealed record GeneratedCoverMetadata(string Title, string Artist, string Album, string AlbumArtist);

        private static GeneratedCoverMetadata ReadGeneratedCoverMetadata(string filePath)
        {
            var title = Path.GetFileNameWithoutExtension(filePath);
            var artist = string.Empty;
            var album = string.Empty;
            var albumArtist = string.Empty;

            try
            {
                if (!Library.IsInitialized)
                {
                    Library.FFmpegDirectory = "ffmpeg";
                    Library.LoadFFmpeg();
                }

                var info = Library.RetrieveMediaInfo(filePath);
                title = FindMetadata(info.Metadata, "title") ?? title;
                artist = FindMetadata(info.Metadata, "artist", "performer") ?? artist;
                album = FindMetadata(info.Metadata, "album") ?? album;
                albumArtist = FindMetadata(info.Metadata, "album_artist", "album-artist", "albumartist") ?? albumArtist;
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Generated album art metadata read skipped: {filePath} - {ex.Message}");
            }

            return new GeneratedCoverMetadata(
                string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(filePath) : title,
                artist,
                string.IsNullOrWhiteSpace(album) ? title : album,
                albumArtist);
        }

        private static string? FindMetadata(IReadOnlyDictionary<string, string> metadata, params string[] keys)
        {
            foreach (var key in keys)
            {
                foreach (var pair in metadata)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
                        return pair.Value.Trim();
                }
            }

            return null;
        }

        private static DrawingGraphicsPath CreateRoundedRectangle(DrawingRectangleF rectangle, float radius)
        {
            var path = new DrawingGraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static DrawingStringFormat CreateSingleLineTextFormat() => new(System.Drawing.StringFormat.GenericTypographic)
        {
            Alignment = System.Drawing.StringAlignment.Near,
            LineAlignment = System.Drawing.StringAlignment.Center,
            FormatFlags = DrawingStringFormatFlags.NoWrap,
            Trimming = DrawingStringTrimming.EllipsisCharacter
        };

        private static DrawingFont CreateFittingFont(DrawingGraphics graphics, string text, float width,
            float maxSize, float minSize, DrawingFontStyle style)
        {
            text = string.IsNullOrWhiteSpace(text) ? "Unknown Artist" : text;
            using var format = CreateSingleLineTextFormat();
            for (var size = maxSize; size >= minSize; size -= 1f)
            {
                var candidate = new DrawingFont("Segoe UI", size, style, DrawingGraphicsUnit.Pixel);
                var measured = graphics.MeasureString(text, candidate, new DrawingSizeF(width, float.MaxValue), format);
                if (measured.Width <= width + 1f)
                    return candidate;

                candidate.Dispose();
            }

            return new DrawingFont("Segoe UI", minSize, style, DrawingGraphicsUnit.Pixel);
        }

        private static uint Fnv1a(string value)
        {
            var hash = 2166136261u;
            foreach (var character in value.ToLowerInvariant())
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash;
        }

        private static string GetInitials(string title)
        {
            var initials = new List<char>(2);
            var takeNext = true;
            foreach (var character in title)
            {
                if (char.IsWhiteSpace(character) || character is '-' or '_')
                {
                    takeNext = true;
                    continue;
                }

                if (!takeNext || !char.IsLetterOrDigit(character))
                    continue;

                initials.Add(char.ToUpperInvariant(character));
                takeNext = false;
                if (initials.Count == 2)
                    break;
            }

            if (initials.Count < 2)
            {
                foreach (var character in title)
                {
                    if (!char.IsLetterOrDigit(character) || initials.Contains(character))
                        continue;

                    initials.Add(char.ToUpperInvariant(character));
                    if (initials.Count == 2)
                        break;
                }
            }

            return initials.Count == 0 ? "♪" : new string(initials.ToArray());
        }

        private static DrawingColor HslToColor(float hue, float saturation, float lightness)
        {
            hue = (hue % 360 + 360) % 360;
            var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            var x = chroma * (1 - Math.Abs((hue / 60f % 2) - 1));
            var match = lightness - chroma / 2;
            var (r, g, b) = hue switch
            {
                < 60 => (chroma, x, 0f),
                < 120 => (x, chroma, 0f),
                < 180 => (0f, chroma, x),
                < 240 => (0f, x, chroma),
                < 300 => (x, 0f, chroma),
                _ => (chroma, 0f, x)
            };
            return DrawingColor.FromArgb(255,
                (int)Math.Clamp((r + match) * 255, 0, 255),
                (int)Math.Clamp((g + match) * 255, 0, 255),
                (int)Math.Clamp((b + match) * 255, 0, 255));
        }

        public void Dispose()
        {
            // 진행 중인 로딩이 끝나며 Release 할 수 있으므로 _loadGate 는 해제하지 않습니다.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                ClearCache();
        }

        private const uint COINIT_APARTMENTTHREADED = 0x2;
        private const uint COINIT_DISABLE_OLE1DDE = 0x4;

        [ComImport]
        [Guid("BCC18B79-BA16-442F-80C4-8A59EA479BDB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(ShellSize size, ShellImageFlags flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShellSize
        {
            public int cx;
            public int cy;
        }

        [Flags]
        private enum ShellImageFlags : uint
        {
            BiggerSizeOk = 0x1,
            ThumbnailOnly = 0x8
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
