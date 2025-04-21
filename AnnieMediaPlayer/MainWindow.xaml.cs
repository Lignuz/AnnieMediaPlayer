using Microsoft.Win32;
using System.Windows;

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
        private int _currentFrame = 0;
        private double _playbackSpeed = 1.0;

        public MainWindow()
        {
            InitializeComponent();
            FFmpegLoader.RegisterFFmpeg(); // FFmpeg 초기화
        }

        private async void OpenVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                StopPlayback(); // 기존 재생 정지

                _videoPath = dialog.FileName;
                _cancellation = new CancellationTokenSource();
                _isPlaying = true;
                _isPaused = false;

                if (double.TryParse(SpeedBox.Text, out double speed))
                {
                    _playbackSpeed = speed;
                }

                await Task.Run(() =>
                {
                    FFmpegHelper.OpenVideo(_videoPath, (frame, frameNumber, currentTime, totalTime) =>
                    {
                        if (_isPaused) return;

                        Dispatcher.Invoke(() =>
                        {
                            VideoImage.Source = frame;
                            _currentFrame = frameNumber;
                            PlaybackSlider.Value = currentTime.TotalSeconds;
                            CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                            TotalTimeText.Text = totalTime.ToString(@"hh\:mm\:ss");
                        });

                        Thread.Sleep((int)(1000 / _playbackSpeed));
                    }, _cancellation.Token);
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
                PlaybackSlider.Value = 0;
                PlayPauseButton.Content = "재생";
            });
        }
    }
}