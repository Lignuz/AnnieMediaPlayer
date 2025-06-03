using System.Windows;
using System.Windows.Input;
using AnnieMediaPlayer.Windows.Settings;

namespace AnnieMediaPlayer
{
    public static class KeyboardInputHandler
    {
        public static void HandleKeyDown(MainWindow window, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 최대화-일반 모드 토글
                if (window.WindowState == WindowState.Maximized)
                    window.WindowState = WindowState.Normal;
                else
                    window.WindowState = WindowState.Maximized;
                e.Handled = true;
            }
            else if (e.Key == Key.OemTilde)
            {
                (window.Width, window.Height) = (window.MinWidth, window.MinHeight);
                window.WindowState = WindowState.Normal;
                e.Handled = true;
            }
            else if (e.Key == Key.D1)
            {
                (window.Width, window.Height) = (640, 480);
                window.WindowState = WindowState.Normal;
                e.Handled = true;
            }
            else if (e.Key == Key.D2)
            {
                (window.Width, window.Height) = (1280, 960);
                window.WindowState = WindowState.Normal;
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
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
                        _ = VideoPlayerController.Open();
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
            else if (e.Key == Key.Z)
            {
                // 기본 속도
                VideoPlayerController.SetSpeedRatio(1.0);
                window.UpdateSpeedInfo();
                e.Handled = true;
            }
            else if (e.Key == Key.X)
            {
                // 느리게
                VideoPlayerController.SpeedDown();
                window.UpdateSpeedInfo();
                e.Handled = true;
            }
            else if (e.Key == Key.C)
            {
                // 빠르게
                VideoPlayerController.SpeedUp();
                window.UpdateSpeedInfo();
                e.Handled = true;
            }
        }
    }
}