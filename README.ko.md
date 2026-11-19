<p align="center">
  <img src="src/Vitals.App/Assets/AppIcon.png" width="128" alt="Vitals 아이콘">
</p>

<h1 align="center">Vitals</h1>

<p align="center">Windows용 가벼운 실시간 PC 모니터입니다.</p>

<p align="center">Author: <b>Manggdy</b></p>

<p align="center"><a href="README.md">English</a> | <b>한국어</b></p>

CPU, 메모리, 디스크, GPU, 네트워크 사용량을 작업 관리자와 같은 카운터로 보여줍니다. 측정 도구가 측정값을 오염시키지 않도록 수집은 작은 C++ 모듈이 맡고, 앱 상단에 이 앱 자체의 CPU·메모리 사용량을 표시합니다.

## 주요 기능

- **실시간 카드**: 카드마다 현재 값, 사용률 막대, 최근 60초 추이 그래프를 보여줍니다. 값과 막대는 60% 이상이면 주황, 85% 이상이면 빨강으로 바뀝니다.
- **Top Processes**: 상위 10개 프로세스를 PID, 이름, CPU, GPU, 메모리, 디스크, 네트워크 기준으로 정렬해 보여줍니다.
- **로그**: 정한 시간 동안 기록하고, 항목별 최소·최대·평균이 담긴 `summary.txt`를 저장합니다.
- **관리자 모드**: CPU 온도와 프로세스별 네트워크 사용량을 볼 수 있습니다.
- **낮은 자체 부하**: 네이티브 수집기와 직접 그리는 카드로 앱 자체 부하를 줄였습니다.

### 실시간 카드

| 카드 | 표시 내용 |
|---|---|
| CPU | 사용률, 터보 포함 클럭, 온도 |
| Memory | 사용률, 사용량 / 전체 |
| Disk | 활성 시간, 읽기·쓰기 속도, 남은 용량 |
| GPU | 가장 바쁜 엔진 기준 사용률, 전용 VRAM, 온도 |
| Network | 물리 어댑터 기준 다운로드·업로드 속도, 누적량 |
| Log | 기록 상태, 경과·남은 시간 |

### 자체 부하를 낮추는 방법

- 네이티브 수집기는 PDH 카운터, IP Helper, 프로세스 API만 쓰고 WMI는 쓰지 않습니다.
- GPU 온도는 그래픽 드라이버(D3DKMT)에서 바로 읽습니다. 작업 관리자와 같은 방식이고 관리자 권한이 필요 없습니다.
- LibreHardwareMonitor는 CPU 온도용으로만, 관리자 권한 + PawnIO가 있을 때만 켭니다. 앱 메모리의 절반 정도를 차지하던 부분입니다.
- 시스템 지표는 1초, 프로세스 목록은 3초마다 수집합니다.
- 창을 최소화하면 화면 갱신을 멈춥니다.
- 카드 하나를 컨트롤 하나로 직접 그려서 창 핸들과 레이아웃 비용을 줄였습니다.
- ETW 네트워크 추적은 관리자 모드에서만 켜지고, 앱을 닫으면 꺼집니다.

## 요구 사항

- Windows 10 또는 11 (x64)
- .NET 8 Desktop Runtime (x64). 없으면 설치 프로그램이 자동으로 설치합니다.
- CPU 온도를 보려면: 관리자 권한과 [PawnIO](https://pawnio.eu/) 드라이버

## 사용법

### Top Processes

- 열 제목(PID, 이름, CPU, GPU, 메모리, 디스크, 네트워크)을 누르면 그 기준으로 정렬하고, 다시 누르면 순서가 반대로 바뀝니다.
- CPU 상위 일부가 아니라 활동 중인 모든 프로세스를 대상으로 정렬합니다.
- Disk는 초당 파일 I/O 양입니다. 캐시에서 읽은 양도 포함해서 작업 관리자보다 크게 나올 수 있습니다.
- 프로세스별 Network는 관리자 권한이 필요하며, 없으면 N/A로 표시됩니다(아래 참고).

### 로그

1. 기록 시간을 입력합니다(`h:mm:ss`, 24시간 이상 가능). `10 min`, `30 min`, `1 hour` 버튼도 있습니다.
2. `Start Log`를 누릅니다.
3. 시간이 끝나면 `summary.txt`가 자동 저장됩니다. `Stop & Save`는 지금까지의 기록을 바로 저장하고, `Discard`는 기록을 삭제합니다.

`summary.txt`에는 항목별 최소·최대·평균과, 기록 기간 전체에서 CPU를 가장 많이 쓴 상위 5개 프로세스가 들어갑니다.

### 관리자 모드

일부 정보는 관리자 권한이 있어야 읽을 수 있습니다. `⋯` → `Restart as administrator`를 누르면 관리자로 다시 시작합니다. 기록 중인 로그는 먼저 저장합니다.

| 정보 | 필요한 것 |
|---|---|
| CPU 온도 | 관리자 권한 + [PawnIO](https://pawnio.eu/) 드라이버 |
| 프로세스별 네트워크 | 관리자 권한 (ETW 추적 사용) |

CPU 온도는 LibreHardwareMonitor가 PawnIO라는 서명된 커널 드라이버로 읽습니다. PC마다 한 번 설치하면 됩니다.

```powershell
winget install namazso.PawnIO
```

빠진 조건이 있으면 CPU 카드에 이유가 표시됩니다: `N/A (needs PawnIO driver)` 또는 `N/A (run as admin)`.

## 데이터와 설정

| 데이터 | 위치 |
|---|---|
| 로그와 설정 | `%LocalAppData%\Vitals` |

## 소스에서 빌드

.NET 8 SDK와 Visual Studio 2022(C++ 데스크톱 개발 워크로드)가 필요합니다. 앱을 빌드하면 네이티브 DLL도 먼저 빌드됩니다.

### 빌드

```powershell
dotnet build .\src\Vitals.App\Vitals.App.csproj -c Release
.\src\Vitals.App\bin\Release\net8.0-windows\Vitals.exe
```

`Vitals.sln`이 아니라 앱 프로젝트를 빌드하세요. `dotnet` CLI로는 C++ 프로젝트를 직접 빌드할 수 없습니다.

### 배포

릴리스는 [Velopack](https://velopack.io/)으로 만듭니다. CLI는 한 번만 설치하면 됩니다.

```powershell
dotnet tool install -g vpk
```

릴리스 노트(`artifacts\velopack\release-notes-<버전>.md`)를 먼저 작성한 뒤 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-velopack.ps1 -Version 1.0.0
```

스크립트가 하는 일:

- `.pdb` 없이 framework-dependent `win-x64` 빌드 생성
- 앱 아이콘이 들어간 설치 프로그램 생성
- x64 .NET 8 Desktop Runtime이 없으면 설치 프로그램이 자동 설치하도록 설정

| 출력 위치 | 내용 |
|---|---|
| `artifacts\publish\<버전>\win-x64` | 배포용 앱 파일 |
| `artifacts\velopack\Releases` | 설치 프로그램, 패키지 |

vpk 0.0.1298은 릴리스 노트에 `&`가 있으면 패키징에 실패합니다. 대신 "and"로 쓰세요.

## 프로젝트 구조

| 경로 | 내용 |
|---|---|
| `src\Vitals.App` | C# WinForms 앱 (.NET 8, x64) |
| `src\Vitals.Native` | C++ 수집 DLL (`Vitals.Native.dll`) |
| `scripts\publish-velopack.ps1` | 릴리스와 설치 프로그램 생성 |
| `scripts\build-native.ps1` | 네이티브 DLL 빌드 (앱 빌드 시 자동 호출) |

## 사용한 라이브러리

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) + [PawnIO](https://pawnio.eu/): CPU 온도
- [Velopack](https://velopack.io/): 설치 프로그램
