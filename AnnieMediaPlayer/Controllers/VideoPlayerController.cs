using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using FFmpeg.AutoGen;
using System.Windows.Controls;
using System.Diagnostics;

namespace AnnieMediaPlayer
{
    public static class VideoPlayerController
    {
        private static CancellationTokenSource? _cancellation;
        private static FFmpegContext? _context;
        private static AVRational _streamTimeBase;
        private static bool _isPlaying;
        private static bool _isPaused;
        private static bool _isSliderDragging;
        private static TimeSpan _videoDuration = TimeSpan.Zero;
        private static TimeSpan[] _playbackSpeeds = new[]
        {
            TimeSpan.FromSeconds(100),
            TimeSpan.FromSeconds(50),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(33.3) // placeholder, will override with actual FPS on open
        };
        private static int _speedIndex = 4;

        public static bool PauseForSeeking { get; set; } = false;
        public static bool WaitForPauseForSeeking { get; set; } = false;
        private static DateTime _lastFrameTime = DateTime.MinValue;
        private static double _actualFps = 0.0;
        public static double ActualFps => _actualFps;
        private static TimeSpan _currentVideoTime = TimeSpan.Zero; // 현재 비디오 시간을 저장할 필드
        public static TimeSpan CurrentVideoTime => _currentVideoTime;
        public static DateTime PlaybackStartTime { get; set; }


        public static void InitializeUI(MainWindow window)
        {
            _isPlaying = false;
            _isPaused = false;
            _context = null;
            _cancellation = null;
            window.PlayPauseButton.IsEnabled = false;
            window.StopButton.IsEnabled = false;
        }

        public static async void OpenVideo(MainWindow window)
        {
            var dialog = new OpenFileDialog
            {
                Title = LanguageManager.GetResourceString("Text.OpenDialogTitle"),
                Filter = LanguageManager.GetResourceString("Text.OpenDialogFilter")
            };

            if (dialog.ShowDialog() == true)
            {
                Stop(window);

                _cancellation = new CancellationTokenSource();
                _isPlaying = true;
                _isPaused = false;

                window.PlayPauseButton.IsEnabled = true;
                window.StopButton.IsEnabled = true;
                window.PlayPauseButton.SetResourceReference(ContentControl.ContentProperty, "Text.Pause");

                try
                {
                    PlaybackStartTime = DateTime.UtcNow;
                    await Task.Run(() =>
                    {
                        FFmpegHelper.OpenVideo(dialog.FileName, (frame, frameNumber, currentTime, totalTime, context) =>
                        {
                            _context = context;
                            unsafe { _streamTimeBase = context.FormatContext->streams[context.VideoStreamIndex]->time_base; }
                            _videoDuration = totalTime;
                            _currentVideoTime = currentTime; // 현재 비디오 시간 업데이트

                            // ⚠ FPS 기반 딜레이 재계산 (playbackSpeeds[5])
                            double fps = context.Fps;
                            if (fps > 0)
                                _playbackSpeeds[5] = TimeSpan.FromMilliseconds(1000.0 / fps);

                            window.Dispatcher.Invoke(() =>
                            {
                                window.FileNameText.Tag = dialog.FileName;
                                window.VideoImage.Source = frame;
                                window.VideoImage.Stretch = System.Windows.Media.Stretch.Uniform;
                                window.PlaybackSlider.Maximum = totalTime.TotalSeconds;
                                if (!_isSliderDragging)
                                    window.PlaybackSlider.Value = currentTime.TotalSeconds;
                                window.CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                                window.TotalTimeText.Text = totalTime.ToString(@"hh\:mm\:ss");
                                window.FrameNumberText.Text = frameNumber.ToString();

                                // 실제 FPS 계산
                                if (!_isPaused && _lastFrameTime != DateTime.MinValue)
                                {
                                    var frameElapsed = DateTime.UtcNow - _lastFrameTime;
                                    if (frameElapsed.TotalSeconds > 0)
                                        _actualFps = 1.0 / frameElapsed.TotalSeconds;
                                }
                                _lastFrameTime = DateTime.UtcNow;

                                // 속도 라벨도 즉시 업데이트
                                SpeedController.UpdateSpeedLabel(window);
                            });
                        }, _cancellation.Token, () => window.Dispatcher.Invoke(() => Stop(window)));
                    });
                }
                catch (Exception ex)
                {
                    // 예외 발생 시 사용자에게 알림
                    window.Dispatcher.Invoke(() =>
                    {
                        string desc = LanguageManager.GetResourceString("Text.OpenVideoFailed");
                        string title = LanguageManager.GetResourceString("Text.Error");
                        MessageBox.Show(window, $"{desc}:\n{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);
                        Stop(window);
                    });
                }
            }
        }

        public static void TogglePlayPause(MainWindow window)
        {
            if (!_isPlaying) return;

            _isPaused = !_isPaused;
            PlaybackStartTime = DateTime.UtcNow - _currentVideoTime;
            window.PlayPauseButton.SetResourceReference(ContentControl.ContentProperty, _isPaused ? "Text.Play" : "Text.Pause");
        }

        public static void Stop(MainWindow window)
        {
            _isPlaying = false;
            _isPaused = false;
            _cancellation?.Cancel();
            _context = null;
            _streamTimeBase = default;

            _actualFps = 0.0;
            _lastFrameTime = DateTime.MinValue;
            _currentVideoTime = TimeSpan.Zero;
            WaitForPauseForSeeking = false;

            window.FileNameText.Tag = "";
            window.VideoImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/background01.png"));
            window.VideoImage.Stretch = System.Windows.Media.Stretch.UniformToFill;
            window.CurrentTimeText.Text = "00:00:00";
            window.TotalTimeText.Text = "00:00:00";
            window.FrameNumberText.Text = "0";
            window.PlaybackSlider.Value = 0;
            window.PlayPauseButton.SetResourceReference(ContentControl.ContentProperty, "Text.Play");
            window.PlayPauseButton.IsEnabled = false;
            window.StopButton.IsEnabled = false;

            SpeedController.UpdateActualSpeedLabel(window);
        }

        public static void OnSliderDragStart(MainWindow window)
        {
            _isSliderDragging = true;
        }

        public static void OnSliderDragEnd(MainWindow window)
        {
            _isSliderDragging = false;

            if (_isPlaying && _context != null)
            {
                PauseForSeeking = true;
                Thread.Sleep(50); // 디코딩 쓰레드가 멈출 시간을 줌

                try
                {
                    var seekTime = TimeSpan.FromSeconds(window.PlaybackSlider.Value);
                    var timeBase = _streamTimeBase;
                    long seekTarget = (long)(seekTime.TotalSeconds / ffmpeg.av_q2d(timeBase));

                    var bmp = FFmpegHelper.SeekAndDecodeFrame(_context, seekTarget, out int frameNum, out TimeSpan currentTime);
                    if (bmp != null)
                    {
                        _currentVideoTime = currentTime;
                        PlaybackStartTime = DateTime.UtcNow - _currentVideoTime;
                        WaitForPauseForSeeking = true;

                        window.Dispatcher.Invoke(() =>
                        {
                            window.VideoImage.Source = bmp;
                            window.CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                            window.FrameNumberText.Text = frameNum.ToString();
                        });
                    }
                }
                finally
                {
                    PauseForSeeking = false;
                }
            }
        }

        public static void OnSliderValueChanged(MainWindow window)
        {
            if (_isSliderDragging)
            {
                var time = TimeSpan.FromSeconds(window.PlaybackSlider.Value);
                window.CurrentTimeText.Text = time.ToString(@"hh\:mm\:ss");
            }
        }

        public static FFmpegContext? Context => _context;
        public static bool IsPlaying => _isPlaying;
        public static bool IsPaused => _isPaused;
        public static int SpeedIndex => _speedIndex;
        public static TimeSpan[] PlaybackSpeeds => _playbackSpeeds;
        public static TimeSpan VideoDuration => _videoDuration;
        public static AVRational streamTimeBase => _streamTimeBase;

        public static void IncreaseSpeed()
        {
            if (_speedIndex < _playbackSpeeds.Length - 1)
            {
                _actualFps = 0;
                _speedIndex++;
                PlaybackStartTime = DateTime.UtcNow - _currentVideoTime;
            }
        }

        public static void DecreaseSpeed()
        {
            if (_speedIndex > 0)
            {
                _actualFps = 0;
                _speedIndex--;
                PlaybackStartTime = DateTime.UtcNow - _currentVideoTime;
            }
        }
    }
}