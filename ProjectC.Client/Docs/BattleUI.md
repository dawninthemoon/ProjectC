# Project C — 프리팹 기반 전투 UI

2026-10-30. Unity **6000.0.81f1**에서 구현·검증했다. 설치된 실제 프로젝트 버전이며 6.3 검증 결과로 표기하지 않는다.

## 바로 실행

1. 저장소 루트에서 `cmake --build build --config Debug --parallel 4` 후 `./build/Debug/ProjectCNetworkServer.exe --port 7777` 실행.
2. Unity에서 `Assets/Scenes/BattleScene.unity`를 열고 Play. 이전 `SampleScene`은 전투 화면 진입 씬이 아니다.
3. Windows 빌드는 `Project C > Build Windows Multiplayer Client`. 결과는 `Build/Multiplayer/ProjectC.Client.exe`다. 기본 1280×720 창 모드이며 창 크기를 변경할 수 있다.
4. 두 클라이언트에서 CONNECT → 한쪽 CREATE MATCH → 표시된 MatchId를 다른 쪽에 입력하고 JOIN MATCH ID.
5. DECK 버튼으로 덱 선택 후 양쪽 READY TO PLAY. 자동으로 전투 화면과 첫 턴이 시작된다.

화면 설계 기준은 1920×1080이다. CanvasScaler의 Expand 설정과 중앙 기준 Table을 사용해 다른 비율에서 전체 화면을 유지한다. 1280×720에서도 플레이할 수 있으며, 손패가 많을 때 작은 글자는 왼쪽 상세 정보로 확인한다.

## 실제 에셋과 편집

| 에셋 | 역할 |
|---|---|
| `Assets/Scenes/BattleScene.unity` | BattleScreen 프리팹 인스턴스, 카메라, Input System EventSystem. 빌드 시작 씬 |
| `Assets/Prefabs/Battle/BattleScreen.prefab` | Canvas, 배경, 전장, 로비, 버튼, 로그, 상세 정보, 결과창, 연출 오브젝트 및 직렬화된 참조 |
| `BattleCard.prefab` | 손패·필드·묘지 공통 카드. Face/Inset/PlaceholderArt/Name/Stats/Status/Accent |
| `CardBack.prefab` | 상대 손패 전용. 이름·스탯 표시 오브젝트 비활성화 |
| `PlayerPanel.prefab` | 플레이어 이름, Life, 덱·손패·묘지 장수, 현재 턴 표시 |

Prefab Mode에서 위치·색상·폰트·카드 프레임을 직접 편집할 수 있다. `PlaceholderArt`는 일러스트 교체 지점이다. 런타임에 Canvas나 버튼을 생성하지 않고, 동적 카드만 저장된 카드 프리팹을 Instantiate한다. 기존 OnGUI 및 자동 UI 생성 Bootstrap은 제거했다.

`Assets/Editor/AuthorBattleAssets.cs`는 에셋 제작 때 사용한 **에디터 전용** 도구다. 게임 실행/에셋 임포트 시 자동 실행하지 않는다. `Project C > Author battle prefabs` 메뉴는 저장된 프리팹과 BattleScene을 다시 작성하므로, 직접 편집한 에셋을 유지할 때 실행하지 않는다. 일반 실행·빌드에는 이 메뉴가 필요 없다.

## 스크립트 책임

| 스크립트 | 역할 |
|---|---|
| `Network/MultiplayerDebugScreen.cs` | 기존 이름/API를 유지한 네트워크 컨트롤러. 화면 그리기 없음. 이벤트·메시지를 Unity 메인 스레드에서 처리 |
| `Battle/BattlePresenter.cs` | 공개 스냅샷 바인딩, 선택 상태, 명령 버튼, 새 게임/재접속/결과 화면 |
| `LobbyPanelView.cs` | 주소·생성·참가·덱 선택·준비·재대전·퇴장·재접속 |
| `CardZoneView.cs`, `CardWidget.cs` | 프리팹 카드 재사용, 호버·선택·상태·공격 대상 표시 |
| `PlayerPanelView.cs`, `CardDetailView.cs` | Life/장수 및 카드 상세 정보 |
| `BattleLogView.cs` | 최근 5개 이벤트, 스크롤 가능한 최근 40개 기록, 명령 거절 이유 |
| `BattleEffectsView.cs` | 턴 배너, 드로우 이동, 순차 카드 이벤트 팝업. 게임 상태 변경 없음 |

`EventReceived`, `NoticeReceived`, PlayerId/Lobby/Decks/Busy 등 읽기 API와 LeaveMatch/CloseMatch/RequestSnapshot을 컨트롤러에 추가했다. WebSocketTransport의 수신 큐·송신 큐·순번 검사는 유지한다.

## 조작과 표시

- 손패 선택 → SUMMON SELECTED. 서버의 `CanSummon`이 참일 때만 버튼 활성화. 일반 소환은 현재 규칙대로 턴당 1회, 무료다. 없는 코스트/자원을 만들지 않았다.
- ENTER BATTLE PHASE → 공격 가능한 자신의 필드 카드 선택 → 강조된 상대 필드 카드 선택 → ATTACK TARGET.
- 상대 필드가 비어 있을 때만 DIRECT ATTACK 활성화. 소환 멀미/공격 사용 상태를 별도로 표시한다.
- END TURN은 기존 서버의 페이즈 진행·턴 종료·상대 턴 시작 처리를 호출한다. Start turn을 누를 필요 없다.
- 호버 시 카드가 확대되고 왼쪽 상세 정보가 바뀐다. 선택 카드는 위로 올라오며 호버 해제 후 선택 카드 설명으로 돌아간다. 상세 패널은 고정 영역이어서 화면 밖으로 나가지 않는다.
- GRAVEYARDS는 양쪽 공개 묘지를 표시한다. 카드가 많으면 가로 스크롤/드래그한다. 같은 버튼으로 닫는다. BATTLE LOG도 같은 방식으로 열고 닫는다.
- LOBBY / CONNECTION에서 진행 중 연결 해제와 재접속이 가능하다. 재접속은 기존 토큰과 최신 스냅샷을 사용한다.
- 승패는 서버 결과만 표시한다. 양쪽 REQUEST REMATCH로 새 게임, RETURN TO LOBBY로 퇴장한다.

## 연출

턴 배너는 스냅샷의 TurnNumber/TurnStarted/ActivePlayerId로 표시한다. 입력을 가로막지 않으며 상태 갱신을 지연하지 않는다. 재접속 시 현재 턴을 표시할 수 있지만 놓친 행동을 임의로 재생하지 않는다.

드로우는 서버 CardMoved(Deck → Hand) 이벤트로 시작한다. 저장된 DrawCard 오브젝트를 덱 위치에서 손패 위치로 이동시킨다. 상대는 HIDDEN 카드 뒷면만 이동하며 공개되지 않은 카드 데이터는 만들지 않는다. **기본 테스트 덱은 DrawPerTurn=0**이므로 기본 게임에서 매 턴 드로우가 없는 것이 정상이다. 실제 드로우 테스트만 별도 데이터 사본의 DrawPerTurn을 1로 설정한다.

소환·효과 발동·피해·파괴는 짧은 순차 팝업으로 표시한다. 공개 카드 이름/효과 설명을 사용하고 카드가 비공개이면 일반 문구만 표시한다. 최대 24개의 장식 이벤트를 대기시키며 네트워크 이벤트 로그와 권위 상태는 별도로 즉시 반영한다. 새 매치/새 게임에서 연출 큐와 선택을 초기화한다.

## 서버 공개 DTO 추가

프로토콜 버전 2와 명령은 유지했다. `MatchSession.cpp` 공개 DTO에 다음 표시 전용 필드를 추가했다.

- CardView: `RulesText`, `ManifestationText`, `SummoningSick`.
- StateView: `FieldLimit`.
- EventView: `FromZone`, `ToZone`.

효과 설명은 실제 공개된 CardDefinition의 트리거·대상·액션·수치에서 생성한다. 현현 가능 형태는 정보만 표시하며 원격 Transform이나 내부 효과 명령을 추가로 노출하지 않는다. 상대 손패 배열은 계속 비어 있고, 덱 구성·순서와 토큰은 UI 로그에 출력하지 않는다.

## 자동 검증

- Windows MSVC 빌드 및 CTest 3/3 통과. 기존 GameCore **284개 그룹** 통과.
- Linux Docker 빌드와 컨테이너 빌드 단계 CTest 3/3 통과.
- Unity PlayMode: 프리팹 호버/선택/숨김 카드/팝업/입력, 실제 4클라이언트·2매치·UI 전투·승리·재대전·재접속, 오래된 epoch/다른 매치 무시, 실제 서버 드로우 테스트.
- 1920×1080, 1280×720, 10장 손패 Canvas 렌더링 캡처. `build/UnityMatchValidation/Artifacts`에 저장된다. 화면 배치 캡처의 카드 데이터는 테스트 fixture이며 실제 연결 검증과 구분한다.
- `ClientValidation.csproj`는 네트워크 컨트롤러 C# 컴파일만 검사한다. 전체 uGUI C# 컴파일·직렬화 참조는 Unity PlayMode와 Windows Player 빌드로 검사한다.

자동화는 사용자 프로젝트를 잠그지 않도록 `build/UnityMatchValidation` 사본에서 실행했다. 결과 로그는 `build/BattleAuthor.log`, `build/BattlePlayMode.xml`, `build/BattlePlayMode.log`, `build/BattleWindowsBuild.log`다. Unity Test Runner에서는 PlayMode 전체 실행으로 같은 테스트를 실행할 수 있다. C++ 서버 기본 경로가 다르면 Unity 명령행에 `-projectcServerPath <서버 exe 절대 경로>`를 전달한다.

## 수동 검증 절차와 한계

1. Windows exe 두 개를 각각 실행하여 CONNECT/CREATE/JOIN/READY 클릭, 서로 다른 손패·상대 뒷면 확인.
2. P1 Strikers, P2 Wardens 선택. P1 소환 후 턴 종료. P2 소환 시 효과 팝업 및 ATK 증가 확인 후 턴 종료.
3. P1 전투 페이즈에서 자신의 준비된 카드와 상대 카드를 선택, ATTACK TARGET. 양쪽 필드/묘지/Life 일치 확인.
4. 각자 턴을 넘긴 뒤 P1 전투 페이즈에서 DIRECT ATTACK. 양쪽 승패 화면 확인.
5. 양쪽 재대전 요청, 필드·Life·선택·연출 초기화 확인. 진행 중 DISCONNECT/CONNECT로 복구 확인.
6. 창 크기 변경, 실제 마우스 호버/드래그/스크롤, 키보드 입력과 클립보드 붙여넣기 확인.

자동 테스트는 버튼 이벤트 및 UI raycast 경로를 검사한다. 두 독립 Windows 창을 사람이 끝까지 마우스로 플레이한 검증은 별도 수동 항목이다. 카드 그림·리더 초상화·음성은 플레이스홀더이고 한국어 현지화는 아직 하지 않았다. 복잡한 타깃 조건, 자원 비용, 새로운 카드 규칙을 임의로 추가하지 않았다. 장문의 다중 효과 설명과 극단적으로 큰 FieldLimit의 UI는 추후 확장이 필요하다.

다음 단계는 실제 카드 아트 교체, 한글 폰트/문구, 더 다양한 카드의 상세 정보 스크롤, 드래그 조작, 독립 Windows 창 수동 플레이 검수다. 아트 교체와 배치 수정은 프리팹에서 진행할 수 있다.
