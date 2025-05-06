using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AnnieMediaPlayer
{
    public class BaseWindow : Window
    {
        public static readonly DependencyProperty IsWindowActiveProperty =
            DependencyProperty.Register("IsWindowActive", typeof(bool), typeof(BaseWindow), new PropertyMetadata(true));

        public static readonly DependencyProperty IsSnappedProperty =
            DependencyProperty.Register(nameof(IsSnapped), typeof(bool), typeof(BaseWindow), new PropertyMetadata(false));
        public bool IsWindowActive
        {
            get => (bool)GetValue(IsWindowActiveProperty);
            set => SetValue(IsWindowActiveProperty, value);
        }

        public bool IsSnapped
        {
            get => (bool)GetValue(IsSnappedProperty);
            set => SetValue(IsSnappedProperty, value);
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

        private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}