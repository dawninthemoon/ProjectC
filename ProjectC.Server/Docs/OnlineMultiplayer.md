# Online Multiplayer Vertical Slice

2026-10-30 로컬 저장소의 실제 GameCore API를 기준으로 구현했다. GameCore 규칙은 변경하지 않았다.

## 실행

Windows (Visual Studio 2022 C++ 도구, CMake, Python 3 필요):

```powershell
cmake -S ProjectC.Server -B build
cmake --build build --config Debug --parallel 4
ctest --test-dir build -C Debug --output-on-failure
./build/Debug/ProjectCNetworkServer.exe --port 7777
```

`ProjectCServer`는 기존 결정론 시나리오 실행 파일로 유지한다. 실제 대기 서버는 별도 타깃인 `ProjectCNetworkServer`다. 기본 포트는 7777이고, `--port 1..65535`로 변경한다. Windows Ctrl+C, Linux SIGINT/SIGTERM을 처리한다.

Docker:

```powershell
docker compose up --build -d
docker compose logs server
docker compose down
```

호스트 바인딩은 `127.0.0.1:7777`이다. `PROJECTC_PORT` 환경 변수로 호스트 포트를 바꿀 수 있다. 이미지 자체 서버는 `0.0.0.0:7777`에서 대기한다. Linux CLI로 직접 실행할 때도 같은 CMake 명령을 쓰며 `--config Debug`는 생략할 수 있다.

Unity:

1. 현재 저장소 버전인 **6000.0.81f1**로 `ProjectC.Client`를 연다. 요청서의 6.3 LTS와 실제 프로젝트 버전이 달라 임의 업그레이드는 하지 않았다.
2. SampleScene을 Play하면 런타임 부트스트랩이 IMGUI 디버그 화면을 자동 생성한다. 씬에 컴포넌트를 배치할 필요가 없다.
3. `Project C > Build Windows Multiplayer Client` 메뉴로 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe`를 만든다. 에디터 Play와 빌드 실행 파일을 각각 한 클라이언트로 쓰거나, 실행 파일을 두 번 실행한다.
4. 양쪽에서 주소 `ws://127.0.0.1:7777`을 확인하고 Connect + Join을 누른다. 첫 접속은 P1, 두 번째는 P2다. 세 번째 신규 참가자는 MatchFull로 거부한다.
5. Disconnect 후 같은 실행 인스턴스의 Reconnect로 복구한다. 토큰은 메모리에만 보관한다. 서버를 재시작했다면 연결을 끊고 Forget session을 눌러 이전 토큰을 비운다.

## 파일 구성과 아키텍처

- `Network.cmake`: Boost 1.86.0과 nlohmann/json 3.11.3 다운로드, SHA-256 검증, 네트워크 및 테스트 타깃.
- `Network/MatchSession.hpp`, `MatchSession.cpp`: 전송과 무관한 ClientSession 바인딩, JSON 검증, 매치, 명령 허용 목록, 공개 DTO 생성, 중복 결과 캐시, 재접속.
- `Network/WebSocketServer.cpp`: Boost.Asio/Beast accept/read/write, 연결 수·메시지·송신 큐 제한, 종료 신호.
- `Network/main.cpp`: 포트 인자 검증과 서버 진입점.
- `Tests/NetworkTests.cpp`: 소켓 없는 프로토콜·세션·비공개 상태·재접속·Replay 검사.
- `Tests/WebSocketIntegration.py`: Python 표준 라이브러리만으로 RFC6455 handshake/frame을 보내는 실제 연결 검사.
- Unity `WebSocketTransport.cs`: ClientWebSocket 연결·송수신·취소, 수신/알림 큐. Unity API 의존성이 없다.
- Unity `Protocol.cs`: 공개 상태/메시지 DTO. 서버 내부 GameState와 분리한다.
- Unity `MultiplayerDebugScreen.cs`: 메인 스레드 Update에서 수신 큐를 처리하고 IMGUI로 상태·선택·명령·거부·승패를 표시한다.
- Unity `Editor/BuildLocalClient.cs`: Windows 디버그 플레이어 빌드 메뉴와 batchmode 진입점.
- Unity `Tests/ClientValidation.csproj`: 실제 Unity DLL 참조 C# 컴파일 검증.
- Unity `Tests/ClientTransportIntegration.csproj`, `ClientTransportIntegration.cs`: Unity와 동일한 통신 소스로 두 C# 클라이언트의 실제 플레이를 검사한다.
- `Dockerfile`, `.dockerignore`, 루트 `compose.yaml`, `.github/workflows/ServerCI.yml`: Linux 이미지, 포트, Windows/Linux 빌드·테스트 CI.

```text
TCP/WebSocket Connection (Beast)
  -> ClientSession (서버가 할당한 Player, Send 콜백)
  -> MatchSession (프로토콜 검증/허용 목록/요청 캐시/공개 DTO)
  -> ProcessCommand(GameState, GameCommand) (기존 GameCore)
  -> CommandAccepted -> GameEvents -> StateSnapshot -> [MatchEnded]
```

단일 프로세스, 단일 매치, 단일 `io_context::run()` 스레드다. 모든 네트워크 콜백과 MatchSession 상태 변경은 이 실행 경로에서 직렬 처리한다. GameCore와 MatchSession에는 Beast 소켓을 넣지 않는다. 워커 스레드를 추가할 경우 반드시 매치 executor/strand 경계를 먼저 추가해야 한다.

각 연결은 비동기 read 하나와 write 하나만 동시에 수행한다. 클라이언트별 FIFO 송신 큐는 128개/1 MiB까지 허용하고 넘으면 연결을 닫는다. 수신 메시지는 16 KiB, 동시 전송 연결은 32개로 제한한다. Beast handshake/idle timeout을 사용한다. binary frame, 초과 크기, 전송 오류는 연결을 닫는다. 문법 오류는 ServerError로 응답하고 읽기를 계속한다. 종료할 때 acceptor와 연결 소켓을 닫아 대기 상태에서도 프로세스를 정상 종료한다.

## 프로토콜 v1

UTF-8 JSON text WebSocket이다. 키는 PascalCase다. 서버 메시지는 `Type`, `ProtocolVersion`, `MatchId`, `RequestId`, `SequenceNumber`를 가진다. 현재 MatchId는 `local-1`이다. 이벤트/브로드캐스트의 RequestId는 빈 문자열이다.

| Type | 방향 | 주요 필드/역할 |
|---|---|---|
| JoinMatch | C -> S | RequestId, 재접속일 때 MatchId와 SessionToken |
| MatchJoined | S -> C | PlayerId, SessionToken; 토큰을 다른 플레이어에게 전송하지 않는다 |
| MatchStarted | S -> C | 두 좌석 준비 완료 |
| SubmitCommand | C -> S | MatchId, RequestId, SequenceNumber, Command; 선택적 PlayerId는 바인딩 검증만 한다 |
| CommandAccepted | S -> C | 해당 RequestId의 실행 성공과 새 SequenceNumber |
| CommandRejected | S -> C | 해당 RequestId와 Reason |
| GameEvents | S -> C | 플레이어별 검열된 Payload 배열, GameCore 발생 순서 |
| StateSnapshot | S -> C | 플레이어별 공개 Payload; 초기/매 명령 성공/재동기화/재접속 |
| StateResyncRequest | C -> S | MatchId, RequestId; 최신 스냅샷 요청 |
| MatchEnded | S -> C | WinnerPlayerId, EndReason |
| ServerError | S -> C | 잘못된 스키마·버전은 InvalidRequest, 참가 실패 등은 구체적인 Reason |

```json
{"Type":"JoinMatch","ProtocolVersion":1,"RequestId":"join-a"}
```

```json
{"Type":"SubmitCommand","ProtocolVersion":1,"MatchId":"local-1","RequestId":"summon-a","SequenceNumber":1,"Command":{"Type":"PlayCard","CardId":1}}
```

```json
{"Type":"JoinMatch","ProtocolVersion":1,"MatchId":"local-1","RequestId":"resume-a","SessionToken":"<서버가 발급한 64자 hex 토큰>"}
```

객체/문자열/정수 타입, 문자열 길이와 ASCII 제어문자, 숫자 부호·범위, 필수 필드, 미등록 필드를 검사한다. bool/float를 정수로 허용하지 않는다. 중복 JSON 키와 깊이 32를 초과하는 입력도 거부한다. RequestId는 1..64자, 숫자 SequenceNumber는 JSON 안전 정수 범위를 사용한다. JSON 라이브러리 예외 메시지나 입력 본문을 응답/로그에 복사하지 않는다.

허용 명령은 다음 여섯 종류다.

| Command.Type | 추가 필드 |
|---|---|
| PlayCard | CardId |
| StartTurn | 없음 |
| AdvancePhase | 없음 |
| EndTurn | 없음 |
| Attack | CardId, TargetCardId |
| DirectAttack | CardId |

Transform, ChangeLife, Draw, ActivateEffect 등은 이 vertical slice에서 원격 명령으로 노출하지 않는다. 소환 등에 의해 자동 발생하는 기존 Trigger는 GameCore가 처리한다. 신규 카드/효과 규칙을 확정하지 않았다.

## 권한, 순서, 정보 공개

PlayerId는 MatchSession이 연결된 ClientSession에 1 또는 2로 설정한다. 미참가 세션은 명령을 제출할 수 없다. 요청의 PlayerId는 권한의 근거가 아니며, 전달했다면 서버 바인딩과 일치해야 한다. 명령의 행위자와 공격 상대 ID는 서버가 구성한다. Command 내부 PlayerId 같은 미등록 필드는 거부한다. 카드 소유권·활성 플레이어·페이즈·소환 횟수·공격 가능 여부는 기존 ProcessCommand가 최종 검증한다.

초기 턴 시작 시 SequenceNumber=1이고, 성공한 플레이어 명령마다 1 증가한다. 거부는 증가시키지 않는다. 클라이언트는 자신이 마지막으로 받은 StateSnapshot의 SequenceNumber를 보낸다. 불일치 시 StaleSequence로 거부한다. 요청자에게 CommandAccepted를 먼저 보내고, 양쪽에 같은 순번의 GameEvents와 StateSnapshot을 보낸다. 한 연결의 FIFO가 이 순서를 보존한다.

플레이어별 RequestId 결과 캐시는 재접속 이후에도 유지한다. 동일한 검증된 JSON 요청 재전송은 기존 응답과 최신 스냅샷만 돌려주며 이벤트를 다시 실행하지 않는다. 같은 ID를 다른 내용으로 쓰면 RequestIdConflict다. 처리 가능한 명령 요청 결과(규칙/오래된 순번/허용 목록 거부 포함)를 최대 4096개 보관하고 초과 시 RequestLimit으로 거부한다. 오래된 ID를 제거해 재실행하는 동작은 하지 않는다. 형식 검증 실패/권한 불일치 등 명령 처리 이전 거부는 캐시하지 않는다.

스냅샷은 내부 GameState를 통째로 직렬화하지 않는다. 자신의 Hand에만 카드 목록을 넣고 상대 Hand는 빈 배열이다. 양쪽 Deck은 개수만 전송하며 카드 ID·정의·순서를 전송하지 않는다. 공개 Field/Graveyard와 Life, 손패/덱 개수, 턴·페이즈·승자는 양쪽에 동일하다. 이벤트는 EventsForPlayer() 후 현재 뷰어가 볼 수 없는 카드 참조도 제거하고 허용된 필드만 DTO에 넣는다. 카드 정의 전체 목록/플레이어 덱 목록은 네트워크 DTO에 없다.

Unity는 수신 순서로 이벤트 로그를 처리하고 뒤따르는 스냅샷을 원자적으로 적용한다. 중복/이전 이벤트는 무시하고 순번에 공백이 있으면 StateResyncRequest를 보낸다. 명령 응답만으로 로컬 규칙을 낙관적으로 실행하지 않는다.

## MatchSession 수명과 재접속

프로세스 시작 시 빈 MatchSession을 하나 만든다. 두 좌석을 처음 배정하면 고정 테스트 정의/덱으로 초기화하고 P1 턴을 시작한다. 연결이 끊겨도 상태·좌석·요청 결과는 유지하며 상대는 자기 차례의 합법적 동작을 계속할 수 있다. 기권/타임아웃 승리는 이번 구현에 없다.

토큰은 Windows BCryptGenRandom 또는 Linux getrandom에서 32바이트를 생성해 64자 hex로 전달한다. 평문 로그나 디스크에 저장하지 않는다. 토큰과 MatchId가 일치하고 기존 연결이 끊긴 좌석만 다시 바인딩한다. 기존 연결이 살아 있으면 SessionConnected로 거부하며 강제 탈취하지 않는다. 재접속에는 이벤트 이력 대신 최신 뷰어 전용 스냅샷을 제공한다. 종료된 게임도 재접속하면 Finished/WinnerPlayerId를 복구한다.

현재 좌석은 프로세스 수명 동안 예약된다. 게임이 끝나도 새 매치를 자동 생성하지 않으므로 새로운 게임은 서버를 재시작한다. 토큰은 프로세스 재시작 시 소멸한다. 토큰은 bearer 인증이므로 로컬 ws 용도이며 외부 배포 시 wss/만료/토큰 회전 설계가 필요하다.

## 고정 데이터와 수동 플레이 검증

테스트 설정: InitialLife=3, InitialHandSize=1, DrawPerTurn=0, AllowManualDraw=false. P1 덱은 Test Attacker(ATK3/HP5) 2장, P2 덱은 Test Defender(ATK4/HP3) 2장이다. 기존 GameCore 데모와 같은 임시 검증용 설정이다. P1 손패 ID=1, P2 손패 ID=3이다.

1. 서버를 시작하고 A/B를 연결한다. ID와 MatchId, P1/P2 자신의 손패, 상대 손패 개수만 보이는지 확인한다.
2. P1은 손패 #1을 클릭한 뒤 Summon을 누른다. P2에도 공개 필드 #1이 보인다.
3. P1은 Next phase 두 번(Main -> Battle -> End), End turn을 누른다.
4. P2는 Start turn, 손패 #3 선택, Summon을 누른다. Next phase 두 번, End turn을 누른다.
5. P1은 Start turn, Next phase로 Battle에 들어간다. 자신의 필드 #1, 상대 필드 #3을 선택하고 Attack target을 누른다.
6. 양쪽에서 P1 #1 HP=1, P2 묘지 #3을 확인한다. P1은 Next phase, End turn을 누른다.
7. P2는 Start turn, Next phase 두 번, End turn을 누른다.
8. P1은 Start turn, Next phase, #1 선택, Direct attack을 누른다.
9. 양쪽에서 P2 Life=0, P1 VICTORY/P2 DEFEAT을 확인한다.
10. 진행 중 Disconnect/Reconnect와 Resync snapshot도 검증한다. 불법 페이즈 소환/타인 카드 조작 등은 CommandRejected로 표시된다.

소환한 몬스터는 다음 자신의 턴까지 공격할 수 없다. End turn은 End 페이즈에서만 허용한다. 다음 플레이어는 Start turn을 직접 누른다. 이 동작은 기존 GameCore API를 그대로 따른다.

## 테스트와 개발 검증

CTest에는 기존 GameStateTests(내부 284개 그룹), NetworkTests, WebSocketIntegration을 등록한다. Python 추가 패키지는 필요 없다.

실제 WebSocket 테스트는 2인 참가/세 번째 신규 참가 거부, 잘못된 JSON·필수 필드·타입·범위·중복 키·깊이·크기, PlayerId 위조, 타인 카드, 내부 Command, 잘못된 MatchId, 동일 요청 재전송/충돌, 오래된 순번, 동일 순번 명령 두 개의 연속 전송, 초기 비공개 상태, 매 명령 뒤 공개 상태 일치, 연결 종료/재접속/재동기화, 전투 HP와 묘지, 직접 공격 종료와 양쪽 승자를 검사한다.

C# 대체 검증:

```powershell
dotnet build ProjectC.Client/Tests/ClientValidation.csproj
# Unity 설치 위치가 다르면 -p:UnityManagedPath=".../Editor/Data/Managed/UnityEngine"
dotnet run --project ProjectC.Client/Tests/ClientTransportIntegration.csproj -- C:/Dev/ProjectC/build/Debug/ProjectCNetworkServer.exe
```

첫 명령은 Unity 실제 DLL을 참조해 UI 포함 C# 코드를 컴파일한다. 두 번째는 .NET 10 SDK를 사용하며 Unity와 같은 WebSocketTransport 소스로 두 연결을 만들고 전체 전투/승패·공개 상태·종료 후 재접속을 검사한다. CI는 Windows MSVC/Linux GCC에서 CTest와 C# 연결 테스트를 실행하고 Docker 빌드 중에도 CTest를 실행한다. Docker CI는 대기 중 프로세스 유지와 SIGTERM 종료 코드 0도 확인한다. CI 워크플로 파일 변경은 로컬 검증과 구분하며 원격 Actions 실행은 commit/push하지 않아 수행하지 않았다.

## 범위와 다음 단계

로컬 단일 매치의 최소 네트워크 플레이를 구현한다. 계정, DB, 랭킹, 클라우드, 관전, RP 응전 스택은 없다. 모바일/WebGL 전송은 검증하지 않았고 특히 WebGL의 ClientWebSocket은 지원 대상으로 삼지 않는다. UI는 카드 그림 없는 IMGUI이며 이벤트를 로그에 순서대로 반영한 뒤 스냅샷으로 상태를 갱신한다.

후속 작업은 Unity 두 창의 수동 플레이 확인, 요청한 Unity 6.3 LTS 전환 여부 결정, 자동화된 Unity PlayMode UI 테스트, 매치 재시작/토큰 만료 정책, 효과 선택 UI와 검증된 명령 스키마 확대다. 외부 서비스 배포는 현재 범위에 포함되지 않는다.

## 의존성과 검증 결과 (2026-10-30)

- [Boost 1.86.0 공식 배포](https://www.boost.org/releases/1.86.0/): Asio/Beast/System 헤더를 사용한다. Boost Software License 1.0. 아카이브 SHA-256을 Network.cmake에 고정했다.
- [nlohmann/json 3.11.3 공식 릴리스](https://nlohmann.github.io/json/home/releases/v3.11.3/): JSON 파싱/직렬화. MIT License. 공식 릴리스 아카이브 SHA-256을 고정했다.
- 별도 패키지 설치 대신 CMake FetchContent가 다운로드하고 검증한다. 최초 빌드는 인터넷과 Boost 전체 헤더 아카이브 저장 공간이 필요하다. WebSocket 서버에 Boost 바이너리 라이브러리나 OpenSSL은 필요 없다.

| 검증 | 결과 |
|---|---|
| Windows Visual Studio 2022 MSVC, Debug | 서버/기존 데모/테스트 모두 빌드 성공 |
| 기존 GameCore | 284개 그룹 통과, 기존 시나리오 Replay matches=true |
| Windows CTest | GameStateTests, NetworkTests, WebSocketIntegration 3/3 통과 |
| C# Unity DLL 참조 빌드 | netstandard2.1, 경고 0/오류 0 |
| 동일 C# 전송 소스의 실제 두 연결 | 전체 전투·직접 공격 승리·공개 상태 일치·종료 후 재접속 통과 |
| 실제 Unity Editor batchmode | 6000.0.81f1 Windows Development 플레이어 빌드 성공, 에디터 종료 코드 0 |
| Linux Docker GCC Release | 이미지 빌드 및 CTest 3/3 통과 |
| compose 외부 포트 실제 연결 | 호스트 17777 -> 컨테이너 7777로 전체 WebSocket 시나리오 통과 |
| 컨테이너 수명/종료 | 테스트 후에도 Running=true, docker stop SIGTERM 종료 코드 0 |
| Unity 두 창에서 버튼 조작 | 수동 검증 필요; 실제 수행 완료로 보고하지 않음 |
| 원격 GitHub Actions | 워크플로 구성 완료, 원격 실행 미수행 |

로컬 증거는 `build/UnityValidation.log`, `build/DockerValidation.log`와 생성된 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe`에 있다. 이 빌드 산출물은 Git 추적 대상에서 제외한다. 검증용 컨테이너와 compose 네트워크는 종료·제거했다. Git commit/push는 하지 않았다.
