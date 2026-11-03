# Unity / Battle Server / Website API 통합

Render Free 배포는 [임시 결과 큐 배포 안내](RenderFreeDeployment.md)를 따른다.
영구 디스크 없이 실행할 수 있지만, 컨테이너 교체 시 미전송 결과의 복구는 보장하지 않는다.

## 구현 범위와 계약

Website 저장소 `dawninthemoon/ProjectC-Website`의 `docs/battle-server-api.md`와 로컬 체크아웃
`ceab7734472f12583207d84906df2f0100154122`의 NestJS controller/service를 비교했다.
첨부 문서는 계약 참고 자료로 사용했다. Website 저장소의 소스와 운영 DB는 수정하지 않았다.

- 사용자 API: Bearer JWT, camelCase JSON. `GET /api/decks`는 배열이며 덱 ID는 `id`다.
- 내부 API: `X-Battle-Api-Key`. JWT 검증 본문은 `{ "accessToken": "..." }`다.
- 덱 검증에는 서버 카탈로그 버전을 보내고 `playerId`, `deckId`, `catalogVersion`, `snapshotId`,
  `cardIds`를 검증한다. 카드 구성은 기존 `CardCatalog::PersonalDeckError`와 초기화 경로를 사용한다.
- 결과에는 참가자별 `playerId`, `deckId`, `snapshotId`가 모두 필요하다. 결과 본문의 카드는 받지 않는다.
- 결과 응답의 `rankChanges`는 배열 순서 대신 사용자 UUID로 대응시킨다.
- 결과와 랭크 계산은 C++ 서버 / Website API가 각각 담당한다. Unity는 결과·RP/MMR을 제출하지 않는다.

GameCore 소스, 기본 규칙, HyakkiYagyo 카드 효과는 변경하지 않았다. 기존 CreateMatch / JoinMatch /
SubmitDeck / SetReady 개발 흐름도 유지한다. 랭크 RP 표시만 추가했으며 전투 응전 체인은 추가하지 않았다.

변경 파일:

| 영역 | 파일 |
|---|---|
| 서버 API/결과 큐 신규 | `Network/WebsiteApi.hpp`, `Network/WebsiteApi.cpp` |
| 서버 매칭/세션 | `Network/MatchManager.hpp`, `Network/MatchManager.cpp`, `Network/MatchSession.hpp`, `Network/MatchSession.cpp` |
| 서버 설정/빌드 | `Network/main.cpp`, `Network.cmake`, `Dockerfile`, `.github/workflows/ServerCI.yml`의 libcurl 의존성 설치 |
| Unity API/보안 저장소 신규 | `Assets/Scripts/Network/WebsiteSession.cs`, `SecureSessionStore.cs` 및 `.meta` |
| Unity 연결/로비 | `Assets/Scripts/FrontEnd/ClientApp.cs`, `ConnectionScenePresenter.cs`, `LobbyScenePresenter.cs`, `ServerConnectionSettings.cs` |
| Unity 전투/프로토콜 처리 | `Assets/Scripts/Network/MultiplayerDebugScreen.cs`, `Assets/Scripts/Battle/BattlePresenter.cs`, `Assets/Scripts/ProjectC.Client.Runtime.asmdef` |
| 검증 | `ProjectC.Server/Tests/RankedSessionTests.cpp`, `WebsiteBattleIntegration.py`, `NetworkTests.cpp`, `WebSocketIntegration.py`; `ProjectC.Client/Assets/Tests/PlayMode/WebsiteSessionPlayModeTests.cs` 및 `.meta`; `Tools/TestWebsiteIntegration.py` |
| 문서/비공개 산출물 제외 | 이 문서, 저장소 루트 `.gitignore` |

## 실행 흐름

1. ConnectionScene에서 OS 저장소의 세션을 읽는다. 유효한 세션을 복구하거나 refresh를 실행한다.
   정보가 전혀 없을 때만 guest를 생성한다. 기존 세션의 인증 실패를 새 게스트 생성으로 숨기지 않는다.
2. 사용자 정보, Website 개인 덱, 랭크를 조회한다. 신규 게스트에는 자동 생성 덱이 없으므로 기존 덱
   편집기로 견본을 복사·저장한 뒤 로비의 **덱 관리 → 선택한 로컬 덱 업로드**로 Website에 저장한다.
   업로드는 명시적으로 새 Website 덱을 만드는 기능이다. 로컬 편집·저장 자체는 기존처럼 로컬이다.
3. 로비의 기존 덱 선택 버튼으로 Website 덱을 순환 선택한다. 로비에 덱 이름 목록과 현재 선택을 표시한다.
4. WebSocket 연결 후 Authenticate를 실행한다. QueueMatch에서도 최신 JWT를 다시 검증하고 덱 소유권과
   불변 스냅샷을 확인한 뒤 기존 매칭 큐에 넣는다. HTTP는 별도 worker에서 실행된다.
5. 두 사용자의 검증된 카드로 자동 준비·시작한다. 클라이언트의 추가 SubmitDeck/Ready 입력은 필요 없다.
   랭크 매치에서는 덱 변경, 준비 변경, 같은 방의 Rematch, 호스트 CloseMatch를 거부한다.
6. 기존 SessionToken으로 진행 중 매치에 재접속한다. 검증된 UUID는 좌석에서 복원되며 외부에서 받은
   정수 PlayerId를 계정으로 사용하지 않는다. JWT와 Refresh Token은 상대에게 전송하지 않는다.
7. 연결 종료 후 120초간 복귀를 허용한다. 유예 만료 시 상대가 연결 중이면 Disconnect 패배,
   양쪽 모두 끊겼으면 Aborted다. 진행 중 LeaveMatch는 Surrender다. 일반 게임 종료는 Normal/Draw다.
   종료 판정은 Network 계층의 결과 메타데이터이며 GameCore 리플레이는 그대로 검증된다.
8. 종료 결과를 디스크 큐에 고정한 뒤 Website API에 제출한다. 성공한 사용자의 랭크 변화만 전달하고,
   Unity가 `GET /api/rank/me`로 현재 랭크를 다시 조회한다. 저장 대기 중에는 대기 상태를 표시한다.

## WebSocket 프로토콜 추가

버전 **2**와 기존 필드 이름을 유지한다. 다음은 추가된 메시지/필드다.

```json
{"Type":"Authenticate","ProtocolVersion":2,"RequestId":"auth-1","AccessToken":"<JWT>"}
{"Type":"QueueMatch","ProtocolVersion":2,"RequestId":"queue-1","AccessToken":"<JWT>","DeckId":"<Website UUID>"}
```

- `Authenticated`: Authenticate 성공. 연결 준비 완료는 인증 응답과 DeckCatalog를 모두 기다린다.
- Website 모드의 QueueMatch: `DeckId`는 Website UUID, `AccessToken`은 필수다.
  Website 연동을 끈 개발 서버의 기존 견본 덱 QueueMatch 형식은 유지한다.
- `RankUpdated`: `MatchId`와 본인만의 `Payload`를 보낸다. Payload는 API rankChanges 항목이다.
  상대의 랭크 변화 배열 전체나 덱 스냅샷은 보내지 않는다.
- `MatchEnded.OutcomeReason`: 네트워크 종료의 `Surrender`, `Disconnect`, `Aborted` 문자열이다.
  기존 정수 EndReason은 유지한다. 기존 Snapshot의 Finished/WinnerPlayerId도 서버 판정을 반영한다.
- 오류 Reason: `AuthenticationFailed`, `AccountBusy`, `DeckNotFound`, `CatalogVersionMismatch`,
  `InvalidDeck`, `ApiRateLimited`, `WebsiteUnavailable`, `WebsiteNotConfigured`, `AccountMismatch`,
  `RankedMatchLocked` 등. API의 상세 오류 본문은 노출하지 않는다.

랭크 매치 생성 시 UUID v4를 한 번 만들고 WebSocket MatchId, ReplayRecord.MatchId,
Website matchId에 동일하게 사용한다. 기존 문자열 MatchId를 사용하는 개발 매치는 그대로다.
랭크 재대전은 로비로 나와 새 매치를 생성한다. 같은 UUID에 다른 게임 결과를 덮어쓰지 않는다.

## 세션 저장과 네트워크 설정

Windows Editor / Windows Standalone의 토큰 쌍은 **Windows Credential Manager**에 원자적으로 저장한다.
저장 대상은 API 주소별로 분리한다. PlayerPrefs와 덱 파일에는 Refresh Token을 넣지 않는다.
refresh는 SemaphoreSlim으로 직렬화한다. 응답 유실 가능성이 있으면 기존 refresh를 다시 사용하지 않고,
아직 유효한 JWT로 `/api/auth/session`에서 독립 family를 복구한다. JWT까지 만료되면 계정 복구 오류를
표시하고 기존 정보를 보존한다. `/api/auth/revoke` 호출 경로도 구현했다.

`Resources/Network/ServerConnectionSettings`에서 운영/로컬 WebSocket 주소와 Website 주소를 각각 설정한다.
기본 운영 주소는 `wss://projectc-server.onrender.com`, `https://projectc-api.onrender.com`이다.
로컬 기본값은 `ws://127.0.0.1:7777`, `http://127.0.0.1:8080`이다.
`Local Website Enabled`를 켜면 로컬에서도 인증/Website 덱을 사용한다. 끄면 기존 개발 모드다.
원격 연결에는 HTTPS/WSS만 허용하며, HTTP/WS는 loopback에서만 인증에 사용한다.
Unity HTTP와 연결 화면은 설정된 타임아웃을 사용한다. C++ HTTP는 연결 10초, 요청 전체 30초다.
서버 libcurl의 인증서/호스트 검증은 활성화되어 있으며 리다이렉트는 따라가지 않는다.

## 결과 큐와 운영 복구

`BATTLE_RESULT_OUTBOX` 아래 매치 UUID별 JSON에 **동일 결과 본문, 종료 시각, 스냅샷**을 보존한다.
임시 파일 flush/fsync 후 원자적으로 교체하며, 시작 시 쓰기 가능 여부를 검사한다.
요청 횟수도 HTTP 호출 전에 저장한다. **파일시스템이 보존된 재시작에 한해** 미완료 작업을 다시 읽는다.
Free 컨테이너 재시작·재배포·절전으로 파일이 사라지면 아직 API에 전달되지 않은 결과는 복구하지 못한다.
전송 실패/5xx/429는 4, 8, 16, 32, 64초 간격으로 최대 총 6회 요청한다.
4xx 계약 오류 및 횟수 초과 작업은 자동 삭제하지 않는다. 성공한 200 응답만 완료 처리한다.
로그에는 matchId, HTTP 상태, libcurl 오류 번호, 시도 횟수만 남긴다.

로컬 또는 파일 접근이 가능한 영구 디스크 환경에서 운영자가 실패 원인을 해결한 뒤 서버를 중지하고 해당 JSON의 `attempts`와 `nextAttempt`만 0으로
되돌려 재시도할 수 있다. `body`의 MatchId/endedAt/참가자/스냅샷/결과는 바꾸지 않는다.
409 충돌은 기존 DB 결과를 조사해야 하며 자동 덮어쓰기를 하지 않는다.
파일에는 계정·덱 식별자가 있으므로 공개 저장소/공개 HTTP 경로에 두지 않는다.

## Render 환경 변수와 배포

Battle Server에 다음을 설정한다.

| 변수 | 값/역할 |
|---|---|
| `BATTLE_API_KEY` | 이미 등록된 Website API와 동일한 키. 환경 변수로만 주입 |
| `WEBSITE_API_URL` | `https://projectc-api.onrender.com` (키가 있으면 이 주소가 기본값) |
| `PROJECTC_DATA_DIRECTORY` | `/ProjectC/Data/HyakkiYagyo` |
| `BATTLE_RESULT_OUTBOX` | 쓰기 가능한 디렉터리. Docker 기본값 `/ProjectC/BattleOutbox`; Free에서는 임시 디스크 |
| `BATTLE_RESULT_STORAGE` | Free/기본값 `ephemeral`. 실제 영구 디스크를 연결한 환경에서만 `persistent`로 선언 |

Website 연동 시 기본 전투 테스트용 Data 프로필은 시작 단계에서 거부한다.
API와 C++/Unity의 `hyakki-2026-11-02-v1-fc4b30ed6ac5` 카탈로그를 맞춘다.
설정만 다른 로컬 테스트를 위해 `--data` 인수도 유지하며 환경 변수보다 우선한다.

배포 절차:

1. Website API의 현재 계약/카탈로그 배포를 확인한다. 이번 변경에는 DB migration이 없다.
2. Render Battle 서비스에 위 환경 변수를 적용한다. **Free는 영구 디스크 없이** `ephemeral`로 실행한다.
   디렉터리는 컨테이너 UID 10001에 쓰기 가능해야 한다. Free의 미전송 파일은 재배포/재시작/절전으로 사라질 수 있다.
   영구 디스크를 사용하는 유료 환경에서는 실제 마운트 경로를 지정한 뒤 `persistent`로 선언한다.
3. 기존 저장소 루트 컨텍스트와 `ProjectC.Server/Dockerfile`로 이미지를 빌드·배포한다.
   Dockerfile에 빌드용 libcurl 개발 패키지, 런타임 libcurl·CA 인증서를 추가했다.
   기존 7777 포트와 WebSocket 서비스 설정을 유지한다.
4. 시작 로그와 카탈로그/큐 디렉터리 설정 오류가 없는지 확인한다. 서버는 단일 인스턴스로 운영한다.
   현재 매치·계정 중복 방지·재접속 상태는 프로세스 메모리 안에 있다.
5. Unity 6000.0.81f1에서 클라이언트를 빌드한다. 운영 환경을 선택한 별도 테스트 계정 2개로 수동 검증한다.
6. 두 클라이언트의 덱 선택 → 매칭 → 자동 시작 → 종료 → 결과 저장 → 최신 랭크 표시와 접속 복구를 확인한다.

실제 Render 배포, 운영 계정 생성/결과 제출, 커밋/푸시는 이번 작업에서 실행하지 않았다.

## 검증 및 남은 수동 확인

- Windows CMake 빌드 및 CTest: 기존 9개와 WebsiteBattleIntegration, OutboxRecoveryIntegration 총 11개. NetworkTests에는
  Surrender/Disconnect/양쪽 연결 종료의 유예·승자·리플레이 검증을 추가했다.
- Unity PlayMode: MatchmakingPlayModeTests와 WebsiteSessionPlayModeTests 총 12개.
- Ubuntu 24.04 Docker Release 이미지 빌드 및 이미지 빌드 안의 CTest 11개 통과.
- `Tools/TestWebsiteIntegration.py`: 별도 PostgreSQL 17 Docker 컨테이너와 실제 Website NestJS를 실행한다.
  Website의 `npm test`, `npm run test:e2e`, `npm run build` 후 실제 C++ WebSocket 전투를 진행한다.
  커밋 직후 첫 결과 응답을 503으로 바꾸어 응답 유실·DB 멱등성을 검증한다. 테스트 DB/컨테이너는 제거한다.

```powershell
ctest --test-dir ProjectC.Server/out/build/x64-Debug --output-on-failure
python Tools/TestWebsiteIntegration.py ProjectC.Server/out/build/x64-Debug/ProjectCNetworkServer.exe C:/Dev/ProjectC-Website/backend
```

| 요청한 시나리오 | 자동 검증 범위 |
|---|---|
| 신규 게스트 / 세션 갱신 / 개인 덱 조회 | Unity loopback 테스트 + 실제 NestJS/PostgreSQL |
| 두 클라이언트 매칭 / 자동 시작 / 서로 다른 덱 | 실제 C++ WebSocket + 실제 NestJS/PostgreSQL |
| 정상 종료 / DB 저장 / RP·MMR 갱신 / 최신 랭크 | 실제 GameCore 경기 + SQL 조회 + rank/me, Unity API 테스트 |
| 결과 중복 제출 | 실제 DB 커밋 후 응답 유실·동일 본문 재전송·랭크 불변 확인 |
| API 실패 | 인증 503, 결과 503, 소유권/인증 실패, 디스크 큐 재시도·횟수 제한 |
| 재접속 | 실제 SessionToken WebSocket 복구와 기존 회귀 테스트 |
| 대기 취소 / 연결 종료 | 검증 요청 중 취소·연결 종료와 큐 제거, NetworkTests의 유예 만료 |

남은 플랫폼/운영 범위:

- macOS/Linux/Android/iOS/WebGL의 OS 보안 저장소는 아직 구현하지 않았다. 해당 빌드는 명시적 오류를
  반환하며 장기 토큰을 일반 파일에 저장하는 우회는 없다. 현재 통합 대상은 Windows다.
- 로컬 덱 편집과 Website CRUD의 양방향 동기화·기존 Website 덱 덮어쓰기는 구현 범위에 넣지 않았다.
  Website 덱 조회/선택과 명시적인 새 덱 업로드를 지원한다.
- 서버 재시작으로 진행 중 매치 자체를 복원하는 기능은 기존에도 없으며 추가하지 않았다.
  종료되어 디스크에 기록된 결과도 해당 파일시스템이 남아 있을 때만 복원된다. 결과 저장이 재시작 뒤 완료된 경우 최신 랭크는 조회할 수
  있지만 이전 경기의 RP/MMR 변화 팝업을 다시 재생하는 조회 API는 없다.
- 실제 Render TLS/절전 후 시작 지연, 영구 디스크 재배포 복원, 두 Windows 빌드의 그래픽 UI·오디오·팝업은
  수동 확인해야 한다. 운영 DB를 사용하는 테스트는 실행하지 않았다.
- 같은 Windows 로그인에서도 개발 계정 프로필을 다르게 지정하면 별도 게스트로 테스트할 수 있다.
  [개발용 다중 계정 프로필](../../ProjectC.Client/Docs/DevelopmentAccountProfiles.md)을 따른다.
  동일 프로필의 두 프로세스는 같은 계정을 복구하며 서버의 중복 대전 방지는 유지된다.
