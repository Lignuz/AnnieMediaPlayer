using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace AnnieMediaPlayer.Options
{
    public enum Languages
    {
        ko = 0,
        en = 1,
    }

    public enum Themes
    {
        Light = 0,
        Dark = 1,
    };


    public class Option : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public List<Languages> AvailableLanguages { get; set; }
        public List<Themes> AvailableThemes { get; set; }

        public Option()
        {
            AvailableLanguages = Enum.GetValues(typeof(Languages)).Cast<Languages>().ToList();
            AvailableThemes = Enum.GetValues(typeof(Themes)).Cast<Themes>().ToList();
        }

        private Languages _selectedLanguage;
        public Languages SelectedLanguage
        {
            get { return _selectedLanguage; }
            set 
            {
                if (value != _selectedLanguage)
                {
                    _selectedLanguage = value; 
                    OnPropertyChanged(); 
                }
            }
        }

        private Themes _selectedTheme;
        public Themes SelectedTheme
        {
            get { return _selectedTheme; }
            set
            {
                if (value != _selectedTheme)
                {
                    _selectedTheme = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _useOverlayControl;
        public bool UseOverlayControl
        {
            get { return _useOverlayControl; }
            set
            {
                if (value != _useOverlayControl)
                {
                    _useOverlayControl = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    public class EnumToLocalizedStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Themes theme)
            {
                if (OptionViewModel.Instance.ThemeDisplayNames.TryGetValue(theme, out string? key))
                {
                    return Application.Current.Resources[key] as string;
                }
            }
            else if (value is Languages language)
            {
                if (OptionViewModel.Instance.LanguageDisplayNames.TryGetValue(language, out string? key))
                {
                    return Application.Current.Resources[key] as string;
                }
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
