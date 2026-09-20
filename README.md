# SimpleCommit · 커밋 알리미

GitHub, GitGud, Codeberg, GitLab.com의 공개 저장소를 모니터링하고 최신 소스 ZIP을 받는 Windows 프로그램입니다.

## 실행

1. `SimpleCommit.exe`를 원하는 위치에 저장해 실행합니다.
2. **+ 추가** 또는 **여러 개 추가**에서 저장소 링크를 등록합니다.
3. 다운로드할 항목을 체크하고 **체크한 ZIP 다운로드**를 누릅니다.

Windows 10/11 및 .NET Framework 4.7.2 이상이 필요합니다. 별도 설치 프로그램은 없습니다.

## 기능

- 여러 저장소 등록, 브랜치 자동 탐색, 링크 일괄 등록
- 실행 시 확인 후 주기적으로 자동 확인 (기본 180분, 설정에서 변경)
- 최근 커밋 시간·내용·ZIP 최신 여부 표시
- 최근 커밋순 정렬 또는 드래그 정렬, 목록 순서와 체크 상태 저장
- 체크한 항목 일괄 다운로드, 최신 ZIP은 건너뛰기
- 기존 ZIP 자동 탐색·연결, 개별 및 기본 다운로드 폴더 설정
- 오른쪽 아래 자체 알림, 표시 시간 설정 및 미리보기 (소리 없음)
- Windows 로그인 시 트레이 자동 시작
- 더블클릭 또는 우클릭 메뉴로 저장소 웹페이지 열기

## 사용 시 참고

- 인증 없는 공개 저장소를 지원합니다. API 요청 한도가 적용될 수 있습니다.
- ZIP 최신 판정은 마지막으로 성공한 서버 확인의 커밋 ID를 기준으로 합니다.
- 자동 연결한 기존 ZIP의 판정은 파일명·내부 폴더명·ZIP 커밋 메타데이터를 사용하며, 내용 전체 비교는 아닙니다.
- 설정과 등록 목록은 `%LOCALAPPDATA%\SimpleCommit\settings.json`에 저장됩니다.
- 창의 X 버튼은 트레이로 숨깁니다. 트레이 우클릭 → **종료**로 완전히 종료합니다.
- EXE를 이동했다면 새 위치에서 한 번 실행해 자동 시작 경로를 갱신하세요.
- 자세한 안내는 [사용법.txt](사용법.txt)를 참고하세요.

## 빌드

Windows PowerShell에서 저장소 폴더로 이동한 뒤 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Windows에 포함된 .NET Framework C# 컴파일러를 사용합니다. NuGet 패키지는 필요하지 않습니다.

## 파일

- `SimpleCommit.cs`: 저장소 API, 설정, ZIP 식별 및 정렬 로직
- `MainWindow.cs`: 목록, 설정 탭, 브랜치 선택, 트레이
- `BulkRepoDialog.cs`: 저장소 일괄 등록
- `ToastWindow.cs`: 자체 알림창
- `Tests.cs`: 로컬 동작 검증
- `app.ico`, `app-icon.png`: 앱 아이콘
- `ding.wav`, `generate-sound.ps1`: 제작한 사운드 원본 (현재 알림에서는 재생하지 않음)

자동 생성된 개인 설정, 다운로드한 외부 저장소, 테스트 산출물은 이 저장소에 포함하지 않습니다.
