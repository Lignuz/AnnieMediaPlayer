# Annie Media Player

Annie Media Player is a WPF-based media player for frame-by-frame navigation, slow-motion video playback, and audio playback.

Key features include:

- Frame-by-frame navigation
- Slow-motion playback (down to one frame every 100 seconds)
- FFmpeg decoding via FFME (Unosquare.FFME.Windows)

## Update configuration

Update checks are disabled when no update settings file is present. To enable them for a build, copy the template into the application project and fill in the HTTPS endpoints:

```powershell
Copy-Item update-settings.example.json AnnieMediaPlayer/update-settings.json
```

Set `ManifestUrl` to an endpoint that returns JSON such as `{"Version":"1.0.0.0","ReleaseNotes":"Release notes"}`. The app adds the installed version as a `version` query parameter. Set `InstallerUrl` to a direct HTTPS download of the Windows installer. The installer must be no larger than 512 MiB.

Build or publish with `AnnieMediaPlayer/update-settings.json` present. The project copies it next to the output executable; the distributed application also needs this file beside its executable. The template contains no real endpoints, and the actual settings file is ignored by Git. Do not commit it. Builds without the file continue to work, but do not check for or install updates.

The updater launches the downloaded installer with administrator privileges. It requires HTTPS but does not currently verify a SHA-256 checksum, so configure only trusted endpoints and installer files.

## License

This project is licensed under the GNU General Public License v3.0.
See the [LICENSE](LICENSE) file for details.

## Dependencies

- FFmpeg (GPL v3) – Precompiled binaries by Gyan.dev: https://www.gyan.dev/ffmpeg/builds/
- FFmpeg.AutoGen (LGPL v3) – https://github.com/Ruslan-B/FFmpeg.AutoGen
- FFME.Windows (Ms-PL) – https://github.com/unosquare/ffmediaelement
- System.Drawing.Common (MIT) – https://www.nuget.org/packages/System.Drawing.Common/9.0.4
- Vortice.Windows (MIT) – Vortice.Direct2D1, Vortice.Direct3D11, Vortice.Direct3D9 – https://github.com/amerkoleci/Vortice.Windows
- SharpGen.Runtime (MIT) – dependency of Vortice.Windows – https://github.com/SharpGenTools/SharpGenTools


📘 다른 언어로 보기: [🇰🇷 한국어](README.ko.md)
