using System.Windows.Controls;
using System.Windows.Documents;

namespace AnnieMediaPlayer
{
    public static class SpeedController
    {
        public static void SpeedUp(MainWindow window)
        {
            VideoPlayerController.IncreaseSpeed();
            UpdateSpeedLabel(window);
        }

        public static void SpeedDown(MainWindow window)
        {
            VideoPlayerController.DecreaseSpeed();
            UpdateSpeedLabel(window);
        }

        public static void UpdateSpeedLabel(MainWindow window)
        {
            var speed = VideoPlayerController.PlaybackSpeeds[VideoPlayerController.SpeedIndex];
            TextBlock textBlock = window.SpeedLabel;
            textBlock.Inlines.Clear();

            // 원본 속도 (1배속) 표시
            if (VideoPlayerController.SpeedIndex == 5)
            {
                if (VideoPlayerController.Context != null)
                {
                    double fps = VideoPlayerController.Context.Fps;
                    string fpsStr = fps.ToString("0.00");

                    Run speedRun = new Run("1배속 (");
                    Run fpsRun = new Run(fpsStr + "fps");
                    Run closeParen = new Run(")");
                    textBlock.Inlines.Add(speedRun);
                    textBlock.Inlines.Add(fpsRun);
                    textBlock.Inlines.Add(closeParen);
                }
                else
                {
                    // 영상이 없을 때는 단순히 "1배속"만 표시
                    textBlock.Inlines.Add(new Run("1배속"));
                }
            }
            else
            {
                if (speed.TotalSeconds >= 1)
                {
                    // 속도 값
                    Run valueRun = new Run(speed.TotalSeconds.ToString());
                    textBlock.Inlines.Add(valueRun);

                    // 단위 (초)
                    Run secondRun = LanguageManager.GetLocalizedRun("Text.Sec");
                    textBlock.Inlines.Add(secondRun);

                    // 슬래시
                    textBlock.Inlines.Add(new Run("/"));

                    // 단위 (프레임)
                    Run frameRun = LanguageManager.GetLocalizedRun("Text.Frame");
                    textBlock.Inlines.Add(frameRun);
                }
                else
                {
                    int fps = (int)Math.Round(1.0 / speed.TotalSeconds);
                    textBlock.Text = $"{fps}fps";
                }
            }

            UpdateActualSpeedLabel(window);
        }

        public static void UpdateActualSpeedLabel(MainWindow window)
        {
            string actualStr = "";
            double actualFps = VideoPlayerController.ActualFps;
            if (VideoPlayerController.Context != null)
            {
                actualStr = actualFps > 0 ? actualFps.ToString("0.00") : "-";
                actualStr = $"{actualStr}fps";
            }
            window.ActualFpsText.Text = actualStr;
        }
    }
}