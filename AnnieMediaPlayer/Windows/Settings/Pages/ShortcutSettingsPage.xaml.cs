using System.Windows.Controls;

namespace AnnieMediaPlayer.Windows.Settings.Pages
{
    public class ShortcutKeyInfo
    {
        public string? Shortcut { get; set; }
        public string? Action { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// ShortcutSettingsPage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class ShortcutSettingsPage : UserControl
    {
        public ShortcutSettingsPage()
        {
            InitializeComponent();

            Loaded += ShortcutSettingsPage_Loaded;
            Unloaded += ShortcutSettingsPage_Unloaded;
            LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
        }

        private void ShortcutSettingsPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // DataGrid에 데이터 바인딩
            LoadShortcutKeys();
        }

        private void ShortcutSettingsPage_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            LanguageManager.LanguageChanged -= LanguageManager_LanguageChanged;
        }

        private void LanguageManager_LanguageChanged(object? sender, EventArgs e)
        {
            // 언어 변경 이벤트가 발생하면 DataGrid 데이터를 다시 로드합니다.
            LoadShortcutKeys();
        }

        private void LoadShortcutKeys()
        {
            // LanguageManager.GetResourceString 메서드를 사용하여 규격에 맞는 리소스 문자열을 가져옵니다.
            ShortcutKeyInfo MakeShortcutInfo(string key) => new()
            {
                Shortcut = LanguageManager.GetResourceString($"Text.Shortcut.{key}.Shortcut"),
                Action = LanguageManager.GetResourceString($"Text.Shortcut.{key}.Action"),
                Description = LanguageManager.GetResourceString($"Text.Shortcut.{key}.Description")
            };

            var shortcutKeys = new List<ShortcutKeyInfo>
            {
                MakeShortcutInfo("F5"),
                MakeShortcutInfo("F4"),
                MakeShortcutInfo("Space"),
                MakeShortcutInfo("CtrlSpace"),
                MakeShortcutInfo("LeftArrow"),
                MakeShortcutInfo("RightArrow"),
                MakeShortcutInfo("Tab"),
                MakeShortcutInfo("Enter"),
                MakeShortcutInfo("Z"),
                MakeShortcutInfo("X"),
                MakeShortcutInfo("C"),
                MakeShortcutInfo("CtrlR"),
                MakeShortcutInfo("CtrlShiftR"),
                MakeShortcutInfo("CtrlV"),
                MakeShortcutInfo("CtrlH"),
            };
            ShortcutKeysDataGrid.ItemsSource = shortcutKeys;
        }
    }
}
