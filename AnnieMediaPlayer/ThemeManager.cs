using AnnieMediaPlayer.Options;
using System.Collections;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnnieMediaPlayer
{
    public static class ThemeManager
    {
        public static event EventHandler? ThemeChanged;

        private static Themes _theme = Themes.Light;
        public static Themes theme 
        {
            get { return _theme; } 
            private set 
            { 
                if (_theme != value)
                {
                    _theme = value; 
                }
            } 
        }

        public static void ToggleTheme()
        {
            if (theme == Themes.Light)
            {
                ApplyDarkTheme();
            }
            else if (theme == Themes.Dark)
            {
                ApplyLightTheme();
            }
        }

        public static void ApplyLightTheme()
        {
            ApplyThemeColors(Themes.Light);
        }

        public static void ApplyDarkTheme()
        {
            ApplyThemeColors(Themes.Dark);
        }

        public static void ApplyThemeColors(Themes newTheme, bool animate = true)
        {
            if (theme == newTheme)
                return;

            theme = newTheme;
            string themePrefix = theme.ToString();

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
                            Color color = (Color)colorDict[colorKey];
                            if (animate)
                            {
                                AnimateBrush(brush, color);
                            }
                            else
                            {
                                SetColor(brushKey, color);
                            }
                        }
                    }
                }
            }

            // 변경 후 이벤트 발생
            ThemeChanged?.Invoke(null, EventArgs.Empty);
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

        // 애니메이션 적용
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

        // 애니메이션 미적용으로 바로 교체 (초기 셋팅 변경용)
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
