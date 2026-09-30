using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AnnieMediaPlayer
{
    public static class TitleBarController
    {
        private static Point _mouseDownPoint;
        private static bool _isDragging;
        private static bool _dragStarted;
        private static MouseButton _dragButton;
        private static UIElement? _capturedElement;

        public static void MouseLeftButtonDown(MainWindow window, MouseButtonEventArgs e)
        {
            MouseButtonDown(window, e, true);
        }

        public static void MouseRightButtonDown(MainWindow window, MouseButtonEventArgs e)
        {
            MouseButtonDown(window, e, false);
        }

        private static void MouseButtonDown(MainWindow window, MouseButtonEventArgs e, bool allowDoubleClick)
        {
            if (FindParent<ButtonBase>(e.OriginalSource as DependencyObject) != null)
                return;

            if (allowDoubleClick && e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                EndDrag();
                ToggleWindowState(window);
                e.Handled = true;
            }
            else if (e.ButtonState == MouseButtonState.Pressed &&
                     (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Right))
            {
                _mouseDownPoint = e.GetPosition(window);
                _dragButton = e.ChangedButton;
                _isDragging = true;
                _dragStarted = false;
                _capturedElement = window;
                Mouse.Capture(_capturedElement);
                e.Handled = true;
            }
        }

        public static void MouseMove(MainWindow window, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            var buttonState = _dragButton == MouseButton.Left ? e.LeftButton : e.RightButton;
            if (buttonState != MouseButtonState.Pressed)
            {
                EndDrag();
                return;
            }

            Point currentPoint = e.GetPosition(window);
            double deltaX = Math.Abs(currentPoint.X - _mouseDownPoint.X);
            double deltaY = Math.Abs(currentPoint.Y - _mouseDownPoint.Y);

            if (!_dragStarted &&
                deltaX <= SystemParameters.MinimumHorizontalDragDistance &&
                deltaY <= SystemParameters.MinimumVerticalDragDistance)
                return;

            // PointToScreen 은 물리 픽셀을 돌려주므로, Left/Top 과 같은 DIP 단위로 바꿔서 씁니다.
            var screenPoint = window.PointToScreen(currentPoint);
            if (PresentationSource.FromVisual(window)?.CompositionTarget is { } target)
                screenPoint = target.TransformFromDevice.Transform(screenPoint);

            if (!_dragStarted)
            {
                _dragStarted = true;

                if (window.WindowState == WindowState.Maximized)
                {
                    var horizontalRatio = window.ActualWidth > 0 ? currentPoint.X / window.ActualWidth : 0.5;
                    var verticalRatio = window.ActualHeight > 0 ? currentPoint.Y / window.ActualHeight : 0.5;
                    var pointerOffset = new Point(window.Width * horizontalRatio, window.Height * verticalRatio);

                    window.WindowState = WindowState.Normal;
                    _mouseDownPoint = pointerOffset;
                }
            }

            window.Left = screenPoint.X - _mouseDownPoint.X;
            window.Top = screenPoint.Y - _mouseDownPoint.Y;
        }

        public static void MouseLeftButtonUp()
        {
            EndDrag();
        }

        public static void MouseRightButtonUp()
        {
            EndDrag();
        }

        public static void Cancel()
        {
            EndDrag();
        }

        private static void EndDrag()
        {
            _isDragging = false;
            _dragStarted = false;

            if (_capturedElement != null && ReferenceEquals(Mouse.Captured, _capturedElement))
                _capturedElement.ReleaseMouseCapture();

            _capturedElement = null;
        }

        private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T match)
                    return match;

                element = element is Visual || element is Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : null;
            }

            return null;
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
