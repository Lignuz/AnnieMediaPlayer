using System.Windows;
using System.Windows.Input;

namespace AnnieMediaPlayer
{
    public static class TitleBarController
    {
        private static Point _mouseDownPoint;
        private static bool _isDragging = false;

        public static void MouseLeftButtonDown(MainWindow window, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState(window);
            }
            else if (e.ButtonState == MouseButtonState.Pressed)
            {
                if (window.WindowState == WindowState.Maximized)
                {
                    _mouseDownPoint = e.GetPosition(window);
                    _isDragging = true;
                }
                window.DragMove();
            }
        }

        public static void MouseMove(MainWindow window, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(window);
                double deltaX = Math.Abs(currentPoint.X - _mouseDownPoint.X);
                double deltaY = Math.Abs(currentPoint.Y - _mouseDownPoint.Y);

                if (deltaX > SystemParameters.MinimumHorizontalDragDistance || deltaY > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = false;

                    if (window.WindowState == WindowState.Maximized)
                    {
                        var mouseX = Mouse.GetPosition(window).X;
                        var percent = mouseX / window.ActualWidth;

                        window.WindowState = WindowState.Normal;
                        window.Left = SystemParameters.WorkArea.Width * percent - (window.Width / 2);
                        window.Top = 0;
                    }

                    window.DragMove();
                }
            }
        }

        public static void MouseLeftButtonUp()
        {
            _isDragging = false;
        }

        private static void ToggleWindowState(Window window)
        {
            if (window.WindowState == WindowState.Normal)
                window.WindowState = WindowState.Maximized;
            else
                window.WindowState = WindowState.Normal;
        }
    }
}