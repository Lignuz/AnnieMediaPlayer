using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AnnieMediaPlayer
{
    //////////////////////////////////////////////////////
    // WindowStyle="None" 에 대한 수동 윈도우 크기 조절 //
    //////////////////////////////////////////////////////

    public static class WindowResizeHelper
    {
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private static void ResizeWindow(Window window, int direction)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)direction, IntPtr.Zero);
        }

        public static void Top(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTTOP);
        public static void Bottom(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTBOTTOM);
        public static void Left(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTLEFT);
        public static void Right(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTRIGHT);

        public static void TopLeft(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTTOPLEFT);
        public static void TopRight(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTTOPRIGHT);
        public static void BottomLeft(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTBOTTOMLEFT);
        public static void BottomRight(MouseButtonEventArgs e, Window window) => ResizeWindow(window, HTBOTTOMRIGHT);
    }
}