TechArchive Vitals

Manggdy가 만든 Windows용 가벼운 실시간 PC 모니터입니다.

CPU, 메모리, 디스크, GPU, 네트워크 사용량을 작업 관리자와 같은 카운터로 보여줍니다.
측정 도구가 측정값을 오염시키지 않도록, 수집은 작은 C++ 모듈이 맡고
앱 상단에 이 앱 자체의 CPU·메모리 사용량을 표시합니다.


기능

[실시간 카드]
카드마다 현재 값, 사용률 막대, 최근 60초 추이 그래프를 보여줍니다.
값과 막대는 60% 이상이면 주황, 85% 이상이면 빨강으로 바뀝니다.

- CPU: 사용률, 터보 포함 클럭, 온도
- Memory: 사용률, 사용량 / 전체
- Disk: 활성 시간, 읽기·쓰기 속도, 남은 용량
- GPU: 가장 바쁜 엔진 기준 사용률, 전용 VRAM, 온도
- Network: 물리 어댑터 기준 다운로드·업로드 속도, 누적량
- Log: 기록 상태, 경과·남은 시간

[Top Processes]
- 상위 10개 프로세스를 보여줍니다.
- 열 제목(PID, 이름, CPU, GPU, 메모리, 디스크, 네트워크)을 누르면 그 기준으로 정렬하고,
  다시 누르면 순서가 반대로 바뀝니다.
- CPU 상위 일부가 아니라 활동 중인 모든 프로세스를 대상으로 정렬합니다.
- Disk는 초당 파일 I/O 양입니다. 캐시에서 읽은 양도 포함해서 작업 관리자보다 크게 나올 수 있습니다.
- 프로세스별 Network는 관리자 권한이 필요하며, 없으면 N/A로 표시됩니다(아래 참고).

[로그]
1. 기록 시간을 입력합니다(h:mm:ss, 24시간 이상 가능). 10 min / 30 min / 1 hour 버튼도 있습니다.
2. Start Log를 누릅니다.
3. 시간이 끝나면 summary.txt가 자동 저장됩니다.
   Stop & Save는 지금까지 기록을 바로 저장하고, Discard는 기록을 삭제합니다.

summary.txt에는 항목별 최소·최대·평균과, 기록 기간 전체에서 CPU를 가장 많이 쓴 상위 5개 프로세스가 들어갑니다.

로그와 설정 저장 위치:
%LocalAppData%\Vitals

[관리자 모드]
일부 정보는 관리자 권한이 있어야 읽을 수 있습니다.
⋯ 메뉴 → Restart as administrator 를 누르면 관리자로 다시 시작합니다(기록 중인 로그는 먼저 저장).

- CPU 온도: 관리자 권한 + PawnIO 드라이버 필요
- 프로세스별 네트워크: 관리자 권한 필요(ETW 추적 사용)

CPU 온도는 LibreHardwareMonitor가 PawnIO라는 서명된 커널 드라이버로 읽습니다.
PC마다 한 번 설치하면 됩니다.

  winget install namazso.PawnIO

빠진 조건이 있으면 CPU 카드에 이유가 표시됩니다: N/A (needs PawnIO driver) 또는 N/A (run as admin)


자체 부하를 낮추는 방법

- 네이티브 수집기는 PDH 카운터, IP Helper, 프로세스 API만 쓰고 WMI는 쓰지 않습니다.
- 시스템 지표는 1초, 프로세스 목록은 3초마다 수집합니다.
- 창을 최소화하면 화면 갱신을 멈춥니다.
- 카드 하나를 컨트롤 하나로 직접 그려서 창 핸들과 레이아웃 비용을 줄였습니다.
- ETW 네트워크 추적은 관리자 모드에서만 켜지고, 앱을 닫으면 꺼집니다.


프로젝트 구성

- src\Vitals.App: C# WinForms 앱 (.NET 8, x64)
- src\Vitals.Native: C++ 수집 DLL (Vitals.Native.dll)
- scripts\publish-velopack.ps1: 릴리스와 설치 프로그램 생성
- scripts\build-native.ps1: 네이티브 DLL 빌드(앱 빌드 시 자동 호출)


빌드와 실행

.NET 8 SDK와 Visual Studio 2022(C++ 데스크톱 개발 워크로드)가 필요합니다.
앱을 빌드하면 네이티브 DLL도 먼저 빌드됩니다.

  dotnet build .\src\Vitals.App\Vitals.App.csproj -c Release
  .\src\Vitals.App\bin\Release\net8.0-windows\Vitals.exe

Vitals.sln이 아니라 앱 프로젝트를 빌드하세요. dotnet CLI로는 C++ 프로젝트를 직접 빌드할 수 없습니다.


릴리스와 자동 업데이트

릴리스는 Velopack으로 만듭니다. CLI는 한 번만 설치하면 됩니다.

  dotnet tool install -g vpk

릴리스 노트(artifacts\velopack\release-notes-<버전>.md)를 먼저 작성한 뒤 실행합니다.

  powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.13

스크립트가 하는 일:
- .pdb 없이 framework-dependent win-x64 빌드 생성
- 앱 아이콘이 들어간 설치 프로그램과 업데이트 피드 생성
- x64 .NET 8 Desktop Runtime이 없으면 설치 프로그램과 업데이트가 자동 설치하도록 설정
- 업데이트 주소(-DefaultUpdateSource)를 app-defaults.json에 기록

출력 위치:
- artifacts\publish\<버전>\win-x64: 배포용 앱 파일
- artifacts\velopack\Releases: 설치 프로그램, 패키지, releases.win.json

업데이트를 배포하려면 artifacts\velopack\Releases의 파일을 모두 업데이트 주소
(기본값 https://tech.trendarchive.io/Utility/PC_Monitoring/)에 올리면 됩니다.
설치된 앱은 시작 직후와 ⋯ → Check for updates 에서 새 버전을 확인하고,
사용자에게 물은 뒤 내려받아 재시작합니다.

참고:
- 자동 업데이트는 Velopack 설치 프로그램으로 설치한 앱에서만 동작합니다. 빌드·publish 폴더에서는 동작하지 않습니다.
- vpk 0.0.1298은 릴리스 노트에 & 가 있으면 패키징에 실패합니다. 대신 and 로 쓰세요.
- 로컬에서 업데이트를 시험하려면 -DefaultUpdateSource "<전체 경로>\artifacts\velopack\Releases" 로 빌드해서 설치한 뒤, 더 높은 버전을 빌드하면 됩니다.
