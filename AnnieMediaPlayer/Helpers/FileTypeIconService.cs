using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer.Helpers
{
    internal static class FileTypeIconService
    {
        private const uint FileAttributeNormal = 0x00000080;
        private const uint ShgfiIcon = 0x00000100;
        private const uint ShgfiSmallIcon = 0x00000001;
        private const uint ShgfiUseFileAttributes = 0x00000010;

        private static readonly Dictionary<string, BitmapSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object SyncRoot = new();

        public static BitmapSource? GetIcon(string filePath)
        {
            var extension = Path.GetExtension(filePath);

            lock (SyncRoot)
            {
                if (Icons.TryGetValue(extension, out var cachedIcon))
                    return cachedIcon;

                var icon = LoadIcon(extension);
                Icons.Add(extension, icon);
                return icon;
            }
        }

        private static BitmapSource? LoadIcon(string extension)
        {
            var fileName = string.IsNullOrEmpty(extension) ? "file" : $"file{extension}";
            var result = SHGetFileInfo(
                fileName,
                FileAttributeNormal,
                out var fileInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);

            if (result == IntPtr.Zero || fileInfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    fileInfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(16, 16));
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                DestroyIcon(fileInfo.hIcon);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string? szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string? szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            out SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
