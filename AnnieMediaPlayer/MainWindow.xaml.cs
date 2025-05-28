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
using Unosquare.FFME.Common;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : BaseWindow
    {
        public static MainViewModel MainViewModel => (App.Current.MainWindow.DataContext as MainViewModel)!;

        public MainWindow()
        {
            InitializeComponent();

            FFMELoader.Initialize();
            VideoPlayerController.Initialize(ffmeMediaElement);
            VideoPlayerController.OnMediaOpened += VideoPlayerController_OnMediaOpened;
            VideoPlayerController.OnMediaEnded += VideoPlayerController_OnMediaEnded;
            VideoPlayerController.OnMediaFailed += VideoPlayerController_OnMediaFailed;
            VideoPlayerController.OnPositionChanged += VideoPlayerController_OnPositionChanged;
            VideoPlayerController.OnMediaStateChanged += VideoPlayerController_OnMediaStateChanged;
            VideoPlayerController.OnVideoFrameRendered += VideoPlayerController_OnVideoFrameRendered;
            VideoPlayerController.OnFrameStepStateChanged += VideoPlayerController_OnFrameStepStateChanged;
            UpdateSetSpeedLabel();

            BackgroundImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/background01.png"));

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
            OptionViewModel.Instance.OptionChanged(OptionViewModel.Instance.DefaultOption, OptionViewModel.Instance.CurrentOption);
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            await VideoPlayerController.Stop();
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

        // 파일 열림 처리. 
        // 파일 닫힘 처리는 OnMediaStateChanged 에서 합니다.
        private void VideoPlayerController_OnMediaOpened(object? sender, MediaOpenedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                FileNameText.Tag = e.Info.MediaSource;
                BackgroundImage.Visibility = Visibility.Collapsed;
                StopButton.IsEnabled = true;
                PlayPauseButton.IsEnabled = true;
                PlayPauseButton.SetResourceReference(ContentControl.ContentProperty, "Text.Pause");

                TimeSpan duration = e.Info.Duration;
                TimeSpan startTime = TimeSpan.Zero;
                long frameNumber = 0;
                PlaybackSlider.Maximum = duration.TotalSeconds;
                PlaybackSlider.Value = startTime.TotalSeconds;
                TotalTimeText.Text = duration.ToString(@"hh\:mm\:ss");
                CurrentTimeText.Text = startTime.ToString(@"hh\:mm\:ss");
                FrameNumberText.Text = frameNumber.ToString();
            });
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
            Dispatcher.BeginInvoke(() =>
            {
                TimeSpan currentTime = e.Position;
                if (VideoPlayerController.IsSliderDragging == false)
                {
                    PlaybackSlider.Value = currentTime.TotalSeconds;
                }
                CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
            });
        }

        // 미디어 상태 변경시 이벤트 
        private void VideoPlayerController_OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
        {
            // 닫힌 상태에서는 값을 가져올 때 오류가 발생할 수 있으므로 일단 처리하지 않도록 합니다 .
            if (e.MediaState == MediaPlaybackState.Close)
            {
                return;
            }

            bool isOpened = VideoPlayerController.IsOpened;
            string playPauseText = string.Empty;

            // 정지 or 닫힘 상태
            if (e.MediaState == MediaPlaybackState.Close ||
                e.MediaState == MediaPlaybackState.Stop)
            {
                playPauseText = "Text.Play";
            }
            // 파일이 열려있는 상태
            else
            {
                if (VideoPlayerController.IsSliderDragging == false)
                {
                    // 느린 재생(프레임스텝) 모드일 때
                    if (VideoPlayerController.IsFrameStepMode)
                    {
                        playPauseText = VideoPlayerController.IsFrameStepPaused ? "Text.Play" : "Text.Pause";
                    }
                    else
                    {
                        if (e.MediaState == MediaPlaybackState.Pause)
                            playPauseText = "Text.Play";
                        else if (e.MediaState == MediaPlaybackState.Play)
                            playPauseText = "Text.Pause";
                    }
                }
            }

            Dispatcher.Invoke(() =>
            {
                StopButton.IsEnabled = isOpened;
                PlayPauseButton.IsEnabled = isOpened;
                PlayStateText.Visibility = isOpened ? Visibility.Visible : Visibility.Collapsed;

                if (isOpened == false)
                {
                    FileNameText.Tag = string.Empty;
                    BackgroundImage.Visibility = Visibility.Visible;

                    TimeSpan duration = TimeSpan.Zero;
                    TimeSpan startTime = TimeSpan.Zero;
                    long frameNumber = 0;
                    PlaybackSlider.Maximum = duration.TotalSeconds;
                    PlaybackSlider.Value = startTime.TotalSeconds;
                    TotalTimeText.Text = duration.ToString(@"hh\:mm\:ss");
                    CurrentTimeText.Text = startTime.ToString(@"hh\:mm\:ss");
                    FrameNumberText.Text = frameNumber.ToString();
                }
                else
                {
                    string playStateText = (playPauseText == "Text.Play") ? "Text.Pause" : "Text.Playing";
                    playStateText = LanguageManager.GetResourceString(playStateText);
                    if (string.IsNullOrEmpty(playStateText) == false)
                    {
                        playStateText = $"[{playStateText}]";
                        PlayStateText.Text = LanguageManager.GetResourceString(playStateText);
                    }
                }

                if (string.IsNullOrEmpty(playPauseText) == false)
                    PlayPauseButton.SetResourceReference(ContentControl.ContentProperty, playPauseText);
            });
            UpdateSpeedInfo();
        }

        // 렌더 될때마다 
        private void VideoPlayerController_OnVideoFrameRendered(object? sender, RenderingVideoEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                long frameNumber = e.PictureNumber - 1;
                FrameNumberText.Text = frameNumber.ToString();
            });
            UpdateCalcSpeedLabel();
        }

        private void VideoPlayerController_OnFrameStepStateChanged(object? sender, EventArgs e)
        {
            // MediaStateChanged와 동일하게 버튼 텍스트 갱신
            Dispatcher.Invoke(() =>
            {
                string playPauseText;
                if (VideoPlayerController.IsFrameStepMode)
                {
                    playPauseText = VideoPlayerController.IsFrameStepPaused ? "Text.Play" : "Text.Pause";
                }
                else
                {
                    playPauseText = VideoPlayerController.IsPlaying ? "Text.Pause" : "Text.Play";
                }
                PlayPauseButton.SetResourceReference(ContentControl.ContentProperty, playPauseText);
            });
        }

        void UpdateSpeedInfo()
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
                        double fps = VideoPlayerController.VideoFps;
                        string fpsStr = fps.ToString("0.00");

                        Run speedRun = LanguageManager.GetLocalizedRun("Text.1x.Speed");
                        Run linefeedRun = new Run("\n");
                        Run openParen = new Run(" (");
                        Run fpsRun = new Run(fpsStr + "fps");
                        Run closeParen = new Run(")");
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
        

        private void OpenVideo_Click(object sender, RoutedEventArgs e) => _ = VideoPlayerController.OpenAndPlay();
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
                    _ = VideoPlayerController.OpenAndPlay(filePath);
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
        }
    }
}
