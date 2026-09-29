using System.Runtime.InteropServices;
using System.Text;

namespace AnnieMediaPlayer
{
    // 태그 문자열 보정: 옛 MP3 태그(ID3)는 인코딩을 ISO-8859-1 로 표시해 두고 실제로는 시스템 코드 페이지
    // (한글 Windows 는 CP949)로 저장한 경우가 많습니다. FFmpeg 는 표시된 대로 읽어 "´«, ÄÚ" 처럼 깨지므로,
    // Windows 탐색기와 같이 원래 바이트를 시스템 코드 페이지로 다시 읽습니다.
    internal static class MetadataText
    {
        private static readonly Encoding? LegacyEncoding = CreateLegacyEncoding();

        public static string Repair(string text)
        {
            if (LegacyEncoding is null)
                return text;

            // ISO-8859-1 로 읽힌 문자열은 모든 글자가 U+00FF 이하입니다. 확장 문자(U+0080 이상)가 없으면 보정할 것이 없습니다.
            var hasExtended = false;
            foreach (var c in text)
            {
                if (c > 'ÿ')
                    return text;
                if (c >= '\u0080')
                    hasExtended = true;
            }

            if (!hasExtended)
                return text;

            string decoded;
            try
            {
                decoded = LegacyEncoding.GetString(Encoding.Latin1.GetBytes(text));
            }
            catch (DecoderFallbackException)
            {
                return text; // 시스템 코드 페이지의 바이트 배열이 아니면 원래 문자열(서유럽 문자 등)입니다.
            }

            // 서유럽 문자로 된 제목을 잘못 바꾸지 않도록, ASCII 가 아닌 글자가 모두 한글·한자·가나로 풀릴 때만 바꿉니다.
            foreach (var c in decoded)
            {
                if (c >= '\u0080' && !IsCjk(c))
                    return text;
            }

            return decoded;
        }

        // 시스템 코드 페이지가 한글·일본어·중국어처럼 2바이트 문자를 쓰는 경우에만 보정합니다.
        private static Encoding? CreateLegacyEncoding()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var encoding = Encoding.GetEncoding((int)GetACP(), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                return encoding.IsSingleByte ? null : encoding;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        private static bool IsCjk(char c) =>
            c is >= 'ᄀ' and <= 'ᇿ'   // 한글 자모
            or >= '　' and <= 'ヿ'     // CJK 기호·가나
            or >= '㄰' and <= '㆏'     // 한글 호환 자모
            or >= '一' and <= '鿿'     // 한자
            or >= '가' and <= '힣'     // 한글 음절
            or >= '豈' and <= '﫿'     // 호환 한자
            or >= '＀' and <= '￯';    // 전각 문자

        [DllImport("kernel32.dll")]
        private static extern uint GetACP();
    }
}
