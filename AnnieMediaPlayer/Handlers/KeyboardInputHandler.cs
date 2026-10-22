using System.Windows;
using System.Windows.Input;
using AnnieMediaPlayer.Options;
using AnnieMediaPlayer.Windows.Settings;

namespace AnnieMediaPlayer
{
    public static class KeyboardInputHandler
    {
        public static void HandleKeyDown(MainWindow window, KeyEventArgs e)
        {
            // 단축키마다 정해진 수정 키 조합일 때만 동작합니다. (예: Ctrl + Z 로 배속이 바뀌지 않도록)
            var modifiers = Keyboard.Modifiers;
            var noModifiers = modifiers == ModifierKeys.None;
            var ctrl = modifiers == ModifierKeys.Control;
            var ctrlShift = modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

            if (e.Key == Key.F1 && noModifiers)
            {
                window.ShowInfoWindow();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && noModifiers)
            {
                // 최대화-일반 모드 토글
                if (window.WindowState == WindowState.Maximized)
                    window.WindowState = WindowState.Normal;
                else
                    window.WindowState = WindowState.Maximized;
                e.Handled = true;
            }
            else if (e.Key == Key.OemTilde && noModifiers)
            {
                (window.Width, window.Height) = (window.MinWidth, window.MinHeight);
                window.WindowState = WindowState.Normal;
                e.Handled = true;
            }
            else if (e.Key == Key.D1 && noModifiers)
            {
                (window.Width, window.Height) = (640, 480);
                window.WindowState = WindowState.Normal;
                e.Handled = true;
            }
            else if (e.Key == Key.D2 && noModifiers)
            {
                (window.Width, window.Height) = (1280, 960);
                window.WindowState = WindowState.Normal;
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && noModifiers)
            {
                // 속성 패널 토글링
                MainViewModel vm = MainWindow.vm;
                vm.IsPropertiesPanelOpen = !vm.IsPropertiesPanelOpen;
                e.Handled = true;
            }
            else if (e.Key == Key.F8 && noModifiers)
            {
                window.TogglePlaylistWindow();
                e.Handled = true;
            }
            else if (e.Key == Key.F5 && noModifiers)
            {
                // 환경 설정
                e.Handled = true;
                var win = new SettingsWindow
                {
                    Owner = window,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
            else if (e.Key == Key.F4 && noModifiers)
            {
                // 정지
                _ = window.StopPlaybackAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Space && (noModifiers || ctrl))
            {
                // Ctrl + Space : 정지
                if (ctrl)
                {
                    _ = window.StopPlaybackAsync();
                }
                // Space : 현재 항목 재생 또는 일시정지 (목록이 비어 있으면 파일 열기)
                else
                {
                    _ = window.PlayOrOpenAsync();
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
                    _ = window.SeekStepWithMessageAsync(next);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Z && noModifiers)
            {
                // 기본 속도
                VideoPlayerController.SetSpeedRatio(1.0);
                window.UpdateSpeedInfo();
                window.NotifySpeedChanged();
                e.Handled = true;
            }
            else if (e.Key == Key.X && noModifiers)
            {
                // 느리게
                VideoPlayerController.SpeedDown();
                window.UpdateSpeedInfo();
                window.NotifySpeedChanged();
                e.Handled = true;
            }
            else if (e.Key == Key.C && noModifiers)
            {
                // 빠르게
                VideoPlayerController.SpeedUp();
                window.UpdateSpeedInfo();
                window.NotifySpeedChanged();
                e.Handled = true;
            }
            else if (e.Key == Key.R && (ctrl || ctrlShift))
            {
                var option = OptionViewModel.Instance.CurrentOption;
                // Ctrl + Shift + R : 반시계 방향으로 회전, Ctrl + R : 시계 방향으로 회전
                option.RotateAngle = ctrlShift
                    ? Utilities.GetPreviousEnumValue<RotateAngle>(option.RotateAngle)
                    : Utilities.GetNextEnumValue<RotateAngle>(option.RotateAngle);
                e.Handled = true;
            }
            else if (e.Key == Key.H && ctrl)
            {
                // Ctrl + H : 좌우 반전
                OptionViewModel.Instance.CurrentOption.UseFlipHorizontal = !OptionViewModel.Instance.CurrentOption.UseFlipHorizontal;
                e.Handled = true;
            }
            else if (e.Key == Key.V && ctrl)
            {
                // Ctrl + V : 상하 반전
                OptionViewModel.Instance.CurrentOption.UseFlipVertical = !OptionViewModel.Instance.CurrentOption.UseFlipVertical;
                e.Handled = true;
            }
        }
    }
}
