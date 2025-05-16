using System.Windows;
using System.Windows.Input;
using AnnieMediaPlayer.Windows.Settings;
using FFmpeg.AutoGen;

namespace AnnieMediaPlayer
{
    public static class KeyboardInputHandler
    {
        private static unsafe AVRational GetTimeBase(FFmpegContext context)
        {
            return context.FormatContext->streams[context.VideoStreamIndex]->time_base;
        }
        
        public static void HandleKeyDown(MainWindow window, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
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
                if (VideoPlayerController.IsPlaying)
                    VideoPlayerController.Stop(window);
            }
            else if (e.Key == Key.Space)
            {
                // Ctrl + Space : 정지
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (VideoPlayerController.IsPlaying)
                        VideoPlayerController.Stop(window);
                }
                // Space : 열기, 일시정지/재생 토글
                else
                {
                    if (VideoPlayerController.IsPlaying)
                        VideoPlayerController.TogglePlayPause(window);
                    else
                        VideoPlayerController.OpenVideo(window);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.Right)
            {
                // 1 프레임씩 전후 이동
                // 일시 정지 상태에만 동작합니다.
                if (VideoPlayerController.IsPlaying && VideoPlayerController.IsPaused && VideoPlayerController.Context != null)
                {
                    double step = 1.0 / VideoPlayerController.Context.Fps;
                    double currentSeconds = window.PlaybackSlider.Value;
                    double newTime = e.Key == Key.Left
                        ? Math.Max(0, currentSeconds - step)
                        : Math.Min(VideoPlayerController.VideoDuration.TotalSeconds, currentSeconds + step);

                    window.PlaybackSlider.Value = newTime;

                    AVRational timeBase = VideoPlayerController.streamTimeBase;  //GetTimeBase(VideoPlayerController.Context);
                    long seekTarget = (long)(newTime / ffmpeg.av_q2d(timeBase));

                    var bmp = FFmpegHelper.SeekAndDecodeFrame(VideoPlayerController.Context, seekTarget, out int frameNum, out TimeSpan currentTime, false);
                    if (bmp != null)
                    {
                        window.VideoImage.Source = bmp;
                        window.CurrentTimeText.Text = currentTime.ToString(@"hh\:mm\:ss");
                        window.FrameNumberText.Text = frameNum.ToString();
                    }
                }
                e.Handled = true;
            }
        }
    }
}