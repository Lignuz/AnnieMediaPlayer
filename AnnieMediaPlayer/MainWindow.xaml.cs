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
                _videoPath = dialog.FileName;
                _cancellation?.Cancel();
                _cancellation = new CancellationTokenSource();

                await Task.Run(() => PlayVideo(_cancellation.Token));
            }
        }

        private unsafe void PlayVideo(CancellationToken token)
        {
            FFmpegHelper.OpenVideo(_videoPath, (frame, frameNumber) =>
            {
                Dispatcher.Invoke(() =>
                {
                    VideoImage.Source = frame;
                    FrameLabel.Text = frameNumber.ToString();
                });
                Thread.Sleep(1000); // 1초에 1프레임
            }, token);
        }
    }
}