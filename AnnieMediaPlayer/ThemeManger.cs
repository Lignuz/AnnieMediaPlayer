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
            var brushDict = FindResourceDictionary("Brushes.xaml");
            var colorDict = FindResourceDictionary("ThemeColors.xaml");

            if (brushDict == null || colorDict == null)
                return;

            foreach (DictionaryEntry entry in brushDict)
            {
                if (entry.Key is string brushKey && entry.Value is SolidColorBrush brush)
                {
                    if (brushKey.EndsWith("Brush"))
                    {
                        // Brush 키에서 정확히 Brush만 떼고, Color 키 만들기
                        string baseName = brushKey.Substring(0, brushKey.Length - "Brush".Length);
                        string colorKey = themePrefix + baseName + "Color";

                        if (colorDict.Contains(colorKey))
                        {
                            AnimateBrush(brush, (Color)colorDict[colorKey]);
                        }
                    }
                }
            }
        }

        private static ResourceDictionary? FindResourceDictionary(string partialPath)
        {
            foreach (var dict in Application.Current.Resources.MergedDictionaries)
            {
                var result = FindInDictionary(dict, partialPath);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static ResourceDictionary? FindInDictionary(ResourceDictionary dict, string partialPath)
        {
            if (dict.Source != null && dict.Source.OriginalString.Contains(partialPath))
                return dict;

            foreach (var subDict in dict.MergedDictionaries)
            {
                var result = FindInDictionary(subDict, partialPath);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static void AnimateBrush(SolidColorBrush brush, Color toColor)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = toColor,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }

        // (참고용) 
        private static void AnimateColor(string brushKey, Color toColor)
        {
            if (Application.Current.Resources[brushKey] is SolidColorBrush brush)
            {
                AnimateBrush(brush, toColor);
            }
        }

        // (참고용) SetColor는 AnimateColor 도입으로 현재는 사용 안함
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
