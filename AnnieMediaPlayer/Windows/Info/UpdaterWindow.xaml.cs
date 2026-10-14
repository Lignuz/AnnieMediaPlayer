using System.Windows;

namespace AnnieMediaPlayer.Windows.Info
{
    /// <summary>
    /// UpdaterWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class UpdaterWindow : BaseWindow
    {
        private readonly UpdaterViewModel updaterVm = MainWindow.vm.updateVm;
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private bool _isClosed;
        private bool _downloadActive;

        public UpdaterWindow()
        {
            InitializeComponent();
            DataContext = updaterVm;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosed || _downloadActive || updaterVm.IsDownloading)
                return;
            _downloadActive = true;
            UpdateButton.IsEnabled = false;
            try
            {
                await updaterVm.DownloadAndInstallUpdateAsync(_lifetimeCancellation.Token);
            }
            finally
            {
                _downloadActive = false;
                if (_isClosed)
                    _lifetimeCancellation.Dispose();
                else
                    UpdateButton.IsEnabled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            _lifetimeCancellation.Cancel();
            if (!_downloadActive)
                _lifetimeCancellation.Dispose();
            base.OnClosed(e);
        }
    }
}
