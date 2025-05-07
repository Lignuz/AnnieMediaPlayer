using AnnieMediaPlayer.Options;
using System.Windows.Controls;

namespace AnnieMediaPlayer.Windows.Settings.Pages
{
    /// <summary>
    /// GeneralSettingsPage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class GeneralSettingsPage : UserControl
    {
        public GeneralSettingsPage()
        {
            InitializeComponent();

            // Binding 으로 처리한 텍스트는 동적 변경이 안되므로 
            // 언어 변경을 감지해서 다시 설정하도록 합니다. 
            LanguageManager.LanguageChanged += (s, e) =>
            {
                var option = OptionViewModel.Instance.CurrentOption;

                combo_languages.ItemsSource = null;
                combo_languages.ItemsSource = option.AvailableLanguages;
                combo_languages.SelectedItem = option.SelectedLanguage;

                combo_themes.ItemsSource = null;
                combo_themes.ItemsSource = option.AvailableThemes;
                combo_themes.SelectedItem = option.SelectedTheme;
            };
        }
    }
}