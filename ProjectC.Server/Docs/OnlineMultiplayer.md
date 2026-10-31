**2026-10-31 갱신:** 실험 카드 데이터와 `PlayerAction`·`ResolveChoice`·`CancelAction`, 비공개 선택 토큰, 재접속 중 선택 복구는 [HyakkiYagyo.md](HyakkiYagyo.md)에 정리했다. Windows 및 Ubuntu WSL CTest는 각각 4/4, GameCore는 308개 그룹, Unity PlayMode는 6/6 통과했다. 이번 변경의 Docker 검증은 로컬 Docker 엔진이 꺼져 있어 미수행이다.

# Online Multiplayer — Match Management & Playable Client

2026-10-30. 현재 구현은 JSON WebSocket **ProtocolVersion 2**다. 이전 `local-1` 단일 매치 프로토콜(v1)과 호환되지 않으므로 서버와 Unity 클라이언트를 함께 다시 빌드한다. 기존 GameCore 규칙과 실행 타깃은 유지했다.

## 실행과 설정

```powershell
cd C:\Dev\ProjectC
cmake -S ProjectC.Server -B build
cmake --build build --config Debug --parallel 4
ctest --test-dir build -C Debug --output-on-failure
.\build\Debug\ProjectCNetworkServer.exe --port 7777 --max-matches 64
```

`ProjectCServer`는 기존 GameCore 데모이고, 실제 서버는 `ProjectCNetworkServer`다. Ctrl+C 또는 Linux SIGINT/SIGTERM으로 종료한다. 서버는 기본 `0.0.0.0:7777`에서 대기한다.

| 인자 | 기본값 | 의미 |
|---|---|---|
| --port | 7777 | 1..65535 |
| --max-matches | 64 | Waiting/Ready/Playing/Finished를 포함한 동시 방 수, 1..1024 |
| --data | 실행 파일 옆 Data 폴더 | Cards.json, Decks.json; 없으면 개발 소스 경로 |
| --waiting-timeout | 900 | 대기/준비 매치 만료 초 |
| --playing-timeout | 1800 | 진행 매치의 활동 없는 시간 제한 초 |
| --finished-timeout | 300 | 종료 결과/재대전 대기 매치 만료 초 |
| --max-replays | 128 | 별도 메모리 아카이브 보관 수, 1..4096 |
| --replay-retention | 3600 | Replay 보관 초 |

타임아웃 인자는 1..86400초다. 유효한 로비 변경, 게임 명령 성공, 재접속은 활동 시간을 갱신한다. 읽기 전용 재동기화나 불법 요청은 갱신하지 않는다. 1초 주기의 유지 관리에서 만료를 처리한다. 진행 중이라도 설정 시간 동안 활동이 없으면 Closed로 정리한다. 승패를 임의로 만들지 않으며 그 시점까지의 Replay를 보관한다.

```powershell
docker compose up --build -d
docker compose logs -f server
docker compose down
```

compose는 호스트 `127.0.0.1:7777`로 노출한다. `PROJECTC_PORT`, `PROJECTC_MAX_MATCHES`, `PROJECTC_WAITING_TIMEOUT`, `PROJECTC_PLAYING_TIMEOUT`, `PROJECTC_FINISHED_TIMEOUT` 환경 변수를 지원한다. 내부 포트는 7777이다.

## 아키텍처와 파일

```text
Beast Connection (read 1개, FIFO write 1개)
  -> ClientSession (서버가 바인딩한 MatchId/PlayerId)
  -> MatchManager (생성, 라우팅, 제한, 만료, ReplayArchive)
  -> MatchSession (로비, 게임 회차, 권한, 요청 캐시, 공개 DTO)
  -> 기존 ProcessCommand(GameState, GameCommand)
```

한 프로세스의 단일 `io_context::run()` 실행 경로에서 모든 매치 상태를 직렬 변경한다. 매치별 별도 GameState/좌석/순번/토큰/요청 결과/현재 Replay를 가진다. 네트워크 수신/송신 큐와 게임 규칙의 책임은 분리한다. 스레드를 추가하려면 먼저 매치 executor/strand 경계를 도입해야 한다.

주요 추가 파일:

- `Network/MatchManager.hpp/.cpp`: ServerConfig, 다중 매치, 수명/용량 관리, Replay 아카이브.
- `Network/CardCatalog.hpp/.cpp`: 외부 카드/덱 검증, 서버 시드 기반 셔플, GameState 초기화.
- `Network/Protocol.hpp/.cpp`: 공통 JSON 검증, 버전/봉투, OS 난수 토큰.
- `Data/Cards.json`, `Data/Decks.json`, `Data/README.md`: 실제 예제 정의/덱/스키마.
- Unity `Assets/Scripts/ProjectC.Client.Runtime.asmdef`: 런타임 통신/UI 어셈블리.
- Unity `Assets/Tests/PlayMode/MultiplayerPlayModeTests.cs`와 asmdef: 네 Unity 클라이언트의 실제 플레이 및 회차/매치 메시지 격리 테스트.
- `Tests/ContainerLifecycle.py`: 네 연결/두 Playing 매치가 살아 있는 컨테이너의 정상 종료 검증.

주요 수정 파일:

- `Network/MatchSession.hpp/.cpp`: 기존 명령 검증/이벤트 projection/스냅샷/중복 정책을 매치별로 확장하고 로비·재대전·자동 턴을 추가.
- `Network/WebSocketServer.cpp`, `Network/main.cpp`: MatchManager 연결, 설정, 유지 관리 타이머, 클라이언트 수/종료 로그.
- `Include/ProjectC/GameConfig.hpp`: 미확정 덱 구성 제한 값만 추가. 기존 규칙 처리 코드는 유지.
- `Network.cmake`, `Dockerfile`, `compose.yaml`, `.github/workflows/ServerCI.yml`: 데이터 배포, 빌드, 네 클라이언트/다중 매치 테스트.
- Unity `Protocol.cs`, `MultiplayerDebugScreen.cs`: v2 DTO, Lobby와 Game table, 준비/덱/재대전/로비 복귀, 카드 표시/명령 제어.
- `Tests/NetworkTests.cpp`, `Tests/WebSocketIntegration.py`, Unity `Tests/ClientTransportIntegration.cs`: 기존 검증 범위를 유지하면서 다중 매치/수명/효과/재대전 검사로 확장.
- `.gitignore`, `Docs/TcgRulesAndEffects.md`, 이 문서: 산출물과 현재 개발 상태.

기존 `ProjectCGameCore`, `ProjectCServer`, `ProjectCTests`, `ProjectCNetwork`, `ProjectCNetworkServer`, `ProjectCNetworkTests` 타깃을 유지한다. 새 외부 라이브러리는 추가하지 않았다. Boost 1.86.0 / nlohmann-json 3.11.3은 기존 SHA-256 고정을 그대로 사용한다.

## 로비와 매치 수명

```text
CreateMatch -> WaitingForPlayers -> JoinMatch
  -> SetReady(각자 덱 선택) -> Ready -> 자동 StartGame -> Playing
  -> GameCore 종료 -> Finished
  -> 두 플레이어 Rematch 동의 -> 새 GameNumber -> Playing
  -> LeaveMatch / CloseMatch / 만료 / 서버 종료 -> Closed -> 제거
```

Ready는 양쪽 연결과 준비가 갖춰진 순간의 상태이며 LobbyState로 통지한 뒤 자동 시작한다. `StartMatch` API도 존재하지만 호스트이고 Ready일 때만 실행 가능하다. 정상 UI 흐름에서는 양쪽 Ready가 시작 버튼 역할을 한다.

플레이어 ID는 서버가 방의 두 좌석에 배정하며 프로세스 내에서 구분된다. 예를 들어 Match A는 P1/P2, Match B는 P3/P4다. 계정 ID가 아니다. 카드 인스턴스 ID도 게임마다 전역적으로 새 범위를 할당하여 타 매치·이전 회차의 카드 ID와 충돌하지 않는다.

대기 중 LeaveMatch는 본인 좌석을 비운다. 호스트가 나가면 남은 플레이어에게 호스트 권한을 넘긴다. 빈 방은 닫힌다. **진행 중/종료 후 LeaveMatch는 방을 닫고 양쪽을 로비로 돌려보낸다.** 진행 중 이탈을 기권 승리로 임의 확정하지 않았다. CloseMatch는 호스트만 가능하며 진행 중 게임도 닫을 수 있다.

게임 Finished에서는 기존 재접속 토큰을 모두 무효화한다. 현재 연결된 두 클라이언트는 그대로 재대전에 동의할 수 있다. 종료 후 연결을 끊은 클라이언트는 옛 토큰으로 복귀할 수 없으므로 새 매치를 사용한다. Playing 중 연결 해제는 좌석/상태/토큰을 유지하며, 재접속하면 최신 개인 스냅샷으로 복구한다. 기존 연결이 살아 있으면 동일 토큰의 새 연결은 SessionConnected로 거부한다.

재대전은 두 명의 동의가 필요하다. 덱을 다시 선택할 수 있고, 새 시드로 셔플한 새 GameState를 만든다. Life, Zone, 턴, 소환/공격 플래그, 카드 인스턴스, 현재 명령 기록과 요청 캐시를 초기화한다. MatchId/두 플레이어 ID는 유지하고 GameNumber를 증가시킨다. 토큰은 새로 발급하며 이벤트 순번은 1부터 다시 시작한다.

Closed 매치는 처리 함수가 반환한 뒤 또는 유지 관리 타이머에서 제거한다. 종료된 게임의 Replay는 방과 독립적인 MatchManager 메모리 아카이브에 남으며 보관 수/기간에 따라 제거한다. 디스크/DB 저장은 구현하지 않았고 프로세스 재시작 시 사라진다.

## 프로토콜 v2와 새 API

UTF-8 JSON WebSocket text를 쓴다. 응답 봉투에는 `Type`, `ProtocolVersion=2`, `MatchId`, `RequestId`, `GameNumber`, `SequenceNumber`가 있다. 게임 시작 전 회차는 0, 첫 게임은 1, 재대전은 2다. 사용자 변경 요청에는 현재 GameNumber가 필요하다.

| 요청 | 필수 추가 필드 | 동작/주요 응답 |
|---|---|---|
| ListDecks | 없음 | DeckCatalog: 덱 ID/표시 이름/장수. 실제 카드 목록은 없음 |
| CreateMatch | 선택 DeckId | 새 고유 MatchId, MatchCreated, LobbyState, StateSnapshot |
| JoinMatch | MatchId; 선택 DeckId/SessionToken | MatchJoined, LobbyState, StateSnapshot |
| SetReady | MatchId, GameNumber, Ready; 선택 DeckId | LobbyAccepted, LobbyState; 양쪽 준비 시 자동 시작 |
| StartMatch | MatchId, GameNumber | 호스트/Ready 전용; 정상 경로는 자동 시작 |
| LeaveMatch | MatchId, GameNumber | MatchLeft; 진행 중/종료 게임이면 양쪽 MatchClosed |
| Rematch | MatchId, GameNumber; 선택 DeckId | LobbyAccepted; 두 동의 시 새 회차/새 토큰/새 게임 |
| CloseMatch | MatchId, GameNumber | 호스트 전용 MatchClosed |
| SubmitCommand | MatchId, GameNumber, SequenceNumber, Command | CommandAccepted 또는 CommandRejected |
| StateResyncRequest | MatchId | 최신 LobbyState와 본인용 StateSnapshot |

그 외 응답/알림은 `SessionUpdated`, `MatchStarted`, `GameEvents`, `StateSnapshot`, `MatchEnded`, `ServerError`다. `SessionUpdated`의 SessionToken은 본인에게만 전송하고 UI/로그에 출력하지 않는다. MatchEnded는 SessionTokenRevoked=true를 포함한다.

```json
{"Type":"CreateMatch","ProtocolVersion":2,"RequestId":"create-a","DeckId":"Strikers"}
```

```json
{"Type":"JoinMatch","ProtocolVersion":2,"RequestId":"join-b","MatchId":"M1-xxxxxxxx","DeckId":"Wardens"}
```

```json
{"Type":"SetReady","ProtocolVersion":2,"RequestId":"ready-a","MatchId":"M1-xxxxxxxx","GameNumber":0,"Ready":true,"DeckId":"Strikers"}
```

```json
{"Type":"SubmitCommand","ProtocolVersion":2,"RequestId":"summon-a","MatchId":"M1-xxxxxxxx","GameNumber":1,"SequenceNumber":1,"Command":{"Type":"PlayCard","CardId":1}}
```

카드 ID는 스냅샷에서 읽는다. 위의 1은 예시이며 두 번째 매치/재대전에서 재사용하면 안 된다.

허용 Command.Type은 `PlayCard(CardId)`, `AdvancePhase`, `EndTurn`, `Attack(CardId,TargetCardId)`, `DirectAttack(CardId)`, `ActivateEffect(CardId,EffectId,Targets)`다. `EndTurn`은 남은 페이즈 진행 + EndTurnCommand + 상대 StartTurnCommand를 하나의 서버 트랜잭션으로 처리한다. 명령 적용 실패 시 전체 상태 변경을 버린다. 성공한 내부 명령/이벤트 순서는 Replay에 그대로 기록한다. 클라이언트가 StartTurn을 따로 누를 필요가 없다.

ActivateEffect의 Targets는 효과 동작별 배열이며 각 대상은 `{PlayerId,CardId,Zone}`이다. 주체 PlayerId는 ClientSession에서 결정한다. 대상 PlayerId를 보내도 타인의 카드 발동 권한이 생기지 않는다. 동작 배열 최대 16, 동작별 대상 최대 128, 정수 타입/범위/알 수 없는 필드를 엄격히 검사하고 GameCore가 대상 규칙·최소/최대 수·중복·순차 유효성을 다시 검증한다. Operation/Amount/TransformDefinitionId 주입은 거부한다.

자기 필드의 공개 CardView에는 `Activations[{EffectId,CanActivate,IsManifest,Selections:[{MinCount,MaxCount,Candidates:[{PlayerId,CardId,Zone}]}]}]`를 제공한다. 후보는 ValidateTargets에서 얻으며 상대 손패·모든 덱의 카드 ID는 포함하지 않는다. 상대 필드의 Activations는 빈 배열이다. FriendlyDeck 선택이 필요한 효과 UI와 Deck 타깃 요청은 현재 지원하지 않는다. 자유 현현 규칙은 추가하지 않았으며 AllowedManifestations만으로 액션을 생성하지 않는다. 별도 `Tests/InteractionData`의 Activated 효과로 대상 선택·현현·Replay를 검증한다.

Transform/ChangeLife/Draw/StartTurn은 일반 네트워크 요청으로 노출하지 않는다. 데이터에 정의된 소환 Trigger는 기존 GameCore가 자동 실행한다. 2026-10-30 카드 인터랙션 확장으로 `ActivateEffect(CardId,EffectId,Targets)`를 허용한다. 효과 자체는 서버 정의만 사용하며, 아래 추가 설명과 [BattleUI.md](../../ProjectC.Client/Docs/BattleUI.md)를 따른다. 기본 예제 덱과 GameCore 규칙은 변경하지 않았다.

## 격리, 권한과 비공개 상태

연결의 서버 바인딩 MatchId/PlayerId와 목적 매치를 함께 확인한다. 타 매치 요청은 NotInMatch, 없는 방은 MatchNotFound, 미참가 요청은 NotJoined다. 클라이언트가 전달한 PlayerId는 권한 근거로 사용하지 않는다. 전달했다면 바인딩과 일치해야 한다. Command 내부에 행위자/피해량/정의 변경을 넣을 수 없다. 공격 상대는 서버가 현재 방의 다른 좌석으로 결정한다.

카드 정의/소유권/턴/페이즈/소환/전투 검증은 기존 ProcessCommand가 수행한다. JSON 객체·타입·범위·필수/미등록 키·중복 키·깊이(32)와 메시지 크기(16 KiB)를 검증한다. binary frame과 초과 메시지/전송 오류는 연결을 종료한다. 클라이언트별 송신 FIFO는 128개/1 MiB 제한이다. 동시 전송 연결은 `2 * MaxMatches + 16`으로 제한한다.

각 매치의 성공 요청은 SequenceNumber를 1 증가시킨다. 요청자에게 CommandAccepted를 먼저 보낸 다음 해당 방의 두 연결에 GameEvents -> StateSnapshot -> [MatchEnded] 순서로 전송한다. 같은 회차에서 동일 RequestId/내용은 캐시 응답과 최신 스냅샷만 돌려주며 상태를 재실행하지 않는다. ID 충돌은 RequestIdConflict, 오래된 순번은 StaleSequence다. 캐시는 좌석당 회차별 최대 4096개이며 초과 시 RequestLimit이다.

재대전에서는 GameNumber를 먼저 검사하여 StaleGame으로 이전 회차 입력을 차단한다. 캐시를 초기화했더라도 같은 SequenceNumber/옛 카드 ID가 새 게임에 적용되지 않는다. Unity도 MatchId/GameNumber를 검사하고 이전 방/회차 스냅샷·이벤트를 버린다.

스냅샷은 GameState 전체 직렬화가 아닌 공개 DTO다. 자신의 손패만 카드 목록을 전송한다. 상대 손패와 양쪽 덱은 장수만 공개하며 덱 순서·정의 목록·시드는 전송하지 않는다. LobbyState에서도 상대의 덱 선택은 숨긴다. 이벤트는 EventsForPlayer 후 보이는 카드 참조만 남기고 공개 필드만 구성한다. 공개 Field/Graveyard/Life/턴 상태는 두 클라이언트에서 일치하며 CanSummon/CanAttack은 각 뷰어의 조작 가능 표시다.

토큰은 Windows BCryptGenRandom/Linux getrandom 기반 32바이트 난수(64자 hex)다. 로그에는 MatchId, 회차, 승자, 활성 매치/클라이언트 수, 종료 사유, Replay 검증 성공만 남긴다. 토큰·시드·손패·덱 순서를 출력하지 않는다.

## 데이터, 셔플, Replay

[Data/README.md](../Data/README.md)에 상세 스키마가 있다. Config의 MinDeckSize/MaxDeckSize/MaxCardCopies는 미확정 규칙을 코드에 고정하지 않기 위한 임시 구성 값이다. 기존 core InitializeGame API에 새 덱 제약을 강제로 적용하지 않고 서버 Catalog가 게임 시작 전에 검증한다.

기본 playtest 설정은 Life 3, 초기 손패 2, 턴 드로우 0이다. Strikers는 ATK3/HP5 몬스터, Wardens는 기본 ATK2/HP3에 OnSummoned +1 ATK Trigger를 가진 몬스터를 사용한다. 각각 4장이고 두 정의를 2장씩 사용한다. 현현 목적 정의/AllowedManifestations도 데이터 예제에 포함한다. 자유 Transform 원격 호출은 허용하지 않는다.

서버 OS 난수 시드를 사용해 `mt19937_64` + 명시적 Fisher-Yates/rejection sampling으로 셔플한다. 표준 라이브러리별 std::shuffle 분포 차이에 의존하지 않는다. 덱 정의는 불변으로 유지하고 각 플레이어용 복사본을 섞는다.

ReplayRecord는 MatchId/GameNumber/시드/셔플 후 InitialState/실행된 GameCommand와 CommandResult/FinalState를 서버 전용으로 보관한다. 성공한 자동 턴 명령도 개별 core 명령으로 기록한다. 한 개 core 명령의 거부 결과는 그대로 기록하고, 원자적 명령 그룹 실패는 커밋하지 않으며 기록에서도 제외한다. 보관 전에 ReplayCommands를 다시 실행해 결과 목록과 최종 GameState가 모두 같은지 검사한다. 이 서버 내부 데이터는 원격 API로 제공하지 않는다.

## Unity 사용과 수동 검증

현재 프로젝트는 **6000.0.81f1**이다. 버전을 임의로 바꾸지 않았다. SampleScene Play 또는 `Project C > Build Windows Multiplayer Client` 메뉴를 사용한다. IMGUI 화면은 런타임에 자동 생성한다.

1. 서버를 켜고 Windows 실행 파일 4개(A/B/C/D)를 실행한다. 각 창에서 ws://127.0.0.1:7777에 Connect한다.
2. A가 Strikers로 Create match를 누르고 표시된 MatchId를 B에게 전달한다. B는 Wardens를 고른 뒤 해당 ID로 Join한다.
3. C/D도 별도 매치를 만든다. 두 MatchId와 네 PlayerId가 다름을 확인한다.
4. 각 창의 준비 상태/연결 상태를 확인하고 Ready를 누른다. 양쪽 준비가 되면 자동으로 시작한다.
5. A는 초록색 손패 카드를 선택해 Summon한다. B에게 필드가 보이고 C/D의 상태·이벤트에는 변화가 없어야 한다.
6. End turn을 누르면 상대가 즉시 Main 턴을 시작한다. B도 소환하고 End turn한다.
7. A는 Enter battle phase, 자신의 필드 카드, 상대 필드 카드를 차례로 선택한 뒤 Attack selected target을 누른다. A 카드 HP=2, B 카드 묘지 이동을 확인한다.
8. A End turn, B End turn 후 A Enter battle phase, 자신의 공격자를 선택해 Direct attack한다. 양쪽 승패/Life 0을 확인한다.
9. 양쪽에서 Request rematch를 누른다. GameNumber 증가, Life 3, 빈 필드/묘지, 새 손패·카드 ID와 초기 순번을 확인한다. 덱도 바꿀 수 있다.
10. C/D 진행 중 한쪽 Disconnect -> Reconnect를 누른다. 같은 MatchId/PlayerId와 현재 상태를 복구해야 한다. 끊긴 동안의 이벤트는 전체 스냅샷으로 복구한다.
11. Return to lobby 또는 호스트 Close match로 방을 닫고 같은 연결에서 새 방을 만든다. 진행 중 Leave는 상대도 로비로 이동시킨다.
12. 묘지 토글, 선택 카드 정보, 이벤트 패널, Show protocol debug details를 확인한다. 이벤트 로그에 P0 같은 내부 기본값 대신 턴/소환/전투 설명이 표시된다.

Unity는 Update에서 수신 큐를 소비해 메인 스레드에서 상태/UI를 갱신한다. 서버 이벤트를 순서대로 기록하고 뒤따르는 스냅샷을 원자적으로 적용한다. UI가 임의 피해/승패를 계산하지 않는다. 순번에 공백이 있으면 재동기화를 요청한다. 카드 선택과 공격 가능 표시는 서버 DTO를 따른다.

## 자동 검증과 실행 명령

```powershell
ctest --test-dir build -C Debug --output-on-failure
dotnet build ProjectC.Client/Tests/ClientValidation.csproj
dotnet run --project ProjectC.Client/Tests/ClientTransportIntegration.csproj -- C:/Dev/ProjectC/build/Debug/ProjectCNetworkServer.exe
```

Unity DLL 경로가 다르면 ClientValidation에 `-p:UnityManagedPath=".../Editor/Data/Managed/UnityEngine"`를 전달한다. C# 통신 통합 테스트는 .NET 10 SDK를 사용하고 추가 패키지는 없다.

Unity PlayMode 명령(프로젝트를 다른 에디터가 열고 있으면 별도 사본 사용):

```powershell
& '<Unity>/Editor/Unity.exe' -batchmode -nographics `
  -projectPath '<UnityProject>' -runTests -testPlatform PlayMode `
  -testResults '<Workspace>/build/UnityPlayModeResults.xml' `
  -logFile '<Workspace>/build/UnityPlayMode.log' `
  -projectcServerPath '<Workspace>/build/Debug/ProjectCNetworkServer.exe'
```

PlayMode 테스트는 네 실제 UI 컴포넌트/ClientWebSocket을 C++ 서버에 연결하고 두 매치, 효과, 자동 턴, 전투 승리, 재대전, 재접속을 검사한다. 별도 테스트는 옛 GameNumber 및 타 매치 스냅샷을 버리는지 확인한다. 네 개의 그래픽 창을 사람이 조작한 테스트와는 구분한다.

CTest는 기존 GameStateTests(284개 그룹), NetworkTests, WebSocketIntegration 세 타깃을 유지한다. 소켓 통합 테스트는 두 매치의 전체 수명, 타 매치 카드 조작, 토큰 범위/회전/무효화, 중복 참가/명령, 순번 경쟁, 잘못된 JSON/크기, 초기·재접속 비공개 상태, 최대 방 수, 실제 타이머 만료, 새 게임 초기화를 검사한다. 매치 A는 전투/직접 공격으로 종료하고 매치 B는 독립적인 소환 Trigger/턴/재접속을 진행한다. 양쪽 Replay는 서버에서 별도로 검증한다.

GitHub Actions는 Windows/Linux 빌드, CTest, 네 C# 클라이언트 테스트, Docker 내부 CTest와 활성 2개 게임/4개 연결의 graceful shutdown을 수행하도록 구성했다. 원격 Actions 실행은 commit/push하지 않았으므로 수행하지 않았다. Unity CI는 라이선스/설치가 필요한 별도 환경이므로 현재 로컬 PlayMode/Windows 빌드로 검증한다.

## 한계와 다음 단계

계정/DB/랭킹/관전/RP 스택/클라우드/분산은 없다. 서버 재시작 시 매치와 메모리 Replay는 사라진다. 기존 연결이 살아 있는 동안의 토큰 재사용은 거부하며 별도 토큰 탈취 복구나 계정 인증은 없다. 원격 외부 배포용 TLS/상용 보안/대규모 부하 검증은 범위 밖이다. WebGL/모바일은 검증하지 않았다.

다음 단계는 네 Windows 창의 레이아웃/입력 수동 검증, Replay 파일 내보내기와 보관 정책, Activated 효과 선택 UI/공개 스키마, 확정된 카드/덱 규칙 반영이다. 현재 예제의 능력치·덱 크기·중복 수는 테스트 구성이지 최종 카드 밸런스가 아니다.

## 실제 검증 결과 (2026-10-30)

| 항목 | 결과 |
|---|---|
| Windows Visual Studio 2022 MSVC Debug | 기존/네트워크 타깃 모두 빌드 성공 |
| GameCore 회귀 | 기존 284개 테스트 그룹 통과 |
| Windows CTest | GameStateTests / NetworkTests / WebSocketIntegration 3/3 통과 |
| 실제 WebSocket | 네 클라이언트, 두 독립 매치, 효과·턴·전투·승리·재접속·재대전·새 매치·검증/격리 통과 |
| .NET 통합 | Unity와 동일한 WebSocketTransport 소스로 네 실제 C# 연결 통과 |
| Unity C# 참조 빌드 | netstandard2.1, 경고 0 / 오류 0 |
| Unity PlayMode | 실제 C++ 연결을 포함한 2/2 통과, 종료 코드 0 |
| Unity Windows Development | 6000.0.81f1 빌드 성공, 종료 코드 0 |
| Linux Docker GCC Release | 이미지 빌드 및 CTest 3/3 통과 |
| Docker Compose 실제 연결 | 호스트 17778 포트로 네 클라이언트/두 매치 전체 시나리오 통과 |
| 활성 서버 graceful shutdown | 네 연결/두 Playing 매치의 Replay 검증·세션 정리, 종료 코드 0 |
| git diff --check | 통과 |
| 네 Windows 그래픽 창의 수동 버튼 조작 | 미수행; 위 수동 절차로 별도 확인 필요 |
| 원격 GitHub Actions | 설정 갱신 완료, commit/push하지 않아 실행 미수행 |

Unity 원본 프로젝트가 열려 있어 에디터를 종료하지 않고 `build/UnityMatchValidation` 사본에서 PlayMode/Windows 빌드를 수행했다. 최신 실행 파일은 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe`에 반영했다. 이전 실행 파일 폴더는 같은 Build 아래 `Multiplayer.Previous-*`로 보관했다. 여러 창이 비활성 상태에서도 수신/UI Update를 계속 처리하도록 런타임 Application.runInBackground를 활성화한다. 기존 forceSingleInstance 설정은 0이다.

검증 증거는 `build/UnityPlayModeResults.xml`, `build/UnityPlayMode.log`, `build/UnityMatchBuild.log`, `build/DockerMatchManagement.log`다. 테스트용 compose 컨테이너/네트워크는 정리했다. 사용자 에디터나 다른 서비스는 종료하지 않았다. Git commit/push는 하지 않았다.


## ??? ?? UI (2026-10-30)

Unity ?? ?? `Assets/Scenes/BattleScene.unity`? ????. `BattleScreen`, `BattleCard`, `CardBack`, `PlayerPanel` ?? ???? ???? ?? IMGUI ?? ?? ??? ????. ??/??? ??? v2 ??? ????. ?? DTO? ?? ????? ????? ??, FieldLimit, ??? FromZone/ToZone? ???? GameCore ??? ???? ???.

????????? ????????? ??? [BattleUI.md](../../ProjectC.Client/Docs/BattleUI.md)? ????.
