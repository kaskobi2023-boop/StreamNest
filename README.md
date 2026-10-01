# StreamNest Downloader

Windows용 WPF 영상 다운로더입니다. **치지직·YouTube·숲**과 **일반 웹 영상**을 별도 탭으로 제공하며, yt-dlp·FFmpeg·ffprobe를 별도 프로세스로 실행합니다.

현재 버전: **0.7.7**. 단일 다운로드 기준 보존 버전입니다. 소스는 Git 태그 `v0.7.7`, 실행용 ZIP과 해시는 같은 버전의 GitHub Release에서 확인할 수 있습니다. 이후 개발과 구분한 복원 방법은 `RELEASES.md`를 참고하세요.

앱 코드는 MIT 라이선스이며 외부 구성요소에는 각각의 라이선스가 적용됩니다. 다운로드 권한이 있는 콘텐츠에만 사용하세요.

## 0.7.7의 주요 기능

- 치지직 다시보기·클립, YouTube 개별 영상, SOOP VOD·캐치를 기존 서비스 탭에서 분석하고 저장합니다.
- 일반 영상 탭은 기본 분석 실패 시 별도 WebView2 브라우저로 플레이어의 영상 요청을 확인합니다. 브라우저 우선 분석, 화질 선택, 일반 AES-128 HLS와 MP4 저장을 지원합니다.
- 재인코딩 없이 저장하고 조각 누락·빈 파일·필수 영상/음성·선택 화질을 검사합니다. 완료 후 전체 영상을 다시 디코딩하지 않으며, 길이 차이는 경고와 함께 저장합니다.
- 한 번에 한 영상을 분석하고 다운로드합니다. 다중 다운로드와 계정 관리 화면은 이후 버전의 별도 개발 범위입니다.

## SOOP VOD·클립

- 0.7.1: SOOP이 Secure 속성 없이 발급하는 지정된 로그인 티켓을 수집·전달하도록 수정했습니다. 임시 전달 사본은 HTTPS 전용이며 브라우저 원본 쿠키는 변경하지 않습니다. 추적 쿠키만으로 세션 확인을 통과시키지 않습니다.
- `https://vod.sooplive.com/player/영상번호`, 개별 캐치의 `/catch` 주소 및 구 `vod.afreecatv.com` VOD 주소를 같은 플랫폼 탭에 입력합니다.
- 실제 제공되는 화질을 선택해 MP4로 저장합니다. 원본에 없는 1080p를 만들거나 재인코딩하지 않습니다.
- 여러 파일로 나뉜 VOD는 전체 구간에 공통인 화질로 내려받아 하나로 합칩니다. 구간 변경·누락·서로 다른 코덱으로 합치기 실패 시 완료 처리하지 않습니다.
- 제한 영상은 사용자가 앱의 SOOP 창에서 직접 로그인한 뒤 계정 권한 범위에서 재분석합니다. 비밀번호 내장·Chrome 캐시 수집·지역/연령 제한 우회는 구현하지 않습니다.
- 실시간 방송 녹화와 Catch Story 모음 URL은 지원하지 않습니다. 클립은 개별 `player/영상번호` 주소를 사용하세요. DRM·접근 권한이 없는 영상은 지원하지 않습니다.
- 길이 차이는 `영상 길이의 1% (최소 0.5초, 최대 30초)`까지 무경고 허용하고, 초과하거나 길이를 확인할 수 없으면 경고와 함께 저장합니다. 길이만으로 완전성을 보장하지 않습니다.
- 0.7.4부터 전체 디코딩 검사는 수행하지 않습니다. 파일 읽기·필수 스트림·선택 화질 검사 실패와 조각 미수신은 계속 중단합니다. 경고 결과는 UI에서 `저장 완료 · 주의사항 있음`으로 구분합니다.

## 저장소 구성

- `ChzzkDownloader/`: WPF 앱, 일반 웹 Packer/HLS 및 치지직 DASH 보완 플러그인
- `ChzzkDownloader.Tests/`: 단위 테스트
- `ChzzkDownloader.IntegrationTests/`: 자체 생성 HTTP/HLS 시험, 선택적으로 실행하는 외부 네트워크·계정 테스트
- `PluginTests/`: Python 플러그인 회귀 테스트
- `Setup-Tools.ps1`: 검증된 버전의 프로젝트 로컬 도구 준비
- `Package-Source.ps1`: GitHub용 소스 ZIP 생성
- `RELEASES.md`: 0.7.7 보존 기준과 버전별 복원 방법

이 소스 ZIP에는 실행 파일, `bin/obj`, 다운로드 영상, 로그인 프로필·쿠키, 개인 설정, 작업 로그, 이전 배포본을 넣지 않습니다. 실행용 ZIP과 다릅니다. 파일별 해시는 ZIP의 `SOURCE-FILES.sha256`에서 확인할 수 있습니다.

## 빌드 준비

Windows 10/11 x64, .NET SDK 10 및 PowerShell 7 이상이 필요합니다. SDK 선택은 `global.json`을 따릅니다. 제한 영상 로그인에는 WebView2 Runtime이 필요하지만 공개 영상 다운로드는 WebView2 없이 사용할 수 있습니다.

압축을 푼 이 폴더에서 실행합니다:

Windows의 도구 경로 길이 제한을 피하려면 `C:\src\StreamNest`처럼 짧은 경로에 두세요. 깊은 폴더에서 플러그인 파일의 전체 경로가 260자를 넘으면 빌드는 성공해도 번들 엔진이 플러그인을 로드하지 못할 수 있습니다.

```powershell
.\Setup-Tools.ps1
dotnet build .\ChzzkDownloader\ChzzkDownloader.csproj -c Release
```

준비 스크립트는 yt-dlp **2026.08.19**, FFmpeg/ffprobe **8.1.2**, Node.js **24.18.0**을 공식 배포 경로에서 받아 해시를 대조하고 프로젝트 내부에 배치합니다. FFmpeg 대응 소스와 외부 구성요소 고지도 준비합니다. 인터넷 연결이 필요하며 시스템에 전역 설치하지 않습니다.

## 테스트

```powershell
dotnet test .\ChzzkDownloader.Tests\ChzzkDownloader.Tests.csproj -c Release
dotnet test .\ChzzkDownloader.IntegrationTests\ChzzkDownloader.IntegrationTests.csproj -c Release
```

외부 네트워크·실계정 테스트는 기본적으로 건너뜁니다. 로컬 통합 시험은 합성 영상을 생성하며 운영 앱의 공개 HTTPS 주소 정책을 완화하지 않습니다.

Python 플러그인 시험은 Python 3.10 이상에서 실행합니다. 배포 앱 사용자에게 Python 설치는 필요하지 않습니다.

```powershell
py -3 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
.\.venv\Scripts\python.exe -B -m unittest discover -s PluginTests -v
```

선택적으로 공개 YouTube 분석·다운로드 검증:

```powershell
$env:STREAMNEST_RUN_NETWORK_TESTS = '1'
$env:STREAMNEST_RUN_DOWNLOAD_TESTS = '1'
dotnet test .\ChzzkDownloader.IntegrationTests\ChzzkDownloader.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~PublicVideo_AnalyzesWithoutCookies|FullyQualifiedName~PublicVideo_AppDownloadMethodRecognizesFileInKoreanFolder'
```

계정 관련 시험과 자세한 사용법은 `ChzzkDownloader/README.md`를 참고하세요. 쿠키·프로필·작업 로그를 GitHub에 올리지 마세요.

클립·캐치의 실사용 시험은 `STREAMNEST_RUN_CLIPS=1`과 `STREAMNEST_CHZZK_CLIP_URL`, `STREAMNEST_SOOP_CATCH_URL`을 로컬 환경변수로 지정한 뒤 `FullyQualifiedName~LiveClipIntegrationTests` 필터로 실행합니다. 실제 사용 영상 주소를 시험 코드나 공개 기록에 저장하지 않습니다.

## 실행 배포본 / 소스 배포본

```powershell
# 독립 실행 앱, 도구, 법적 고지와 FFmpeg 대응 소스를 포함하는 ZIP
.\ChzzkDownloader\Publish.ps1

# GitHub에 업로드할 소스만 포함하는 ZIP (기본 위치: 바탕화면)
.\Package-Source.ps1
```

실행 배포본은 `dist/`에 생성됩니다. 해당 폴더의 `StreamNestDownloader.exe`를 실행하세요. 소스 ZIP은 압축을 풀어 GitHub 저장소의 루트에 올릴 수 있도록 구성했습니다. 이 스크립트 자체는 GitHub에 업로드하지 않습니다.

## 완료 판정과 제한

- 모든 탭에서 미수신 조각을 건너뛰지 않고 중단합니다. 검증 전 결과는 작업 임시 폴더에만 둡니다.
- 영상·필요한 음성·선택 화질을 미디어 정보로 확인한 뒤 저장합니다. 정보 확인은 15초 제한과 취소를 지원하며, 길이 차이는 경고 저장합니다. 전체 프레임의 디코딩 손상을 검사하는 방식은 아닙니다.
- 기존 파일은 덮어쓰지 않습니다. 화질이 알려진 경우 파일명에 화질을 표시하고 동일 이름에는 번호를 붙입니다.
- 일반 웹은 공통 음성만으로 별개 영상을 합치지 않으며, 주소 갱신 후에도 현재 작업의 정리 대상 경로를 유지합니다.
- 정적 Packer 처리는 제한된 문자열 해석이며 JavaScript를 실행하지 않습니다. 별도 브라우저 분석에서는 실제 페이지 스크립트를 실행합니다. 일반 웹의 모든 난독화·로그인·Cloudflare/CAPTCHA·DRM·실시간 스트림을 지원하는 것은 아닙니다.
- 미디어 검증은 모든 내용 손실을 보장하는 검사가 아니므로, 조각 누락 중단 정책과 함께 적용합니다. 실패 자료와 중단한 조각은 로컬 임시 폴더에 남을 수 있습니다.

변경점은 `CHANGELOG.md`, 외부 구성요소 정보는 `ChzzkDownloader/THIRD_PARTY_NOTICES.md`를 확인하세요.
