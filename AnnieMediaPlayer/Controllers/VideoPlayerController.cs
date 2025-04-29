using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using FFmpeg.AutoGen;

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
            TimeSpan.FromMilliseconds(33.3) // 약 30fps
        };
        private static int _speedIndex = 4;

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
                Title = "비디오 파일 열기",
                Filter = "비디오 파일 (*.mp4;*.avi;*.mov;*.mkv;*.wmv)|*.mp4;*.avi;*.mov;*.mkv;*.wmv|모든 파일 (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                Stop(window);

                _cancellation = new CancellationTokenSource();
                _isPlaying = true;
                _isPaused = false;

                window.PlayPauseButton.IsEnabled = true;
                window.StopButton.IsEnabled = true;
                window.PlayPauseButton.Content = "일시정지";

                try
                {
                    await Task.Run(() =>
                    {
                        FFmpegHelper.OpenVideo(dialog.FileName, (frame, frameNumber, currentTime, totalTime, context) =>
                        {
                            _context = context;
                            unsafe { _streamTimeBase = context.FormatContext->streams[context.VideoStreamIndex]->time_base; }
                            _videoDuration = totalTime;

                            while (_isPaused && !_cancellation.IsCancellationRequested)
                                Thread.Sleep(100);

                            window.Dispatcher.Invoke(() =>
                            {
                                window.VideoImage.Source = frame;
                                window.VideoImage.Stretch = System.Windows.Media.Stretch.Uniform;
                                window.PlaybackSlider.Maximum = totalTime.TotalSeconds;
                                if (!_isSliderDragging)
                                    window.PlaybackSlider.Value = currentTime.TotalSeconds;
                                window.CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                                window.TotalTimeText.Text = totalTime.ToString(@"hh\:mm\:ss");
                                window.FrameNumberText.Text = frameNumber.ToString();
                            });

                            var start = DateTime.UtcNow;
                            while (!_cancellation.IsCancellationRequested)
                            {
                                var delay = _playbackSpeeds[_speedIndex];
                                if (_isPaused)
                                {
                                    Thread.Sleep(100);
                                    continue;
                                }

                                var elapsed = DateTime.UtcNow - start;
                                if (elapsed >= delay)
                                    break;

                                Thread.Sleep(10);
                            }

                        }, _cancellation.Token, () => window.Dispatcher.Invoke(() => Stop(window)));
                    });
                }
                catch (Exception ex)
                {
                    // 예외 발생 시 사용자에게 알림
                    window.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(window, $"비디오를 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        Stop(window);
                    });
                }
            }
        }

        public static void TogglePlayPause(MainWindow window)
        {
            if (!_isPlaying) return;

            _isPaused = !_isPaused;
            window.PlayPauseButton.Content = _isPaused ? "재생" : "일시정지";
        }

        public static void Stop(MainWindow window)
        {
            _isPlaying = false;
            _isPaused = false;
            _cancellation?.Cancel();
            _context = null;
            _streamTimeBase = default;

            window.VideoImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/background01.png"));
            window.VideoImage.Stretch = System.Windows.Media.Stretch.UniformToFill;
            window.CurrentTimeText.Text = "00:00:00";
            window.TotalTimeText.Text = "00:00:00";
            window.FrameNumberText.Text = "0";
            window.PlaybackSlider.Value = 0;
            window.PlayPauseButton.Content = "재생";
            window.PlayPauseButton.IsEnabled = false;
            window.StopButton.IsEnabled = false;
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
                var seekTime = TimeSpan.FromSeconds(window.PlaybackSlider.Value);
                var timeBase = _streamTimeBase;
                long seekTarget = (long)(seekTime.TotalSeconds / ffmpeg.av_q2d(timeBase));

                var bmp = FFmpegHelper.SeekAndDecodeFrame(_context, seekTarget, out int frameNum, out TimeSpan currentTime);
                if (bmp != null)
                {
                    window.VideoImage.Source = bmp;
                    window.CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                    window.FrameNumberText.Text = frameNum.ToString();
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
                _speedIndex++;
        }

        public static void DecreaseSpeed()
        {
            if (_speedIndex > 0)
                _speedIndex--;
        }
    }
}