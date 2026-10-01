# 단일 다운로드 기준 버전

## 0.7.7 보존 기준

0.7.7은 다중 다운로드 기능으로 확장하기 전의 단일 다운로드 기준 버전입니다. 후속 버전의 변경은 새 버전·새 커밋에서 진행하며 `v0.7.7` 태그와 이미 게시한 0.7.7 실행 ZIP을 덮어쓰지 않습니다.

- 소스 복원 기준: Git 태그 `v0.7.7`.
- 실행 복원 기준: GitHub Release `v0.7.7`의 `StreamNestDownloader-0.7.7-win-x64.zip`.
- 실행 ZIP SHA-256: `1d33e64cadc639f82bca4271e52c93cd57ee613d643bad90c9c9f08b513c3a7e`.
- 별도 소스 ZIP: `StreamNest-0.7.7-GitHub-source.zip`. 파일별 해시는 ZIP 내부의 `SOURCE-FILES.sha256`에서 확인합니다.
- 검증·도구 기록: Release 첨부 `StreamNest-0.7.7-verification.json`, `StreamNest-0.7.7-runtime-files.sha256`.

실행 ZIP에는 앱·런타임·도구·플러그인·사용법·법적 고지·FFmpeg 대응 소스가 포함됩니다. 이 ZIP 자체를 보관하면 도구 다운로드 서버의 상태와 관계없이 같은 배포 파일을 복원할 수 있습니다.

## 검증된 범위

- 치지직 다시보기/클립, YouTube 개별 영상, SOOP VOD/개별 캐치 및 일반 웹 영상의 기존 단일 다운로드 흐름.
- 단위 시험 407개, Python 플러그인 시험 72개와 클립/캐치 및 파일 검증 관련 통합 시험 10개 통과.
- 공개 SOOP 캐치: 39.03초, 2560×1440, 영상·음성 및 시험용 전체 디코딩 통과.
- 공개 치지직 클립: 60.03초, 720×1280, 영상·음성 및 시험용 전체 디코딩 통과.
- 일반 웹의 동적 플레이어·확장자 없는 재생목록·AES-128 HLS·두 화질 저장·취소와 공개 예제의 배포 앱 다운로드 확인.
- 배포 앱의 완료 검사는 빠른 미디어 정보 확인이며 전체 디코딩은 수행하지 않습니다.

전체 WPF 통합 시험 묶음과 모든 사이트·계정·권한 조합을 검증한 결과는 아닙니다. 서버 정책·계정 권한·재생 주소는 배포 이후 달라질 수 있습니다. 보존 버전의 식별·복원과 외부 서비스의 영구 호환성은 구별합니다.

## 소스 복원과 빌드

```powershell
git clone --branch v0.7.7 https://github.com/kaskobi2023-boop/StreamNest.git StreamNest-0.7.7
cd StreamNest-0.7.7
.\Setup-Tools.ps1
dotnet build .\ChzzkDownloader\ChzzkDownloader.csproj -c Release
```

`Setup-Tools.ps1`은 yt-dlp 2026.08.19, FFmpeg/ffprobe 8.1.2, Node.js 24.18.0을 버전·해시로 고정합니다. 도구 다운로드를 사용할 수 없으면 보관한 실행 ZIP의 `Tools`를 소스의 `ChzzkDownloader\Tools`에 복원할 수 있습니다. 배포 빌드에는 대응 라이선스 파일과 `Licenses\Source\ffmpeg-8.1.2.tar.xz`도 필요합니다.

이후 개발을 시작할 때는 태그에서 새 브랜치를 만들고 프로젝트 버전을 올립니다. 동일한 버전 번호의 배포 파일을 다시 생성해 기존 기준 파일과 혼동하지 않도록 합니다.

## 로컬 보존

작업 폴더의 `releases\0.7.7`에 실행 ZIP·소스 ZIP·파일별 해시·검증 기록을 별도 보관합니다. 이 경로는 Git에 추가하지 않으며, 소스와 배포 파일은 Git 태그와 GitHub Release로도 보존합니다.

개인 로그인 프로필·쿠키·설정·작업 로그·실제 다운로드 영상은 공개 소스 및 배포 ZIP에 포함하지 않습니다.

실사용 시험의 영상 주소는 로컬 환경변수로 전달합니다. 공개 시험에는 가상 식별자를 사용하며, Git 커밋 작성자 이메일은 GitHub의 비공개 noreply 주소를 사용합니다.
