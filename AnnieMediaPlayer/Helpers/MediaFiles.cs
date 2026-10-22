using System.IO;
using System.Runtime.InteropServices;

namespace AnnieMediaPlayer
{
    // 끌어 놓기·명령줄로 받은 경로를 재생목록에 넣을 파일 목록으로 바꿉니다.
    internal static class MediaFiles
    {
        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".3gp",
            ".mp3", ".wav", ".flac", ".m4a", ".m4b", ".aac", ".ogg", ".oga", ".opus", ".wma", ".aiff", ".aif", ".ape",
        };

        private static readonly EnumerationOptions FolderEnumeration = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        public static bool IsMediaFile(string path) => Extensions.Contains(Path.GetExtension(path));

        // 파일은 형식과 관계없이 그대로 사용하고(파일 열기의 '모든 파일'과 같음), 폴더는 하위 폴더까지 미디어 파일만
        // 탐색기와 같은 이름순으로 모읍니다. 폴더가 클 수 있으므로 백그라운드에서 호출합니다.
        public static List<string> Expand(IEnumerable<string> paths)
        {
            var files = new List<string>();
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    files.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    var folderFiles = Directory.EnumerateFiles(path, "*", FolderEnumeration).Where(IsMediaFile).ToList();
                    folderFiles.Sort(StrCmpLogicalW);
                    files.AddRange(folderFiles);
                }
            }

            return files;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string first, string second);
    }
}
