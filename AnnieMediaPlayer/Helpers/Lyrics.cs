using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AnnieMediaPlayer
{
    // 가사 한 줄. 시간 정보가 없는 가사는 Time 이 null 입니다.
    public sealed record LyricLine(TimeSpan? Time, string Text);

    public sealed class Lyrics
    {
        public Lyrics(IReadOnlyList<LyricLine> lines, bool isSynced)
        {
            Lines = lines;
            IsSynced = isSynced;
        }

        public IReadOnlyList<LyricLine> Lines { get; }

        // 줄마다 시간 정보가 있어 재생 위치에 맞춰 따라갈 수 있는 가사인지
        public bool IsSynced { get; }

        // 재생 위치에 해당하는 줄의 번호. 첫 줄이 시작되기 전이거나 시간 정보가 없으면 -1 입니다.
        public int IndexAt(TimeSpan position)
        {
            if (!IsSynced)
                return -1;

            int low = 0, high = Lines.Count - 1, found = -1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                if (Lines[mid].Time <= position)
                {
                    found = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return found;
        }
    }

    public static class LyricsLoader
    {
        private const long MaximumFileSize = 1 << 20;
        private static readonly Regex WordTimeTag = new(@"<\d+:\d+(?:[.:]\d+)?>", RegexOptions.Compiled);
        private static readonly Regex TimeTag = new(@"^(\d+):(\d{1,2})(?:[.:](\d{1,3}))?$", RegexOptions.Compiled);

        static LyricsLoader()
        {
            // 한글 Windows 에서 만든 .lrc 파일(CP949 등)을 읽기 위해 코드 페이지 인코딩을 등록합니다.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        // 가사를 찾는 순서: 곡과 같은 이름의 .lrc 파일 → 곡 태그에 들어 있는 가사
        public static Lyrics? Load(string mediaPath, params IReadOnlyDictionary<string, string>?[] metadata)
        {
            try
            {
                var lrcPath = Path.ChangeExtension(mediaPath, ".lrc");
                if (File.Exists(lrcPath) && ReadText(lrcPath) is string fileText && Parse(fileText) is Lyrics fromFile)
                    return fromFile;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                PlayerDiagnostics.Write($"Lyrics file read failed: {ex.Message}");
            }

            foreach (var dictionary in metadata)
            {
                if (dictionary is null)
                    continue;

                foreach (var entry in dictionary)
                {
                    if (IsLyricsKey(entry.Key) && Parse(MetadataText.Repair(entry.Value)) is Lyrics fromTag)
                        return fromTag;
                }
            }

            return null;
        }

        // LRC 형식([mm:ss.xx] 가사)이면 시간 정보가 있는 가사로, 아니면 일반 텍스트 가사로 읽습니다.
        public static Lyrics? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var offset = TimeSpan.Zero;
            var timed = new List<LyricLine>();
            var plain = new List<LyricLine>();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var times = new List<TimeSpan>();
                var isMetaLine = false;
                var index = 0;

                // 줄 앞의 [..] 태그: 시간([01:23.45]) 또는 정보([ar:가수], [offset:+200])
                while (index < line.Length && line[index] == '[')
                {
                    var close = line.IndexOf(']', index);
                    if (close < 0)
                        break;

                    var tag = line.Substring(index + 1, close - index - 1).Trim();
                    if (TryParseTime(tag, out var time))
                    {
                        times.Add(time);
                    }
                    else if (tag.Length > 1 && char.IsLetter(tag[0]) && tag.Contains(':'))
                    {
                        isMetaLine = true;
                        if (tag.StartsWith("offset:", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(tag.AsSpan(7).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
                            offset = TimeSpan.FromMilliseconds(ms);
                    }
                    else
                    {
                        break;
                    }

                    index = close + 1;
                }

                if (times.Count > 0)
                {
                    // 확장 LRC 의 단어별 시간(<mm:ss.xx>)은 표시하지 않습니다.
                    var body = WordTimeTag.Replace(line.Substring(index), string.Empty).Trim();
                    foreach (var time in times)
                        timed.Add(new LyricLine(time, body));
                }
                else if (!isMetaLine)
                {
                    plain.Add(new LyricLine(null, line.Trim()));
                }
            }

            if (timed.Count > 0)
            {
                // offset 이 양수면 가사를 더 일찍 표시합니다.
                var lines = timed
                    .Select(l => l with { Time = l.Time - offset < TimeSpan.Zero ? TimeSpan.Zero : l.Time - offset })
                    .OrderBy(l => l.Time)
                    .ToList();
                return lines.Any(l => l.Text.Length > 0) ? new Lyrics(lines, true) : null;
            }

            // 앞뒤 빈 줄은 없애고, 이어진 빈 줄은 문단 구분으로 하나만 남깁니다.
            var paragraphs = new List<LyricLine>();
            foreach (var line in plain)
            {
                if (line.Text.Length == 0 && (paragraphs.Count == 0 || paragraphs[^1].Text.Length == 0))
                    continue;
                paragraphs.Add(line);
            }
            while (paragraphs.Count > 0 && paragraphs[^1].Text.Length == 0)
                paragraphs.RemoveAt(paragraphs.Count - 1);

            return paragraphs.Count > 0 ? new Lyrics(paragraphs, false) : null;
        }

        // FFmpeg 는 MP3 의 USLT 를 "lyrics-언어" 로, FLAC·OGG 의 가사를 "LYRICS"·"UNSYNCEDLYRICS" 로 읽습니다.
        private static bool IsLyricsKey(string key) =>
            key.Equals("lyrics", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("lyrics-", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("unsyncedlyrics", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("unsynced lyrics", StringComparison.OrdinalIgnoreCase);

        private static bool TryParseTime(string tag, out TimeSpan time)
        {
            time = default;
            var match = TimeTag.Match(tag);
            if (!match.Success)
                return false;

            var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var fraction = match.Groups[3].Value;
            var milliseconds = fraction.Length == 0 ? 0 :
                int.Parse(fraction, CultureInfo.InvariantCulture) * (fraction.Length switch { 1 => 100, 2 => 10, _ => 1 });
            time = new TimeSpan(0, 0, minutes, seconds, milliseconds);
            return true;
        }

        // BOM 이 있으면 그 인코딩으로, 없으면 UTF-8 로 읽고, UTF-8 이 아니면 시스템 기본 코드 페이지로 읽습니다.
        private static string? ReadText(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumFileSize)
                return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
            }
        }
    }
}
