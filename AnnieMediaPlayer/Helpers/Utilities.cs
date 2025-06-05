using System.Diagnostics;
using System.Windows;

namespace AnnieMediaPlayer
{
    internal static class Utilities
    {
        /// <summary>
        /// 원본 크기를 지정된 최대 크기 안에 들어가는 원본 비율에 맞는 크기로 조정합니다.
        /// </summary>
        /// <param name="originalWidth">원본 너비</param>
        /// <param name="originalHeight">원본 높이</param>
        /// <param name="maxWidth">최대 너비</param>
        /// <param name="maxHeight">최대 높이</param>
        /// <returns>조정된 크기</returns>
        public static Size GetScaledSize(int originalWidth, int originalHeight, int maxWidth, int maxHeight)
        {
            if (originalWidth <= 0 || originalHeight <= 0 || maxWidth <= 0 || maxHeight <= 0)
            {
                return new Size(0, 0); // 유효하지 않은 입력
            }

            double widthRatio = (double)maxWidth / originalWidth;
            double heightRatio = (double)maxHeight / originalHeight;

            double scaleFactor = Math.Min(widthRatio, heightRatio);

            int scaledWidth = (int)(originalWidth * scaleFactor);
            int scaledHeight = (int)(originalHeight * scaleFactor);

            return new Size(scaledWidth, scaledHeight);
        }

        /// <summary>
        /// TimeSpan 값을 분:초 또는 시:분:초 형식으로 변환합니다.
        /// 시간이 0보다 크면 시:분:초 형식으로, 그렇지 않으면 분:초 형식으로 표시합니다.
        /// </summary>
        /// <param name="timeSpan">변환할 TimeSpan 값</param>
        /// <returns>형식화된 시간 문자열</returns>
        public static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.Hours > 0)
            {
                return timeSpan.ToString("hh\\:mm\\:ss");
            }
            else
            {
                return timeSpan.ToString("mm\\:ss");
            }
        }

        /// <summary>
        /// TimeSpan 값을 항상 시:분:초 형식으로 변환합니다.
        /// </summary>
        /// <param name="timeSpan">변환할 TimeSpan 값</param>
        /// <returns>형식화된 시간 문자열 (시:분:초)</returns>
        public static string FormatTimeSpanWithHours(TimeSpan timeSpan)
        {
            return timeSpan.ToString("hh\\:mm\\:ss");
        }

        /// <summary>
        /// TimeSpan 값을 항상 분:초 형식으로 변환합니다.
        /// </summary>
        /// <param name="timeSpan">변환할 TimeSpan 값</param>
        /// <returns>형식화된 시간 문자열 (분:초)</returns>
        public static string FormatTimeSpanWithoutHours(TimeSpan timeSpan)
        {
            return timeSpan.ToString("mm\\:ss");
        }

        // 브라우저로 웹페이지를 호출합니다. 
        public static void OpenWebPage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // 예외 처리
                Console.WriteLine($"Failed to open web page: {ex.Message}");
            }
        }

        // 다음 값 가져오기
        public static T GetNextEnumValue<T>(T value, bool wrap = true) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int index = Array.IndexOf(values, value);
            if (index < 0) return value; // not found
            int nextIndex = index + 1;

            if (nextIndex >= values.Length)
                return wrap ? values[0] : values[^1];

            return values[nextIndex];
        }

        // 이전 값 가져오기
        public static T GetPreviousEnumValue<T>(T value, bool wrap = true) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int index = Array.IndexOf(values, value);
            if (index < 0) return value; // not found
            int prevIndex = index - 1;

            if (prevIndex < 0)
                return wrap ? values[^1] : values[0];

            return values[prevIndex];
        }

    }
}
