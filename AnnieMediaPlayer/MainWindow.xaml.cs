using System.Windows;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Collections.Specialized;
using System.Threading;
using AnnieMediaPlayer.Options;
using AnnieMediaPlayer.Windows.Settings;
using AnnieMediaPlayer.Windows;
using AnnieMediaPlayer.Windows.Panels;
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
        private PlaylistWindow? _playlistWindow;
        private string? _openedPlaylistSource;
        private bool _playlistDocked;
        private bool _playlistDockedToRight;
        private bool _playlistSyncHeight;
        private bool _updatingPlaylistDock;
        private bool _playlistNavigationInProgress;
        private readonly AlbumArtService _albumArtService = new();
        private readonly Dictionary<PlaylistItemViewModel, CancellationTokenSource> _albumArtLoads = new();
        private readonly AlbumArtService _currentAlbumArtService = new();
        private CancellationTokenSource? _currentAlbumArtLoad;
        private readonly DispatcherTimer _playlistSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

        public MainWindow()
        {
            InitializeComponent();

            _playlistSaveTimer.Tick += (_, _) => SavePlaylistNow();

            FFMELoader.Initialize();
            if (OptionViewModel.Instance.CurrentOption.UsePlaylistPersistence)
                vm.Playlist.AddFiles(PlaylistStorage.Load());

            vm.Playlist.Items.CollectionChanged += PlaylistItems_CollectionChanged;
            foreach (var item in vm.Playlist.Items)
                QueueAlbumArtLoad(item);

            VideoPlayerController.Initialize(ffmeMediaElement);
            ffmeMediaElement.RenderingAudio += FfmeMediaElement_RenderingAudio;
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

        private bool _isClosing;
        private long _latestFrameIndex;
        private int _frameUiUpdatePending;
        private DateTime _lastCalcSpeedLabelUpdate = DateTime.MinValue;

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isClosing)
                return;

            e.Cancel = true;
            _isClosing = true;

            try
            {
                // 대기 중인 재생목록 저장이 있으면 종료 전에 바로 저장합니다.
                if (_playlistSaveTimer.IsEnabled)
                    SavePlaylistNow();

                CancelAllAlbumArtLoads();
                _currentAlbumArtLoad?.Cancel();
                ffmeMediaElement.RenderingAudio -= FfmeMediaElement_RenderingAudio;
                await VideoPlayerController.Stop();
                await VideoPlayerController.DisposeAsync();
            }
            finally
            {
                // 종료 정리 중에도 창이 열려 있어 재생목록이 바뀔 수 있으므로, 닫기 직전에 한 번 더 저장합니다.
                if (_playlistSaveTimer.IsEnabled)
                    SavePlaylistNow();

                _albumArtService.Dispose();
                _currentAlbumArtService.Dispose();
                Close();
            }
        }

        private void FfmeMediaElement_RenderingAudio(object? sender, RenderingAudioEventArgs e)
        {
            AudioVisualizer.PushAudioSamples(e.Buffer, e.BufferLength, e.SampleRate, e.ChannelCount, e.BitsPerSample);
        }

        // 마우스 휠
        private void win_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            VideoPlayerController.SetVolumeChange(e.Delta > 0);
        }

        // 기본영역 드래그로 이동 지원
        private void win_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsButtonInput(e.OriginalSource as DependencyObject))
                return;

            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
            {
                TitleBarController.MouseLeftButtonDown(this, e);
            }
        }

        private void win_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsButtonInput(e.OriginalSource as DependencyObject))
                return;

            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Right)
                TitleBarController.MouseRightButtonDown(this, e);
        }

        private void win_PreviewMouseMove(object sender, MouseEventArgs e) => TitleBarController.MouseMove(this, e);
        private void win_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TitleBarController.MouseLeftButtonUp();
        private void win_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) => TitleBarController.MouseRightButtonUp();
        private void Window_Deactivated(object? sender, EventArgs e) => TitleBarController.Cancel();

        private static bool IsButtonInput(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is ButtonBase)
                    return true;

                element = element is Visual || element is Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : null;
            }

            return false;
        }


        /////////////////////////////////////////
        // 컨트롤러 콜백에 대한 UI 이벤트 처리 //
        /////////////////////////////////////////

        // 파일 열기할 때 설정
        private void VideoPlayerController_OnMediaOpening(object? sender, MediaOpeningEventArgs e)
        {
            vm.AudioTitle = string.Empty;
            vm.AudioArtist = string.Empty;
            vm.AudioAlbum = string.Empty;

            // Keep audio rendering from waiting behind video frame presentation.
            e.Options.UseParallelRendering = true;
            ffmeMediaElement.RendererOptions.UseLegacyAudioOut =
                OptionViewModel.Instance.CurrentOption.UseLegacyAudioOut;
            PlayerDiagnostics.Write(
                $"Media opening: audio={(ffmeMediaElement.RendererOptions.UseLegacyAudioOut ? "Legacy" : "DirectSound")}, " +
                $"hardware={OptionViewModel.Instance.CurrentOption.UseHWAccelerator}");

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
            vm.IsAudioOnly = !VideoPlayerController.HasVideo;
            if (vm.IsAudioOnly)
            {
                var audioMetadata = e.Info.BestStreams.TryGetValue(AVMediaType.AVMEDIA_TYPE_AUDIO, out var audioStream)
                    ? audioStream.Metadata
                    : null;
                vm.AudioTitle = FindMetadataValue(e.Info.Metadata, audioMetadata, "title")
                    ?? Path.GetFileNameWithoutExtension(e.Info.MediaSource);
                vm.AudioArtist = FindMetadataValue(e.Info.Metadata, audioMetadata,
                    "artist", "performer", "album_artist", "album-artist", "albumartist") ?? string.Empty;
                vm.AudioAlbum = FindMetadataValue(e.Info.Metadata, audioMetadata, "album") ?? string.Empty;
                _ = LoadCurrentAlbumArtAsync(e.Info.MediaSource);
            }
            else
            {
                _currentAlbumArtLoad?.Cancel();
                vm.CurrentAlbumArt = null;
            }
            UpdateSpeedInfo();
            vm.Duration = e.Info.Duration;
            vm.Position = e.Info.StartTime;
            vm.FrameIndex = 0;
            vm.IsPlaying = false;
            _openedPlaylistSource = e.Info.MediaSource;
            var currentPlaylistItem = vm.Playlist.CurrentItem;
            if (currentPlaylistItem != null &&
                string.Equals(currentPlaylistItem.FilePath, _openedPlaylistSource, StringComparison.OrdinalIgnoreCase))
                currentPlaylistItem.Duration = e.Info.Duration;
        }

        private void VideoPlayerController_OnMediaEnded(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => _ = PlayNextPlaylistItemAsync()));
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
            if (e.MediaState == MediaPlaybackState.Close || 
                e.MediaState == MediaPlaybackState.Stop)
            {
                vm.IsOpened = false;
                vm.IsAudioOnly = false;
                vm.FilePath = string.Empty;
                vm.AudioTitle = string.Empty;
                vm.AudioArtist = string.Empty;
                vm.AudioAlbum = string.Empty;
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
                vm.IsAudioOnly = !VideoPlayerController.HasVideo;
                vm.FilePath = VideoPlayerController.CurrentFilePath;
                vm.Duration = VideoPlayerController.TotalDuration;
                vm.Position = VideoPlayerController.CurrentPosition;

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

        private static string? FindMetadataValue(
            IReadOnlyDictionary<string, string>? containerMetadata,
            IReadOnlyDictionary<string, string>? streamMetadata,
            params string[] keys)
        {
            foreach (var metadata in new[] { containerMetadata, streamMetadata })
            {
                if (metadata is null)
                    continue;

                foreach (var key in keys)
                foreach (var entry in metadata)
                {
                    if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(entry.Value))
                        return entry.Value.Trim();
                }
            }

            return null;
        }

        // 렌더 될때마다 
        private void VideoPlayerController_OnVideoFrameRendered(object? sender, RenderingVideoEventArgs e)
        {
            Interlocked.Exchange(ref _latestFrameIndex, e.PictureNumber - 1);

            if (Dispatcher.CheckAccess())
            {
                UpdateVideoFrameUi();
                return;
            }

            // 렌더링 스레드에서 들어오는 프레임 이벤트를 UI 작업 하나로 합칩니다.
            if (Interlocked.Exchange(ref _frameUiUpdatePending, 1) != 0)
                return;

            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
                {
                    try
                    {
                        UpdateVideoFrameUi();
                    }
                    finally
                    {
                        Volatile.Write(ref _frameUiUpdatePending, 0);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                Volatile.Write(ref _frameUiUpdatePending, 0);
            }
        }

        private void UpdateVideoFrameUi()
        {
            vm.FrameIndex = Interlocked.Read(ref _latestFrameIndex);

            // FPS 표시는 매 프레임마다 갱신할 필요가 없으므로 UI 작업을 제한합니다.
            if ((DateTime.UtcNow - _lastCalcSpeedLabelUpdate).TotalMilliseconds >= 250)
            {
                _lastCalcSpeedLabelUpdate = DateTime.UtcNow;
                UpdateCalcSpeedLabel();
            }
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
                if (vm.IsAudioOnly)
                {
                    textBlock.Inlines.Add(new Run(VideoPlayerController.GetSpeedRatio().ToString("0.0")));
                    textBlock.Inlines.Add(LanguageManager.GetLocalizedRun("Text.x.Speed"));
                    return;
                }

                // 일반 배속 표시
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
                    if (vm.IsAudioOnly)
                    {
                        ActualFpsText.Text = string.Empty;
                        return;
                    }
                    actualStr = actualFps > 0 ? actualFps.ToString("0.00") : "-";
                    actualStr = $"{actualStr}fps";
                }
                ActualFpsText.Text = actualStr;
            });
        }
        

        private void OpenVideo_Click(object sender, RoutedEventArgs e) => OpenVideoFromDialog();

        internal void OpenVideoFromDialog()
        {
            var dialog = new OpenFileDialog
            {
                Title = LanguageManager.GetResourceString("Text.OpenDialogTitle"),
                Filter = LanguageManager.GetResourceString("Text.OpenDialogFilter")
            };

            if (dialog.ShowDialog() == true)
                _ = OpenDirectFileAsync(dialog.FileName);
        }

        private async Task OpenDirectFileAsync(string filePath)
        {
            var opened = await VideoPlayerController.Open(filePath);
            if (opened)
            {
                var currentPlaylistItem = vm.Playlist.AddFile(filePath);
                if (currentPlaylistItem != null)
                {
                    vm.Playlist.SetCurrent(currentPlaylistItem);
                    currentPlaylistItem.Duration = vm.Duration;
                }
            }
        }

        private async Task PlayPlaylistItemAsync(PlaylistItemViewModel item)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
            {
                vm.Playlist.Remove(item);
                return;
            }

            var opened = await VideoPlayerController.Open(item.FilePath);
            if (opened)
            {
                vm.Playlist.SetCurrent(item);
                item.Duration = vm.Duration;
            }
            if (opened && !OptionViewModel.Instance.CurrentOption.UseOpenPlay)
                await VideoPlayerController.Play();
        }

        private async Task PlayNextPlaylistItemAsync()
        {
            if (!OptionViewModel.Instance.CurrentOption.UseContinuousPlayback)
                return;

            var currentItem = vm.Playlist.CurrentItem;
            if (currentItem == null ||
                !string.Equals(currentItem.FilePath, _openedPlaylistSource, StringComparison.OrdinalIgnoreCase))
                return;

            await PlayAdjacentPlaylistItemAsync(true);
        }

        private async Task PlayAdjacentPlaylistItemAsync(bool forward)
        {
            if (_playlistNavigationInProgress)
                return;

            _playlistNavigationInProgress = true;
            try
            {
                while (true)
                {
                    var item = forward ? vm.Playlist.GetNextItem() : vm.Playlist.GetPreviousItem();
                    if (item == null)
                        return;

                    if (!File.Exists(item.FilePath))
                    {
                        vm.Playlist.Remove(item);
                        continue;
                    }

                    await PlayPlaylistItemAsync(item);
                    return;
                }
            }
            finally
            {
                _playlistNavigationInProgress = false;
            }
        }

        private void PlaylistButton_Click(object sender, RoutedEventArgs e) => TogglePlaylistWindow();

        public void TogglePlaylistWindow()
        {
            if (_playlistWindow?.IsVisible == true)
            {
                _playlistWindow.Hide();
                PlaylistButton.Tag = "False";
                Activate();
                return;
            }

            if (_playlistWindow != null)
            {
                _playlistWindow.Show();
                if (_playlistDocked)
                    UpdateDockedPlaylistPosition();
                PlaylistButton.Tag = "True";
                _playlistWindow.Activate();
                return;
            }

            var playlistPosition = GetPlaylistInitialPosition(out var canDock);
            _playlistWindow = new PlaylistWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = playlistPosition.X,
                Top = playlistPosition.Y
            };
            _playlistWindow.AddFilesRequested += PlaylistPanel_AddFilesRequested;
            _playlistWindow.RemoveRequested += PlaylistPanel_RemoveRequested;
            _playlistWindow.ClearRequested += PlaylistPanel_ClearRequested;
            _playlistWindow.ItemMoveRequested += PlaylistPanel_ItemMoveRequested;
            _playlistWindow.ItemDoubleClicked += PlaylistPanel_ItemDoubleClicked;
            _playlistWindow.LocationChanged += PlaylistWindow_LocationChanged;
            _playlistWindow.SizeChanged += PlaylistWindow_SizeChanged;
            _playlistWindow.MoveCompleted += PlaylistWindow_MoveCompleted;
            _playlistWindow.ToggleRequested += (_, _) => TogglePlaylistWindow();
            _playlistWindow.Closed += (_, _) =>
            {
                _playlistDocked = false;
                _playlistSyncHeight = false;
                _playlistWindow = null;
                PlaylistButton.Tag = "False";
            };
            _playlistWindow.Show();
            _playlistDocked = canDock;
            _playlistSyncHeight = false;
            UpdateDockedPlaylistPosition();
            PlaylistButton.Tag = "True";
        }

        private Point GetPlaylistInitialPosition(out bool canDock)
        {
            const double playlistWidth = 340;
            const double playlistHeight = 520;
            var workArea = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            var workTopLeft = PointFromScreen(new Point(workArea.Left, workArea.Top));
            var workBottomRight = PointFromScreen(new Point(workArea.Right, workArea.Bottom));
            var workLeft = Left + workTopLeft.X;
            var workRight = Left + workBottomRight.X;
            var workTop = Top + workTopLeft.Y;
            var workBottom = Top + workBottomRight.Y;

            if (WindowState == WindowState.Maximized)
            {
                canDock = false;
                return new Point(workRight - playlistWidth,
                    Math.Clamp(Top, workTop, Math.Max(workTop, workBottom - playlistHeight)));
            }

            var rightSpace = workRight - (Left + ActualWidth);
            var leftSpace = Left - workLeft;
            _playlistDockedToRight = rightSpace >= playlistWidth || rightSpace >= leftSpace;
            canDock = (_playlistDockedToRight ? rightSpace >= playlistWidth : leftSpace >= playlistWidth) &&
                      Top >= workTop && Top + playlistHeight <= workBottom;

            var preferredLeft = _playlistDockedToRight ? Left + ActualWidth : Left - playlistWidth;

            return new Point(
                Math.Clamp(preferredLeft, workLeft, Math.Max(workLeft, workRight - playlistWidth)),
                Math.Clamp(Top, workTop, Math.Max(workTop, workBottom - playlistHeight)));
        }

        private void PlaylistWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (_updatingPlaylistDock || _playlistWindow == null)
                return;

            if (_playlistDocked && !IsPlaylistAtDockPosition())
                _playlistDocked = false;
        }

        private void PlaylistWindow_MoveCompleted(object? sender, EventArgs e)
        {
            if (!_playlistDocked)
                TryDockPlaylistWindow();
        }

        private void TryDockPlaylistWindow()
        {
            if (_playlistWindow == null || WindowState != WindowState.Normal)
                return;

            const double dockDistance = 20;
            var playlistTop = _playlistWindow.Top;
            var playlistBottom = playlistTop + _playlistWindow.Height;
            var verticallyOverlapping = playlistBottom > Top && playlistTop < Top + ActualHeight;

            if (!verticallyOverlapping)
                return;

            var mainInsets = GetVisibleFrameInsets(this);
            var playlistInsets = GetVisibleFrameInsets(_playlistWindow);
            var mainRight = Left + ActualWidth - mainInsets.Right;
            var mainLeft = Left + mainInsets.Left;
            var playlistLeft = _playlistWindow.Left + playlistInsets.Left;
            var playlistRight = _playlistWindow.Left + _playlistWindow.ActualWidth - playlistInsets.Right;

            if (Math.Abs(playlistLeft - mainRight) <= dockDistance)
            {
                _playlistDocked = true;
                _playlistDockedToRight = true;
            }
            else if (Math.Abs(playlistRight - mainLeft) <= dockDistance)
            {
                _playlistDocked = true;
                _playlistDockedToRight = false;
            }
            else
            {
                return;
            }

            _playlistSyncHeight = Math.Abs(_playlistWindow.ActualHeight - ActualHeight) <= 3;

            UpdateDockedPlaylistPosition();
        }

        private void PlaylistWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_updatingPlaylistDock && _playlistDocked && _playlistWindow != null)
                _playlistSyncHeight = Math.Abs(_playlistWindow.ActualHeight - ActualHeight) <= 3;
        }

        private bool IsPlaylistAtDockPosition()
        {
            if (_playlistWindow == null)
                return false;

            var position = GetPlaylistDockPosition();
            return Math.Abs(_playlistWindow.Left - position.X) <= 3 &&
                   Math.Abs(_playlistWindow.Top - position.Y) <= 3;
        }

        private void UpdateDockedPlaylistPosition()
        {
            if (!_playlistDocked || _playlistWindow == null || WindowState != WindowState.Normal)
                return;

            var position = GetPlaylistDockPosition();

            try
            {
                _updatingPlaylistDock = true;
                _playlistWindow.Left = position.X;
                _playlistWindow.Top = position.Y;
                if (_playlistSyncHeight)
                    _playlistWindow.Height = ActualHeight;
            }
            finally
            {
                _updatingPlaylistDock = false;
            }
        }

        private Point GetPlaylistDockPosition()
        {
            var mainInsets = GetVisibleFrameInsets(this);
            var playlistInsets = GetVisibleFrameInsets(_playlistWindow!);
            var left = _playlistDockedToRight
                ? Left + ActualWidth - mainInsets.Right - playlistInsets.Left
                : Left + mainInsets.Left - _playlistWindow!.ActualWidth + playlistInsets.Right;
            return new Point(left, Top + mainInsets.Top - playlistInsets.Top);
        }

        private static Thickness GetVisibleFrameInsets(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var windowRect) ||
                DwmGetWindowAttribute(handle, 9, out var visibleRect, Marshal.SizeOf<NativeRect>()) != 0)
                return new Thickness(0);

            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget == null)
                return new Thickness(0);

            var scale = source.CompositionTarget.TransformFromDevice;
            return new Thickness(
                (visibleRect.Left - windowRect.Left) * scale.M11,
                (visibleRect.Top - windowRect.Top) * scale.M22,
                (windowRect.Right - visibleRect.Right) * scale.M11,
                (windowRect.Bottom - visibleRect.Bottom) * scale.M22);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out NativeRect value, int size);

        private void PlaylistItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 추가·이동은 항목마다 변경 알림이 발생하므로 연속된 변경을 모아 한 번만 저장합니다.
            if (OptionViewModel.Instance.CurrentOption.UsePlaylistPersistence)
            {
                _playlistSaveTimer.Stop();
                _playlistSaveTimer.Start();
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                CancelAllAlbumArtLoads();
                return;
            }

            // 순서만 바뀐 경우에는 진행 중인 앨범 이미지 로딩을 그대로 유지합니다.
            if (e.Action == NotifyCollectionChangedAction.Move)
                return;

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<PlaylistItemViewModel>())
                    CancelAlbumArtLoad(item);
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<PlaylistItemViewModel>())
                    QueueAlbumArtLoad(item);
            }
        }

        private void SavePlaylistNow()
        {
            _playlistSaveTimer.Stop();
            if (OptionViewModel.Instance.CurrentOption.UsePlaylistPersistence)
                PlaylistStorage.Save(vm.Playlist.Items.Select(item => item.FilePath));
        }

        private void QueueAlbumArtLoad(PlaylistItemViewModel item)
        {
            if (_albumArtLoads.ContainsKey(item) || item.AlbumArt is not null)
                return;

            var cancellation = new CancellationTokenSource();
            _albumArtLoads[item] = cancellation;
            _ = LoadAlbumArtAsync(item, cancellation);
        }

        private async Task LoadAlbumArtAsync(PlaylistItemViewModel item, CancellationTokenSource cancellation)
        {
            try
            {
                var result = await _albumArtService.LoadAsync(item.FilePath, 512, cancellation.Token);
                if (!cancellation.IsCancellationRequested && vm.Playlist.Items.Contains(item))
                {
                    if (result.Duration is TimeSpan duration && duration > TimeSpan.Zero && item.Duration <= TimeSpan.Zero)
                        item.Duration = duration;

                    item.AlbumArt = result.Image;
                }
            }
            catch (OperationCanceledException)
            {
                // 항목이 제거되거나 새로고침된 경우의 정상적인 취소입니다.
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Playlist album art update failed: {item.FilePath} - {ex}");
            }
            finally
            {
                if (_albumArtLoads.TryGetValue(item, out var current) && ReferenceEquals(current, cancellation))
                {
                    _albumArtLoads.Remove(item);
                }

                cancellation.Dispose();
            }
        }

        // 시각화 화면은 커버를 크게 그리므로 현재 곡의 앨범 이미지는 재생목록 썸네일(512px)보다 큰 1024px 로 따로 읽습니다.
        // 재생목록 로딩 대기열과 섞이지 않도록 별도 서비스를 사용합니다.
        private async Task LoadCurrentAlbumArtAsync(string filePath)
        {
            _currentAlbumArtLoad?.Cancel();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                vm.CurrentAlbumArt = null;
                return;
            }

            var cancellation = new CancellationTokenSource();
            _currentAlbumArtLoad = cancellation;

            // 고해상도 이미지를 읽는 동안에는 재생목록에서 이미 읽어 둔 이미지를 먼저 보여줍니다.
            vm.CurrentAlbumArt = vm.Playlist.Items.FirstOrDefault(item =>
                string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))?.AlbumArt;

            try
            {
                var result = await _currentAlbumArtService.LoadAsync(filePath, 1024, cancellation.Token);
                if (!cancellation.IsCancellationRequested && result.Image is not null)
                    vm.CurrentAlbumArt = result.Image;
            }
            catch (OperationCanceledException)
            {
                // 다른 곡을 열었거나 창을 닫는 경우의 정상적인 취소입니다.
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Current album art load failed: {filePath} - {ex}");
            }
            finally
            {
                if (ReferenceEquals(_currentAlbumArtLoad, cancellation))
                    _currentAlbumArtLoad = null;

                cancellation.Dispose();
            }
        }

        private void CancelAlbumArtLoad(PlaylistItemViewModel item)
        {
            if (_albumArtLoads.Remove(item, out var cancellation))
                cancellation.Cancel();
        }

        private void CancelAllAlbumArtLoads()
        {
            foreach (var entry in _albumArtLoads)
                entry.Value.Cancel();

            _albumArtLoads.Clear();
        }

        private void PlaylistPanel_AddFilesRequested(object? sender, EventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LanguageManager.GetResourceString("Text.OpenDialogTitle"),
                Filter = LanguageManager.GetResourceString("Text.OpenDialogFilter"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
                vm.Playlist.AddFiles(dialog.FileNames);
        }

        private void PlaylistPanel_RemoveRequested(object? sender, IReadOnlyList<PlaylistItemViewModel> items)
        {
            foreach (var item in items.ToList())
                vm.Playlist.Remove(item);
        }

        private void PlaylistPanel_ClearRequested(object? sender, EventArgs e) => vm.Playlist.Clear();

        private void PlaylistPanel_ItemMoveRequested(object? sender, (IReadOnlyList<PlaylistItemViewModel> Items, int TargetIndex) move) =>
            vm.Playlist.MoveManyTo(move.Items, move.TargetIndex);

        private void PlaylistPanel_ItemDoubleClicked(object? sender, PlaylistItemViewModel item) =>
            _ = PlayPlaylistItemAsync(item);
        private void PreviousPlaylist_Click(object sender, RoutedEventArgs e) =>
            _ = PlayAdjacentPlaylistItemAsync(false);
        private void NextPlaylist_Click(object sender, RoutedEventArgs e) =>
            _ = PlayAdjacentPlaylistItemAsync(true);
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

        private void Window_StateChanged(object sender, EventArgs e)
        {
            UpdateMaxRestoreButton();

            if (WindowState == WindowState.Maximized && _playlistDocked && _playlistWindow?.IsVisible == true)
                _playlistDocked = false;

            UpdateDockedPlaylistPosition();
        }

        private void UpdateMaxRestoreButton()
        {
            MaxRestoreButton.Content = (Geometry)FindResource(
                this.WindowState == WindowState.Maximized ? "RestoreIconData" : "MaximizeIconData");
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            MonitorSnapState();
            UpdateDockedPlaylistPosition();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MonitorSnapState();
            UpdateDockedPlaylistPosition();
        }

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
        private void TitleBar_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => TitleBarController.MouseRightButtonDown(this, e);
        private void TitleBar_MouseMove(object sender, MouseEventArgs e) => TitleBarController.MouseMove(this, e);
        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TitleBarController.MouseLeftButtonUp();
        private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => TitleBarController.MouseRightButtonUp();

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
                    _ = OpenDirectFileAsync(filePath);
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

                var showControlsAnimation = (Storyboard)FindResource("ShowControls");

                showControlsAnimation.Begin(panel_control);

                _hideControlsTimer.Stop();
                _hideControlsTimer.Start();
            }
        }

        private void HideControls()
        {
            if (_isControlsVisible && !_isMouseOverControls)
            {
                _isControlsVisible = false;

                var hideControlsAnimation = (Storyboard)FindResource("HideControls");

                hideControlsAnimation.Begin(panel_control);
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

            // 기본 모드
            if (UseOverlayControl == false)
            {
                grid_bottom.Children.Add(panel_control);
                panel_control.Opacity = 1;
                _isControlsVisible = true;
                _hideControlsTimer.Interval = TimeSpan.FromSeconds(0.5);
                _hideControlsTimer.Stop();
            }
            // 컨트롤 자동 숨김 모드
            else
            {
                grid_center_bottom.Children.Add(panel_control);

                _isControlsVisible = true;
                _isMouseOverControls = false;
                panel_control.Opacity = 1;
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
