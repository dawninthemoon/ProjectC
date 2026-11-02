# 개인 덱 온라인 대전 — 1단계

기준: 2026-11-02, origin/main `7600b2a`. Unity 6000.0.81f1 / C++20.

## 구조와 확정 시점

- `DeckEditorPresenter` → `DeckRepository`의 개인 덱 저장·선택은 유지한다. 저장 형식은 변경하지 않았다.
- `LobbyScenePresenter` / `LobbyPanelView`는 선택 덱 이름·장수와 오류, 양쪽 제출·준비 상태를 표시한다. 기본은 개인 덱이며 덱 버튼으로 기존 서버 테스트 덱을 선택할 수 있다.
- `MultiplayerDebugScreen.SetReady`는 선택한 개인 덱을 복사해서 제출하고, 해당 요청의 `DeckAccepted`를 받은 뒤 `SetReady`를 보낸다. 거절 시 자동 준비하지 않는다.
- `MatchSession::Seat`는 승인된 카드 ID 벡터를 소유한다. 준비 상태에서는 재제출을 거절하며, 준비 취소 후 새 요청으로 교체할 수 있다. 실패한 제출은 이전 승인 덱을 변경하지 않는다.
- 양쪽 준비가 끝나면 `CardCatalog::InitializeCards`가 각 덱을 복사하고 기존 Fisher–Yates 셔플과 GameCore `InitializeGame` 검증을 수행한다. GameState에는 서버의 카드 정의만 사용한다.
- 로컬 파일 편집은 Seat, GameState, Replay의 초기 상태에 영향을 주지 않는다. 접속 복원도 서버 상태만 사용한다. 기존 토큰 회전·정보 은닉·Replay 재실행 검증을 유지한다.
- 개인 덱 재대전은 이전에 승인된 덱을 다시 셔플한다. 새 개인 덱으로 대전하려면 새 매치를 만든다. 서버 제공 덱으로 전환하는 기존 재대전 경로는 유지한다.
- 재접속은 기존과 같이 살아 있는 서버 매치와 현재 세션 토큰을 대상으로 한다. 서버 재시작/프로세스 종료 후 영속 복구를 추가하지 않았다.

## 프로토콜

기존 `ProtocolVersion: 2`의 하위 호환 추가 기능이다. `ListDecks` 응답에 `CatalogVersion`, `PersonalDeckSupported`를 추가했다. 구형/테스트 서버는 개인 덱 기능이 없거나 false이므로 개인 덱 준비가 비활성화되고 서버 덱 경로를 이용할 수 있다.

```json
{
  "Type": "SubmitDeck",
  "ProtocolVersion": 2,
  "RequestId": "unique-request-id",
  "MatchId": "M1-example",
  "GameNumber": 0,
  "CatalogVersion": "hyakki-2026-11-02-v1",
  "DeckName": "나의 백귀야행",
  "DefinitionIds": [1001, 1001, 1001]
}
```

위 목록은 형식 예시이며 실제 제출은 정확히 30장이어야 한다. 카드 효과·능력치·코스트 등의 추가 필드는 거절한다. 연결의 플레이어만 자신의 덱을 제출할 수 있다.

- 성공: `DeckAccepted` + `DeckName`, `CardCount` (제출자에게만).
- 실패: `DeckRejected` + `Reason`(코드), `Message`(한국어 표시문).
- `LobbyState.Payload.Players[].DeckSubmitted`: 상대에게도 공개하는 완료 여부. 서버 제공 덱은 준비 시 완료.
- `LobbyState` 최상위 `PersonalDeckSelected`, `DeckName`, `CardCount`는 수신자 자신의 정보만 담는다. 카드 목록은 응답에 포함하지 않는다.
- 개인 덱 준비는 `SetReady`에 `DeckId`를 생략한다. 명시적인 서버 `DeckId`는 개인 덱 선택을 해제한다. 기존 Create/Join/SetReady/Rematch의 서버 덱 계약은 유지한다.
- 동일 요청 ID와 동일 본문 재전송은 기존 승인 응답만 반환하며 덱을 다시 변경하지 않는다. 다른 본문은 `RequestIdConflict`다. 승인 요청은 좌석당 최대 128개 보관한다.

| 코드 | 의미 |
|---|---|
| CatalogVersionMismatch | 로컬 카드 데이터와 서버 카탈로그 버전 불일치 또는 버전 없는 서버 |
| DeckRulesMismatch | 현재 서버 규칙이 개인 덱의 정확히 30장/동일 카드 3장 규칙과 다름 |
| DeckCardCount | 정확히 30장이 아님 |
| DeckUnknownCard | 서버 카탈로그에 없는 ID |
| DeckIneligibleCard | MainDeckEligible=false (현현 전용 포함) |
| DeckCopyLimit | 동일 ID가 3장 초과 |
| InvalidDeckPayload | 잘못된 타입·범위·필드·이름·배열 |
| DeckLocked | 준비 상태 또는 대전 시작 이후 제출 |
| RequestIdConflict / RequestLimit | 중복 요청 위조 또는 승인 요청 보관 한도 |
| StaleGame | 다른 게임 번호 |
| InvalidRequest (ServerError) | JSON 파싱, 중복 키, 깊이/크기 등 공통 패킷 검증 실패 |

기존 파서는 최대 16 KiB / 깊이 32와 중복 키 거절을 유지한다. 덱 배열의 파싱 상한은 128개, 이름은 UTF-8 240바이트 이내이며 제어 문자를 거절한다. 전송 계층 크기 위반은 기존 WebSocket 종료 정책을 따른다.

카탈로그 버전은 카드 데이터 배포 계약이다. `Data/HyakkiYagyo/Cards.json`과 Unity `Resources/CardData/HyakkiCards.json`에 같은 버전을 둔다. 카드 정의를 바꾸는 배포에서는 **두 파일의 버전을 함께 갱신**해야 한다. 버전은 클라이언트의 신뢰 증명이 아니며, 서버는 버전 일치 후에도 모든 ID와 덱 규칙을 자체 검증한다. 현재 매치의 허용 카드 집합은 로드된 서버 카탈로그이며 별도 밴리스트 규칙을 신설하지 않았다.

## 실행 / 실제 2클라이언트 수동 검증

```sh
cmake -S ProjectC.Server -B build -DCMAKE_BUILD_TYPE=Debug
cmake --build build --config Debug
ctest --test-dir build -C Debug --output-on-failure
./build/ProjectCNetworkServer --port 7777 --data ProjectC.Server/Data/HyakkiYagyo
```

Windows의 실행 파일은 빌드 설정에 따라 `build/Debug/ProjectCNetworkServer.exe`다. 기본 `Data`의 소형 테스트 덱 규칙은 유지되어 개인 덱 대전에는 위 `--data`가 필요하다.

1. Unity 6000.0.81f1에서 LobbyScene을 연다. 두 번째 클라이언트는 기존 `Project C/Build Windows Multiplayer Client` 빌드 또는 별도 프로젝트 복사본에서 실행한다. 다른 기기에서는 서버 IP와 7777 포트를 사용한다.
2. 각 클라이언트의 덱 관리에서 백귀야행 기본 덱을 복사한다. A는 그대로 저장하고, B는 1001 한 장을 빼고 2004 한 장을 추가해 서로 다른 30장 덱으로 저장·선택한다. 독립 저장 경로/OS 사용자/기기를 사용하여 두 프로세스가 같은 개인 덱 파일에 쓰지 않도록 한다.
3. 29장 저장 상태에서는 개인 덱 준비가 비활성화되는지 확인하고 30장으로 복구한다. 현재 이름과 장수를 확인한다.
4. A가 생성한 매치 ID로 B가 참가한다. 개인 덱 모드에서 A만 준비하여 A의 덱 제출 완료/준비와 B의 대기 상태를 확인한다. B도 준비하면 BattleScene으로 이동해야 한다.
5. 양쪽 초기 손패/덱 장수와 카드 플레이·턴 종료를 확인한다. 상대 전체 덱 목록과 손패 내용은 보이지 않아야 한다.
6. 전투의 로비 버튼 → 덱 관리에서 개인 덱을 수정·저장한다. 전투 복귀 후 진행 상태와 확정 덱 이름이 유지되어야 한다. 이후 드로우에도 수정본이 반영되지 않아야 한다.
7. 한 클라이언트에서 네트워크 연결을 끊고 같은 주소로 재접속한다. 자기 손패·턴·덱 장수와 서버 확정 덱이 유지되는지 확인한다. 앱을 종료하면 기존 메모리 토큰도 사라지므로 이 단계에서는 앱을 종료하지 않는다.
8. 종료/방 닫기 후 서버 로그의 `replayVerified=true`를 확인한다. 새 매치에서 덱 버튼으로 서버 제공 덱 모드를 선택하고 기존 준비·대전·재대전 경로를 확인한다.
9. 카드 버전이 다른 테스트 클라이언트에서는 버전 오류와 준비 제한을 확인한다. 위조 ID/과다 중복/현현 전용/잘못된 JSON/중복 요청은 `PersonalDeckWebSocketIntegration.py`와 C++ 테스트로 서버 거절을 검증한다.

## 변경 파일

- 서버 `Network/CardCatalog.{hpp,cpp}`: 개인 덱 상세 검증, 카드 목록 초기화 재사용, 버전 로딩.
- 서버 `Network/MatchSession.{hpp,cpp}`, `MatchManager.cpp`: 좌석 소유 덱, 제출/승인/거절, 준비·재접속·로비 DTO 연결.
- 서버 `Network/Protocol.cpp`: macOS에서도 OS 보안 난수 API로 토큰 생성 (Windows/Linux 경로 유지).
- 서버 `Data/HyakkiYagyo/Cards.json`, 클라이언트 `Resources/CardData/HyakkiCards.json`: 카탈로그 버전.
- 클라이언트 `Scripts/Decks/CardLibrary.cs`, `Scripts/Network/{Protocol,MultiplayerDebugScreen}.cs`: 버전, DTO, 제출과 승인 연동.
- 클라이언트 `Scripts/Battle/LobbyPanelView.cs`, `Scripts/FrontEnd/LobbyScenePresenter.cs`: 덱 선택/검증/상태 표시.
- `Tests/NetworkTests.cpp`, `Tests/PersonalDeckWebSocketIntegration.py`, `Network.cmake`, Unity `Tests/PlayMode/FrontEndPlayModeTests.cs`: 검증 및 회귀 테스트.
- `Docs/LobbyAndDeckEditor.md`, 이 문서: 사용 계약과 검증 절차.

GameCore 규칙/카드 효과, 씬/프리팹 구조, 개인 덱 저장 형식은 변경하지 않았다. 멀리건과 응전·체인은 포함하지 않는다.

## 검증 결과 (2026-11-02, macOS)

- C++20 Debug 빌드 성공 (AppleClang 17).
- CTest 5개 스위트 통과: GameStateTests, NetworkTests, WebSocketIntegration, ContentWebSocketIntegration, PersonalDeckWebSocketIntegration.
- NetworkTests는 서로 다른 승인 덱의 실제 초기 카드 구성, 거절/멱등성/정보 은닉/대전 중 잠금/재접속/Replay를 검사한다. 개인 덱 재대전 검증은 무작위 카드 효과에 의존하지 않도록 **테스트 복사본의 HandLimit만 128**로 두고 덱 소진으로 종료한다. 운영 데이터는 변경하지 않는다.
- 새 WebSocket 테스트는 수정하지 않은 운영 백귀야행 규칙으로 실제 두 TCP/WebSocket 연결의 제출·대전·변조 거절·재접속·Replay를 검증했다. 기존 WebSocket 테스트의 과대 패킷 거절도 통과했다.
- Unity **6000.0.81f1 PlayMode 13/13 통과**. 새 `PersonalDecksSubmitFreezeReconnectAndReject`는 실제 로비/전투 씬과 C++ 서버, 두 Unity 네트워크 컨트롤러를 사용한다. 빈 개인 덱의 준비 차단, 서로 다른 개인 덱, 위조 ID 오류 표시, 전송 후 원본 변경, 저장 덱 수정 후 진행 상태 불변, 재접속을 확인했다.
- 열려 있는 사용자 에디터를 보존하기 위해 `/tmp/projectc-unity-validation` 복사본에서 실행했다. 결과: `/tmp/projectc-unity-tests.xml`, 로그: `/tmp/projectc-unity-validation.log`. 최초 nographics 실행은 화면 캡처와 서버 경로 요건으로 실패했으며, 그래픽 활성화 및 서버/InteractionData 경로를 지정한 최종 실행은 모두 통과했다.
- 서버와 Unity 백귀야행 카드 JSON 전체 내용 일치, `git diff --check` 통과.
- 실제 별도 GUI 창 두 개를 사람이 조작하는 검증은 수행하지 않았다. 위 수동 절차로 최종 UI 확인이 가능하다.
- commit/push는 수행하지 않았다.
