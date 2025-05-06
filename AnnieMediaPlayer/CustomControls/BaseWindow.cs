using System.Windows;
using System.Windows.Media;

namespace AnnieMediaPlayer
{
    public class BaseWindow : Window
    {
        public static readonly DependencyProperty IsWindowActiveProperty =
            DependencyProperty.Register("IsWindowActive", typeof(bool), typeof(BaseWindow), new PropertyMetadata(true));

        public bool IsWindowActive
        {
            get => (bool)GetValue(IsWindowActiveProperty);
            set => SetValue(IsWindowActiveProperty, value);
        }

        public BaseWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Style = Application.Current.Resources["BaseWindowStyle"] as Style;

            this.Activated += (_, _) => IsWindowActive = true;
            this.Deactivated += (_, _) => IsWindowActive = false;
        }
    }
}