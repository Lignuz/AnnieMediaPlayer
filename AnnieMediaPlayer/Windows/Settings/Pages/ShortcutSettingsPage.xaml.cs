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
            // LanguageManager.GetResourceString 메서드를 사용하여 리소스 문자열을 가져옵니다.
            var shortcutKeys = new List<ShortcutKeyInfo>
            {
                new ShortcutKeyInfo
                {
                    Shortcut = LanguageManager.GetResourceString("Text.Shortcut.F5.Shortcut"),
                    Action = LanguageManager.GetResourceString("Text.Shortcut.F5.Action"),
                    Description = LanguageManager.GetResourceString("Text.Shortcut.F5.Description")
                },
                new ShortcutKeyInfo
                {
                    Shortcut = LanguageManager.GetResourceString("Text.Shortcut.F4.Shortcut"),
                    Action = LanguageManager.GetResourceString("Text.Shortcut.F4.Action"),
                    Description = LanguageManager.GetResourceString("Text.Shortcut.F4.Description")
                },
                new ShortcutKeyInfo
                {
                    Shortcut = LanguageManager.GetResourceString("Text.Shortcut.Space.Shortcut"),
                    Action = LanguageManager.GetResourceString("Text.Shortcut.Space.Action"),
                    Description = LanguageManager.GetResourceString("Text.Shortcut.Space.Description")
                },
                new ShortcutKeyInfo
                {
                    Shortcut = LanguageManager.GetResourceString("Text.Shortcut.CtrlSpace.Shortcut"),
                    Action = LanguageManager.GetResourceString("Text.Shortcut.CtrlSpace.Action"),
                    Description = LanguageManager.GetResourceString("Text.Shortcut.CtrlSpace.Description")
                },
                new ShortcutKeyInfo
                {
                    Shortcut = LanguageManager.GetResourceString("Text.Shortcut.LeftArrow.Shortcut"),
                    Action = LanguageManager.GetResourceString("Text.Shortcut.LeftArrow.Action"),
                    Description = LanguageManager.GetResourceString("Text.Shortcut.LeftArrow.Description")
                },
                new ShortcutKeyInfo
                {
                    Shortcut = LanguageManager.GetResourceString("Text.Shortcut.RightArrow.Shortcut"),
                    Action = LanguageManager.GetResourceString("Text.Shortcut.RightArrow.Action"),
                    Description = LanguageManager.GetResourceString("Text.Shortcut.RightArrow.Description")
                },
                new ShortcutKeyInfo
                {
                    Shortcut = LanguageManager.GetResourceString("Text.Shortcut.Tab.Shortcut"),
                    Action = LanguageManager.GetResourceString("Text.Shortcut.Tab.Action"),
                    Description = LanguageManager.GetResourceString("Text.Shortcut.Tab.Description")
                },
            };

            ShortcutKeysDataGrid.ItemsSource = shortcutKeys;
        }
    }
}
