# Measures the default device output under approximately 70% system CPU load.
# Use a non-periodic audio signal so the loopback monitor can identify repeats.
# One run takes roughly 3 hours: audio/video x background/foreground x 15 minutes x 3.
# Example: powershell -ExecutionPolicy Bypass -File Support\PlaybackStallBench\run-output-bench.ps1 -VideoBasePath C:\media\sample.mp4
param(
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Name = "current",
    [string]$FfmeRoot = "",
    [string]$VideoBasePath = "",
    [string]$FfmpegExe = "",
    [ValidateRange(1, 3600)][int]$DurationSeconds = 900,
    [ValidateRange(1, 100)][int]$Repeats = 3,
    [ValidateRange(0, 100)][int]$LoadDuty = 70,
    [switch]$PrepareOnly
)

$ErrorActionPreference = "Stop"
$benchDir = $PSScriptRoot
$repoDir = (Resolve-Path (Join-Path $benchDir "..\..")).Path
$workDir = Join-Path $benchDir ".variants"
$mediaDir = Join-Path $workDir "media"
New-Item -ItemType Directory -Force $workDir, $mediaDir | Out-Null

if (-not $FfmeRoot) { $FfmeRoot = Join-Path $repoDir "ExternalLibs\FFME" }
if (-not $VideoBasePath) { $VideoBasePath = Join-Path $mediaDir "video.mp4" }
if (-not (Test-Path -LiteralPath $VideoBasePath)) {
    throw "비교용 영상이 없습니다. -VideoBasePath 로 720p 영상을 지정해 주세요."
}
$VideoBasePath = (Resolve-Path -LiteralPath $VideoBasePath).Path
$FfmeRoot = (Resolve-Path -LiteralPath $FfmeRoot).Path

if (-not $FfmpegExe) {
    $ffmpegCommand = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($ffmpegCommand) { $FfmpegExe = $ffmpegCommand.Source }
    elseif (Test-Path 'C:\Program Files\GOM\GOMCam2024\ffmpeg.exe') {
        $FfmpegExe = 'C:\Program Files\GOM\GOMCam2024\ffmpeg.exe'
    }
}
if (-not $FfmpegExe -or -not (Test-Path -LiteralPath $FfmpegExe)) {
    throw "ffmpeg 실행 파일을 찾지 못했습니다. -FfmpegExe 를 지정해 주세요."
}

$mediaSeconds = $DurationSeconds + 60
$noiseWav = Join-Path $mediaDir "noise-$mediaSeconds.wav"
$noiseMp3 = Join-Path $mediaDir "noise-$mediaSeconds.mp3"
$audioFile = Join-Path $mediaDir "noise-audio-$mediaSeconds.mp3"
$videoFile = Join-Path $mediaDir "noise-video-$mediaSeconds.mp4"
if (-not (Test-Path -LiteralPath $audioFile) -or -not (Test-Path -LiteralPath $videoFile)) {
    # 48 kHz, stereo, 16-bit PCM. The random sequence is reproducible and
    # non-periodic. Encoding applies -14 dB before the bench's 0.1 volume.
    $sampleRate = 48000
    $bytesPerSecond = $sampleRate * 4
    $dataBytes = [long]$bytesPerSecond * $mediaSeconds
    if ($dataBytes -gt [int]::MaxValue - 36) { throw "테스트 WAV가 RIFF 크기 제한을 초과합니다." }
    $writer = [IO.BinaryWriter]::new([IO.File]::Create($noiseWav))
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes("RIFF"))
        $writer.Write([int](36 + $dataBytes))
        $writer.Write([Text.Encoding]::ASCII.GetBytes("WAVEfmt "))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write([int16]2)
        $writer.Write([int]$sampleRate)
        $writer.Write([int]$bytesPerSecond)
        $writer.Write([int16]4)
        $writer.Write([int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes("data"))
        $writer.Write([int]$dataBytes)
        $random = [Random]::new(12345)
        $buffer = [byte[]]::new($bytesPerSecond)
        for ($second = 0; $second -lt $mediaSeconds; $second++) {
            $random.NextBytes($buffer)
            $writer.Write($buffer)
        }
    }
    finally { $writer.Dispose() }

    & $FfmpegExe -hide_banner -loglevel error -y -i $noiseWav -af volume=0.2 -b:a 192k $noiseMp3
    if ($LASTEXITCODE -ne 0) { throw "노이즈 오디오 인코딩 실패" }

    $coverFile = Join-Path $mediaDir "cover.jpg"
    if (Test-Path -LiteralPath $coverFile) {
        & $FfmpegExe -hide_banner -loglevel error -y -i $noiseMp3 -i $coverFile -map 0:a -map 1:v -c copy -id3v2_version 3 -disposition:v attached_pic $audioFile
    }
    else { Copy-Item -LiteralPath $noiseMp3 -Destination $audioFile -Force }
    if ($LASTEXITCODE -ne 0) { throw "노이즈 오디오 생성 실패" }

    & $FfmpegExe -hide_banner -loglevel error -y -stream_loop -1 -i $VideoBasePath -i $noiseMp3 -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -t $mediaSeconds $videoFile
    if ($LASTEXITCODE -ne 0) { throw "노이즈 포함 영상 생성 실패" }
    Remove-Item -LiteralPath $noiseWav
}

if ($PrepareOnly) {
    Write-Host "오디오: $audioFile"
    Write-Host "영상: $videoFile"
    return
}

$sourceDir = Join-Path $workDir "$Name-output\src"
$binDir = Join-Path $workDir "$Name-output\bin"
New-Item -ItemType Directory -Force $sourceDir, $binDir | Out-Null
Copy-Item (Join-Path $benchDir "PlaybackStallBench.csproj"), (Join-Path $benchDir "Program.cs"), (Join-Path $benchDir "LoopbackMonitor.cs") $sourceDir -Force
& dotnet build (Join-Path $sourceDir "PlaybackStallBench.csproj") -nologo -v:q "-p:FfmeRoot=$FfmeRoot" -o $binDir
if ($LASTEXITCODE -ne 0) { throw "벤치마크 빌드 실패" }

$env:BENCH_FFMPEG_DIR = Join-Path $repoDir "ffmpeg"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$csv = Join-Path $workDir "output-$Name-$stamp.csv"
foreach ($media in @($audioFile, $videoFile)) {
    foreach ($scenario in @("bg-load", "fg-load")) {
        & (Join-Path $binDir "PlaybackStallBench.exe") bench --media $media --variant $Name --csv $csv `
            --parallel-rendering false --audio-cache 256 --process-priority Normal `
            --repeats $Repeats --ui-alloc-kb 0 --scenario $scenario `
            --load-ms ($DurationSeconds * 1000) --load-duty $LoadDuty --target-cpu 70
        if ($LASTEXITCODE -ne 0) { throw "벤치마크 실패: $media / $scenario" }
    }
}

$rows = Import-Csv -LiteralPath $csv
foreach ($row in $rows) {
    $cpu = [double]::Parse($row.system_cpu_percent, [Globalization.CultureInfo]::InvariantCulture)
    if ($cpu -lt 65 -or $cpu -gt 75) {
        Write-Warning "$($row.media) / $($row.scenario) / $($row.repeat): 시스템 CPU $cpu%로 목표 65~75% 범위를 벗어났습니다."
    }
    if ([int]$row.device_silence_ms -ge 30 -or [int]$row.device_repeat_ms -ge 30 -or [int]$row.stuck -ne 0) {
        Write-Warning "$($row.media) / $($row.scenario) / $($row.repeat): 장치 출력 끊김 또는 재생 멈춤이 관찰됐습니다."
    }
}
Write-Host "장치 출력 결과: $csv"
Write-Host "명령 응답 시간: $csv.controls.csv"
