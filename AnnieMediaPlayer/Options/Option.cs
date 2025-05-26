using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        [JsonIgnore]
        public List<Languages> AvailableLanguages => Enum.GetValues(typeof(Languages)).Cast<Languages>().ToList();
        [JsonIgnore]
        public List<Themes> AvailableThemes => Enum.GetValues(typeof(Themes)).Cast<Themes>().ToList();

        private static readonly string OptionFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnnieMediaPlayer", "options.json");

        public Option()
        {
            // 옵션 초기값 지정
            SelectedTheme = Themes.Light;
            SelectedLanguage = Languages.ko;
            UseOverlayControl = false;
            UseSeekFramePreview = false;
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

        private bool _useSeekFramePreview;
        public bool UseSeekFramePreview
        {
            get { return _useSeekFramePreview; }
            set
            {
                if (value != _useSeekFramePreview)
                {
                    _useSeekFramePreview = value;
                    OnPropertyChanged();
                }
            }
        }

        // 옵션 저장
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(OptionFilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(OptionFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"옵션 저장 실패: {ex.Message}");
            }
        }

        // 옵션 불러오기
        public static Option Load()
        {
            try
            {
                if (File.Exists(OptionFilePath))
                {
                    var json = File.ReadAllText(OptionFilePath);
                    var option = JsonSerializer.Deserialize<Option>(json);
                    if (option != null)
                    {
                        return option;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"옵션 불러오기 실패: {ex.Message}");
            }
            // 기본값 반환
            return new Option();
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
