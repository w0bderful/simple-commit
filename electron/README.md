# SimpleCommit Electron

브라우저를 따로 열지 않고 자체 프로그램 창에서 실행하는 Windows x64 버전입니다.

## 사용

`SimpleCommit-Electron-0.1.0.exe`를 실행하세요. 설치 없이 실행하며 Electron과 로컬 서버가 함께 포함되어 있습니다. .NET Framework 4.7.2 이상이 필요합니다.

- 창의 × 버튼: 트레이로 숨김. 자동 확인은 계속됩니다.
- 트레이 아이콘 더블클릭 또는 다시 실행: 기존 창 열기.
- 트레이 메뉴 **종료**: 진행 중인 작업이 없으면 프로그램과 서버를 함께 종료합니다.
- 저장소 페이지 링크만 기본 브라우저로 엽니다.
- 설정의 Windows 자동 시작을 켜면 이 Electron 프로그램을 트레이로 실행합니다. 휴대용 EXE를 이동한 경우 설정을 다시 저장하세요.

설정과 목록은 기존 웹 앱과 동일한 `%LOCALAPPDATA%\SimpleCommitWeb`에 저장합니다. 최초 사용 시 데스크톱 버전 데이터도 자동으로 가져옵니다. 구형 앱과 동시에 실행하지 마세요.

**설정 → 저장소 목록**에서 JSON 내보내기·가져오기를 사용할 수 있습니다.

## 개발/빌드

1. `../web/build.ps1`로 백엔드를 빌드합니다.
2. `pnpm install --frozen-lockfile`
3. `node node_modules/electron/install.js`
4. `pnpm start` 또는 `pnpm run build`

Electron 창은 Node 통합 없이 sandbox/context isolation을 사용합니다. 내장 화면은 실행별 로컬 포트로 연결하며 외부 페이지를 프로그램 안에 로드하지 않습니다.

개발용 연동 검사: `electron . --smoke-test --smoke-data=C:\test\simplecommit`
별도 데이터로 화면 렌더링, 트레이 숨김/복원, 재실행 처리를 검사하고 15초 뒤 종료합니다.
