using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace AnnieMediaPlayer
{
    public enum UpdateCheckState
    {
        None,           // 해당사항 없음
        CheckUpdate,    // 업데이트 확인중
        Available,      // 업데이트 있음
        NotAvailable,   // 업데이트 없음
        Error,          // 업데이트 확인 실패
    }

    public class JsonInfo
    {
        public string? Version { get; set; }
        public string? ReleaseNotes { get; set; }
    }

    public class UpdaterViewModel : ViewModelBase
    {
        private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30); // 이 시간 동안 데이터가 오지 않으면 다운로드를 중단합니다.
        private static readonly TimeSpan MockCheckDelay = TimeSpan.FromSeconds(3);
        private const long MaximumInstallerBytes = 512L * 1024 * 1024; // 이보다 큰 파일은 설치 파일로 보지 않습니다.

        private HttpClient httpClient;

        public UpdaterViewModel()
        {
            httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            httpClient.DefaultRequestHeaders.Add("User-Agent", "AMPUpdater/1.0");

            UpdateCheckState = UpdateCheckState.None;
            ProgressVisibility = Visibility.Hidden;
            ProgressValue = 0.0;
            IsProgressIndeterminate = false;
            ServerVersion = string.Empty;
            ReleaseNote = string.Empty;
            ErrorMessage = string.Empty;
        }

        // 업데이트 주소가 설정되어 있어야 실제로 서버에 확인합니다.
        public static bool IsConfigured => UpdateSettings.Current is not null;

        public async Task CheckUpdateAsync()
        {
            UpdateCheckState = UpdateCheckState.CheckUpdate;

            var currentVersion = AppVersionHelper.Version;
            try
            {
                (ServerVersion, ReleaseNote) = await CheckServerVersionAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                UpdateCheckState = UpdateCheckState.Error;
                return;
            }

            var ret = AppVersionHelper.CompareVersion(currentVersion, ServerVersion);
            if (ret == VersionCompareResult.UpToDate)
            {
                UpdateCheckState = UpdateCheckState.NotAvailable;
            }
            else if (ret == VersionCompareResult.UpdateAvailable)
            {
                UpdateCheckState = UpdateCheckState.Available;
            }
            else
            {
                UpdateCheckState = UpdateCheckState.Error;
            }
        }

        private async Task<(string, string)> CheckServerVersionAsync()
        {
            var settings = UpdateSettings.Current;
            if (settings is null)
            {
                // 주소가 설정되어 있지 않으면 서버에 접속하지 않고, 확인하는 것처럼 보여 준 뒤 최신 버전으로 처리합니다.
                await Task.Delay(MockCheckDelay);
                return (AppVersionHelper.Version, string.Empty);
            }

            var releaseNote = string.Empty;
            var serverVersion = string.Empty;
            var localVersion = AppVersionHelper.Version;
#if DEBUG
            localVersion += "d";
#endif
            var separator = settings.ManifestUrl.Contains('?') ? "&" : "?";
            string url = $"{settings.ManifestUrl}{separator}version={Uri.EscapeDataString(localVersion)}";

            using var timeout = new CancellationTokenSource(CheckTimeout);
            using var httpResponse = await httpClient.GetAsync(url, timeout.Token);
            httpResponse.EnsureSuccessStatusCode();
            EnsureHttps(httpResponse);
            var response = await httpResponse.Content.ReadAsStringAsync(timeout.Token);

            var jsonInfo = JsonSerializer.Deserialize<JsonInfo>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (jsonInfo != null)
            {
                serverVersion = jsonInfo.Version ?? string.Empty;
                releaseNote = jsonInfo.ReleaseNotes ?? string.Empty;
            }
            return (serverVersion, releaseNote);
        }


        public async Task DownloadAndInstallUpdateAsync()
        {
            var settings = UpdateSettings.Current;
            if (settings is null)
                return;

            ErrorMessage = string.Empty;
            string url = settings.InstallerUrl;

            // 임시 폴더에 파일 다운로드
            string tempPath = Path.GetTempPath();
            string fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "AnnieMediaPlayerSetup.exe";
            string filePath = Path.Combine(tempPath, fileName);

            var downloaded = false;
            try
            {
                using var stall = new CancellationTokenSource(StallTimeout);
                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token))
                {
                    response.EnsureSuccessStatusCode();
                    EnsureHttps(response);

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    if (totalBytes > MaximumInstallerBytes)
                        throw new IOException("The installer exceeds the permitted download size.");

                    ProgressValue = 0.0;
                    ProgressVisibility = Visibility.Visible;
                    IsProgressIndeterminate = totalBytes <= 0; // 파일 크기를 모르면 진행률 대신 막대를 움직입니다.
                    var downloadedBytes = 0L;

                    int bufsize = 8192; // 8KB 버퍼 크기
                    using (var contentStream = await response.Content.ReadAsStreamAsync(stall.Token))
                    using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufsize, true))
                    {
                        var buffer = new byte[bufsize];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, stall.Token)) > 0)
                        {
                            stall.CancelAfter(StallTimeout);
                            downloadedBytes += bytesRead;
                            if (downloadedBytes > MaximumInstallerBytes)
                                throw new IOException("The installer exceeds the permitted download size.");

                            await fileStream.WriteAsync(buffer, 0, bytesRead, stall.Token);

                            if (totalBytes > 0)
                            {
                                var progress = (double)downloadedBytes / totalBytes * 100;
                                ProgressValue = progress;
                            }
                        }
                    }
                }

                downloaded = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // 다운로드 실패 메시지 표시
                ErrorMessage = LanguageManager.GetResourceString("Text.Info.Version.Update.Download.Failed");
            }

            if (!downloaded)
            {
                ProgressValue = 0.0;
                IsProgressIndeterminate = false;
                ProgressVisibility = Visibility.Hidden;
                TryDeleteFile(filePath);
                return;
            }

            // 현재 앱 종료 후 업데이트 설치
            InstallUpdateAsync(filePath);
        }

        private void InstallUpdateAsync(string updateFilePath)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(updateFilePath)
                {
                    UseShellExecute = true, // 중요: Verb 속성을 사용하려면 true여야 합니다. .NET Core에서는 기본값이 false로 변경되었습니다.
                    Verb = "runas",         // "runas" 동사는 관리자 권한으로 실행하도록 합니다.
                };
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Windows did not start the installer.");

                App.Current.MainWindow.Close(); // 업데이트 호출 후 애플리케이션 종료
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                TryDeleteFile(updateFilePath);
                ProgressVisibility = Visibility.Hidden;
                IsProgressIndeterminate = false;
                ProgressValue = 0.0;

                // 사용자가 UAC 프롬프트를 취소했을 때 발생합니다.
                if (ex is Win32Exception { NativeErrorCode: 1223 }) // ERROR_CANCELLED
                {
                    ErrorMessage = LanguageManager.GetResourceString("Text.Info.Version.Update.Execute.Admin.Failed");
                }
                else
                {
                    ErrorMessage = LanguageManager.GetResourceString("Text.Info.Version.Update.Execute.Failed");
                }
            }
        }

        // 서버가 다른 주소로 넘겨 보냈더라도 최종 응답이 HTTPS 인 경우에만 사용합니다.
        private static void EnsureHttps(HttpResponseMessage response)
        {
            if (response.RequestMessage?.RequestUri is not { Scheme: "https" })
                throw new HttpRequestException("The response was not served over HTTPS.");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        public UpdateCheckState UpdateCheckState { get => Get(); private set => Set(value); }
        public Visibility ProgressVisibility { get => Get(); private set => Set(value); }
        public double ProgressValue { get => Get(); set => Set(value); }
        public bool IsProgressIndeterminate { get => Get(); private set => Set(value); }
        public string ServerVersion { get => Get(); private set => Set(value); }
        public string ReleaseNote { get => Get(); set => Set(value); }
        public string ErrorMessage { get => Get(); private set => Set(value); }
    }
}
