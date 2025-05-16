using System.ComponentModel;
using System.Runtime.CompilerServices;

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
                OnAllOptionsChanged();
                OnPropertyChanged();
            }
        }

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
            // 옵션 초기값 지정
            CurrentOption = new Option
            {
                SelectedTheme = Themes.Light,
                SelectedLanguage = Languages.ko,
                UseOverlayControl = false,
            };
        }

        private void OnAllOptionsChanged()
        {
            ApplyAllOptions(CurrentOption);
        }

        private void CurrentOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 개별 프로퍼티 변경에 따른 처리
            switch (e.PropertyName)
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
                case (nameof(Option.UseSeekFramePreview)):
                    HandleSeekFramePreviewChanged(CurrentOption.UseSeekFramePreview);
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"알 수 없는 프로퍼티 변경: {e.PropertyName}");
                    break;
            }
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
        

        // 전체 옵션 적용 메서드
        private void ApplyAllOptions(Option? currentOptions)
        {
            if (currentOptions != null)
            {
                
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