# FFmpeg source and build notice

This distribution bundles the following separate GPLv3 programs:

- `Tools/ffmpeg.exe` — FFmpeg 8.1.2 essentials build from gyan.dev
- `Tools/ffprobe.exe` — FFmpeg 8.1.2 essentials build from gyan.dev

## Binary provenance

- Original binary archive: `ffmpeg-8.1.2-essentials_build.zip`
- Download: <https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip>
- Publisher-provided archive SHA-256: `db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec`
- Bundled `ffmpeg.exe` SHA-256: `1326dde4c84ff1f96fe6b8916c5bed29e163e9b5dccf995f6f3db069d143ec5e`
- Bundled `ffprobe.exe` SHA-256: `b49ccc7c6547b141ad5a2f6ec69cc04323d7133d7704d70b331b904c63eecb07`
- Build configuration: run `Tools\ffmpeg.exe -buildconf`

## Included upstream source

The package includes the FFmpeg upstream source archive and its official PGP signature:

- `Source/ffmpeg-8.1.2.tar.xz`
  - SHA-256: `464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c`
  - Original URL: <https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz>
- `Source/ffmpeg-8.1.2.tar.xz.asc`
  - SHA-256: `0a0963fccd70597838073f3e31b20f4a4d8cc2b5e577472c9a5a1f22624246f8`
  - Original URL: <https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz.asc>

The full GPLv3 license text is in `FFmpeg-GPL-3.0.txt`.

The Gyan essentials build is a static build that also contains third-party libraries. Its configuration and library list are published at <https://www.gyan.dev/ffmpeg/builds/>. A public distributor must make the complete corresponding source for the exact distributed build, including covered statically linked components, available from the same release location. This notice records provenance and accompanies the upstream FFmpeg source; it is not legal advice.
