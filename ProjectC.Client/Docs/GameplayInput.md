# Gameplay input coordination

## 기존 문제와 적용 범위

`BattlePresenter.CanAct`, 드래그 상태, 선택 패널 상태에 입력 조건이 흩어져 있었고,
`MultiplayerDebugScreen.SubmitJson`에는 선택 중 일반 명령을 차단하는 공통 검사가 없었다.
선택 확인 시 패널도 즉시 숨겨져 서버 응답 대기와 선택 완료를 구분하기 어려웠다.
기존 GameCore, C++ 서버, WebSocket v2 및 메시지 필드는 변경하지 않는다.

## 책임과 정책

- `GameplayInputCoordinator`: 네트워크 클라이언트가 소유하는 입력 권한 모델이다.
  `GameplayInputAction`별 허용 여부, 서버/로컬 선택, 서버 응답 대기를 관리한다.
  서버 스냅샷의 현재 플레이어, 시작/종료, 멀리건, 선택 해결 상태를 사용한다.
  카드 비용, 공격 가능 대상, 효과 규칙은 서버가 검증하며 여기에서 재구현하지 않는다.
- `InputScope`: GUID와 owner 이름을 가진 `IDisposable` 핸들이다. 모든 활성 scope의
  allowlist를 **교집합**으로 적용한다. 임의 순서 해제와 반복 Dispose가 안전하고,
  Reset 이전 핸들이 나중에 해제되어도 새 scope에 영향을 주지 않는다.
- `ModalBlocker`: 화면 전체 Image, Canvas, GraphicRaycaster와 소유자별 lease를 관리한다.
  마지막 lease까지 배경 차단을 유지한다. 비활성화/파괴된 소유자의 lease는 정리한다.
  각 lease의 통과 조건도 교집합이며, 가장 최근 모달의 전경 UI만 위로 올린다.
- `BattleInteractionController`: 서버 후보/기존 activation 후보로 선택 scope와
  대상 카드·플레이어의 레이캐스트 통과 영역을 구성한다. UI가 숨겨져도 서버 선택 scope는
  해제하지 않는다. 정상적인 로컬 선택 취소만 로컬 scope를 해제한다.

scope가 없을 때 기존 입력 흐름을 유지한다. scope 변경은 `Changed`로 알린다.
덱/손패/묘지/제외 선택은 해당 zone action, 확인, 상세 보기만 허용한다.
필드 선택에서는 후보 카드 ID와 zone에 일치하는 카드와 허용된 영웅만 통과시킨다.
취소는 서버 `CanCancel`을 따른다. 일반 카드 사용, 공격, 페이즈/턴 진행,
배경 선택 해제, 다른 공개 영역 열기, 로비 메뉴/로그 토글은 선택 중 차단한다.
공개 카드 갤러리 열람에도 별도 scope/lease를 사용하며 닫기 UI는 계속 동작한다.
공용 팝업은 기존 프리팹의 전체 화면 차단을 유지하고 빈 allowlist scope를 추가한다.
팝업 자체의 닫기/확인 콜백은 gameplay action과 별도로 동작한다.

## Canvas / Raycast

배틀 root Canvas 아래에 `GameplayModalBlocker`를 런타임 생성한다.
Image는 `raycastTarget=true`, 기본 검정 알파 0.12이며 `Tint`로 조정할 수 있다.
Canvas 정렬은 root + 100, 전경 picker/gallery/detail은 + 101이다.
기존 공용 팝업 정렬 1000은 그대로 유지한다. 전경에 필요한 nested Canvas와
GraphicRaycaster만 추가하며 프리팹의 이름/serialized reference는 변경하지 않는다.
lease 해제 시 기존 정렬/활성 설정을 복원한다. 추가된 Canvas는 일반 정렬로 남겨
같은 프레임의 재표시와 지연 Destroy 충돌을 피한다.
비활성 UI에 Canvas를 추가한 뒤 활성화하면서 정렬이 초기화될 경우 LateUpdate에서 복구한다.

선택 후보의 RectTransform만 `ICanvasRaycastFilter`로 배경 차단을 통과한다.
허용되지 않은 필드 카드는 계속 차단한다. 확인/취소와 갤러리는 전경에서 입력을 받는다.
실제 선택 및 명령 경로에서도 coordinator를 검사하므로 키보드 Escape,
버튼 콜백 직접 호출, 코드에서의 명령 호출로 정책을 우회할 수 없다.

## 서버 확정과 수명

`Selecting → Submitting → WaitingForServer → Resolved`를 구분한다.
전송 직전에 scope를 추가하므로 동기적으로 중복 제출을 막는다.
전송 후 picker를 유지하고 확인/취소를 비활성화한다.
`CommandAccepted`만으로 해제하지 않고, 요청 ID로 연관된 ack의 시퀀스 이상인
`StateSnapshot`에서 `ResolvingPlayerId == 0`을 확인해야 해제한다.
`CancelAction`은 같은 시퀀스에서 끝날 수 있으므로 무조건 시퀀스 증가를 기다리지 않는다.
진행 중 스냅샷은 선택 잠금을 유지한다. 서버 거절은 다시 Selecting으로 복귀한다.
새 ChoiceId는 서버의 다음 선택으로 전환하고, 대기 중 동일 ChoiceId 재수신은 중복 제출
잠금을 해제하지 않는다. 비공개 후보는 기존 서버가 제공한 DTO와 opaque token만 사용한다.

연결 종료, 게임 epoch 변경, 매치 종료, 네트워크 컴포넌트 파괴/비활성화에서 scope를 정리한다.
씬이 로드되면 씬 scope를 초기화하고 서버 스냅샷/PendingChoice/명령 대기로 재구성한다.
선택 UI나 controller만 숨기거나 파괴하면 해당 모달 lease만 해제되며 서버 선택 제한은 남는다.
재표시 시 선택 UI를 재구성한다. 서버 응답 시간 초과에 의한 기존 resync도 계속 사용한다.

수정된 기존 컴포넌트는 `MultiplayerDebugScreen`, `BattlePresenter`,
`BattleInteractionController`, `EffectTargetPicker`, `CardGalleryView`,
`PublicZoneButton`, `CommonPopupView`이다. `SubmitJson`에서 모든 현재 gameplay
command type을 action으로 매핑하고 기존 연결/Busy/매치 검사에 권한 검사를 추가한다.

## 검증

`GameplayInputPlayModeTests`는 실제 BattleScreen 프리팹과 EventSystem.RaycastAll을 사용한다.
덱 갤러리 클릭/확인, 필드 후보와 비후보, 턴 버튼 차단, scope와 모달의 중첩/비순차 해제,
소유자 파괴, UI 비활성화/재표시, 씬 unload/매치 종료/연결 해제를 확인한다.
로컬 C++ 서버의 HyakkiYagyo 1102 효과로 잘못된 선택 거절/재선택, 유효한 선택,
실제 소켓 수신 큐 지연, ack 단독 수신, 중복 확인/일반 명령 차단과 확정 후 복원을 확인한다.
별도 임시 덱 fixture를 사용하며 기본 데이터와 실험 규칙을 섞지 않는다.
운영 API/DB는 호출하지 않는다.

Unity Editor가 프로젝트를 열고 있는 경우 Assets/Packages/ProjectSettings를 별도 검증
프로젝트로 복사하여 실행한다. 실행 중인 사용자의 Editor를 종료하지 않는다.
로컬 서버 실행 파일은 `-projectcServerPath`, 기존 효과 fixture는
`-projectcInteractionData` 인자로 지정한다. 결과는 `Logs/GameplayInputRegression.xml`에 기록한다.
기존 갤러리 선택 테스트는 더 이상 표시하지 않는 `Viewport/Candidates` 버튼 대신
현재 CardGallery의 CardWidget을 사용하도록 갱신했다.

2026-11-04 Unity 6000.0.81f1 검증 결과:

| Fixture | 통과 / 전체 |
| --- | --- |
| GameplayInputPlayModeTests | 7 / 7 |
| BattleUxPlayModeTests | 4 / 5 |
| MultiplayerPlayModeTests (Content 포함) | 2 / 6 |
| SummonPresentationPlayModeTests | 5 / 5 |
| 합계 | 18 / 23 |

선택 효과의 `SpellDragPrivateChoiceReconnectAndCommit`는 통과했다.
다음 기존 실패 5개는 동일한 검증 환경에서 변경 전 gameplay 소스로 대조 실행했을 때도
같은 위치와 값으로 재현됐다. 이 작업에서 관련 없는 동작이나 assertion을 바꾸지 않았다.
대조 결과는 `Logs/GameplayInputBaseline.xml`이다(기존 16개 중 11개 통과).

- `OverlapUsesRotatedAndScaledCardAreaInEveryCanvasMode`: 겹침 0.2843, 기대값 > 0.3.
- `FourClientsTwoMatchesPlayRematchAndReconnect`: 턴 문구의 턴 번호 접미사 차이.
- `OldEpochAndForeignMatchSnapshotsAreIgnored`: 빈 매치 ID 기대에 대해 `A` 유지.
- `RealServerDrawUsesOwnHandAndOpponentBacks`: 기대 카드 수 3, 표시 수 2.
- `ServerDefinedEffectsUseClickTargetsAndManifestContext`: 효과 선택에 들어가기 전 context UI가 없음.

첫 headless 실행에서 기존 소환 연출 테스트가 GameView를 요구했으므로 전체 회귀 실행은
`-batchmode -force-d3d11`로 수행했다. 사용자의 열려 있는 Editor와 원본 프로젝트 설정은
유지했다. 배포, 운영 DB 변경, 커밋/푸시는 수행하지 않았다.

## 지원 범위와 확장

현재 서버 후보를 사용하는 카드/플레이어 선택, 단일/복수 선택, 순차 선택을 지원한다.
위치/빈 필드 슬롯, 텍스트·숫자 입력, 재정렬, 동시 비공개 선택은 별도 action/UI 계약이 필요하다.
현재 게임에는 응전/response chain이 없으며 RespondToEffect/PassResponse를 추가하지 않았다.
추후 실제 응전 상태가 도입되면 해당 action과 scope를 추가하고 SubmitJson 매핑을 확장한다.
연출 큐도 같은 disposable scope로 필요한 action만 허용할 수 있다. 현재의 연출 큐,
skip 및 서버 규칙을 이 작업에서 변경하지 않는다.
