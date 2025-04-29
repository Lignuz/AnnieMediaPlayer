using System.Windows;

namespace AnnieMediaPlayer
{
    public static class ButtonHelper
    {
        public static readonly DependencyProperty IconModeProperty =
            DependencyProperty.RegisterAttached(
                "IconMode", typeof(string), typeof(ButtonHelper), new PropertyMetadata("Stroke"));

        public static void SetIconMode(UIElement element, string value)
        {
            element.SetValue(IconModeProperty, value);
        }

        public static string GetIconMode(UIElement element)
        {
            return (string)element.GetValue(IconModeProperty);
        }
    }
}