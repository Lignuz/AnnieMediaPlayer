using FFmpeg.AutoGen;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _videoPath;
        private CancellationTokenSource _cancellation;
        private bool _isPlaying = false;
        private bool _isPaused = false;
        private bool _isSliderDragging = false;
        private int _currentFrame = 0;
        private TimeSpan _videoDuration = TimeSpan.Zero;
        private long _seekTarget = -1;
        private AVRational _streamTimeBase;
        private bool _seekRequested = false;

        private TimeSpan[] _playbackSpeeds = new[]
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(33.3) // 약 30fps
        };
        private int _speedIndex = 2;

        public MainWindow()
        {
            InitializeComponent();
            FFmpegLoader.RegisterFFmpeg();
            UpdateSpeedLabel();

            PlayPauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
        }

        private async void OpenVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                StopPlayback();

                _videoPath = dialog.FileName;
                _cancellation = new CancellationTokenSource();
                _isPlaying = true;
                _isPaused = false;
                _seekRequested = false;
                _currentFrame = 0;

                PlayPauseButton.IsEnabled = true;
                StopButton.IsEnabled = true;
                PlayPauseButton.Content = "일시정지";

                await Task.Run(() =>
                {
                    FFmpegHelper.OpenVideo(_videoPath, (frame, frameNumber, currentTime, totalTime, context) =>
                    {
                        unsafe
                        {
                            _streamTimeBase = context.FormatContext->streams[context.VideoStreamIndex]->time_base;
                        }

                        if (_seekRequested)
                        {
                            unsafe
                            {
                                ffmpeg.av_seek_frame(context.FormatContext, context.VideoStreamIndex, _seekTarget, ffmpeg.AVSEEK_FLAG_BACKWARD);
                                ffmpeg.avcodec_flush_buffers(context.CodecContext);
                            }
                            _seekRequested = false;
                            _currentFrame = 0;
                            return;
                        }

                        while (_isPaused && !_cancellation.IsCancellationRequested)
                        {
                            Thread.Sleep(100);
                        }

                        _videoDuration = totalTime;

                        Dispatcher.Invoke(() =>
                        {
                            VideoImage.Source = frame;
                            _currentFrame = frameNumber;
                            if (!_isSliderDragging)
                            {
                                PlaybackSlider.Maximum = totalTime.TotalSeconds;
                                PlaybackSlider.Value = currentTime.TotalSeconds;
                                CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                            }
                            TotalTimeText.Text = totalTime.ToString(@"hh\:mm\:ss");
                            FrameNumberText.Text = frameNumber.ToString();
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

                    }, _cancellation.Token, () => Dispatcher.Invoke(StopPlayback));
                });
            }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPlaying) return;

            _isPaused = !_isPaused;
            PlayPauseButton.Content = _isPaused ? "재생" : "일시정지";
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void StopPlayback()
        {
            _isPlaying = false;
            _isPaused = false;
            _cancellation?.Cancel();

            Dispatcher.Invoke(() =>
            {
                VideoImage.Source = null;
                CurrentTimeText.Text = "00:00:00";
                TotalTimeText.Text = "00:00:00";
                FrameNumberText.Text = "0";
                PlaybackSlider.Value = 0;
                PlayPauseButton.Content = "재생";
                PlayPauseButton.IsEnabled = false;
                StopButton.IsEnabled = false;
            });
        }

        private void SpeedDown_Click(object sender, RoutedEventArgs e)
        {
            if (_speedIndex > 0)
            {
                _speedIndex--;
                UpdateSpeedLabel();
            }
        }

        private void SpeedUp_Click(object sender, RoutedEventArgs e)
        {
            if (_speedIndex < _playbackSpeeds.Length - 1)
            {
                _speedIndex++;
                UpdateSpeedLabel();
            }
        }

        private void UpdateSpeedLabel()
        {
            var speed = _playbackSpeeds[_speedIndex];
            if (speed.TotalSeconds >= 1)
            {
                SpeedLabel.Text = $"{(int)speed.TotalSeconds}초/프레임";
            }
            else
            {
                int fps = (int)Math.Round(1.0 / speed.TotalSeconds);
                SpeedLabel.Text = $"{fps}fps";
            }
        }

        private void PlaybackSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSliderDragging = true;
        }

        private void PlaybackSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isSliderDragging = false;

            if (_isPlaying)
            {
                var seekTime = TimeSpan.FromSeconds(PlaybackSlider.Value);

                var timeBase = _streamTimeBase; // → FFmpegContext에서 전달받거나 전역 저장 필요
                _seekTarget = (long)(seekTime.TotalSeconds / ffmpeg.av_q2d(timeBase));

                _seekRequested = true;
            }
        }

        private void PlaybackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSliderDragging)
            {
                var time = TimeSpan.FromSeconds(PlaybackSlider.Value);
                CurrentTimeText.Text = time.ToString(@"hh\:mm\:ss");
            }
        }
    }
}