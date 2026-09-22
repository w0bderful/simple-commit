# Electron 검증

2026-09-22, Electron 44.4.3 / electron-builder 26.15.3 / Windows x64.

- 개발 실행 및 portable EXE 실행 모두 통과
- 별도 테스트 데이터의 저장소 2개와 검은색 테마 로드
- 렌더러 `require` 미노출 확인 (`undefined`)
- 창 닫기 시 트레이 숨김 및 기존 창 복원
- 두 번째 실행이 기존 프로세스로 전달됨 (`SECOND_INSTANCE`)
- `--tray` 실행 시 초기 창 숨김
- 테스트 종료 후 자식 로컬 서버 종료
- 단일 EXE 크기: 약 97 MiB

실제 사용자 데이터는 `%LOCALAPPDATA%\SimpleCommitWeb`을 그대로 사용합니다. 테스트는 `--smoke-data`로 분리했습니다.

참고: https://www.electronjs.org/docs/latest/tutorial/security
