using System.Diagnostics;
using System.IO;
using System.Text;

namespace AnnieMediaPlayer
{
    internal static class PlayerDiagnostics
    {
        private const long MaximumLogSize = 2 * 1024 * 1024;
        private static readonly object SyncRoot = new();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AnnieMediaPlayer");
        private static readonly string LogFilePath = Path.Combine(LogDirectory, "player.log");

        public static string LogPath => LogFilePath;

        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);

                    if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length >= MaximumLogSize)
                        File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);

                    var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                        $"[P{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, line, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"플레이어 진단 로그 기록 실패: {ex.Message}");
            }
        }
    }
}
