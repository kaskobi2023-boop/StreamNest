# StreamNest Downloader

Windows용 WPF 영상 다운로더입니다. **치지직·YouTube**와 **일반 웹 영상**을 별도 탭으로 제공하며, yt-dlp·FFmpeg·ffprobe를 별도 프로세스로 실행합니다.

현재 버전: **0.6.6**. 앱 코드는 MIT 라이선스이며 외부 구성요소에는 각각의 라이선스가 적용됩니다. 다운로드 권한이 있는 콘텐츠에만 사용하세요.

## 저장소 구성

- `ChzzkDownloader/`: WPF 앱, 일반 웹 Packer/HLS 및 치지직 DASH 보완 플러그인
- `ChzzkDownloader.Tests/`: 단위 테스트
- `ChzzkDownloader.IntegrationTests/`: 자체 생성 HTTP/HLS 시험, 선택적으로 실행하는 외부 네트워크·계정 테스트
- `PluginTests/`: Python 플러그인 회귀 테스트
- `Setup-Tools.ps1`: 검증된 버전의 프로젝트 로컬 도구 준비
- `Package-Source.ps1`: GitHub용 소스 ZIP 생성

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
- 영상·필요한 음성·선택 화질·예상 길이와 전체 디코딩을 검증한 후 최종 저장합니다. 검사 중에도 취소할 수 있으며, 긴 영상의 검사는 추가 시간이 필요합니다.
- 기존 파일은 덮어쓰지 않습니다. 화질이 알려진 경우 파일명에 화질을 표시하고 동일 이름에는 번호를 붙입니다.
- 일반 웹은 공통 음성만으로 별개 영상을 합치지 않으며, 주소 갱신 후에도 현재 작업의 정리 대상 경로를 유지합니다.
- Packer 처리는 제한된 문자열 해석이며 JavaScript를 실행하지 않습니다. 모든 난독화·로그인·Cloudflare/CAPTCHA·DRM·실시간 스트림을 지원하는 것은 아닙니다.
- 미디어 검증은 모든 내용 손실을 보장하는 검사가 아니므로, 조각 누락 중단 정책과 함께 적용합니다. 실패 자료와 중단한 조각은 로컬 임시 폴더에 남을 수 있습니다.

변경점은 `CHANGELOG.md`, 외부 구성요소 정보는 `ChzzkDownloader/THIRD_PARTY_NOTICES.md`를 확인하세요.
