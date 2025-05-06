using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            FFmpegLoader.RegisterFFmpeg();
            SpeedController.UpdateSpeedLabel(this);

            VideoPlayerController.Stop(this);
            PlayPauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => KeyboardInputHandler.HandleKeyDown(this, e);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => TitleBarController.MouseLeftButtonDown(this, e);
        private void TitleBar_MouseMove(object sender, MouseEventArgs e) => TitleBarController.MouseMove(this, e);
        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TitleBarController.MouseLeftButtonUp();

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e) => this.WindowState = this.WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e) => ThemeToggleController.ToggleTheme(this);

        // 리사이즈 핸들링
        private void TopResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Top(e, this);
        private void BottomResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Bottom(e, this);
        private void LeftResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Left(e, this);
        private void RightResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.Right(e, this);

        private void TopLeftResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.TopLeft(e, this);
        private void TopRightResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.TopRight(e, this);
        private void BottomLeftResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.BottomLeft(e, this);
        private void BottomRightResizeBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => WindowResizeHelper.BottomRight(e, this);
    }
}
