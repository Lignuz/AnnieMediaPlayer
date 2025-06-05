using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AnnieMediaPlayer.Options;
using AnnieMediaPlayer.Windows.Settings;
using FFmpeg.AutoGen;
using Unosquare.FFME.Common;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : BaseWindow
    {
        public static MainViewModel vm => (MainViewModel)App.Current.FindResource("vm");

        public MainWindow()
        {
            InitializeComponent();

            FFMELoader.Initialize();
            VideoPlayerController.Initialize(ffmeMediaElement);
            VideoPlayerController.OnMediaOpening += VideoPlayerController_OnMediaOpening;
            VideoPlayerController.OnMediaOpened += VideoPlayerController_OnMediaOpened;
            VideoPlayerController.OnMediaEnded += VideoPlayerController_OnMediaEnded;
            VideoPlayerController.OnMediaFailed += VideoPlayerController_OnMediaFailed;
            VideoPlayerController.OnPositionChanged += VideoPlayerController_OnPositionChanged;
            VideoPlayerController.OnMediaStateChanged += VideoPlayerController_OnMediaStateChanged;
            VideoPlayerController.OnVideoFrameRendered += VideoPlayerController_OnVideoFrameRendered;
            VideoPlayerController.OnFrameStepStateChanged += VideoPlayerController_OnFrameStepStateChanged;
            VideoPlayerController.OnSpeedIndexChanged += VideoPlayerController_OnSpeedIndexChanged;
            UpdateSetSpeedLabel();

            BackgroundImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/background01.png"));
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCCALCSIZE = 0x0083;
            const int WM_NCACTIVATE = 0x0086;

            if (msg == WM_NCCALCSIZE)
            {
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_NCACTIVATE)
            {
                // 비활성화/활성화 시에도 NC 그리기를 막음
                handled = true;
                return new IntPtr(1); // 기본 처리를 막고, WM_PAINT 강제 유도
            }

            return IntPtr.Zero;
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
            OptionViewModel.Instance.OptionChanged(OptionViewModel.Instance.DefaultOption, OptionViewModel.Instance.CurrentOption);
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            await VideoPlayerController.Stop();
        }

        // 마우스 휠
        private void win_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            VideoPlayerController.SetVolumeChange(e.Delta > 0);
        }

        // 기본영역 드래그로 이동 지원
        private void win_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }


        /////////////////////////////////////////
        // 컨트롤러 콜백에 대한 UI 이벤트 처리 //
        /////////////////////////////////////////

        // 파일 열기할 때 설정
        private void VideoPlayerController_OnMediaOpening(object? sender, MediaOpeningEventArgs e)
        {
            // 하드웨어 가속 옵션이 꺼져있으면 하드웨어 디바이스 목록을 설정하지 않습니다.
            if (OptionViewModel.Instance.CurrentOption.UseHWAccelerator == false)
                return;

            if (e.Options.VideoStream is StreamInfo videoStream)
            {
                // Hardware device priorities
                var deviceCandidates = new[]
                {
                    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_D3D12VA,
                };

                // Hardware device selection
                var devices = new List<HardwareDeviceInfo>(deviceCandidates.Length);
                foreach (var deviceType in deviceCandidates)
                {
                    var accelerator = videoStream.HardwareDevices.FirstOrDefault(d => d.DeviceType == deviceType);
                    if (accelerator == null) continue;

                    devices.Add(accelerator);
                }

                e.Options.VideoHardwareDevices = devices.ToArray();
            }
        }

        // 파일 열림 처리. 
        // 파일 닫힘 처리는 OnMediaStateChanged 에서 합니다.
        private void VideoPlayerController_OnMediaOpened(object? sender, MediaOpenedEventArgs e)
        {
            vm.IsOpened = true;
            vm.FilePath = e.Info.MediaSource;
            vm.Duration = e.Info.Duration;
            vm.Position = e.Info.StartTime;
            vm.FrameIndex = 0;
            vm.IsPlaying = false;
        }

        private void VideoPlayerController_OnMediaEnded(object? sender, EventArgs e)
        {

        }

        private void VideoPlayerController_OnMediaFailed(object? sender, MediaFailedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"재생 오류: {e.ErrorException.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void VideoPlayerController_OnPositionChanged(object? sender, PositionChangedEventArgs e)
        {
            vm.Position = e.Position;
        }

        // 미디어 상태 변경시 이벤트 
        private void VideoPlayerController_OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
        {
            string playPauseText = string.Empty;
            if (e.MediaState == MediaPlaybackState.Close || 
                e.MediaState == MediaPlaybackState.Stop)
            {
                vm.IsOpened = false;
                vm.FilePath = string.Empty;
                vm.Duration = TimeSpan.Zero;
                vm.Position = TimeSpan.Zero;
                vm.FrameIndex = 0;
                vm.IsPlaying = false;

                if (e.MediaState == MediaPlaybackState.Close)
                {
                    return;
                }
            }
            else
            {
                vm.IsOpened = true;

                if (VideoPlayerController.IsSliderDragging == false)
                {
                    // 느린 재생(프레임스텝) 모드일 때
                    if (VideoPlayerController.IsFrameStepMode)
                    {
                        vm.IsPlaying = !VideoPlayerController.IsFrameStepPaused;
                    }
                    else
                    {
                        vm.IsPlaying = e.MediaState == MediaPlaybackState.Play ? true :
                            e.MediaState == MediaPlaybackState.Pause ? false : vm.IsPlaying;
                    }
                }
            }
            UpdateSpeedInfo();
        }

        // 렌더 될때마다 
        private void VideoPlayerController_OnVideoFrameRendered(object? sender, RenderingVideoEventArgs e)
        {
            vm.FrameIndex = e.PictureNumber - 1;
            UpdateCalcSpeedLabel();
        }

        private void VideoPlayerController_OnFrameStepStateChanged(object? sender, EventArgs e)
        {
            vm.IsPlaying = VideoPlayerController.IsFrameStepMode ? !VideoPlayerController.IsFrameStepPaused : VideoPlayerController.IsPlaying;
        }

        private void VideoPlayerController_OnSpeedIndexChanged(object? sender, EventArgs e)
        {
            vm.IsNormalSpeed = VideoPlayerController.IsNormalSpeed;
            UpdateSetSpeedLabel();
        }

        public void UpdateSpeedInfo()
        {
            UpdateSetSpeedLabel();
            UpdateCalcSpeedLabel();
        }

        void UpdateSetSpeedLabel()
        {
            Dispatcher.BeginInvoke(() =>
            {
                var speed = VideoPlayerController.PlaybackSpeeds[VideoPlayerController.SpeedIndex];
                TextBlock textBlock = SpeedLabel;
                textBlock.Inlines.Clear();

                // 원본 속도 (1배속) 표시
                if (VideoPlayerController.IsNormalSpeed)
                {
                    if (VideoPlayerController.IsOpened)
                    {
                        double speedRatio = VideoPlayerController.GetSpeedRatio();
                        string speedRatioStr = speedRatio.ToString("0.0");

                        double fps = VideoPlayerController.VideoFps;
                        string fpsStr = fps.ToString("0.00");

                        Run speedRatioRun = new Run(speedRatioStr);
                        Run speedRun = LanguageManager.GetLocalizedRun("Text.x.Speed");
                        Run linefeedRun = new Run("\n");
                        Run openParen = new Run(" (");
                        Run fpsRun = new Run(fpsStr + "fps");
                        Run closeParen = new Run(")");

                        textBlock.Inlines.Add(speedRatioRun);
                        textBlock.Inlines.Add(speedRun);
                        textBlock.Inlines.Add(linefeedRun);
                        textBlock.Inlines.Add(openParen);
                        textBlock.Inlines.Add(fpsRun);
                        textBlock.Inlines.Add(closeParen);
                    }
                    else
                    {
                        // 영상이 없을 때는 단순히 "1배속"만 표시
                        Run speedRun = LanguageManager.GetLocalizedRun("Text.1x.Speed");
                        textBlock.Inlines.Add(speedRun);
                    }
                }
                else
                {
                    if (speed.TotalSeconds >= 1)
                    {
                        // 속도 값
                        Run valueRun = new Run(speed.TotalSeconds.ToString());
                        textBlock.Inlines.Add(valueRun);

                        // 단위 (초)
                        Run secondRun = LanguageManager.GetLocalizedRun("Text.Sec");
                        textBlock.Inlines.Add(secondRun);

                        // 슬래시
                        textBlock.Inlines.Add(new Run("/"));

                        // 단위 (프레임)
                        Run frameRun = LanguageManager.GetLocalizedRun("Text.Frame");
                        textBlock.Inlines.Add(frameRun);
                    }
                    else
                    {
                        int fps = (int)Math.Round(1.0 / speed.TotalSeconds);
                        textBlock.Text = $"{fps}fps";
                    }
                }
            });
        }

        void UpdateCalcSpeedLabel()
        {
            Dispatcher.BeginInvoke(() =>
            {
                string actualStr = "";
                double actualFps = VideoPlayerController.ActualFps;
                if (VideoPlayerController.IsOpened)
                {
                    actualStr = actualFps > 0 ? actualFps.ToString("0.00") : "-";
                    actualStr = $"{actualStr}fps";
                }
                ActualFpsText.Text = actualStr;
            });
        }
        

        private void OpenVideo_Click(object sender, RoutedEventArgs e) => _ = VideoPlayerController.Open();
        private void PlayPause_Click(object sender, RoutedEventArgs e) => _ = VideoPlayerController.TogglePlayPause();
        private void Stop_Click(object sender, RoutedEventArgs e) => _ = VideoPlayerController.Stop();

        private void SpeedDown_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayerController.DecreaseSpeed();
            UpdateSetSpeedLabel();
        }
        private void SpeedUp_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayerController.IncreaseSpeed();
            UpdateSetSpeedLabel();
        }

        private void SpeedRatioDown_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayerController.SpeedDown();
            UpdateSetSpeedLabel();
        }

        private void SpeedRatioUp_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayerController.SpeedUp();
            UpdateSetSpeedLabel();
        }

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
                    _ = VideoPlayerController.Open(filePath);
                }
            }
        }


        // 오버레이 컨트롤을 위한 타이머와 플래그
        private bool _isControlsVisible = false;
        private bool _isMouseOverControls = false;
        private System.Windows.Threading.DispatcherTimer _hideControlsTimer = new System.Windows.Threading.DispatcherTimer();

        // 오버레이를 위한 컨트롤 초기화 설정
        private void InitializeOverlayControls()
        {
            // 컨트롤 숨김 타이머 기본 설정 
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

        private void OverlayCanvas_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue == false)
            {
                // 숨길 때에 내용을 지워줍니다.
                OverlayCanvas.Children.Clear();
            }
            else
            {
                // 보여질 때에도 초기화 합니다. 
                OverlayCanvas.Children.Clear();
            }
        }

        // 메시지 보여주기 
        private CancellationTokenSource? _overlayCts;
        public async void ShowOverlayMessage(string message, int durationMs = 1500)
        {
            _overlayCts?.Cancel(); // 기존 표시 중지
            _overlayCts = new CancellationTokenSource();
            var token = _overlayCts.Token;

            OverlayMessage.Text = message;
            OverlayMessage.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            OverlayMessage.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            try
            {
                await Task.Delay(durationMs, token);
            }
            catch (TaskCanceledException)
            {
                return; // 취소된 경우는 아무 처리 안 함
            }

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) =>
            {
                if (!token.IsCancellationRequested)
                    OverlayMessage.Visibility = Visibility.Collapsed;
            };
            OverlayMessage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}
