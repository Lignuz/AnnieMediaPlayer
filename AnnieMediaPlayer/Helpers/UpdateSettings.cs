using System.IO;
using System.Text.Json;

namespace AnnieMediaPlayer
{
    // 업데이트 확인·설치 주소. 저장소에는 포함하지 않고, 실행 파일 옆의 update-settings.json 에서 읽습니다.
    // 파일이 없거나 주소가 올바르지 않으면 null 이며, 이때는 실제 서버에 접속하지 않습니다.
    internal sealed class UpdateSettings
    {
        private const string FileName = "update-settings.json";

        public static UpdateSettings? Current { get; } = Load();

        public string ManifestUrl { get; }
        public string InstallerUrl { get; }

        private UpdateSettings(string manifestUrl, string installerUrl)
        {
            ManifestUrl = manifestUrl;
            InstallerUrl = installerUrl;
        }

        private static UpdateSettings? Load()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, FileName);
                if (!File.Exists(path))
                    return null;

                var values = JsonSerializer.Deserialize<Values>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!IsHttps(values?.ManifestUrl) || !IsHttps(values?.InstallerUrl))
                    return null;

                return new UpdateSettings(values!.ManifestUrl!, values.InstallerUrl!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                PlayerDiagnostics.Write($"Update settings could not be read: {ex.Message}");
                return null;
            }
        }

        private static bool IsHttps(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

        private sealed class Values
        {
            public string? ManifestUrl { get; set; }
            public string? InstallerUrl { get; set; }
        }
    }
}
