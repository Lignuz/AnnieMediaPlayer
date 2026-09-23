// AdditiveBlend.fx — 요소 출력(input)에 레이어 3개를 가산 혼합으로 더합니다.
// 색은 premultiplied alpha 이므로 색과 알파를 그대로 더하고 1 로 자릅니다.
//
// 컴파일 (Windows SDK fxc, 결과 .ps 는 EmbeddedResource 로 포함). fxc 는 BOM 을 읽지 못하므로 이 파일은 BOM 없이 저장합니다:
//   fxc /nologo /T ps_2_0 /E main /Fo AdditiveBlend.ps AdditiveBlend.fx

sampler2D input  : register(s0);
sampler2D layer1 : register(s1);
sampler2D layer2 : register(s2);
sampler2D layer3 : register(s3);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv) + tex2D(layer1, uv) + tex2D(layer2, uv) + tex2D(layer3, uv);
    return saturate(c);
}
