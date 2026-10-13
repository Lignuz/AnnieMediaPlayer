using System.Reflection;

namespace AnnieMediaPlayer.Windows.Info
{
    /// <summary>
    /// InfoWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class InfoWindow : BaseWindow
    {
        UpdaterViewModel updaterVm = MainWindow.vm.updateVm;

        public InfoWindow(bool checkUpdate = false)
        {
            InitializeComponent();

            DataContext = updaterVm;
            if (checkUpdate)
                _ = updaterVm.CheckUpdateAsync();
        }

        private void Update_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();

            UpdaterWindow win = new UpdaterWindow();
            win.Owner = App.Current.MainWindow;
            win.ShowDialog();
        }

        private void ProjectPage_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            string? url = GetRepositoryUrl();
            if (!string.IsNullOrEmpty(url))
            {
                Utilities.OpenWebPage(url);
            }
        }

        public static string? GetRepositoryUrl()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
            var urlAttr = attributes.FirstOrDefault(attr => attr.Key == "RepositoryUrl");
            return urlAttr?.Value;
        }
    }
}
