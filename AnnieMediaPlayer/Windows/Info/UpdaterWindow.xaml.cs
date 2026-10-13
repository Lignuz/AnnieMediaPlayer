using System.Windows;

namespace AnnieMediaPlayer.Windows.Info
{
    /// <summary>
    /// UpdaterWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class UpdaterWindow : BaseWindow
    {
        UpdaterViewModel updaterVm = MainWindow.vm.updateVm;

        public UpdaterWindow()
        {
            InitializeComponent();
            DataContext = updaterVm;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            UpdateButton.IsEnabled = false;
            try
            {
                await updaterVm.DownloadAndInstallUpdateAsync();
            }
            finally
            {
                UpdateButton.IsEnabled = true;
            }
        }
    }
}
