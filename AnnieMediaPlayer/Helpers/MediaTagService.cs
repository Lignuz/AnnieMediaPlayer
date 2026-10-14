using System.Globalization;
using System.IO;

namespace AnnieMediaPlayer
{
    internal sealed record MediaTagValues(string Title, string Artists, string AlbumArtists, string Album,
        string Year, string Track, string Genres, string Comment, string Lyrics);

    internal sealed record MediaTagSnapshot(MediaTagValues Values, long Length, DateTime LastWriteUtc);
    internal sealed record MediaProbe(string Format, int StreamCount, TimeSpan Duration);
    internal enum ArtworkEdit { Keep, Replace, Remove }

    internal static class MediaTagService
    {
        // 영상 파일은 태그를 쓰면서 영상 데이터 위치가 어긋나 재생할 수 없게 될 수 있어 오디오 형식만 편집합니다.
        private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".m4b", ".wma", ".wav", ".aif", ".aiff" };

        public static bool SupportsEditing(string path) => EditableExtensions.Contains(Path.GetExtension(path));

        public static MediaTagSnapshot Read(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("The file is read-only.");
            var before = new FileInfo(path);
            var length = before.Length;
            var lastWrite = before.LastWriteTimeUtc;
            using var file = TagLib.File.Create(path);
            if (!file.Writeable || file.PossiblyCorrupt)
                throw new NotSupportedException("The file cannot be safely edited.");
            var tag = file.Tag;
            var snapshot = new MediaTagSnapshot(ReadValues(tag), length, lastWrite);
            EnsureUnchanged(path, snapshot);
            return snapshot;
        }

        public static async Task SaveAsync(string path, MediaTagSnapshot original, MediaTagValues values,
            ArtworkEdit artworkEdit, byte[]? pictureBytes, Func<bool> isFileInUse, CancellationToken cancellationToken)
        {
            values = values with
            {
                Year = Number(ValidateNumber(values.Year)), Track = Number(ValidateNumber(values.Track)),
                Artists = values.Artists == original.Values.Artists ? values.Artists : string.Join("; ", Split(values.Artists)),
                AlbumArtists = values.AlbumArtists == original.Values.AlbumArtists ? values.AlbumArtists : string.Join("; ", Split(values.AlbumArtists)),
                Genres = values.Genres == original.Values.Genres ? values.Genres : string.Join("; ", Split(values.Genres))
            };
            if (!SupportsEditing(path)) throw new NotSupportedException("Unsupported tag format.");
            if (isFileInUse()) throw new InvalidOperationException("The file is in use by the player.");
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("The file is read-only.");
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var temporary = Path.Combine(directory, $".annie-edit-{Guid.NewGuid():N}{Path.GetExtension(path)}");
            try
            {
                // 원본 대신 같은 폴더의 복사본을 수정하고, 저장된 값을 다시 읽어 확인합니다.
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureUnchanged(path, original);
                    var originalMedia = Probe(path);
                    File.Copy(path, temporary, false);
                    using (var file = TagLib.File.Create(temporary))
                    {
                        if (!file.Writeable || file.PossiblyCorrupt) throw new NotSupportedException("The file cannot be safely edited.");
                        ApplyValues(file.Tag, original.Values, values);
                        if (artworkEdit == ArtworkEdit.Remove) file.Tag.Pictures = Array.Empty<TagLib.IPicture>();
                        else if (artworkEdit == ArtworkEdit.Replace)
                        {
                            if (pictureBytes is null || pictureBytes.Length == 0 || pictureBytes.Length > 16 * 1024 * 1024)
                                throw new ArgumentException("Invalid artwork size.");
                            var picture = new TagLib.Picture(new TagLib.ByteVector(pictureBytes)) { Type = TagLib.PictureType.FrontCover };
                            if (picture.MimeType is not ("image/jpeg" or "image/png")) throw new NotSupportedException("Use a JPEG or PNG image.");
                            file.Tag.Pictures = new TagLib.IPicture[] { picture }
                                .Concat(file.Tag.Pictures.Where(p => p.Type != TagLib.PictureType.FrontCover)).ToArray();
                        }
                        file.Save();
                    }
                    using var verified = TagLib.File.Create(temporary);
                    if (ReadValues(verified.Tag) != values)
                        throw new IOException("This format did not preserve the requested tag values.");
                    if (artworkEdit == ArtworkEdit.Remove && verified.Tag.Pictures.Length != 0 ||
                        artworkEdit == ArtworkEdit.Replace && !verified.Tag.Pictures.Any(p => p.Type == TagLib.PictureType.FrontCover && p.Data.Data.SequenceEqual(pictureBytes!)))
                        throw new IOException("This format did not preserve the requested artwork.");
                    // 태그 라이브러리가 읽을 수 있어도 재생에 쓰는 FFmpeg 가 같은 미디어로 열지 못하는 결과가 나올 수 있으므로,
                    // 원본과 같은 형식·스트림·길이로 열리는지 확인한 뒤에만 원본을 교체합니다.
                    if (!IsSameMedia(originalMedia, Probe(temporary)))
                        throw new IOException("The edited file did not open as the same media. The original file was not changed.");
                    cancellationToken.ThrowIfCancellationRequested();
                }, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                // UI 스레드에서 재생 연결을 다시 확인한 뒤, 중간 대기 없이 원본을 교체합니다.
                if (isFileInUse()) throw new InvalidOperationException("The file is in use by the player.");
                // 마지막 검사와 교체 사이에 다른 프로그램이 원본을 쓰지 못하게 합니다.
                using var originalHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                EnsureUnchanged(path, original);
                cancellationToken.ThrowIfCancellationRequested();
                File.Replace(temporary, path, null);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { PlayerDiagnostics.Write($"Tag temporary file cleanup failed: {ex.Message}"); }
                }
            }
        }

        private static MediaProbe Probe(string path)
        {
            if (!Unosquare.FFME.Library.IsInitialized)
                throw new NotSupportedException("The media library is not ready.");
            var info = Unosquare.FFME.Library.RetrieveMediaInfo(path);
            // 내장 앨범 이미지는 별도 스트림으로 잡히므로, 이미지를 추가·삭제해도 같은 미디어로 보도록 제외합니다.
            var streamCount = info.Streams.Values.Count(stream => (stream.Disposition & FFmpeg.AutoGen.ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0);
            return new MediaProbe(info.Format, streamCount, info.Duration);
        }

        private static bool IsSameMedia(MediaProbe original, MediaProbe edited)
        {
            // 길이를 알 수 없는 파일(음수 값)은 그 값이 같은지만 봅니다.
            var durationMatches = original.Duration < TimeSpan.Zero || edited.Duration < TimeSpan.Zero
                ? original.Duration == edited.Duration
                : (original.Duration - edited.Duration).Duration() <= TimeSpan.FromSeconds(Math.Max(0.5, original.Duration.TotalSeconds * 0.01));
            return original.Format == edited.Format && original.StreamCount == edited.StreamCount && durationMatches;
        }

        private static void EnsureUnchanged(string path, MediaTagSnapshot original)
        {
            var info = new FileInfo(path);
            if (info.Length != original.Length || info.LastWriteTimeUtc != original.LastWriteUtc)
                throw new IOException("The file changed after its tags were loaded. Reopen file information.");
        }

        private static MediaTagValues ReadValues(TagLib.Tag tag) => new(tag.Title ?? "", string.Join("; ", tag.Performers),
            string.Join("; ", tag.AlbumArtists), tag.Album ?? "", Number(tag.Year), Number(tag.Track),
            string.Join("; ", tag.Genres), tag.Comment ?? "", tag.Lyrics ?? "");

        private static string Number(uint value) => value == 0 ? "" : value.ToString(CultureInfo.InvariantCulture);
        private static uint ValidateNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number > 9999)
                throw new ArgumentException("Year and track must be between 0 and 9999.");
            return number;
        }

        private static string[] Split(string value) => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        private static void ApplyValues(TagLib.Tag tag, MediaTagValues original, MediaTagValues edited)
        {
            if (edited.Title != original.Title) tag.Title = edited.Title;
            if (edited.Artists != original.Artists) tag.Performers = Split(edited.Artists);
            if (edited.AlbumArtists != original.AlbumArtists) tag.AlbumArtists = Split(edited.AlbumArtists);
            if (edited.Album != original.Album) tag.Album = edited.Album;
            if (edited.Year != original.Year) tag.Year = ValidateNumber(edited.Year);
            if (edited.Track != original.Track) tag.Track = ValidateNumber(edited.Track);
            if (edited.Genres != original.Genres) tag.Genres = Split(edited.Genres);
            if (edited.Comment != original.Comment) tag.Comment = edited.Comment;
            if (edited.Lyrics != original.Lyrics) tag.Lyrics = edited.Lyrics;
        }
    }
}
