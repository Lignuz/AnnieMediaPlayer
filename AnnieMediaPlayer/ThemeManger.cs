using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnnieMediaPlayer
{
    public static class ThemeManager
    {
        private static bool _isDarkTheme = false;
        public static bool IsDarkTheme => _isDarkTheme; 

        public static void ToggleTheme()
        {
            _isDarkTheme = !_isDarkTheme;

            if (_isDarkTheme)
                ApplyDarkTheme();
            else
                ApplyLightTheme();
        }

        public static void ApplyLightTheme()
        {
            AnimateColor("BackgroundBrush", (Color)Application.Current.Resources["LightBackgroundColor"]);
            AnimateColor("BorderBrush", (Color)Application.Current.Resources["LightBorderColor"]);
            AnimateColor("TitleTextBrush", (Color)Application.Current.Resources["LightTitleTextColor"]);
            AnimateColor("ButtonBackgroundBrush", (Color)Application.Current.Resources["LightButtonBackground"]);
            AnimateColor("ButtonBorderBrush", (Color)Application.Current.Resources["LightButtonBorder"]);
            AnimateColor("ButtonHoverBrush", (Color)Application.Current.Resources["LightButtonHoverBackground"]);
            AnimateColor("ButtonPressedBrush", (Color)Application.Current.Resources["LightButtonPressedBackground"]);
            AnimateColor("ButtonDisabledBackgroundBrush", (Color)Application.Current.Resources["LightButtonDisabledBackground"]);
            AnimateColor("ButtonDisabledBorderBrush", (Color)Application.Current.Resources["LightButtonDisabledBorder"]);
            AnimateColor("ButtonDisabledForegroundBrush", (Color)Application.Current.Resources["LightButtonDisabledForeground"]);
            AnimateColor("ThemeButtonFillBrush", (Color)Application.Current.Resources["LightThemeButtonFillColor"]);
            AnimateColor("CloseButtonHoverBrush", (Color)Application.Current.Resources["LightCloseButtonHoverBackground"]);
            AnimateColor("CloseButtonPressedBrush", (Color)Application.Current.Resources["LightCloseButtonPressedBackground"]);
            AnimateColor("SliderBarBrush", (Color)Application.Current.Resources["LightSliderBarColor"]);
            AnimateColor("ThumbBrush", (Color)Application.Current.Resources["LightThumbColor"]);
            AnimateColor("ThumbHoverBrush", (Color)Application.Current.Resources["LightThumbHoverColor"]);
            AnimateColor("ThumbDraggingBrush", (Color)Application.Current.Resources["LightThumbDraggingColor"]);
            AnimateColor("ThumbDisabledBrush", (Color)Application.Current.Resources["LightThumbDisabledColor"]);
            AnimateColor("VideoBackgroundBrush", (Color)Application.Current.Resources["LightVideoBackground"]);
        }

        public static void ApplyDarkTheme()
        {
            AnimateColor("BackgroundBrush", (Color)Application.Current.Resources["DarkBackgroundColor"]);
            AnimateColor("BorderBrush", (Color)Application.Current.Resources["DarkBorderColor"]);
            AnimateColor("TitleTextBrush", (Color)Application.Current.Resources["DarkTitleTextColor"]);
            AnimateColor("ButtonBackgroundBrush", (Color)Application.Current.Resources["DarkButtonBackground"]);
            AnimateColor("ButtonBorderBrush", (Color)Application.Current.Resources["DarkButtonBorder"]);
            AnimateColor("ButtonHoverBrush", (Color)Application.Current.Resources["DarkButtonHoverBackground"]);
            AnimateColor("ButtonPressedBrush", (Color)Application.Current.Resources["DarkButtonPressedBackground"]);
            AnimateColor("ButtonDisabledBackgroundBrush", (Color)Application.Current.Resources["DarkButtonDisabledBackground"]);
            AnimateColor("ButtonDisabledBorderBrush", (Color)Application.Current.Resources["DarkButtonDisabledBorder"]);
            AnimateColor("ButtonDisabledForegroundBrush", (Color)Application.Current.Resources["DarkButtonDisabledForeground"]);
            AnimateColor("ThemeButtonFillBrush", (Color)Application.Current.Resources["DarkThemeButtonFillColor"]);
            AnimateColor("CloseButtonHoverBrush", (Color)Application.Current.Resources["DarkCloseButtonHoverBackground"]);
            AnimateColor("CloseButtonPressedBrush", (Color)Application.Current.Resources["DarkCloseButtonPressedBackground"]);
            AnimateColor("SliderBarBrush", (Color)Application.Current.Resources["DarkSliderBarColor"]);
            AnimateColor("ThumbBrush", (Color)Application.Current.Resources["DarkThumbColor"]);
            AnimateColor("ThumbHoverBrush", (Color)Application.Current.Resources["DarkThumbHoverColor"]);
            AnimateColor("ThumbDraggingBrush", (Color)Application.Current.Resources["DarkThumbDraggingColor"]);
            AnimateColor("ThumbDisabledBrush", (Color)Application.Current.Resources["DarkThumbDisabledColor"]);
            AnimateColor("VideoBackgroundBrush", (Color)Application.Current.Resources["DarkVideoBackground"]);
        }

        private static void AnimateColor(string brushKey, Color toColor)
        {
            if (Application.Current.Resources[brushKey] is SolidColorBrush brush)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
                {
                    To = toColor,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
        }

        // AnimateColor 로 대체합니다.
        private static void SetColor(string brushKey, Color color)
        {
            if (Application.Current.Resources[brushKey] is SolidColorBrush brush)
            {
                if (brush.IsFrozen)
                {
                    // Clone 한 다음 컬러 변경하고 리소스 교체
                    SolidColorBrush clone = brush.Clone();
                    clone.Color = color;
                    Application.Current.Resources[brushKey] = clone;
                }
                else
                {
                    // 그냥 컬러만 변경
                    brush.Color = color;
                }
            }
        }
    }
}
