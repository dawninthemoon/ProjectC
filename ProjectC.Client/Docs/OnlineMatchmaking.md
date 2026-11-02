# 온라인 자동 매치메이킹

로비의 대전 시작 패널에서 서버에 접속하고 **자동 매칭 시작**을 누른다. 서버가 FIFO로 상대를 배정한다. 대기 중에는 경과 시간과 서버가 응답한 대기 순서가 표시되며 **매칭 취소**를 누를 수 있다. 대기 순서는 마지막 서버 응답 값이며 실시간 순위 갱신을 의미하지 않는다.

매칭이 성립하면 기존 방의 좌석/준비 UI를 그대로 사용한다. 양쪽이 준비하면 기존 `StateSnapshot.Started` 기반 전투 씬 이동이 실행된다. 개인 덱 제출과 멀리건, 재접속, 친선전 생성/참가는 기존 경로를 유지한다. 자동 준비는 추가하지 않았다.

`QueueMatch.DeckId`는 현재 서버에서 서버 카탈로그 ID만 허용한다. 서버 덱 모드에서는 선택한 ID를 보내고, 개인 덱 모드에서는 필드를 생략한다. 로컬 개인 덱 UUID는 서버 카탈로그 ID가 아니므로 전송하지 않는다. 선택한 개인 덱은 매칭 후 준비 버튼에서 기존 `SubmitDeck → DeckAccepted → SetReady` 순서로 제출한다. 개인 덱을 QueueMatch 단계에서 UUID로 지정하려면 별도 서버 프로토콜 지원이 필요하다.

상태는 `Idle`, `Queuing`, `Matched`, `Error`로 관리한다. 시작 요청 직후부터 중복 요청과 친선전 동작을 차단한다. 취소는 서버 확인 후 Idle로 돌아가며, 먼저 매치가 성립했으면 늦은 취소 응답/NotQueued로 매치를 지우지 않는다. 중복 MatchmakingFound는 게임 번호, 스냅샷, 진행 중 덱 제출과 멀리건 선택을 초기화하지 않는다. AlreadyQueued는 대기를 유지하고, InvalidDeck/AlreadyJoined는 오류를 표시한다. NotQueued는 대기 종료를 확인하는 응답으로 처리한다. 대기 중 연결 종료는 Error로 전환하며 자동으로 재등록하지 않는다. 전송 실패로 큐 등록 여부가 불확실하면 연결을 닫아 서버가 큐 항목을 정리하도록 한다.

## Unity UI

`LobbyScreen.prefab/…/MatchPanel`에 다음 오브젝트를 추가했다. 수동 Inspector 참조 할당은 필요 없다.

- `QueueMatch`: Image + Button, 자식 `Label`: Text. 자동 매칭 시작.
- `CancelQueue`: Image + Button, 자식 `Label`: Text. 대기 중 표시하며 취소 응답 전까지 비활성화.
- 기존 `Status` Text: 연결/매칭 상태, 경과 시간, 대기 순서 및 서버 오류 표시.

`LobbyPanelView.Bind`가 이름으로 연결한다. 같은 조회 루트 아래에서 이름을 중복하지 않는다. 기존 버전의 LobbyScreen 프리팹은 **Project C → Upgrade lobby matchmaking UI**로 두 버튼을 추가할 수 있다. 이 명령은 FrontEnd 씬 전체를 다시 작성하지 않는다. 초기 제작용 Author lobby and deck editor 명령은 실행할 필요가 없다. BattleScene의 기존 로비 패널에는 자동 매칭 버튼이 필요하지 않다.

## 수동 확인

1. 두 클라이언트에서 접속 후 자동 매칭 시작. 첫 번째의 대기 시간/순서, 양쪽 좌석 배정을 확인한다.
2. 대기 중 취소, 빠른 연속 클릭, 취소 직후 상대 등록, 대기 중 연결 종료와 재접속을 확인한다.
3. 개인 덱을 선택한 두 클라이언트에서 매칭 후 준비하여 덱 제출, 멀리건 활성 서버의 멀리건, 전투 씬 진입을 확인한다.
4. 친선전 생성/ID 참가, 전투 중 재접속, 종료 후 나가기와 다시 자동 매칭을 확인한다.
5. 실제 창 해상도에서 패널 상단의 두 버튼과 상태 Text가 읽히는지 확인한다.

## 자동 검증 결과

Unity 6000.0.81f1의 별도 검증 프로젝트에서 PlayMode 전체 23/23 통과. 기존 테스트 15개와 신규 8개(실제 서버 자동 매칭 통합 1개, 메시지/오류/경합 7개)를 실행했다. 기존 개인 덱, 재접속, 멀리건과 전투 UI 테스트를 포함한다. Windows x64 Development 빌드도 성공했다. 서버와 GameCore 소스는 변경하지 않았다.

- 테스트 결과: `build/MatchmakingPlayMode.xml`, `build/Matchmaking-tests.log`
- 빌드 로그: `build/Matchmaking-build.log`
- 검증용 실행 파일: `build/UnityMatchValidation/Build/Multiplayer/ProjectC.Client.exe`

원본 프로젝트에서도 `AuthorFrontEndAssets.ValidateMatchmakingScene`으로 LobbyScene을 직접 열어 두 버튼과 Text 컴포넌트를 확인했다. 원본 씬은 LobbyScreen 프리팹 인스턴스를 사용하므로 버튼 추가는 프리팹에 저장된다. 확인 로그는 `build/Matchmaking-original-scene.log`이며, 원본 Windows 빌드는 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe`, 빌드 로그는 `build/Matchmaking-original-build.log`다.

실제 두 Windows 창의 수동 조작 및 해상도별 시각 확인은 수행하지 않았다.
