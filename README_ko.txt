TechArchive PC Monitoring

TechArchive에서 제공하는 저부하 네이티브 수집기 기반의 가볍고 작은 Windows 데스크톱 실시간 모니터링 프로그램입니다.

구성

- src\PCMonitoring.App
  - C# WinForms UI
  - 실시간 대시보드
  - 시간 지정 요약 로그
  - Velopack 자동 업데이트 연동
- src\Monitoring.Native
  - C++ 네이티브 수집 DLL
  - Windows API 및 PDH 기반 샘플링

실시간 모니터링 항목

- CPU 사용률
- 시스템에서 제공되는 경우 CPU 온도
- 메모리 사용률 및 사용량
- 디스크 사용률, 남은 용량, 읽기 속도, 쓰기 속도
- GPU 사용률 및 전용 VRAM 사용량
- 시스템에서 제공되는 경우 GPU 온도
- 네트워크 수신/송신 속도
- CPU 사용률 기준 상위 5개 프로세스

로그 기능

`hh:mm:ss` 형식으로 시간을 입력한 뒤 `Start Log`를 누르면 됩니다.

앱은 설정된 로그 루트 폴더 아래에 실행마다 세션 폴더를 하나씩 만들고, 아래 파일만 저장합니다.

- summary.txt

summary.txt에는 아래 내용이 포함됩니다.

- 각 리소스 항목의 최소값 / 최대값 / 평균값
- 로그 시간 전체 동안 누적 CPU 사용량이 높은 상위 5개 프로세스 순위

기본 로그 저장 위치

%LocalAppData%\PCMonitoring\logs

로그 루트 폴더는 앱 안에서 직접 변경할 수 있고, `Open Folder` 버튼으로 바로 열 수 있습니다.

저메모리 설계 포인트

현재 버전은 아래 방식으로 오버헤드를 낮추고 있습니다.

- 기존 WPF 대신 WinForms 사용
- 상위 프로세스 표시는 가벼운 `ListView` 사용
- UI 적체를 막기 위해 크기 1의 bounded snapshot channel 사용
- 핵심 샘플링보다 프로세스 갱신 주기를 더 느리게 설정
- 네이티브 측에서 온도 조회 결과를 캐시
- 업데이트 후에도 설정과 로그가 유지되도록 `%LocalAppData%`에 설정 저장

빌드

dotnet build .\src\PCMonitoring.App\PCMonitoring.App.csproj -c Debug

실행

.\src\PCMonitoring.App\bin\Debug\net8.0-windows\PCMonitoring.App.exe

Velopack 자동 업데이트

앱은 아래 두 가지 형태의 업데이트 소스를 사용할 수 있습니다.

- `https://...` 형태의 피드 URL
- Velopack 릴리스 피드가 들어 있는 로컬 폴더 또는 네트워크 공유 폴더 경로

중요

- 자동 업데이트는 Velopack 설치 프로그램으로 설치된 앱에서만 동작합니다.
- 단순 `dotnet publish` 폴더나 복사한 `.exe`는 UI는 열리지만 자체 업데이트는 수행할 수 없습니다.

앱 내부 설정 방법

- `Update source (URL or folder)` 항목에 업데이트 소스를 입력
- 필요하면 `Auto check on startup` 활성화 유지
- 즉시 테스트하려면 `Check Updates` 버튼 클릭

빌드 시 기본 업데이트 소스를 미리 넣어두면, 설치된 앱은 시작 직후 자동으로 업데이트를 확인합니다. 새 버전이 있으면 사용자에게 진행 여부를 묻고, 동의하면 업데이트를 다운로드한 뒤 자동으로 재시작합니다.

릴리스 패키지 생성

먼저 Velopack CLI를 한 번 설치합니다.

dotnet tool install -g vpk

그 다음 아래 명령으로 릴리스 패키지를 만듭니다.

powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.0

운영용 업데이트 주소를 앱에 기본값으로 넣고 싶다면 아래처럼 사용하면 됩니다.

powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.0 -DefaultUpdateSource "https://tech.trendarchive.io/Utility/PC_Monitoring/"

이 스크립트는 다음 작업을 수행합니다.

- .NET 8 Desktop Runtime 공유 방식의 win-x64 빌드 생성
- 배포 패키지에서 .pdb 디버그 기호 제외
- Velopack 설치 파일 및 업데이트 피드 생성
- 필요하면 기본 업데이트 소스를 앱에 내장

대상 PC에는 x64 .NET 8 Desktop Runtime이 설치되어 있어야 합니다.

기본 출력 경로

- publish 결과물: artifacts\publish\<version>\win-x64
- Velopack 피드 및 설치 파일: artifacts\velopack\Releases

로컬에서 업데이트 테스트하는 방법

1. 위 스크립트로 1.0.0 버전을 빌드합니다.
2. artifacts\velopack\Releases 안의 설치 파일로 앱을 설치합니다.
3. 설치된 앱에서 `Update source (URL or folder)` 값을 아래 경로로 설정합니다.

C:\Users\mrkim\Desktop\codex\PC Monitoring\artifacts\velopack\Releases

4. 그 다음 더 높은 버전, 예를 들어 아래처럼 새 버전을 다시 빌드합니다.

powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.1

5. 설치된 앱에서 `Check Updates`를 실행합니다.

원격 서버에 업데이트 배포

artifacts\velopack\Releases 폴더 안의 파일들을 정적 파일 호스팅 또는 릴리스 저장소에 업로드한 뒤, 그 URL을 앱의 업데이트 소스로 지정하면 됩니다. Velopack은 `releases.win.json` 같은 채널 피드 파일을 읽고 맞는 패키지를 내려받습니다.

배포자 기준 업데이트 릴리즈 방법

1. 다음 버전 번호를 정합니다. 예: `1.0.1`
2. 기존 설치본에 넣었던 것과 같은 `-DefaultUpdateSource` 값을 사용해서 `publish-velopack.ps1`를 다시 실행합니다.
3. 생성된 `artifacts\velopack\Releases` 폴더의 새 파일들을 같은 업데이트 경로에 업로드합니다.
4. 이미 설치한 사용자는 다음 실행 시 자동으로 새 버전을 확인하고 업데이트할 수 있습니다.
