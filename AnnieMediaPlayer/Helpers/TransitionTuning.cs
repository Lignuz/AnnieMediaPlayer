using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnnieMediaPlayer
{
    // 화면 전환 효과의 길이·강도. 효과를 조정할 때는 이 값만 바꾸면 됩니다.
    internal static class TransitionTuning
    {
        public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan BlurDuration = TimeSpan.FromMilliseconds(360);

        // 이전 화면이 최대로 흐려질 때의 반경(WPF 블러 반경, DIP)과, 새 화면이 처음 시작할 때의 반경 비율
        public const double BlurRadius = 30;
        public const double IncomingBlurRatio = 0.75;

        // Direct2D 가우시안 블러의 표준편차는 WPF 블러 반경의 절반 정도가 같은 정도의 흐림입니다.
        public const float BlurSigma = (float)(BlurRadius / 2);

        public static IEasingFunction Ease { get; } = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 블러 전환을 쓸 수 있는 상태인지: Windows 애니메이션이 켜져 있고, WPF 가 하드웨어로 렌더링할 때.
        // 그렇지 않으면 페이드로 대체합니다. (소프트웨어 렌더링에서는 블러가 매우 느립니다.)
        public static bool ShouldUseBlur(Visual visual) =>
            SystemParameters.ClientAreaAnimation && !IsSoftwareRendering(visual);

        public static bool IsSoftwareRendering(Visual visual) =>
            SystemParameters.IsRemoteSession || RenderCapability.Tier >> 16 == 0 ||
            RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly ||
            PresentationSource.FromVisual(visual) is HwndSource { CompositionTarget.RenderMode: RenderMode.SoftwareOnly };
    }
}
