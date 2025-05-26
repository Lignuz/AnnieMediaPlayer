using System.Configuration;
using System.Data;
using System.Windows;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 애플리케이션 종료 시 옵션 저장
            Options.OptionViewModel.Instance.CurrentOption.Save();
            base.OnExit(e);
        }
    }
}
