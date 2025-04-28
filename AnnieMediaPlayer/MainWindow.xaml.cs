using FFmpeg.AutoGen;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Point _mouseDownPoint;
        private bool _isDragging = false;

        private string _videoPath;
        private CancellationTokenSource _cancellation;
        private bool _isPlaying = false;
        private bool _isPaused = false;
        private bool _isSliderDragging = false;
        private int _currentFrame = 0;
        private TimeSpan _videoDuration = TimeSpan.Zero;
        private AVRational _streamTimeBase;
        private FFmpegContext? _context; // 클래스 필드 추가

        private TimeSpan[] _playbackSpeeds = new[]
        {
            TimeSpan.FromSeconds(100),
            TimeSpan.FromSeconds(50),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(33.3) // 약 30fps
        };
        private int _speedIndex = 4;

        public MainWindow()
        {
            InitializeComponent();
            FFmpegLoader.RegisterFFmpeg();
            UpdateSpeedLabel();

            StopPlayback();
            PlayPauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
        }

        private async void OpenVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "비디오 파일 열기",
                Filter = "비디오 파일 (*.mp4;*.avi;*.mov;*.mkv;*.wmv)|*.mp4;*.avi;*.mov;*.mkv;*.wmv|모든 파일 (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                StopPlayback();

                _videoPath = dialog.FileName;
                _cancellation = new CancellationTokenSource();
                _isPlaying = true;
                _isPaused = false;
                _currentFrame = 0;

                PlayPauseButton.IsEnabled = true;
                StopButton.IsEnabled = true;
                PlayPauseButton.Content = "일시정지";

                try
                {
                    await Task.Run(() =>
                    {
                        FFmpegHelper.OpenVideo(_videoPath, (frame, frameNumber, currentTime, totalTime, context) =>
                        {
                            _context = context; // 최초 저장

                            unsafe
                            {
                                _streamTimeBase = context.FormatContext->streams[context.VideoStreamIndex]->time_base;
                            }

                            while (_isPaused && !_cancellation.IsCancellationRequested)
                            {
                                Thread.Sleep(100);
                            }

                            _videoDuration = totalTime;

                            Dispatcher.Invoke(() =>
                            {
                                VideoImage.Source = frame;
                                VideoImage.Stretch = System.Windows.Media.Stretch.Uniform;
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
                catch (Exception ex)
                {
                    // 예외 발생 시 사용자에게 알림
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(this, $"비디오를 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        StopPlayback();
                    });
                }
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
            _context = null;
            _streamTimeBase = default;

            Dispatcher.Invoke(() =>
            {
                // VideoImage.Source = null;
                VideoImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/background01.png"));
                VideoImage.Stretch = System.Windows.Media.Stretch.UniformToFill;
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

            if (_isPlaying && _context != null)
            {
                var seekTime = TimeSpan.FromSeconds(PlaybackSlider.Value);
                var timeBase = _streamTimeBase;
                long seekTarget = (long)(seekTime.TotalSeconds / ffmpeg.av_q2d(timeBase));

                BitmapSource? bmp = FFmpegHelper.SeekAndDecodeFrame(_context, seekTarget, out int frameNum, out TimeSpan currentTime);
                if (bmp != null)
                {
                    VideoImage.Source = bmp;
                    CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                    FrameNumberText.Text = frameNum.ToString();
                }
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                // Ctrl + Space : 정지
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (_isPlaying)
                    {
                        StopPlayback();
                    }   
                }
                // Space : 열기, 일시정지/재생 토글
                else
                {
                    if (_isPlaying)
                    {
                        PlayPause_Click(null, null);
                    }
                    else
                    {
                        OpenVideo_Click(null, null);
                    }
                }
                e.Handled = true; // 포커스를 가진 다른 컨트롤로 전달되지 않게 막음
            }
            else if (e.Key == Key.Left || e.Key == Key.Right)
            {
                if (_isPlaying && _isPaused && _context != null)
                {
                    double step = 1.0 / _context.Fps;
                    double currentSeconds = PlaybackSlider.Value;
                    double newTime = e.Key == Key.Left
                        ? Math.Max(0, currentSeconds - step)
                        : Math.Min(_videoDuration.TotalSeconds, currentSeconds + step);

                    PlaybackSlider.Value = newTime;

                    var timeBase = _streamTimeBase;
                    long seekTarget = (long)(newTime / ffmpeg.av_q2d(timeBase));

                    var bmp = FFmpegHelper.SeekAndDecodeFrame(_context, seekTarget, out int frameNum, out TimeSpan currentTime, false);
                    if (bmp != null)
                    {
                        VideoImage.Source = bmp;
                        CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                        FrameNumberText.Text = frameNum.ToString();
                    }
                }
                e.Handled = true;
            }
        }

        private void ToggleWindowState()
        {
            this.WindowState = (this.WindowState == WindowState.Normal) ? WindowState.Maximized : WindowState.Normal;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
            }
            else if (e.ButtonState == MouseButtonState.Pressed)
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    _mouseDownPoint = e.GetPosition(this);
                    _isDragging = true;
                }

                DragMove(); // 윈도우 드래그
            }
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(this);
                double deltaX = Math.Abs(currentPoint.X - _mouseDownPoint.X);
                double deltaY = Math.Abs(currentPoint.Y - _mouseDownPoint.Y);

                if ((deltaX > SystemParameters.MinimumHorizontalDragDistance) ||
                    (deltaY > SystemParameters.MinimumVerticalDragDistance))
                {
                    _isDragging = false;

                    if (this.WindowState == WindowState.Maximized)
                    {
                        var mouseX = Mouse.GetPosition(this).X;
                        var percent = mouseX / this.ActualWidth;

                        this.WindowState = WindowState.Normal;
                        this.Left = SystemParameters.WorkArea.Width * percent - (this.Width / 2);
                        this.Top = 0;
                    }

                    DragMove();
                }
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.WindowState = WindowState.Maximized;
                MaxRestoreButton.Content = "❐"; // 최대화되면 Restore 아이콘으로 변경
            }
            else
            {
                this.WindowState = WindowState.Normal;
                MaxRestoreButton.Content = "□"; // 복원되면 Maximize 아이콘으로 변경
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}