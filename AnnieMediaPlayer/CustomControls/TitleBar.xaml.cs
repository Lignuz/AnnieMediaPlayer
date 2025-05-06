using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AnnieMediaPlayer.CustomControls
{
    /// <summary>
    /// TitleBar.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class TitleBar : UserControl
    {
        public static readonly DependencyProperty TitleTextProperty =
            DependencyProperty.Register("TitleText", typeof(string), typeof(TitleBar), new PropertyMetadata(string.Empty));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }

        public TitleBar()
        {
            InitializeComponent();
        }

        private void DockPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Window.GetWindow(this)?.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) =>
            Window.GetWindow(this)?.Close();
    }
}