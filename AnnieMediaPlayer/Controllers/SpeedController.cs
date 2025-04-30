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
            if (speed.TotalSeconds >= 1)
            {
                TextBlock textBlock = window.SpeedLabel;
                textBlock.Inlines.Clear();

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
                window.SpeedLabel.Text = $"{fps}fps";
            }
        }
    }
}