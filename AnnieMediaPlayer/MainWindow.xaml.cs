using System.Windows;
using System.Windows.Controls;
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

            InitializeOverlayControls();
            OptionViewModel.Instance.UseOverlayControlChanged += UseOverlayControlChanged;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            VideoPlayerController.Stop(this);
        }

        // 기본영역 드래그로 이동 지원
        private void win_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void OpenVideo_Click(object sender, RoutedEventArgs e) => VideoPlayerController.OpenVideo(this);
        private void PlayPause_Click(object sender, RoutedEventArgs e) => VideoPlayerController.TogglePlayPause(this);
        private void Stop_Click(object sender, RoutedEventArgs e) => VideoPlayerController.Stop(this);

        private void SpeedDown_Click(object sender, RoutedEventArgs e) => SpeedController.SpeedDown(this);
        private void SpeedUp_Click(object sender, RoutedEventArgs e) => SpeedController.SpeedUp(this);

        private void PlaybackSlider_PreviewMouseMove(object sender, MouseEventArgs e) => VideoPlayerController.OnSliderMouseHover(this, e);
        private void PlaybackSlider_MouseLeave(object sender, MouseEventArgs e) => VideoPlayerController.OnSliderMouseLeave(this, e);
        private void PlaybackSlider_DragStateChanged(object sender, EventArgs e)
        {
            if (sender == PlaybackSlider)
            {
                if (PlaybackSlider.isDragging)
                    VideoPlayerController.OnSliderDragStart(this);
                else
                    VideoPlayerController.OnSliderDragEnd(this);
            }
        }
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

        private void BaseWindow_DragEnter(object sender, DragEventArgs e)
        {

        }

        private void BaseWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    // 비디오 파일 열기
                    string filePath = files[0];
                    VideoPlayerController.OpenVideo(this, filePath);
                }
            }
        }


        // 오버레이 컨트롤을 위한 타이머와 플래그
        private bool _isControlsVisible = false;
        private bool _isMouseOverControls = false;
        private System.Windows.Threading.DispatcherTimer _hideControlsTimer;

        // 오버레이를 위한 컨트롤 초기화 설정
        private void InitializeOverlayControls()
        {
            // 컨트롤 숨김 타이머 초기화
            _hideControlsTimer = new System.Windows.Threading.DispatcherTimer();
            _hideControlsTimer.Interval = TimeSpan.FromSeconds(0.5);
            _hideControlsTimer.Tick += HideControlsTimer_Tick;

            // 초기 상태 설정
            panel_control.Opacity = 1;
            panel_titlebar.Opacity = 1;
        }

        private void HideControlsTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isMouseOverControls)
            {
                HideControls();
            }
            _hideControlsTimer.Stop();
        }

        private void ShowControls()
        {
            if (!_isControlsVisible)
            {
                _isControlsVisible = true;

                var showTitleBarAnimation = (Storyboard)FindResource("ShowControls");
                var showControlsAnimation = (Storyboard)FindResource("ShowControls");

                showTitleBarAnimation.Begin(panel_control);
                showControlsAnimation.Begin(panel_titlebar);

                _hideControlsTimer.Stop();
                _hideControlsTimer.Start();
            }
        }

        private void HideControls()
        {
            if (_isControlsVisible && !_isMouseOverControls)
            {
                _isControlsVisible = false;

                var hideTitleBarAnimation = (Storyboard)FindResource("HideControls");
                var hideControlsAnimation = (Storyboard)FindResource("HideControls");

                hideTitleBarAnimation.Begin(panel_control);
                hideControlsAnimation.Begin(panel_titlebar);
            }
        }

        private void overlayCheck_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (UseOverlayControl)
            {
                // 마우스가 움직일 때마다 컨트롤 표시
                ShowControls();

                // 마우스가 움직일 때마다 타이머 재설정
                _hideControlsTimer.Stop();
                _hideControlsTimer.Start();
            }
        }

        private bool UseOverlayControl => OptionViewModel.Instance.CurrentOption.UseOverlayControl;
        private void UseOverlayControlChanged(object? sender, EventArgs e)
        {
            Grid? oldParent = panel_control.Parent as Grid;
            oldParent?.Children.Remove(panel_control);
            oldParent = panel_titlebar.Parent as Grid;
            oldParent?.Children.Remove(panel_titlebar);

            // 기본 모드
            if (UseOverlayControl == false)
            {
                grid_bottom.Children.Add(panel_control);
                grid_top.Children.Add(panel_titlebar);

                ShowControls();
                _hideControlsTimer.Interval = TimeSpan.FromSeconds(0.5);
                _hideControlsTimer.Stop();
            }
            // 컨트롤 자동 숨김 모드
            else
            {
                grid_center_bottom.Children.Add(panel_control);
                grid_center_top.Children.Add(panel_titlebar);

                _isControlsVisible = true;
                _isMouseOverControls = false;
                _hideControlsTimer.Interval = TimeSpan.FromSeconds(0.1);
                _hideControlsTimer.Start();
            }
        }

        private void panel_MouseEnter(object sender, MouseEventArgs e)
        {
            if (UseOverlayControl)
            {
                _isMouseOverControls = true;
                ShowControls();
            }
        }

        private void panel_MouseLeave(object sender, MouseEventArgs e)
        {
            if (UseOverlayControl)
            {
                _isMouseOverControls = false;
                _hideControlsTimer.Start();
            }
        }
    }
}
