using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AnnieMediaPlayer.CustomControls
{
    // 요소 출력에 브러시 레이어 3개를 가산 혼합으로 더하는 효과입니다.
    // WPF 에는 가산 혼합 모드가 없어 픽셀 셰이더(Shaders/AdditiveBlend.fx)로 처리합니다.
    public sealed class AdditiveBlendEffect : ShaderEffect
    {
        private static readonly PixelShader Shader = CreateShader();

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty(nameof(Input), typeof(AdditiveBlendEffect), 0);
        public static readonly DependencyProperty Layer1Property =
            RegisterPixelShaderSamplerProperty(nameof(Layer1), typeof(AdditiveBlendEffect), 1);
        public static readonly DependencyProperty Layer2Property =
            RegisterPixelShaderSamplerProperty(nameof(Layer2), typeof(AdditiveBlendEffect), 2);
        public static readonly DependencyProperty Layer3Property =
            RegisterPixelShaderSamplerProperty(nameof(Layer3), typeof(AdditiveBlendEffect), 3);

        public AdditiveBlendEffect()
        {
            PixelShader = Shader;
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(Layer1Property);
            UpdateShaderValue(Layer2Property);
            UpdateShaderValue(Layer3Property);
        }

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }

        public Brush Layer1
        {
            get => (Brush)GetValue(Layer1Property);
            set => SetValue(Layer1Property, value);
        }

        public Brush Layer2
        {
            get => (Brush)GetValue(Layer2Property);
            set => SetValue(Layer2Property, value);
        }

        public Brush Layer3
        {
            get => (Brush)GetValue(Layer3Property);
            set => SetValue(Layer3Property, value);
        }

        private static PixelShader CreateShader()
        {
            // pack URI 대신 어셈블리 리소스 스트림을 사용하여 Application 이 없는 환경에서도 로드되게 합니다.
            var shader = new PixelShader();
            using (var stream = typeof(AdditiveBlendEffect).Assembly.GetManifestResourceStream("AnnieMediaPlayer.Shaders.AdditiveBlend.ps")
                ?? throw new InvalidOperationException("AdditiveBlend.ps resource not found."))
            {
                shader.SetStreamSource(stream);
            }
            shader.Freeze();
            return shader;
        }
    }
}
