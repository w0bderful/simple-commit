# SimpleCommit Electron

Electron 내장 Node.js에서 백엔드를 실행합니다. C# 소스, C# 서버 실행 파일, .NET Framework 의존성이 없습니다.

## 실행

`SimpleCommit-Electron-0.1.0.exe`를 실행합니다. 닫기 버튼은 트레이로 숨기며, 트레이 더블클릭 또는 재실행하면 기존 창을 엽니다. 작업 중에는 종료를 막아 ZIP 저장을 보호합니다.

설정의 Windows 자동 시작을 켜면 이 프로그램을 트레이에서 실행합니다. 휴대용 EXE를 이동했다면 새 위치에서 다시 실행하세요.

데이터는 `%LOCALAPPDATA%\SimpleCommitWeb`의 `settings.json`과 `repositories.json`에 저장합니다. 현재 파일 형식을 그대로 사용하며 이전 폴더를 자동으로 가져오지 않습니다. 설정에서 JSON 목록 내보내기/가져오기를 지원합니다.

## 개발

```powershell
pnpm install --frozen-lockfile
node node_modules/electron/install.js
pnpm test
pnpm start
pnpm run build
```

개발용 화면 검사:

```powershell
node_modules/electron/dist/electron.exe . --smoke-test --smoke-data=C:\test\simplecommit
```

별도 데이터로 화면 연결, 렌더링, 트레이 숨김/복원, 재실행 처리를 검사하고 15초 뒤 종료합니다. 테스트 모드에서는 자동 확인과 자동 시작 등록을 수행하지 않습니다.

창은 Node 통합 없이 sandbox/context isolation을 사용합니다. HTTP 서버는 127.0.0.1의 임의 포트에만 바인딩하고 실행마다 생성한 토큰을 요구합니다. 외부 저장소 링크는 기본 브라우저에서 엽니다.
