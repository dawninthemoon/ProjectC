# 드로우·손패 표시 상태 개선

2026-11-02, Unity 6000.0.81f1. C++ GameCore, 프로토콜 v2, 기본/백귀야행 카드 데이터는 변경하지 않는다.

## 원인과 변경 파일

기존 BattlePresenter는 최신 손패를 먼저 렌더링한 뒤 이벤트를 처리했다. BattleEffectsView는 설명 팝업과 별도 드로우 복제본을 같은 FIFO에서 재생했다. CardZoneView는 배열 위치로 위젯을 재바인딩했고, CardTableLayout.Arrange는 매번 최종 위치로 즉시 이동했다. Hover도 처음 저장한 sibling index로 복원해 카드 수가 바뀌면 순서가 어긋날 수 있었다.

- `MultiplayerDebugScreen.cs`: 기존 스냅샷 처리 완료 후 UI에 알리는 로컬 SnapshotReceived 이벤트를 추가했다. 서버 상태 갱신과 명령 승인은 연출을 기다리지 않는다. wire 메시지 변경 없음.
- `BattlePresentationState.cs`: 서버 DTO의 독립 복사본으로 화면의 손패·필드·묘지·공개 제외 영역과 수량을 관리한다. 서버 이벤트의 이동·피해·변신을 반영하며 게임 규칙을 계산하지 않는다. 명령 가능 여부는 최신 서버 DTO를 따른다.
- `BattlePresenter.cs`: 이벤트와 해당 스냅샷을 묶어 순서대로 재생하고 완료 시 스냅샷으로 표시 상태를 보정한다. 연속 Draw는 간격을 두고 겹쳐 재생하며 다음 상태 변화는 앞선 Draw의 착지를 기다린다. 기존 소환·피해 강조/배치는 이벤트 순서대로 적용한다.
- `CardZoneView.cs`: 공개 카드는 ID로 같은 위젯을 유지하고 제거된 위젯은 비활성 풀에 보관한다. 상대 손패는 수량으로만 관리한다. 활성 위젯 목록과 Sorting을 관리한다.
- `CardTableLayout.cs`: 배치와 드로우 도착점이 같은 GetSlot 계산을 사용한다. 위치·회전을 보간하고 비행 중 위젯의 Transform은 건드리지 않는다. 초기 표시와 복구 때만 Snap한다.
- `BattleEffectsView.cs`: 실제 손패 위젯을 기존 DragVisualRoot로 옮겨 덱 중심에서 Bézier 경로로 이동시킨다. 도착하면 같은 위젯을 손패 계층에 돌려놓는다. 추가 Canvas/드로우 복제본은 만들지 않는다. 기존 직렬화 필드와 옛 DrawCard 오브젝트는 호환성을 위해 보존하되 복제본을 표시하지 않는다.
- `CardWidget.cs`: 비행 중 Hover/클릭/드래그를 차단한다. 일반 Hover는 맨 위로 올리고 종료 시 현재 손패 순서로 복원한다. 부모 비활성화 중에는 hierarchy 순서를 변경하지 않는다.
- `BattleVisualSettings.cs`, `BattleVisualSettings.asset`: 아래 설정을 제공한다.
- `DrawPresentationPlayModeTests.cs`, `DrawInteractionPlayModeTests.cs`: 표시 상태와 실제 서버 입력 회귀를 검증한다. 기존 테스트 파일은 수정하지 않았다.

턴 배너와 설명 팝업은 독립적으로 재생한다. 새 설명은 현재 팝업을 교체하고 모든 이벤트 기록은 전투 로그에 남는다. 상대 손패는 비행 중에도 CardView 없이 뒷면만 표시한다. 이벤트와 스냅샷이 다른 프레임에 도착해 자기 카드 정의가 아직 없으면 ID만 있는 임시 표시를 사용하고 다음 스냅샷에서 보정한다. 로컬 카탈로그로 숨겨진 정의를 추정하지 않는다.

재접속/resync/새 게임/비활성화 시 대기열과 연출을 정리하고 최신 상태로 복구한다. Unity가 비활성화 중 hierarchy 이동을 금지하므로 필요한 재부모화는 재활성화 후 수행한다. 재생 예외와 과도한 적체도 최신 스냅샷으로 복구한다.

현재 BattleScreen에는 옛 LobbyPanel, Resync, ConnectionLabel, DebugLabel이 없다. 코드의 필수 참조를 선택 사항으로 처리해 분리된 LobbyScene과 호환시켰다. 사용자 변경이 있던 BattleScreen.prefab은 수정하지 않았다.

## Inspector 설정

`Assets/Prefabs/Battle/BattleVisualSettings.asset`에서 조절한다. 기존 설정 참조를 그대로 사용하므로 씬/프리팹 재생성이나 수동 참조 연결은 필요 없다.

| Inspector 항목 | 기본값 | 의미 |
|---|---:|---|
| Draw Duration | 0.65초 | 한 카드의 비행 시간 |
| Draw Interval | 0.16초 | 연속 드로우 출발 간격 |
| Draw Arc Height | 120 | 공유 연출 영역 좌표에서 Bézier 제어점 높이 |
| Draw Scale | 1.08 | 이동 중 크기 곡선의 확대량 조절 |
| Draw Rotation | -16도 | 출발 회전; 도착 시 손패 회전으로 보간 |
| Hand Reposition Duration | 0.28초 | 기존 카드의 재배치 시간 |
| Hand Spacing | 22 | 카드 너비에 더하는 간격; 영역 너비에 맞춰 압축 |
| Hand Curve | 24 | 부채꼴 양끝의 수직 내려감 |
| Hand Rotation | 7도 | 부채꼴 양끝 회전 |
| Animation Ease | EaseInOut | 드로우와 재배치 진행 커브 |

기존 Hover Delay, Layout Move(필드 배치), Popup Duration, Turn Fade/Hold도 유지한다. 상대 손패는 기존 Compact 배치를 사용한다.

## 검증과 남은 확인

새 표시 테스트는 손패 0/1/5/9장, 실제 위젯 비행, 설명 동시 시작, 연속 드로우, ID 유지, 최종 위치·회전·Sorting, Hover, 새 스냅샷, 이벤트/스냅샷의 프레임 분리, 상대 정보 비공개, 연결 무효화, 비활성화/재활성화와 비행 중 파괴를 검증한다.

새 서버 테스트는 기존 테스트 데이터의 임시 복사본에서 매 턴 드로우를 켜고 덱을 늘려 드래그 소환, 공격, 직접 공격, 드로우 이후 위젯 유지와 재대전을 검증한다. 기본/백귀야행 원본 데이터는 수정하지 않는다.

전체 결과: `../build/DrawRegression.xml`, 로그: `../build/DrawRegression.log`. 개별 전투 회귀 결과: `../build/DrawCombat.xml`. 실제 Canvas 캡처: `Artifacts/Draw-before.png`, `Draw-departure.png`, `Draw-flight.png`, `Draw-landed.png` (1280×720 / 1920×1080).

기존 FourClientsTwoMatchesPlayRematchAndReconnect에는 제거된 LobbyOverlay를 직접 접근하는 assertion이 남아 있다(`MultiplayerPlayModeTests.cs:259`). 기존 테스트는 보존하고 이 실패를 새 드로우/입력 테스트 결과와 구분해 보고한다.

수동 확인: BattleScene에서 실제 드로우 효과를 사용해 출발 속도·곡률·Hover 감각을 확인하고 위 설정 에셋을 조절한다. 자동 테스트와 정지 캡처는 사람이 직접 플레이하며 평가하는 연출 감각을 대체하지 않는다.
