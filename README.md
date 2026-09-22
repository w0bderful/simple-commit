# SimpleCommit

GitHub, GitGud, Codeberg, GitLab.com의 공개 저장소 커밋을 확인하고 소스 ZIP을 다운로드하는 Windows Electron 앱입니다. UI와 백엔드 모두 JavaScript이며 별도 .NET 설치가 필요하지 않습니다.

## 실행

릴리스의 `SimpleCommit.exe`를 실행합니다. 창을 닫으면 트레이에서 자동 확인을 계속하며, 다시 실행하면 기존 창을 엽니다. 트레이 메뉴의 **종료**로 완전히 종료합니다.

- 여러 저장소 일괄 등록, 브랜치 자동 조회, 최근 커밋 시간과 ZIP 최신 여부 표시
- 선택 목록 저장, 전체 선택/취소, 최근 커밋순 또는 드래그 정렬
- 개별/기본 다운로드 폴더, 기존 ZIP 자동 연결, 최신 ZIP 건너뛰기
- 브랜치 이름을 유지한 ZIP 저장, 성공한 다운로드 이후 이전 ZIP 삭제
- 기본 180분 자동 확인, 설정에서 간격 및 Windows 트레이 자동 시작 변경
- 바탕화면 오른쪽 아래 무음 자체 알림, 트레이에서도 표시, 확인할 때까지 유지 또는 표시 시간 설정
- Material 밝은/검은 테마, JSON 저장소 목록 내보내기/가져오기

ZIP 최신 여부는 마지막 서버 확인의 커밋 ID와 파일의 메타데이터를 기준으로 합니다. 공개 API 요청 한도가 적용될 수 있습니다.

## 데이터

`%LOCALAPPDATA%\SimpleCommitWeb`에 저장합니다.

- `settings.json`: 앱 설정
- `repositories.json`: 저장소, 선택 상태, 커밋 및 ZIP 정보
- 각 파일의 `.bak`: 직전 저장 백업

설정과 목록을 분리해서 저장하며 이전 폴더 자동 가져오기나 마이그레이션은 하지 않습니다. 목록은 설정 화면에서 직접 가져올 수 있습니다.

## 개발 및 빌드

Windows에서 Node.js와 pnpm을 사용합니다.

```powershell
cd electron
pnpm install --frozen-lockfile
node node_modules/electron/install.js
pnpm test
pnpm start
pnpm run build
```

결과: `electron/dist/SimpleCommit.exe`

- `electron/main.cjs`: 창, 트레이, 단일 인스턴스, 네이티브 파일 선택 및 자동 시작
- `electron/backend.cjs`: 루프백 API, 저장소 조회, ZIP 처리 및 데이터 저장
- `electron/www/`: 화면 HTML/CSS/JavaScript
- `electron/backend.test.cjs`: 백엔드 회귀 검사

실제 화면 검사는 [Electron 안내](electron/README.md), 검증 범위는 [VALIDATION.md](electron/VALIDATION.md)를 참고하세요.

## 릴리스

소스 변경을 커밋한 뒤 저장소 루트에서 `./release.ps1`을 실행합니다. 현재 버전의 마지막 숫자를 1 올리고 테스트, 빌드, 소스 푸시, 프리뷰 릴리스 게시를 수행합니다. 실행 파일명은 항상 `SimpleCommit.exe`입니다.

- 예: 0.0.1 → 0.0.2 → 0.0.3
- 이전 릴리스도 삭제: `./release.ps1 -RemovePrevious`
- 명시적으로 버전 지정: `./release.ps1 -Version 0.0.1 -RemovePrevious`

이미 게시한 버전에는 다시 배포하지 않습니다. 새 릴리스 파일의 해시를 검증한 뒤 요청된 이전 릴리스를 삭제합니다. Git 태그 이력은 보존합니다.
