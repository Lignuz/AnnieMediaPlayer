# 애니 미디어 플레이어

프레임 단위로 이동하고 최대 100초에 1프레임 간격으로 느리게 재생할 수 있는 WPF 기반 미디어 플레이어입니다. 오디오 파일 재생도 지원합니다.

## 업데이트 설정

업데이트 설정 파일이 없으면 업데이트 확인이 비활성화됩니다. 빌드에서 업데이트를 사용하려면 예제 파일을 애플리케이션 프로젝트 폴더에 복사하고 HTTPS 주소를 입력하세요.

```powershell
Copy-Item update-settings.example.json AnnieMediaPlayer/update-settings.json
```

`ManifestUrl`에는 `{"Version":"1.0.0.0","ReleaseNotes":"릴리스 안내"}` 형식의 JSON을 반환하는 주소를 지정합니다. 앱은 설치된 버전을 `version` 쿼리 매개변수로 추가합니다. `InstallerUrl`에는 Windows 설치 프로그램을 직접 내려받을 수 있는 HTTPS 주소를 지정하세요. 설치 파일은 512MiB 이하여야 합니다.

`AnnieMediaPlayer/update-settings.json`이 있는 상태로 빌드하거나 게시하면 프로젝트가 실행 파일 옆에 설정 파일을 복사합니다. 배포본에도 이 파일을 실행 파일과 함께 넣어야 합니다. 예제 파일에는 실제 주소가 없으며, 실제 설정 파일은 Git에서 무시됩니다. 실제 설정 파일은 커밋하지 마세요. 설정 파일 없이 빌드해도 앱은 실행되지만 업데이트 확인과 설치는 동작하지 않습니다.

업데이터는 내려받은 설치 프로그램을 관리자 권한으로 실행합니다. HTTPS 연결은 확인하지만 현재 SHA-256 해시는 검증하지 않으므로, 신뢰할 수 있는 주소와 설치 파일만 설정하세요.

## 라이선스

이 프로젝트는 **GNU 일반 공중 사용 허가서 제3판 (GPL v3)** 하에 배포됩니다.  
자세한 내용은 [LICENSE](LICENSE) 파일을 참고하세요.

## 사용 라이브러리

- FFmpeg (GPL v3) – Precompiled binaries by Gyan.dev: https://www.gyan.dev/ffmpeg/builds/
- FFmpeg.AutoGen (LGPL v3) – https://github.com/Ruslan-B/FFmpeg.AutoGen
- FFME.Windows (Ms-PL) – https://github.com/unosquare/ffmediaelement
- System.Drawing.Common (MIT) – https://www.nuget.org/packages/System.Drawing.Common/9.0.4
- Vortice.Windows (MIT) – Vortice.Direct2D1, Vortice.Direct3D11, Vortice.Direct3D9 – https://github.com/amerkoleci/Vortice.Windows
- SharpGen.Runtime (MIT) – Vortice.Windows 의존 패키지 – https://github.com/SharpGenTools/SharpGenTools
- TagLibSharp (LGPL-2.1-only) – 미디어 태그와 내장 앨범 이미지 편집 – https://github.com/mono/taglib-sharp
