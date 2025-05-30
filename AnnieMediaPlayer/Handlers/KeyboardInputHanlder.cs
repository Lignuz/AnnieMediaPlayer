using System.Windows;
using System.Windows.Input;
using AnnieMediaPlayer.Windows.Settings;

namespace AnnieMediaPlayer
{
    public static class KeyboardInputHandler
    {
        public static void HandleKeyDown(MainWindow window, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                // 속성 패널 토글링
                MainViewModel vm = MainWindow.vm;
                vm.IsPropertiesPanelOpen = !vm.IsPropertiesPanelOpen;
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                // 환경 설정 
                var win = new SettingsWindow
                {
                    Owner = window,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
            else if (e.Key == Key.F4)
            {
                // 정지
                if (VideoPlayerController.IsOpened)
                    _ = VideoPlayerController.Stop();
            }
            else if (e.Key == Key.Space)
            {
                // Ctrl + Space : 정지
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (VideoPlayerController.IsOpened)
                        _ = VideoPlayerController.Stop();
                }
                // Space : 열기, 일시정지/재생 토글
                else
                {
                    if (VideoPlayerController.IsOpened)
                        _ = VideoPlayerController.TogglePlayPause();
                    else
                        _ = VideoPlayerController.OpenAndPlay();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.Right)
            {
                // 1 프레임씩 전후 이동
                // 일시 정지 상태에만 동작합니다.
                if (VideoPlayerController.IsOpened && VideoPlayerController.IsPaused)
                {
                    bool next = (e.Key == Key.Right);
                    _= VideoPlayerController.SeekStep(next);
                }
                e.Handled = true;
            }
        }
    }
}