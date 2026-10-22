using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AnnieMediaPlayer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private bool _showingUnexpectedError;

        // "연결 프로그램"·파일 연결로 실행할 때 받은 파일·폴더 경로입니다. 메인 창이 열린 뒤 재생합니다.
        internal static IReadOnlyList<string> StartupPaths { get; private set; } = Array.Empty<string>();

        protected override void OnStartup(StartupEventArgs e)
        {
            StartupPaths = e.Args;

            // 처리되지 않은 예외를 진단 로그에 남깁니다. UI 스레드 예외는 알린 뒤 계속 실행합니다.
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                PlayerDiagnostics.Write($"Unhandled exception (terminating={args.IsTerminating}): {args.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                PlayerDiagnostics.Write($"Unobserved task exception: {args.Exception}");
                args.SetObserved();
            };

            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            PlayerDiagnostics.Write($"Dispatcher unhandled exception: {e.Exception}");

            // 메인 창을 만드는 중의 오류는 계속 실행해도 쓸 수 있는 창이 없으므로 그대로 종료합니다.
            if (MainWindow is not { IsLoaded: true })
                return;

            e.Handled = true;

            // 오류 창을 띄운 동안 같은 오류가 반복되면 창이 겹쳐 쌓이므로 로그만 남깁니다.
            if (_showingUnexpectedError)
                return;

            _showingUnexpectedError = true;
            try
            {
                MessageBox.Show(
                    string.Format(LanguageManager.GetResourceString("Text.Error.Unexpected"), e.Exception.Message, PlayerDiagnostics.LogPath),
                    LanguageManager.GetResourceString("Text.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _showingUnexpectedError = false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 대기 중인 변경이 있을 수 있으므로 종료할 때 한 번 더 저장합니다.
            Options.OptionViewModel.Instance.SaveNow();
            base.OnExit(e);
        }
    }
}
