using System.Reflection;

namespace AnnieMediaPlayer.Windows.Info
{
    /// <summary>
    /// InfoWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class InfoWindow : BaseWindow
    {
        public InfoWindow()
        {
            InitializeComponent();

            // 업데이트 확인시 true 로 설정합니다.
            DataContext = new InfoWindowViewModel(checkUpdate: false); 
        }

        private void Update_Click(object sender, System.Windows.RoutedEventArgs e)
        {

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

    public enum UpdateCheckState
    {
        None,           // 해당사항 없음
        Ready,          // 준비중
        CheckUpdate,    // 업데이트 확인중
        Available,      // 업데이트 있음
        NotAvailable,   // 업데이트 없음
        Error,          // 업데이트 확인 실패
    }

    public class InfoWindowViewModel : ViewModelBase
    {
        public InfoWindowViewModel(bool checkUpdate = false)
        {
            if (checkUpdate)
            {
                UpdateCheckState = UpdateCheckState.Ready;
                _ = CheckUpdateAsync();
            }
            else
            {
                UpdateCheckState = UpdateCheckState.None;
            }
        }

        private async Task CheckUpdateAsync()
        {
            UpdateCheckState = UpdateCheckState.CheckUpdate;

            var currentVersion = AppVersionHelper.Version;
            var serverVersion = await CheckServerVersionAsync();
            
            var ret = AppVersionHelper.CompareVersion(currentVersion, serverVersion);
            if (ret == VersionCompareResult.UpToDate)
            {
                UpdateCheckState = UpdateCheckState.NotAvailable;
            }
            else if (ret == VersionCompareResult.UpdateAvailable)
            {
                UpdateCheckState = UpdateCheckState.Available;
            }
            else
            {
                UpdateCheckState = UpdateCheckState.Error;
            }
        }

        private async Task<string> CheckServerVersionAsync()
        {
            // 서버와 통신하여 버전 정보를 가져오는 로직을 구현합니다.
            await Task.Delay(3000); 
            var serverVersion = AppVersionHelper.Version; // 예시로 현재 버전을 반환합니다.

            return serverVersion;
        }

        public UpdateCheckState UpdateCheckState { get => Get(); set => Set(value); }
    }
}
