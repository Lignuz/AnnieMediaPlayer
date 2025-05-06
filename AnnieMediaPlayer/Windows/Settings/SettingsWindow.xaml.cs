using System.Windows;
using System.Windows.Controls;

namespace AnnieMediaPlayer.Windows.Settings
{
    /// <summary>
    /// SettingsWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class SettingsWindow : BaseWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();

            SettingsNav.SelectedIndex = 0; // 기본 페이지로 General 설정 페이지를 선택합니다.
        }

        private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsNav.SelectedItem is ListBoxItem item && item.Tag is string pageKey)
            {
                NavigateToPage(pageKey);
            }
        }

        private void NavigateToPage(string pageKey)
        {
            UserControl page = pageKey switch
            {
                "General" => new Pages.GeneralSettingsPage(),
                "Advanced" => new Pages.AdvancedSettingsPage(),
                _ => new Pages.GeneralSettingsPage()
            };
            ContentArea.Content = page;
        }
    }
}
