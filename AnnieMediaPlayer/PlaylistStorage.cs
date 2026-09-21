using System.IO;
using System.Text.Json;

namespace AnnieMediaPlayer
{
    internal static class PlaylistStorage
    {
        private static readonly string PlaylistFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AnnieMediaPlayer", "playlist.json");

        public static IReadOnlyList<string> Load()
        {
            try
            {
                if (!File.Exists(PlaylistFilePath))
                    return Array.Empty<string>();

                var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PlaylistFilePath));
                return paths is null ? Array.Empty<string>() : paths;
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Playlist load failed: {ex}");
                return Array.Empty<string>();
            }
        }

        public static void Save(IEnumerable<string> filePaths)
        {
            string? temporaryPath = null;
            try
            {
                var directory = Path.GetDirectoryName(PlaylistFilePath)!;
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $"playlist.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(filePaths));
                File.Move(temporaryPath, PlaylistFilePath, true);
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Playlist save failed: {ex}");
            }
            finally
            {
                if (temporaryPath != null && File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
