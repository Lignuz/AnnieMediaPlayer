using System.Collections;
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
            ApplyThemeColors("Light");
        }

        public static void ApplyDarkTheme()
        {
            ApplyThemeColors("Dark");
        }

        private static void ApplyThemeColors(string themePrefix)
        {
            var themeDict = Application.Current.Resources.MergedDictionaries
                              .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("ThemeColors.xaml"));

            if (themeDict == null)
                return;

            foreach (DictionaryEntry entry in themeDict)
            {
                if (entry.Key is string brushKey && entry.Value is SolidColorBrush)
                {
                    if (brushKey.EndsWith("Brush"))
                    {
                        string colorKey = themePrefix + brushKey.Replace("Brush", "Color");

                        if (Application.Current.Resources.Contains(colorKey))
                        {
                            AnimateColor(brushKey, (Color)Application.Current.Resources[colorKey]);
                        }
                    }
                }
            }
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
