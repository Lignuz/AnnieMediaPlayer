using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnnieMediaPlayer.Options;
using Unosquare.FFME.Common;
using MediaElement = Unosquare.FFME.MediaElement;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows.Data;

namespace AnnieMediaPlayer
{
    public static class VideoPlayerController
    {
        private static FFMEPlayer _ffmePlayer = null!;
        private static MediaElement _mediaElement = null!;

        // 현재 상태 확인용 프로퍼티 (외부에서 접근할 때 오류 발생 가능성이 있어서 왠만하면 이벤트 핸들러로 받아온 값으로 처리하도록 합시다.)
        private static bool IsMediaValid => _ffmePlayer != null && _mediaElement != null;
        public static TimeSpan CurrentPosition => IsMediaValid && IsOpened ? _ffmePlayer._mediaElement.Position : TimeSpan.Zero;
        public static TimeSpan TotalDuration => IsMediaValid && IsOpened && _ffmePlayer._mediaElement.NaturalDuration != null
            ? (TimeSpan)_ffmePlayer._mediaElement.NaturalDuration
            : TimeSpan.Zero;
        public static MediaPlaybackState PlaybackState => IsMediaValid ? _ffmePlayer._mediaElement.MediaState : MediaPlaybackState.Close;

        public static bool IsOpened { get => (PlaybackState != MediaPlaybackState.Close && PlaybackState != MediaPlaybackState.Stop); }
        public static bool IsPaused { get => (PlaybackState == MediaPlaybackState.Pause); }
        public static bool IsPlaying { get => (PlaybackState == MediaPlaybackState.Play); }
        public static bool IsSeeking { get => IsOpened ? _ffmePlayer._mediaElement.IsSeeking : false; }
        public static double VideoFps { get => IsOpened ? _ffmePlayer._mediaElement.VideoFrameRate : 0.0; }
        public static long CurrentFrameNumber { get => IsOpened ? _ffmePlayer.LastRenderedFrameNumber : 0; }
        public static string CurrentFilePath
        {
            get
            {
                if (!IsMediaValid || !IsOpened)
                    return string.Empty;

                var source = _ffmePlayer._mediaElement.Source;
                return source == null ? string.Empty : source.IsFile ? source.LocalPath : source.OriginalString;
            }
        }

        public static bool IsSliderDragging { get; private set; } = false;
        public static bool IsSliderDraggingOnPlaying { get; private set; } = false;

        // 사용자 스피드 재생 
        public static bool IsCustomSpeedPlaying { get => (PlaybackState == MediaPlaybackState.Play) && (IsNormalSpeed == false); }

        // 이벤트 핸들러
        public static event EventHandler<MediaInitializingEventArgs>? OnMediaInitializing;
        public static event EventHandler<MediaOpeningEventArgs>? OnMediaOpening;
        public static event EventHandler<MediaOpenedEventArgs>? OnMediaOpened;
        public static event EventHandler<EventArgs>? OnMediaReady;
        public static event EventHandler<EventArgs>? OnMediaEnded;
        public static event EventHandler<MediaFailedEventArgs>? OnMediaFailed;
        public static event EventHandler<EventArgs>? OnMediaClosed;
        public static event EventHandler<MediaOpeningEventArgs>? OnMediaChanging;
        public static event EventHandler<MediaOpenedEventArgs>? OnMediaChanged;
        public static event EventHandler<PositionChangedEventArgs>? OnPositionChanged;
        public static event EventHandler<MediaStateChangedEventArgs>? OnMediaStateChanged;
        public static event EventHandler<RenderingVideoEventArgs>? OnVideoFrameRendered;
        public static event EventHandler? OnFrameStepStateChanged;
        public static event EventHandler? OnSpeedIndexChanged;

        // 초기화 메서드 
        public static void Initialize(MediaElement mediaElement)
        {
            _mediaElement = mediaElement ?? throw new ArgumentNullException(nameof(mediaElement));
            _ffmePlayer = new FFMEPlayer(_mediaElement);

            // FFMEPlayer 이벤트 구독
            _ffmePlayer.OnMediaInitializing += FfmePlayer_OnMediaInitializing;
            _ffmePlayer.OnMediaOpening += FfmePlayer_OnMediaOpening;
            _ffmePlayer.OnMediaOpened += FfmePlayer_OnMediaOpened;
            _ffmePlayer.OnMediaReady += FfmePlayer_OnMediaReady;
            _ffmePlayer.OnMediaEnded += FfmePlayer_OnMediaEnded;
            _ffmePlayer.OnMediaFailed += FfmePlayer_OnMediaFailed;
            _ffmePlayer.OnMediaClosed += FfmePlayer_OnMediaClosed;
            _ffmePlayer.OnMediaChanging += FfmePlayer_OnMediaChanging;
            _ffmePlayer.OnMediaChanged += FfmePlayer_OnMediaChanged;
            _ffmePlayer.OnPositionChanged += FfmePlayer_OnPositionChanged;
            _ffmePlayer.OnMediaStateChanged += FfmePlayer_OnMediaStateChanged;
            _ffmePlayer.OnVideoFrameRendered += FfmePlayer_OnVideoFrameRendered;
            _ffmePlayer.OnMessageLogged += FfmePlayer_OnMessageLogged;
            _ffmePlayer.OnAudioDeviceStopped += FfmePlayer_OnAudioDeviceStopped;
            OptionViewModel.Instance.UseSeekFramePreviewChanged += OptionViewModel_UseSeekFramePreviewChanged;
        }

        // 열기 
        public static async Task<bool> Open()
        {
            var dialog = new OpenFileDialog
            {
                Title = LanguageManager.GetResourceString("Text.OpenDialogTitle"),
                Filter = LanguageManager.GetResourceString("Text.OpenDialogFilter")
            };

            if (dialog.ShowDialog() == true)
            {
                return await Open(dialog.FileName);
            }
            return false;
        }

        // 열기 
        public static Task<bool> Open(string filePath)
        {
            if (_ffmePlayer == null) return Task.FromResult(false);

            PlayerDiagnostics.Write($"Open requested: {filePath}");

            Task seekTask;
            Task previewTask;
            bool playWhenOpened;
            lock (_seekRequestSync)
            {
                // 기존 Seek가 끝나기 전에는 MediaElement에 Open을 동시에 요청하지 않습니다.
                if (_seekProcessingStopped || _mediaChanging)
                    return Task.FromResult(false);

                _mediaChanging = true;
                _mediaVersion++;
                _pendingSeekValue = null;
                _previewQueue.Clear();
                seekTask = _seekProcessorCompletion?.Task ?? Task.CompletedTask;
                previewTask = _previewProcessorTask ?? Task.CompletedTask;
                playWhenOpened = OptionViewModel.Instance.CurrentOption.UseOpenPlay;

                var openTask = OpenCoreAsync(filePath, seekTask, previewTask, playWhenOpened);
                _mediaOpenTask = openTask;
                return openTask;
            }
        }

        private static async Task<bool> OpenCoreAsync(string filePath, Task seekTask, Task previewTask, bool playWhenOpened)
        {
            try
            {
                await seekTask;
                await previewTask;

                await _seekOperationGate.WaitAsync();
                try
                {
                    // 프리뷰 작업이 끝난 뒤 기존 FFmpeg 컨텍스트를 먼저 해제합니다.
                    // 새 미디어를 여는 동안 이전 컨텍스트와 네이티브 리소스가 겹치지 않게 합니다.
                    var previousGrabber = ffmpegFrameGrabber;
                    ffmpegFrameGrabber = null;
                    previousGrabber?.Dispose();
                    PlayerDiagnostics.Write("Previous preview decoder disposed; opening media.");

                    var opened = await _ffmePlayer.Open(filePath);
                    PlayerDiagnostics.Write($"FFME Open completed: success={opened}");
                    if (opened == false)
                        return opened;

                    // FFME reports the newly opened media as Stop. Normalize it to
                    // Pause when automatic playback is disabled so the play button
                    // can resume the media from the first frame.
                    if (playWhenOpened == false)
                    {
                        var paused = await _ffmePlayer.Pause();
                        PlayerDiagnostics.Write($"FFME Pause completed: success={paused}");
                        return opened;
                    }

                    var playing = await _ffmePlayer.Play();
                    PlayerDiagnostics.Write($"FFME Play completed: success={playing}");
                    return opened;

                }
                finally
                {
                    _seekOperationGate.Release();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Open 오류: {ex.Message}");
                PlayerDiagnostics.Write($"Open failed: {ex}");
                return false;
            }
            finally
            {
                lock (_seekRequestSync)
                {
                    _mediaChanging = false;
                    _mediaOpenTask = null;
                }
                PlayerDiagnostics.Write("Open finished.");
            }
        }

        // 재생
        public static async Task<bool> Play()
        {
            if (CanIssuePlaybackCommand() == false)
                return false;

            if (_isFrameStepMode)
            {
                ResumeFrameStep();
                return true;
            }
            if (_ffmePlayer == null) return false;
            return await ExecuteMediaCommandAsync(() => _ffmePlayer.Play());
        }

        // 일시 정지
        public static async Task<bool> Pause()
        {
            if (CanIssuePlaybackCommand() == false)
                return false;

            if (_isFrameStepMode)
            {
                PauseFrameStep();
                return true;
            }
            if (_ffmePlayer == null) return false;
            return await ExecuteMediaCommandAsync(() => _ffmePlayer.Pause());
        }

        // 재생 / 일시 정지 토글
        public static async Task<bool> TogglePlayPause()
        {
            if (CanIssuePlaybackCommand() == false)
                return false;

            if (_isFrameStepMode)
            {
                if (_isFrameStepPaused)
                {
                    ResumeFrameStep();
                }
                else
                {
                    PauseFrameStep();
                }
                return true;
            }
            if (PlaybackState == MediaPlaybackState.Play)
                return await Pause();
            else if (PlaybackState == MediaPlaybackState.Pause ||
                     PlaybackState == MediaPlaybackState.Stop)
                return await Play();
            return false;
        }

        // 정지
        public static async Task<bool> Stop()
        {
            if (CanIssuePlaybackCommand() == false)
                return false;

            if (_ffmePlayer == null) return false;
            return await ExecuteMediaCommandAsync(() => _ffmePlayer.Stop());
        }

        // 탐색 (슬라이더에서 호출될 경우)
        public static async Task<bool> Seek(TimeSpan position)
        {
            if (_ffmePlayer == null) return false;
            lock (_seekRequestSync)
            {
                if (_seekProcessingStopped || _mediaChanging)
                    return false;
            }

            await _seekOperationGate.WaitAsync();
            try
            {
                lock (_seekRequestSync)
                {
                    if (_seekProcessingStopped || _mediaChanging)
                        return false;
                }

                if (_ffmePlayer == null || !_ffmePlayer._mediaElement.IsSeekable)
                    return false;

                return await _ffmePlayer.Seek(position);
            }
            finally
            {
                _seekOperationGate.Release();
            }
        }

        // 현재 프레임 앞-뒤로 이동
        public static async Task<bool> SeekStep(bool next = true)
        {
            if (_ffmePlayer == null) return false;
            lock (_seekRequestSync)
            {
                if (_seekProcessingStopped || _mediaChanging)
                    return false;
            }

            return await ExecuteMediaCommandAsync(async () =>
            {
                if (_ffmePlayer == null || !_ffmePlayer._mediaElement.IsSeekable)
                    return false;

                return await _ffmePlayer.SeekStep(next);
            });
        }

        private static bool CanIssuePlaybackCommand()
        {
            lock (_seekRequestSync)
                return _seekProcessingStopped == false && _mediaChanging == false;
        }

        private static async Task<bool> ExecuteMediaCommandAsync(Func<Task<bool>> command)
        {
            if (CanIssuePlaybackCommand() == false)
                return false;

            await _seekOperationGate.WaitAsync();
            try
            {
                if (CanIssuePlaybackCommand() == false)
                    return false;

                return await command();
            }
            finally
            {
                _seekOperationGate.Release();
            }
        }

        // 재생 속도 설정
        public static void SetSpeedRatio(double speed)
        {
            if (_ffmePlayer == null) return;
            _ffmePlayer.SetSpeedRatio(speed);
        }

        public static void SpeedUp()
        {
            if (_ffmePlayer == null) return;

            double speed = _ffmePlayer.GetSpeedRatio();
            speed += 0.1;
            if (speed > 4.0) speed = 4.0; // 최대 속도 제한
            _ffmePlayer.SetSpeedRatio(speed);
        }

        public static void SpeedDown()
        {
            double speed = _ffmePlayer.GetSpeedRatio();
            speed -= 0.1;
            if (speed < 0.1) speed = 0.1; // 최소 속도 제한
            _ffmePlayer.SetSpeedRatio(speed);
        }

        public static double GetSpeedRatio()
        {
            if (_ffmePlayer == null) return 1.0;
            return _ffmePlayer.GetSpeedRatio();
        }

        // 볼륨 설정
        public static void SetVolume(double volume)
        {
            if (_ffmePlayer == null) return;
            _ffmePlayer.SetVolume(volume);
        }

        public static double GetVolume()
        {
            if (_ffmePlayer == null) return 0.0;
            return _ffmePlayer.GetVolume();
        }

        public static void SetVolumeChange(bool up = true)
        {
            if (_ffmePlayer == null) return;

            double volume = _ffmePlayer.GetVolume();
            double setVolume = volume + (up ? 0.1 : -0.1);
            setVolume = Math.Max(0.0, Math.Min(setVolume, 1.0)); // 볼륨 범위 제한 (0.0 ~ 1.0)
            if (volume != setVolume)
            {
                _ffmePlayer.SetVolume(setVolume);
            }
        }

        /////////////////////////////
        // UI 업데이트를 위한 FFMEPlayer 이벤트 핸들러들

        private static void FfmePlayer_OnMediaInitializing(object? sender, MediaInitializingEventArgs e)
        {
            OnMediaInitializing?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaOpening(object? sender, MediaOpeningEventArgs e)
        {
            OnMediaOpening?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaOpened(object? sender, MediaOpenedEventArgs e)
        {
            // 프리뷰 디코더는 실제로 미리보기를 요청할 때 백그라운드에서 생성합니다.
            // 디코더 교체는 OpenCoreAsync에서 직렬화하므로 늦게 도착한 이전
            // MediaOpened 이벤트가 현재 디코더를 해제하지 않도록 합니다.
            OnMediaOpened?.Invoke(sender, e);
        }

        private static void OptionViewModel_UseSeekFramePreviewChanged(object? sender, EventArgs e)
        {
            if (OptionViewModel.Instance.CurrentOption.UseSeekFramePreview)
                return;

            _previewQueue.Clear();
            FFmpegFrameGrabber? grabber;
            lock (_seekRequestSync)
            {
                grabber = ffmpegFrameGrabber;
                ffmpegFrameGrabber = null;
            }

            grabber?.Dispose();
        }

        private static void FfmePlayer_OnMediaReady(object? sender, EventArgs e)
        {
            OnMediaReady?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaEnded(object? sender, EventArgs e)
        {
            OnMediaEnded?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaFailed(object? sender, MediaFailedEventArgs e)
        {
            PlayerDiagnostics.Write($"Media failed: {e.ErrorException}");
            OnMediaFailed?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMessageLogged(object? sender, MediaLogMessageEventArgs e)
        {
            if (e.MessageType == MediaLogMessageType.Warning || e.MessageType == MediaLogMessageType.Error)
            {
                var message = $"FFME {e.MessageType} [{e.AspectName}]: {e.Message}";
                _ = Task.Run(() => PlayerDiagnostics.Write(message));
            }
        }

        private static async void FfmePlayer_OnAudioDeviceStopped(object? sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref _audioDeviceRecoveryStarted, 1) != 0)
                return;

            try
            {
                PlayerDiagnostics.Write("Audio device stopped; recreating audio renderer.");
                var changed = await ExecuteMediaCommandAsync(() => _ffmePlayer.ChangeMedia());
                PlayerDiagnostics.Write($"Audio renderer recreation completed: success={changed}");
            }
            catch (Exception ex)
            {
                PlayerDiagnostics.Write($"Audio renderer recreation failed: {ex}");
            }
            finally
            {
                Volatile.Write(ref _audioDeviceRecoveryStarted, 0);
            }
        }

        private static void FfmePlayer_OnMediaClosed(object? sender, EventArgs e)
        {
            PlayerDiagnostics.Write("Media closed event received.");
            OnMediaClosed?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaChanging(object? sender, MediaOpeningEventArgs e)
        {
            PlayerDiagnostics.Write($"Media changing event received: {e.Info.MediaSource}");
            OnMediaChanging?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaChanged(object? sender, MediaOpenedEventArgs e)
        {
            PlayerDiagnostics.Write($"Media changed event received: {e.Info.MediaSource}");
            OnMediaChanged?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnPositionChanged(object? sender, PositionChangedEventArgs e)
        {
            OnPositionChanged?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
        {
            PlayerDiagnostics.Write($"Media state changed: {e.MediaState}");
            if (e.MediaState == MediaPlaybackState.Close || e.MediaState == MediaPlaybackState.Stop)
            {
                _pendingSeekValue = null;
            }

            if (e.MediaState != MediaPlaybackState.Play)
            {
                ResetFpsMeasurement();

                // 참조가능성이 있어서 일단 유지하고 있도록 합니다.
                // 새 미디어를 열 때까지 현재 grabber는 유지합니다.
            }
            OnMediaStateChanged?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnVideoFrameRendered(object? sender, RenderingVideoEventArgs e)
        {
            var now = Stopwatch.GetTimestamp();
            lock (_fpsSync)
            {
                if (_fpsWindowStartTimestamp == 0)
                {
                    _fpsWindowStartTimestamp = now;
                    _fpsWindowFrameCount = 0;
                }
                else
                {
                    _fpsWindowFrameCount++;
                    var elapsedSeconds = (double)(now - _fpsWindowStartTimestamp) / Stopwatch.Frequency;
                    if (elapsedSeconds >= FpsMeasurementWindowSeconds)
                    {
                        _actualFps = _fpsWindowFrameCount / elapsedSeconds;
                        _fpsWindowStartTimestamp = now;
                        _fpsWindowFrameCount = 0;
                    }
                }
            }

            OnVideoFrameRendered?.Invoke(sender, e);
        }

        private static void ResetFpsMeasurement()
        {
            lock (_fpsSync)
            {
                _actualFps = 0.0;
                _fpsWindowStartTimestamp = 0;
                _fpsWindowFrameCount = 0;
            }
        }

        // VideoPlayerController 해제 (애플리케이션 종료 시 호출)
        public static async Task DisposeAsync()
        {
            Task openTask;
            Task seekTask;
            Task previewTask;
            lock (_seekRequestSync)
            {
                _seekProcessingStopped = true;
                _mediaChanging = true;
                _pendingSeekValue = null;
                _previewQueue.Clear();
                openTask = _mediaOpenTask ?? Task.CompletedTask;
                seekTask = _seekProcessorCompletion?.Task ?? Task.CompletedTask;
                previewTask = _previewProcessorTask ?? Task.CompletedTask;
            }

            // 진행 중인 미디어 열기, Seek, 프리뷰가 끝난 뒤 FFME를 해제합니다.
            await openTask;
            await seekTask;
            await previewTask;
            await _seekOperationGate.WaitAsync();
            _seekOperationGate.Release();

            var grabber = ffmpegFrameGrabber;
            ffmpegFrameGrabber = null;
            grabber?.Dispose();

            UnsubscribeFFMEPlayerEvents();

            if (_ffmePlayer != null)
            {
                await _ffmePlayer.DisposeAsync();
#pragma warning disable CS8625 // Null 리터럴을 null을 허용하지 않는 참조 형식으로 변환할 수 없습니다.
                _ffmePlayer = null;
                _mediaElement = null;
#pragma warning restore CS8625 // Null 리터럴을 null을 허용하지 않는 참조 형식으로 변환할 수 없습니다.
            }
        }

        private static void UnsubscribeFFMEPlayerEvents()
        {
            if (_ffmePlayer == null) return;

            _ffmePlayer.OnMediaInitializing -= FfmePlayer_OnMediaInitializing;
            _ffmePlayer.OnMediaOpening -= FfmePlayer_OnMediaOpening;
            _ffmePlayer.OnMediaOpened -= FfmePlayer_OnMediaOpened;
            _ffmePlayer.OnMediaReady -= FfmePlayer_OnMediaReady;
            _ffmePlayer.OnMediaEnded -= FfmePlayer_OnMediaEnded;
            _ffmePlayer.OnMediaFailed -= FfmePlayer_OnMediaFailed;
            _ffmePlayer.OnMediaClosed -= FfmePlayer_OnMediaClosed;
            _ffmePlayer.OnMediaChanging -= FfmePlayer_OnMediaChanging;
            _ffmePlayer.OnMediaChanged -= FfmePlayer_OnMediaChanged;
            _ffmePlayer.OnPositionChanged -= FfmePlayer_OnPositionChanged;
            _ffmePlayer.OnMediaStateChanged -= FfmePlayer_OnMediaStateChanged;
            _ffmePlayer.OnVideoFrameRendered -= FfmePlayer_OnVideoFrameRendered;
            _ffmePlayer.OnMessageLogged -= FfmePlayer_OnMessageLogged;
            _ffmePlayer.OnAudioDeviceStopped -= FfmePlayer_OnAudioDeviceStopped;
            OptionViewModel.Instance.UseSeekFramePreviewChanged -= OptionViewModel_UseSeekFramePreviewChanged;
        }


        private static TimeSpan[] _playbackSpeeds = new[]
        {
            TimeSpan.FromSeconds(100),
            TimeSpan.FromSeconds(50),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(33.3) // placeholder, will override with actual FPS on open
        };
        private const int _normalSpeedIndex = 5; // 기본 속도 (x1.0)
        private static int _speedIndex = _normalSpeedIndex;
        private static bool _isFrameStepMode = false;
        private static bool _isFrameStepPaused = false;
        private static DispatcherTimer? _frameStepTimer = null;

        private static double _actualFps = 0.0;
        private const double FpsMeasurementWindowSeconds = 1.0;
        private static readonly object _fpsSync = new();
        private static long _fpsWindowStartTimestamp;
        private static int _fpsWindowFrameCount;

        private static readonly object _seekRequestSync = new();
        private static readonly SemaphoreSlim _seekOperationGate = new(1, 1);
        private static TaskCompletionSource<bool>? _seekProcessorCompletion;
        private static bool _seekProcessorRunning;
        private static bool _seekProcessingStopped;
        private static bool _mediaChanging;
        private static long _mediaVersion;
        private static Task? _mediaOpenTask;
        private static int _audioDeviceRecoveryStarted;

        // 슬라이더 탐색에 대한 상태 변수 추가
        private static bool _isPreviewing = false;
        private readonly record struct PreviewRequest(double PositionX, double TrackLength);
        private static readonly ConcurrentQueue<PreviewRequest> _previewQueue = new();
        private static Task? _previewProcessorTask;

        private static TimeSpan? _pendingSeekValue = null; // 대기 중인 Seek 값

        public static TimeSpan[] PlaybackSpeeds => _playbackSpeeds;
        public static int SpeedIndex
        {
            get => _speedIndex;
            set
            {
                if (value != _speedIndex)
                {
                    _speedIndex = value;
                    OnSpeedIndexChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }
        public static bool IsNormalSpeed => SpeedIndex == _normalSpeedIndex; // 1배속인지 확인하는 속성
        public static bool IsFrameStepMode => _isFrameStepMode;
        public static bool IsFrameStepPaused => _isFrameStepPaused;
        public static double ActualFps
        {
            get
            {
                lock (_fpsSync)
                    return _actualFps;
            }
        }
        public static FFmpegFrameGrabber? ffmpegFrameGrabber { get; private set; } = null;

        public static void OnSliderMouseHover(MainWindow window, MouseEventArgs e)
        {
            if (OptionViewModel.Instance.CurrentOption.UseSeekFramePreview == false)
                return;

            if (IsOpened && _mediaChanging == false)
            {
                var slider = window.PlaybackSlider;
                var mousePosition = e.GetPosition(slider);
                var trackLength = slider.ActualWidth;
                if (trackLength <= 0)
                    return;

                // 마우스 이동 이벤트가 프레임 생성보다 빠를 수 있으므로 최신 위치만 유지합니다.
                _previewQueue.Clear();
                _previewQueue.Enqueue(new PreviewRequest(mousePosition.X, trackLength));

                if (!_isPreviewing)
                {
                    _previewProcessorTask = ProcessPreviewQueue(window);
                }
            }
        }

        public static void OnSliderMouseLeave(MainWindow window, MouseEventArgs e)
        {
            _previewQueue.Clear();
            window.OverlayCanvas.Children.Clear();
        }

        private async static Task ProcessPreviewQueue(MainWindow window)
        {
            _isPreviewing = true;
            try
            {
                while (_mediaChanging == false && _previewQueue.TryDequeue(out PreviewRequest request))
                {
                    if (_previewQueue.Count == 0)
                    {
                        var grabber = ffmpegFrameGrabber;
                        long mediaVersion = _mediaVersion;
                        if (grabber == null && OptionViewModel.Instance.CurrentOption.UseSeekFramePreview)
                        {
                            var filePath = CurrentFilePath;
                            if (string.IsNullOrWhiteSpace(filePath) == false)
                                grabber = await CreateFrameGrabberAsync(filePath, mediaVersion);
                        }

                        if (grabber != null)
                        {
                            var slider = window.PlaybackSlider;
                            double ratio = Math.Max(0, Math.Min(1, request.PositionX / request.TrackLength));
                            double seekTime = slider.Minimum + (ratio * (slider.Maximum - slider.Minimum));
                            TimeSpan targetTime = TimeSpan.FromSeconds(seekTime);

                            int w = grabber.Width;
                            int h = grabber.Height;
                            Size size = Utilities.GetScaledSize(w, h, 200, 150);

                            // 프리뷰를 위한 비동기 작업
                            TimeSpan currentTime = TimeSpan.Zero;
                            BitmapSource? bmp;
                            try
                            {
                                bmp = await Task.Run(() => grabber.GetFrameAt(targetTime, size, out currentTime));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Preview frame error: {ex.Message}");
                                continue;
                            }

                            // 프리뷰 이미지 업데이트
                            if (bmp != null && _mediaChanging == false && mediaVersion == _mediaVersion && ReferenceEquals(grabber, ffmpegFrameGrabber))
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    var image = new Image
                                    {
                                        Source = bmp,
                                        Width = size.Width,
                                        Height = size.Height,
                                        Stretch = Stretch.Uniform
                                    };

                                    var textblock = new TextBlock
                                    {
                                        Text = Utilities.FormatTimeSpan(currentTime),
                                        Padding = new Thickness(2),
                                        VerticalAlignment = VerticalAlignment.Top,
                                        HorizontalAlignment = HorizontalAlignment.Left,
                                        Background = (SolidColorBrush)Application.Current.MainWindow.FindResource("BackgroundBrush")
                                    };

                                    var grid = new Grid();
                                    grid.Children.Add(image);
                                    grid.Children.Add(textblock);

                                    var border = new Border
                                    {
                                        Padding = new Thickness(2),
                                        Background = Brushes.Black, // 테두리처럼 보이게
                                        Child = grid
                                    };

                                    // 위치 계산
                                    var target = window.PlaybackSlider;
                                    var mousePos = new Point(request.PositionX, target.ActualHeight / 2);

                                    double targetWidth = target.ActualWidth;
                                    double targetHeight = target.ActualHeight;
                                    double popupWidth = size.Width + (grid.Margin.Left + grid.Margin.Right);
                                    double popupHeight = size.Height + (grid.Margin.Top + grid.Margin.Bottom);

                                    // 재생 슬라이더 기준 10px 위로 배치
                                    double offsetX = mousePos.X - popupWidth / 2;
                                    double offsetY = -(popupHeight + 10);
                                    offsetX = Math.Max(0, Math.Min(offsetX, targetWidth - popupWidth));
                                    Point relativePos = target.TranslatePoint(new Point(offsetX, offsetY), window.OverlayCanvas);

                                    Canvas.SetLeft(border, relativePos.X);
                                    Canvas.SetTop(border, relativePos.Y);

                                    // 기존 요소 제거하고 새로 추가
                                    window.OverlayCanvas.Children.Clear();
                                    window.OverlayCanvas.Children.Add(border);
                                });
                            }
                        }
                    }
                }
            }
            finally
            {
                _isPreviewing = false;
                _previewProcessorTask = null;
            }

            if (_mediaChanging == false && !_previewQueue.IsEmpty)
            {
                _previewProcessorTask = ProcessPreviewQueue(window);
            }
        }

        private static async Task<FFmpegFrameGrabber?> CreateFrameGrabberAsync(string filePath, long mediaVersion)
        {
            FFmpegFrameGrabber? newGrabber = null;
            try
            {
                newGrabber = await Task.Run(() => new FFmpegFrameGrabber(filePath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Create FFmpegFrameGrabber Error: {ex.Message}");
                return null;
            }

            var accepted = false;
            lock (_seekRequestSync)
            {
                if (_mediaChanging == false && mediaVersion == _mediaVersion &&
                    OptionViewModel.Instance.CurrentOption.UseSeekFramePreview &&
                    ffmpegFrameGrabber == null)
                {
                    ffmpegFrameGrabber = newGrabber;
                    accepted = true;
                }
            }

            if (accepted == false)
            {
                newGrabber.Dispose();
                return null;
            }

            return newGrabber;
        }

        public static void OnSliderDragStart(MainWindow window)
        {
            IsSliderDragging = true;
            IsSliderDraggingOnPlaying = IsPlaying;
        }

        public static void OnSliderDragEnd(MainWindow window)
        {
            bool resumePlayback = IsSliderDraggingOnPlaying;
            IsSliderDragging = false;
            IsSliderDraggingOnPlaying = false;

            // 다시 바인딩합니다.
            Binding newBinding = new Binding("Position.TotalSeconds") // 바인딩할 ViewModel 속성 이름
            {
                Mode = BindingMode.OneWay, // 바인딩 모드 (XAML과 동일하게)
            };
            var slider = window.PlaybackSlider;
            slider.SetBinding(Slider.ValueProperty, newBinding);

            _ = ResumeAfterSliderSeekAsync(resumePlayback);
        }

        public static void OnSliderValueChanged(MainWindow window)
        {
            if (IsSliderDragging)
            {
                var time = TimeSpan.FromSeconds(window.PlaybackSlider.Value);
                Debug.WriteLine($"OnSliderValueChanged: {time}");

                if (IsOpened)
                {
                    var seekTime = TimeSpan.FromSeconds(window.PlaybackSlider.Value);

                    _ = PerformSeek(seekTime);
                }
            }
        }

        // 슬라이더 드래그 중에는 가장 최근 요청만 처리합니다.
        public static Task PerformSeek(TimeSpan position)
        {
            lock (_seekRequestSync)
            {
                if (_seekProcessingStopped || _mediaChanging)
                    return Task.CompletedTask;

                _pendingSeekValue = position;
                if (_seekProcessorRunning == false)
                {
                    _seekProcessorRunning = true;
                    _seekProcessorCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ = ProcessPendingSeeksAsync();
                }

                return _seekProcessorCompletion?.Task ?? Task.CompletedTask;
            }
        }

        private static async Task ProcessPendingSeeksAsync()
        {
            while (true)
            {
                TimeSpan position;

                lock (_seekRequestSync)
                {
                    if (_seekProcessingStopped || _pendingSeekValue.HasValue == false)
                    {
                        _seekProcessorRunning = false;
                        _seekProcessorCompletion?.TrySetResult(true);
                        _seekProcessorCompletion = null;
                        return;
                    }

                    position = _pendingSeekValue.Value;
                    _pendingSeekValue = null;
                }

                try
                {
                    await _seekOperationGate.WaitAsync();
                    try
                    {
                        if (_seekProcessingStopped == false && _mediaChanging == false && _ffmePlayer != null && IsOpened)
                        {
                            Debug.WriteLine($"Seek 시작: {position}");
                            await _ffmePlayer.Seek(position);
                            Debug.WriteLine($"Seek 완료: {_ffmePlayer._mediaElement.ActualPosition}");
                        }
                    }
                    finally
                    {
                        _seekOperationGate.Release();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Seek 오류: {ex.Message}");
                }
            }
        }

        private static async Task ResumeAfterSliderSeekAsync(bool resumePlayback)
        {
            if (resumePlayback == false)
                return;

            Task seekTask;
            lock (_seekRequestSync)
            {
                seekTask = _seekProcessorCompletion?.Task ?? Task.CompletedTask;
            }

            await seekTask;
            if (_mediaChanging == false && IsOpened && IsPlaying == false)
                await Play();
        }

        public static void StartFrameStepMode(TimeSpan interval)
        {
            StopFrameStepMode(false);
            _isFrameStepMode = true;
            _isFrameStepPaused = false;
            _frameStepTimer = new DispatcherTimer();
            _frameStepTimer.Interval = interval;
            _frameStepTimer.Tick += async (s, e) =>
            {
                if (!_isFrameStepPaused)
                    await SeekStep(true);
            };
            _frameStepTimer.Start();
            OnFrameStepStateChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void StopFrameStepMode(bool notify = true)
        {
            _isFrameStepMode = false;
            _isFrameStepPaused = false;
            _frameStepTimer?.Stop();
            _frameStepTimer = null;

            if (notify == true)
            {
                OnFrameStepStateChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static void PauseFrameStep()
        {
            if (_isFrameStepMode && !_isFrameStepPaused)
            {
                _isFrameStepPaused = true;
                _frameStepTimer?.Stop();
                OnFrameStepStateChanged?.Invoke(null, EventArgs.Empty);
            }
        }
        public static void ResumeFrameStep()
        {
            if (_isFrameStepMode && _isFrameStepPaused)
            {
                _isFrameStepPaused = false;
                _frameStepTimer?.Start();
                OnFrameStepStateChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static bool IsActuallyPlaying
        {
            get
            {
                // 일반 재생이거나, 슬로우 재생 모드면 "재생 중"으로 간주
                return (PlaybackState == MediaPlaybackState.Play) || _isFrameStepMode;
            }
        }

        public static void IncreaseSpeed()
        {
            if (SpeedIndex < _playbackSpeeds.Length - 1)
            {
                SpeedIndex++;
                ApplyCurrentSpeed();
            }
        }

        public static void DecreaseSpeed()
        {
            if (SpeedIndex > 0)
            {
                SpeedIndex--;
                ApplyCurrentSpeed();
            }
        }

        public static async void ApplyCurrentSpeed()
        {
            // 프레임스텝 모드 해제 전에 "실제 사용자가 원했던 상태"를 저장
            bool wasFrameStepMode = _isFrameStepMode;
            bool wasFrameStepPaused = _isFrameStepPaused;
            bool wasActuallyPlaying = false;

            if (wasFrameStepMode)
                wasActuallyPlaying = !_isFrameStepPaused; // 프레임스텝 모드에서 일시정지 아니면 재생 중
            else
                wasActuallyPlaying = IsPlaying; // 일반 모드에서는 IsPlaying

            // 닫힘 상태에서는 반응하지 않음
            if (IsOpened == false) return;

            if (SpeedIndex < _normalSpeedIndex)
            {
                await Pause();
                StartFrameStepMode(_playbackSpeeds[SpeedIndex]);
                if (!wasActuallyPlaying)
                    PauseFrameStep();
                else
                    ResumeFrameStep();
            }
            else
            {
                StopFrameStepMode();
                SetSpeedRatio(1.0);
                if (wasActuallyPlaying)
                    await Play();
                else
                    await Pause();
            }
        }
    }
}
