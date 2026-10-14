using System.IO;
using System.Runtime.InteropServices;

namespace AnnieMediaPlayer
{
    internal static class FileExplorer
    {
        public static Task ShowFileAsync(string filePath) => Task.Run(() =>
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("The playlist file no longer exists.", fullPath);

            Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0));
            var item = IntPtr.Zero;
            try
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(fullPath, IntPtr.Zero, out item, 0, out _));
                // 파일의 전체 ID를 넘기면 부모 폴더가 열리고 해당 파일이 선택됩니다.
                Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0));
            }
            finally
            {
                if (item != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(item);
                CoUninitialize();
            }
        });

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributes, out uint resultAttributes);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoInitializeEx(IntPtr reserved, uint concurrencyModel);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern void CoUninitialize();
    }
}
