using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AnnieMediaPlayer.Options
{
    public class OptionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static OptionViewModel? _instance;
        public static OptionViewModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new OptionViewModel();
                }
                return _instance;
            }
        }

        private Option _currentOption = null!; // CS8618 방지
        public Option CurrentOption
        {
            get { return _currentOption; }
            set
            {
                if (_currentOption != null)
                {
                    _currentOption.PropertyChanged -= CurrentOption_PropertyChanged;
                }
                _currentOption = value;
                if (_currentOption != null)
                {
                    _currentOption.PropertyChanged += CurrentOption_PropertyChanged;
                }
                OnPropertyChanged();
            }
        }

        public Option DefaultOption => new Option(); // 기본 옵션 인스턴스 생성

        /// <summary>
        /// Enum 값의 다국어 처리를 위한 연결 리소스 딕셔너리
        /// </summary>
        public Dictionary<Themes, string> ThemeDisplayNames { get; } = new Dictionary<Themes, string>
        {
            { Themes.Light, "Text.Themes.Light" },
            { Themes.Dark, "Text.Themes.Dark" }
        };

        public Dictionary<Languages, string> LanguageDisplayNames { get; } = new Dictionary<Languages, string>
        {
            { Languages.ko, "Text.Languages.Korean" },
            { Languages.en, "Text.Languages.English" }
        };


        private OptionViewModel()
        {
            // 옵션 불러오기
            CurrentOption = Option.Load();
        }

        private void CurrentOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            CurrentOption_PropertyChanged(e.PropertyName);
        }

        private void CurrentOption_PropertyChanged(string? propertyName)
        {
            // 개별 프로퍼티 변경에 따른 처리
            switch (propertyName)
            {
                case nameof(Option.SelectedTheme):
                    HandleThemeChanged(CurrentOption.SelectedTheme);
                    break;
                case nameof(Option.SelectedLanguage):
                    HandleLanguageChanged(CurrentOption.SelectedLanguage);
                    break;
                case nameof(Option.UseOverlayControl):
                    HandleUseOverlayControlChanged(CurrentOption.UseOverlayControl);
                    break;
                case nameof(Option.UseSeekFramePreview):
                    HandleSeekFramePreviewChanged(CurrentOption.UseSeekFramePreview);
                    break;
                default:
                    Debug.WriteLine($"알 수 없는 프로퍼티 변경: {propertyName}");
                    break;
            }
        }

        public class ChangedOptionProperty
        {
            public ChangedOptionProperty(string name, object? def, object? cur)
            {
                this.name = name;
                this.def = def;
                this.cur = cur;
            }

            public string name { get; set; } = string.Empty;
            public object? def { get; set; }
            public object? cur { get; set; }
        }

        // 기준옵션과 비교하여 변경된 프로퍼티를 확인합니다.
        public List<ChangedOptionProperty> CheckOptionChanged(Option baseOption, Option currentOption)
        {
            // 변경된 프로퍼티 이름과 값을 저장할 리스트
            var changedProperties = new List<ChangedOptionProperty>();

            foreach (var prop in typeof(Option).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                // 읽기/쓰기 가능한 프로퍼티만
                if (!prop.CanRead || !prop.CanWrite) continue;

                // [JsonIgnore] 등으로 제외하고 싶은 프로퍼티는 Attribute로 필터링 가능
                if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

                var defaultValue = prop.GetValue(baseOption);
                var currentValue = prop.GetValue(currentOption);

                if (!Equals(defaultValue, currentValue))
                {
                    changedProperties.Add(new ChangedOptionProperty(prop.Name, defaultValue, currentValue));
                }
            }

            return changedProperties;
        }


        /// <summary>
        /// 개별 프로퍼티 변경 처리 메서드
        /// </summary>
        private void HandleLanguageChanged(Languages newLanguage)
        {
            LanguageManager.ChangeLanguage(newLanguage);
        }

        private void HandleThemeChanged(Themes newTheme)
        {
            ThemeManager.ApplyThemeColors(newTheme);
        }

        public event EventHandler? UseOverlayControlChanged;
        private void HandleUseOverlayControlChanged(bool newUseOverlayControl)
        {
            if (UseOverlayControlChanged != null)
            {
                UseOverlayControlChanged(this, EventArgs.Empty);
            }
        }

        public event EventHandler? UseSeekFramePreviewChanged;
        private void HandleSeekFramePreviewChanged(bool newUseOverlayControl)
        {
            if (UseSeekFramePreviewChanged != null)
            {
                UseSeekFramePreviewChanged(this, EventArgs.Empty);
            }
        }

        // 기본 옵션과 현재 옵션을 비교하여 변경된 프로퍼티를 확인해서 알림을 발생시킵니다.
        public void OptionChanged(Option baseOption, Option currentOption)
        {   
            var changedProperties = CheckOptionChanged(baseOption, currentOption);
            foreach (var changedProperty in changedProperties)
            {
                Debug.WriteLine($"{changedProperty.name}: 기본값={changedProperty.def}, 현재값={changedProperty.cur}");
                CurrentOption_PropertyChanged(changedProperty.name);
            }
        }


        /// <summary>
        /// 옵션 변경 관련 유틸리티 메서드 
        /// </summary>
        public void ToggleTheme()
        {
            if (CurrentOption.SelectedTheme == Themes.Dark)
                CurrentOption.SelectedTheme = Themes.Light;
            else if (CurrentOption.SelectedTheme == Themes.Light)
                CurrentOption.SelectedTheme = Themes.Dark;
        }
    }
}