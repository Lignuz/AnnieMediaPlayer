﻿using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Concurrent;
using System.Windows.Media;
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
        public static string CurrentFilePath { get => IsOpened ? _ffmePlayer._mediaElement.Source.AbsolutePath : string.Empty; }

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
        public static async Task<bool> Open(string filePath)
        {
            if (_ffmePlayer == null) return false;
            return await _ffmePlayer.Open(filePath);
        }

        // 재생
        public static async Task<bool> Play()
        {
            if (_isFrameStepMode)
            {
                ResumeFrameStep();
                return true;
            }
            if (_ffmePlayer == null) return false;
            return await _ffmePlayer.Play();
        }

        // 일시 정지
        public static async Task<bool> Pause()
        {
            if (_isFrameStepMode)
            {
                PauseFrameStep();
                return true;
            }
            if (_ffmePlayer == null) return false;
            return await _ffmePlayer.Pause();
        }

        // 재생 / 일시 정지 토글
        public static async Task<bool> TogglePlayPause()
        {
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
            else if (PlaybackState == MediaPlaybackState.Pause)
                return await Play();
            return false;
        }

        // 정지
        public static async Task<bool> Stop()
        {
            if (_ffmePlayer == null) return false;
            return await _ffmePlayer.Stop();
        }

        // 탐색 (슬라이더에서 호출될 경우)
        public static async Task<bool> Seek(TimeSpan position)
        {
            if (_ffmePlayer == null) return false;
            if (_ffmePlayer._mediaElement.IsSeekable)
            {
                return await _ffmePlayer.Seek(position);
            }
            return false;
        }

        // 현재 프레임 앞-뒤로 이동
        public static async Task<bool> SeekStep(bool next = true)
        {
            if (_ffmePlayer == null) return false;
            if (_ffmePlayer._mediaElement.IsSeekable)
            {
                return await _ffmePlayer.SeekStep(next);
            }
            return false;
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
            try
            {
                string path = e.Info.MediaSource;
                if (string.IsNullOrEmpty(path) == false)
                {
                    ffmpegFrameGrabber = new FFmpegFrameGrabber(path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Create FFmpegFrameGrabber Error: {ex.Message}");
            }
            OnMediaOpened?.Invoke(sender, e);
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
            string errorMessage = e.ErrorException.Message;
            OnMediaFailed?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaClosed(object? sender, EventArgs e)
        {
            OnMediaClosed?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaChanging(object? sender, MediaOpeningEventArgs e)
        {
            OnMediaChanging?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaChanged(object? sender, MediaOpenedEventArgs e)
        {
            OnMediaChanged?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnPositionChanged(object? sender, PositionChangedEventArgs e)
        {
            OnPositionChanged?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
        {
            if (e.MediaState == MediaPlaybackState.Close || e.MediaState == MediaPlaybackState.Stop)
            {
                _pendingSeekValue = null;
                _lastRenderDateTime = null;
                _actualFps = 0.0;

                // 참조가능성이 있어서 일단 유지하고 있도록 합니다.
//                 ffmpegFrameGrabber?.Dispose();
//                 ffmpegFrameGrabber = null;
            }
            OnMediaStateChanged?.Invoke(sender, e);
        }

        private static void FfmePlayer_OnVideoFrameRendered(object? sender, RenderingVideoEventArgs e)
        {
            DateTime renderedDateTime = DateTime.UtcNow;
            if (_lastRenderDateTime.HasValue)
            {
                var frameElapsed = renderedDateTime - _lastRenderDateTime;
                if (frameElapsed.Value.TotalSeconds > 0)
                {
                    _actualFps = 1.0 / frameElapsed.Value.TotalSeconds;
                }
            }
            _lastRenderDateTime = renderedDateTime;

            OnVideoFrameRendered?.Invoke(sender, e);
        }

        // VideoPlayerController 해제 (애플리케이션 종료 시 호출)
        public static async Task DisposeAsync()
        {
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
        private static DateTime? _lastRenderDateTime;

        // 슬라이더 탐색에 대한 상태 변수 추가
        private static bool _isPreviewing = false;
        private static ConcurrentQueue<MouseEventArgs> _previewQueue = new ConcurrentQueue<MouseEventArgs>();

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
        public static double ActualFps => _actualFps;
        public static FFmpegFrameGrabber? ffmpegFrameGrabber { get; private set; } = null;

        public static void OnSliderMouseHover(MainWindow window, MouseEventArgs e)
        {
            if (OptionViewModel.Instance.CurrentOption.UseSeekFramePreview == false)
                return;

            if (IsOpened)
            {
                _previewQueue.Enqueue(e);

                if (!_isPreviewing)
                {
                    ProcessPreviewQueue(window);
                }
            }
        }

        public static void OnSliderMouseLeave(MainWindow window, MouseEventArgs e)
        {
            _previewQueue.Clear();
        }

        private async static void ProcessPreviewQueue(MainWindow window)
        {
            _isPreviewing = true;
            while (_previewQueue.TryDequeue(out MouseEventArgs? e))
            {
                if (_previewQueue.Count == 0)
                {
                    if (ffmpegFrameGrabber != null)
                    {
                        Slider slider = window.PlaybackSlider;
                        Point mousePos = e.GetPosition(slider);

                        double trackLength = slider.ActualWidth;
                        double ratio = Math.Max(0, Math.Min(1, mousePos.X / trackLength));
                        double seekTime = slider.Minimum + (ratio * (slider.Maximum - slider.Minimum));
                        TimeSpan targetTime = TimeSpan.FromSeconds(seekTime);

                        int w = ffmpegFrameGrabber.Width;
                        int h = ffmpegFrameGrabber.Height;
                        Size size = Utilities.GetScaledSize(w, h, 200, 150);

                        // 프리뷰를 위한 비동기 작업
                        TimeSpan currentTime = TimeSpan.Zero;
                        var bmp = await Task.Run(() => ffmpegFrameGrabber?.GetFrameAt(targetTime, size, out currentTime));

                        // 프리뷰 이미지 업데이트
                        if (bmp != null)
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
                                mousePos = e.GetPosition(target);

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

            _isPreviewing = false;
            if (!_previewQueue.IsEmpty)
            {
                ProcessPreviewQueue(window);
            }
        }

        public static void OnSliderDragStart(MainWindow window)
        {
            IsSliderDragging = true;
            IsSliderDraggingOnPlaying = IsPlaying;
        }

        public static void OnSliderDragEnd(MainWindow window)
        {
            IsSliderDragging = false;
            IsSliderDraggingOnPlaying = false;

            // 다시 바인딩합니다.
            Binding newBinding = new Binding("Position.TotalSeconds") // 바인딩할 ViewModel 속성 이름
            {
                Mode = BindingMode.OneWay, // 바인딩 모드 (XAML과 동일하게)
            };
            var slider = window.PlaybackSlider;
            slider.SetBinding(Slider.ValueProperty, newBinding);
        }

        public static async void OnSliderValueChanged(MainWindow window)
        {
            if (IsSliderDragging)
            {
                var time = TimeSpan.FromSeconds(window.PlaybackSlider.Value);
                Debug.WriteLine($"OnSliderValueChanged: {time}");

                if (IsOpened)
                {
                    var seekTime = TimeSpan.FromSeconds(window.PlaybackSlider.Value);

                    var _IsSliderDraggingOnPlaying = IsSliderDraggingOnPlaying;
                    await PerformSeek(seekTime);
                    if (_IsSliderDraggingOnPlaying && IsPlaying == false)
                    {
                        await Play();
                    }
                }
            }
        }

        // 비동기적인 Seek 작업을 관리하는 메서드
        public static async Task PerformSeek(TimeSpan position)
        {
            if (_ffmePlayer == null) return;

            // 현재 Seek 작업이 진행 중이라면, 새로운 요청을 대기열에 추가 (덮어쓰기)
            if (IsSeeking)
            {
                _pendingSeekValue = position;
                Debug.WriteLine($"Seek 작업 중. 새로운 Seek 요청 {position} 저장.");
                return;
            }
            
            _pendingSeekValue = null; // 새로운 Seek 시작 시 대기 중인 값 초기화

            try
            {
                Debug.WriteLine($"Seek 시작: {position}");
                await _ffmePlayer.Seek(position);
                Debug.WriteLine($"Seek 완료: {_ffmePlayer._mediaElement.ActualPosition}");

                
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Seek 오류: {ex.Message}");
            }
            finally
            {
                // Seek이 완료된 후, 대기 중인 Seek 요청이 있는지 확인
                if (_pendingSeekValue.HasValue)
                {
                    Debug.WriteLine($"대기 중인 Seek 요청 감지: {_pendingSeekValue.Value}. 다시 Seek를 시작합니다.");
                    // 대기 중인 값으로 다시 Seek 수행 (재귀 호출)
                    // 이때, 재귀 호출을 피하기 위해 직접 PerformSeek을 호출하는 대신,
                    // 대기 중인 값이 처리되도록 하는 플래그를 관리하는 것이 좋습니다.
                    // 여기서는 간단하게 다시 호출하지만, 복잡한 시나리오에서는 TaskCompletionSource 등을 고려할 수 있습니다.
                    var lastPendingValue = _pendingSeekValue.Value;
                    _pendingSeekValue = null; // 재귀 호출 전에 초기화
                    await PerformSeek(lastPendingValue);
                }
            }
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