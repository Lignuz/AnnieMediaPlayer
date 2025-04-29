using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AnnieMediaPlayer
{
    public static class ThemeManager
    {
        private static bool _isDarkTheme = false;

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
            SetColor("BackgroundBrush", (Color)Application.Current.Resources["LightBackgroundColor"]);
            SetColor("BorderBrush", (Color)Application.Current.Resources["LightBorderColor"]);
            SetColor("TitleTextBrush", (Color)Application.Current.Resources["LightTitleTextColor"]);
            SetColor("ButtonBackgroundBrush", (Color)Application.Current.Resources["LightButtonBackground"]);
            SetColor("ButtonBorderBrush", (Color)Application.Current.Resources["LightButtonBorder"]);
            SetColor("ButtonHoverBrush", (Color)Application.Current.Resources["LightButtonHoverBackground"]);
            SetColor("ButtonPressedBrush", (Color)Application.Current.Resources["LightButtonPressedBackground"]);
            SetColor("ButtonDisabledBackgroundBrush", (Color)Application.Current.Resources["LightButtonDisabledBackground"]);
            SetColor("ButtonDisabledBorderBrush", (Color)Application.Current.Resources["LightButtonDisabledBorder"]);
            SetColor("ButtonDisabledForegroundBrush", (Color)Application.Current.Resources["LightButtonDisabledForeground"]);
            SetColor("ThemeButtonFillBrush", (Color)Application.Current.Resources["LightThemeButtonFillColor"]);
            SetColor("CloseButtonHoverBrush", (Color)Application.Current.Resources["LightCloseButtonHoverBackground"]);
            SetColor("CloseButtonPressedBrush", (Color)Application.Current.Resources["LightCloseButtonPressedBackground"]);
            SetColor("SliderBarBrush", (Color)Application.Current.Resources["LightSliderBarColor"]);
            SetColor("ThumbBrush", (Color)Application.Current.Resources["LightThumbColor"]);
            SetColor("ThumbHoverBrush", (Color)Application.Current.Resources["LightThumbHoverColor"]);
            SetColor("ThumbDraggingBrush", (Color)Application.Current.Resources["LightThumbDraggingColor"]);
            SetColor("ThumbDisabledBrush", (Color)Application.Current.Resources["LightThumbDisabledColor"]);
            SetColor("VideoBackgroundBrush", (Color)Application.Current.Resources["LightVideoBackground"]);
        }

        public static void ApplyDarkTheme()
        {
            SetColor("BackgroundBrush", (Color)Application.Current.Resources["DarkBackgroundColor"]);
            SetColor("BorderBrush", (Color)Application.Current.Resources["DarkBorderColor"]);
            SetColor("TitleTextBrush", (Color)Application.Current.Resources["DarkTitleTextColor"]);
            SetColor("ButtonBackgroundBrush", (Color)Application.Current.Resources["DarkButtonBackground"]);
            SetColor("ButtonBorderBrush", (Color)Application.Current.Resources["DarkButtonBorder"]);
            SetColor("ButtonHoverBrush", (Color)Application.Current.Resources["DarkButtonHoverBackground"]);
            SetColor("ButtonPressedBrush", (Color)Application.Current.Resources["DarkButtonPressedBackground"]);
            SetColor("ButtonDisabledBackgroundBrush", (Color)Application.Current.Resources["DarkButtonDisabledBackground"]);
            SetColor("ButtonDisabledBorderBrush", (Color)Application.Current.Resources["DarkButtonDisabledBorder"]);
            SetColor("ButtonDisabledForegroundBrush", (Color)Application.Current.Resources["DarkButtonDisabledForeground"]);
            SetColor("ThemeButtonFillBrush", (Color)Application.Current.Resources["DarkThemeButtonFillColor"]);
            SetColor("CloseButtonHoverBrush", (Color)Application.Current.Resources["DarkCloseButtonHoverBackground"]);
            SetColor("CloseButtonPressedBrush", (Color)Application.Current.Resources["DarkCloseButtonPressedBackground"]);
            SetColor("SliderBarBrush", (Color)Application.Current.Resources["DarkSliderBarColor"]);
            SetColor("ThumbBrush", (Color)Application.Current.Resources["DarkThumbColor"]);
            SetColor("ThumbHoverBrush", (Color)Application.Current.Resources["DarkThumbHoverColor"]);
            SetColor("ThumbDraggingBrush", (Color)Application.Current.Resources["DarkThumbDraggingColor"]);
            SetColor("ThumbDisabledBrush", (Color)Application.Current.Resources["DarkThumbDisabledColor"]);
            SetColor("VideoBackgroundBrush", (Color)Application.Current.Resources["DarkVideoBackground"]);
        }

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
