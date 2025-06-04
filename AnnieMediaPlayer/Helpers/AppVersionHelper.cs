using System.Reflection;

namespace AnnieMediaPlayer
{
    public enum VersionCompareResult
    {
        InvalidFormat,      // 버전 체크 오류
        UpToDate,           // 현재 버전이 최신
        UpdateAvailable     // 업데이트 가능
    }

    public static class AppVersionHelper
    {
        // 어플리케이션의 버전 정보
        public static string Version =>
            Assembly.GetEntryAssembly()?
                .GetName()
                .Version?
                .ToString() ?? "Unknown";

        // 버전 비교를 위한 메서드
        public static VersionCompareResult CompareVersion(string currentVersion, string latestVersion)
        {
            // 유효성 검사: 둘 다 null/empty 아니어야 함
            if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(latestVersion))
                return VersionCompareResult.InvalidFormat;

            // 버전 정보는 반드시 숫자 4개로 구성되어야 함
            string[] currentParts = currentVersion.Split('.');
            string[] latestParts = latestVersion.Split('.');

            if (currentParts.Length != 4 || latestParts.Length != 4)
                return VersionCompareResult.InvalidFormat;

            // 각 부분을 int로 파싱
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    int current = int.Parse(currentParts[i]);
                    int latest = int.Parse(latestParts[i]);

                    if (current < latest)
                        return VersionCompareResult.UpdateAvailable;
                    else if (current > latest)
                        return VersionCompareResult.UpToDate;
                    // 같으면 다음 자리 비교
                }
            }
            catch
            {
                return VersionCompareResult.InvalidFormat;
            }

            // 모든 자리가 동일한 경우
            return VersionCompareResult.UpToDate;
        }
    }
}
