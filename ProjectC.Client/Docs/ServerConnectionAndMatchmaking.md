# 서버 연결 씬과 로비 자동 매칭

2026-11-03. 요청에 따라 **구현만 진행**했다. Unity 실행/컴파일 확인, 빌드, 테스트 작성·실행, Render 실접속 및 화면 검증은 수행하지 않았다.

## 실행 흐름

`ConnectionScene → LobbyScene → MatchmakingFound → BattleScene`

- `ConnectionScene`이 Build Settings의 첫 활성 씬이다. 기존 ClientServices/ClientApp의 DontDestroyOnLoad 수명주기를 재사용한다. 뒤의 씬에 있는 중복 ClientServices만 파괴하므로 기존 소켓은 유지된다.
- 연결 시도 중 점 애니메이션과 “서버에 연결 중”을 표시한다. 소켓 연결뿐 아니라 기존 ListDecks/DeckCatalog 핸드셰이크까지 기다린다.
- 실패 또는 기본 60초 타임아웃 시 진행 중 연결을 폐기하고 공통 모달에 `서버에 연결할 수 없습니다.`와 `재시도` 하나를 표시한다. 재시도 중 중복 요청을 막는다.
- 로비·매칭·대전 준비 중 연결이 끊어지면 연결 씬으로 이동한다. 기존 SessionToken/JoinMatch 복원은 재사용하며 새 복잡한 재접속 시스템은 만들지 않았다. 재연결 성공 시 로비를 거쳐 서버의 기존 대전이 있으면 배틀로 돌아간다.

## 환경 설정

`Assets/Resources/Network/ServerConnectionSettings.asset`

- Default Environment: **Production**.
- Production: **`wss://projectc-server.onrender.com`**, 별도 포트 없음.
- Local: **`ws://127.0.0.1:7777`**.
- Timeout Seconds: 60. Render 기동 대기를 고려한 값이며 Inspector에서 변경 가능하다.

로비 **설정 → 서버: Production/Local** 버튼으로 환경을 전환하면 연결 씬으로 이동해 다시 연결한다. 선택은 `ProjectC.Connection.Environment` PlayerPrefs에 저장한다. 첫 실행에는 위 에셋의 기본 환경을 사용한다. 이미 선택한 환경이 있으면 저장값이 우선한다. 검색 중 또는 대전에 들어간 상태에서는 환경 변경 버튼을 잠근다.

## 매칭과 덱

서버는 이미 `QueueMatch`, `CancelQueue`, `MatchmakingQueued`, `MatchmakingCancelled`, `MatchmakingFound`를 구현하고 있다. 서버가 대기열에서 두 사용자를 고른 뒤 기존 CreateMatch/JoinMatch 절차로 세션을 만든다. 서버 코드와 JSON 필드/프로토콜 버전은 변경하지 않았다.

로비의 MatchPanel에는 덱 선택, 검색 시작/취소, 상태만 표시한다. 수동 연결/주소/방 ID/방 생성/참가/Ready 컨트롤은 비활성화했으며 기존 직렬화 필드와 참조는 호환성을 위해 남겼다. 개인 덱 저장소와 서버 DeckCatalog를 재사용해 덱 버튼으로 저장된 개인 덱들과 서버 덱을 순환한다. 개인 덱을 편집·복사하는 기존 덱 관리 화면도 유지한다.

- 저장된 개인 덱이 있으면 기존 선택을 사용한다. 서버 덱은 자동으로 첫 항목을 골랐다는 이유만으로 선택 완료 처리하지 않으며 사용자가 덱 버튼으로 선택해야 한다.
- 덱 미선택/미완성/카탈로그 불일치 상태에서는 검색 버튼을 비활성화한다. 기존 DeckValidator는 표시 피드백에만 사용하며 실제 덱과 규칙 검증은 서버가 담당한다.
- 큐 진입 시 개인 덱의 ID 목록과 이름, 카탈로그 버전 또는 서버 DeckId를 복사해 고정한다.
- 서버가 매치를 할당하면 고정한 개인 덱으로 기존 SubmitDeck을 보내고, DeckAccepted 후 기존 SetReady를 자동 전송한다. 서버 덱도 고정한 DeckId로 SetReady를 자동 전송한다. 별도 Ready 버튼을 요구하지 않는다.
- MatchmakingFound/서버 매치 할당 상태에서 배틀 씬으로 이동한다. 준비 중에는 배틀의 기존 수동 로비 패널을 다시 노출하지 않는다. 게임 시작/멀리건은 기존 서버 흐름을 따른다.
- 큐 요청 및 취소 요청 ID와 기존 Busy 상태를 사용해 연타·중복 요청을 막는다. 매칭 성립과 취소가 경합하면 서버가 이미 할당한 매치를 우선한다.
- 준비 중 덱 제출/서버 거절은 공통 팝업으로 알리고 로비로 나가도록 한다. 연결 단절은 연결 씬에서 처리한다.

## 공통 팝업

`Assets/Resources/UI/CommonPopup.prefab`와 `CommonPopupView`를 사용한다.

```csharp
ClientApp.Instance.ShowPopup(
    "메시지",
    new PopupAction("확인", OnConfirm),
    new PopupAction("취소", OnCancel));
```

독립 Overlay Canvas(sortingOrder=1000), 화면 전체 검은 반투명 Blocker, 메시지, 수평 버튼 컨테이너 및 비활성 버튼 템플릿으로 구성한다. 버튼은 필요한 수만큼 만들고 재사용한다. 각 버튼을 누르면 모달 루트와 차단 패널을 먼저 숨긴 뒤 콜백을 실행한다. 씬 이동 때도 모달을 닫는다. 확인/취소 등 다중 버튼을 지원하며 현재 연결 실패에는 재시도 하나만 쓴다.

## 변경 파일

- 신규 `Scripts/FrontEnd/ServerConnectionSettings.cs`, `ConnectionScenePresenter.cs`와 meta.
- 신규 `Scripts/CommonUI/CommonPopupView.cs` 및 폴더/meta.
- 신규 `Resources/Network/ServerConnectionSettings.asset`, `Resources/UI/CommonPopup.prefab` 및 폴더/meta.
- 신규 `Prefabs/FrontEnd/ConnectionScreen.prefab`, `Scenes/ConnectionScene.unity` 및 meta. 기존 카메라/EventSystem/ClientServices 씬 구성을 재사용했다.
- `Prefabs/FrontEnd/ClientServices.prefab`: 연결 설정 에셋 참조.
- `Prefabs/FrontEnd/LobbyScreen.prefab`: 수동 컨트롤 비활성화, 덱/검색 배치, 환경 전환 버튼.
- `Scripts/FrontEnd/ClientApp.cs`: 환경 저장/전환, 공통 모달, 연결 단절 시 씬 이동.
- `Scripts/FrontEnd/LobbyScenePresenter.cs`, `Scripts/Battle/LobbyPanelView.cs`: 덱 선택·검색/취소 표시와 자동 배틀 이동.
- `Scripts/Network/MultiplayerDebugScreen.cs`: 연결 준비/오류 상태, 덱 선택과 고정, 기존 프로토콜의 자동 덱 제출/Ready 연결.
- `Scripts/Battle/BattlePresenter.cs`: 자동 매칭 중 기존 수동 로비 숨김.
- `ProjectSettings/EditorBuildSettings.asset`: ConnectionScene을 첫 씬으로 추가.
- `Editor/AuthorFrontEndAssets.cs`: 향후 로비 제작 시 환경 버튼·새 시작 씬·숨김 컨트롤 반영.
- `Editor/AuthorBattleAssets.cs`: 기존 작성 중이던 Production 주소 문자열의 잘못된 스킴을 올바른 상수로 연결.

## Unity 에디터 설정

씬/프리팹/설정 참조와 Build Settings는 파일에 반영했다. 별도 제작 도구 실행이나 수동 연결을 요구하지 않는다. Unity가 새 에셋을 import한 후 `ConnectionScene`에서 시작하면 된다. 환경/타임아웃은 위 설정 에셋 또는 로비 설정에서 바꾼다. 에디터에서 로비 씬을 직접 시작해도 연결 준비 전에는 연결 씬으로 보낸다.

이번 작업에서 생성한 UI/씬 파일의 실제 Unity import, C# 컴파일, 네트워크 접속 및 매칭 동작은 **미검증**이다. 테스트/검증을 실행하지 말라는 요청을 따랐으며 실행 결과를 확인했다고 보고하지 않는다. 기존 전투 연출 작업과 사용자가 임포트한 에셋은 되돌리지 않았다.
