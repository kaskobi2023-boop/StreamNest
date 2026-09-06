# Third-party notices

이 앱은 다음 외부 구성요소를 별도 프로그램 또는 NuGet 패키지로 사용합니다.

## yt-dlp

- Project: https://github.com/yt-dlp/yt-dlp
- Core project license: The Unlicense
- Bundled version at build time: `2026.08.19`
- Bundled executable third-party notices: `Licenses/yt-dlp-THIRD_PARTY_LICENSES.txt`

## FFmpeg

- Project: https://ffmpeg.org/
- Windows build: https://www.gyan.dev/ffmpeg/builds/
- Bundled build at build time: FFmpeg/ffprobe 8.1.2 essentials, configured with GPL v3 components
- License and source information: https://ffmpeg.org/legal.html and https://ffmpeg.org/download.html#get-sources
- Full GPLv3 text: `Licenses/FFmpeg-GPL-3.0.txt`
- Binary provenance and source notice: `Licenses/FFmpeg-SOURCE.md`
- Binary release includes upstream source: `Licenses/Source/ffmpeg-8.1.2.tar.xz`. The updater also downloads its `.asc` signature into the development directory.
- The GitHub source-only archive excludes vendor executables/source tarballs; run `Setup-Tools.ps1` before building a binary release.

FFmpeg는 이 앱과 별도 프로세스로 실행됩니다.

## Node.js

- Project: https://nodejs.org/
- Bundled version: `24.18.0`
- Purpose: JavaScript challenge runtime used by yt-dlp for current YouTube extraction
- License and third-party notices: `Licenses/node-LICENSE.txt`

## Microsoft Edge WebView2

- Package: Microsoft.Web.WebView2
- Project and terms: https://developer.microsoft.com/microsoft-edge/webview2/
- NuGet: https://www.nuget.org/packages/Microsoft.Web.WebView2

## 참고 프로젝트 (코드 미포함)

- JTech-CO/chzzk-downloader — MIT License — https://github.com/JTech-CO/chzzk-downloader
- JunSeong96/chzk-saver — Apache License 2.0 — https://github.com/JunSeong96/chzk-saver

이 두 프로젝트는 치지직 인증·미디어 흐름을 이해하기 위한 참고 자료이며 저장소의 소스 파일은 이 배포본에 포함하지 않습니다.
