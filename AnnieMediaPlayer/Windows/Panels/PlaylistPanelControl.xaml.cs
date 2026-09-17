using System.Windows;
using System.Windows.Controls;

namespace AnnieMediaPlayer.Windows.Panels
{
    public partial class PlaylistPanelControl : UserControl
    {
        public event EventHandler? AddFilesRequested;
        public event EventHandler? RemoveRequested;
        public event EventHandler? ClearRequested;
        public event EventHandler<PlaylistItemViewModel>? ItemDoubleClicked;

        public PlaylistItemViewModel? SelectedItem => PlaylistList.SelectedItem as PlaylistItemViewModel;

        public PlaylistPanelControl()
        {
            InitializeComponent();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddFilesRequested?.Invoke(this, EventArgs.Empty);
        private void RemoveButton_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
        private void ClearButton_Click(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

        private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveButton.IsEnabled = SelectedItem != null;
        }

        private void PlaylistList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectedItem != null &&
                ItemsControl.ContainerFromElement(PlaylistList, e.OriginalSource as DependencyObject) is ListBoxItem)
                ItemDoubleClicked?.Invoke(this, SelectedItem);
        }
    }
}
