using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AnnieMediaPlayer.Options;
using AnnieMediaPlayer.Windows.Settings;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : BaseWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            FFmpegLoader.RegisterFFmpeg();
            SpeedController.UpdateSpeedLabel(this);

            VideoPlayerController.Stop(this);
            PlayPauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;

            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            this.BeginAnimation(Window.OpacityProperty, fadeIn);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            VideoPlayerController.Stop(this);
        }

        private void OpenVideo_Click(object sender, RoutedEventArgs e) => VideoPlayerController.OpenVideo(this);
        private void PlayPause_Click(object sender, RoutedEventArgs e) => VideoPlayerController.TogglePlayPause(this);
        private void Stop_Click(object sender, RoutedEventArgs e) => VideoPlayerController.Stop(this);

        private void SpeedDown_Click(object sender, RoutedEventArgs e) => SpeedController.SpeedDown(this);
        private void SpeedUp_Click(object sender, RoutedEventArgs e) => SpeedController.SpeedUp(this);

        private void PlaybackSlider_PreviewMouseMove(object sender, MouseEventArgs e) => VideoPlayerController.OnSliderMouseHover(this, e);
        private void PlaybackSlider_MouseLeave(object sender, MouseEventArgs e) => VideoPlayerController.OnSliderMouseLeave(this, e);
        private void PlaybackSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => VideoPlayerController.OnSliderDragStart(this);
        private void PlaybackSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e) => VideoPlayerController.OnSliderDragEnd(this);
        private void PlaybackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => VideoPlayerController.OnSliderValueChanged(this);

        private void Window_StateChanged(object sender, EventArgs e) => UpdateMaxRestoreButton();

        private void UpdateMaxRestoreButton()
        {
            MaxRestoreButton.Content = (Geometry)FindResource(
                this.WindowState == WindowState.Maximized ? "RestoreIconData" : "MaximizeIconData");
        }

        private void Window_LocationChanged(object sender, EventArgs e) => MonitorSnapState();
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => MonitorSnapState();

        void MonitorSnapState()
        {
            bool isDragging = Mouse.LeftButton == MouseButtonState.Pressed;
            IsSnapped = isDragging ? false : IsSnappedLikeWindows11(this);
        }

        private bool IsSnappedLikeWindows11(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var workArea = screen.WorkingArea;
            var dpiX = VisualTreeHelper.GetDpi(window).DpiScaleX;
            var dpiY = VisualTreeHelper.GetDpi(window).DpiScaleY;

            double left = window.Left;
            double top = window.Top;
            double width = window.Width;
            double height = window.Height;

            // DPI 보정
            var scaledWorkArea = new Rect(
                workArea.Left / dpiX,
                workArea.Top / dpiY,
                workArea.Width / dpiX,
                workArea.Height / dpiY
            );

            var tolerance = 10.0;
            var isNearWorkArea = Math.Abs(left - scaledWorkArea.Left) < tolerance ||
                                 Math.Abs(top - scaledWorkArea.Top) < tolerance ||
                                 Math.Abs((left + width) - (scaledWorkArea.Right)) < tolerance ||
                                 Math.Abs((top + height) - (scaledWorkArea.Bottom)) < tolerance;

            return isNearWorkArea;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => KeyboardInputHandler.HandleKeyDown(this, e);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => TitleBarController.MouseLeftButtonDown(this, e);
        private void TitleBar_MouseMove(object sender, MouseEventArgs e) => TitleBarController.MouseMove(this, e);
        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TitleBarController.MouseLeftButtonUp();

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e) => this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.ShowDialog();
        }
        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e) => OptionViewModel.Instance.ToggleTheme();

        // 리사이즈 핸들링
        private void TopResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Top(e, this);
        private void BottomResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Bottom(e, this);
        private void LeftResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Left(e, this);
        private void RightResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Right(e, this);

        private void TopLeftResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.TopLeft(e, this);
        private void TopRightResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.TopRight(e, this);
        private void BottomLeftResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.BottomLeft(e, this);
        private void BottomRightResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.BottomRight(e, this);

        // 테마가 변경됨
        private async void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            if (!ThemeToggleButton.IsEnabled)
                return;

            ThemeToggleButton.IsEnabled = false;

            // 회전 애니메이션
            var rotateAnim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseInOut }
            };
            ThemeToggleRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

            // Scale 애니메이션
            var scale = new ScaleTransform(1, 1);
            ThemeToggleButton.LayoutTransform = scale;
            var scaleDown = new DoubleAnimation(1.0, 0.85, TimeSpan.FromMilliseconds(100));
            var scaleUp = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(300)
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
            await Task.Delay(200);
            ThemeToggleButton.Content = FindResource(ThemeManager.theme == Options.Themes.Dark ? "ThemeDarkIconData" : "ThemeLightIconData");

            await Task.Delay(300);
            ThemeToggleButton.IsEnabled = true;
        }
    }
}
