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
                window.SpeedLabel.Text = $"{(int)speed.TotalSeconds}초/프레임";
            }
            else
            {
                int fps = (int)Math.Round(1.0 / speed.TotalSeconds);
                window.SpeedLabel.Text = $"{fps}fps";
            }
        }
    }
}