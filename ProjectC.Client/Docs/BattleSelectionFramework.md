# 공용 카드 선택 바 및 방어 배정 UI

작업일: 2026-11-12. Unity 6000.0.81f1 / macOS 15.8.1.

## 범위와 현재 연결 상태

첨부 `ProjectC_전투UI_Codex구현명세.md`, 폐기·강령·포식 목업 3장과 추가 방어 배정 이미지를 시각 기준으로 사용했다. 사용자가 서버 미완성을 알리고 그래픽 표시 틀부터 구현하도록 승인한 범위다. 문서 안의 신규 게임 규칙을 클라이언트 규칙으로 구현하지 않았다.

- 기존 온라인 `ActionChoiceView`와 `EffectSelectionView`는 실제 새 선택 바를 사용한다. 확인·취소·서버 거절·입력 잠금은 기존 명령 경로를 유지한다.
- 폐기·강령·포식별 안내, 응전 혼합 목록·패스, 방어 배정·공격 카운터는 별도 프리뷰 씬에서 조작할 수 있다. 신규 규칙의 온라인 연결 완료를 뜻하지 않는다.
- 작업 도중 사용자가 pull한 HEAD는 `4122189`다. `YakouMatch`, `YakouChain`에 비용·기력·방어·응전 패스 프로토타입이 있지만 `MatchSession` / WebSocket / Unity DTO 연결은 아직 없다. `ProjectC.Server/Docs/YakouV03Migration.md`의 “Prototype state-machine implementation”, “Raid response window”에도 이 한계가 명시되어 있다.
- 따라서 라이브 선택 안내는 서버가 보내는 최소·최대 수량과 `IsCost`를 사용한다. 손패·필드·묘지라는 위치만으로 폐기·포식·강령 규칙을 추정하지 않는다. 프리뷰는 명시적인 화면 종류를 지정한다.
- 서버, GameCore, 통신 프로토콜, 운영 API·DB를 수정하지 않았다. 커밋·푸시하지 않았다.

## S0: 확인한 재사용 구성

| 구성 | 실제 위치 및 재사용 방식 |
|---|---|
| 전투 화면 | `Assets/Scenes/BattleScene.unity`에서 사용하는 `Assets/Prefabs/Battle/BattleScreen.prefab` 수정 |
| CanvasScaler | 1920×1080, Scale With Screen Size, Screen Match Mode=Expand. 기존 설정 유지 |
| 손패 카드 / 필드 카드 | `Assets/Prefabs/Battle/BattleCard.prefab`, `BattleFieldCard.prefab`. 원본 프레임·일러스트·배치 유지 |
| 턴 버튼 | `BattlePresenter.EndTurn`. 아트·크기·기존 이벤트 유지, 전용 모드에서 동작용 투명 버튼과 문구만 덮어씀 |
| 상세창 | `CardDetailView`, `BattlePresenter.InspectCard`. 후보 카드 Hover에 재사용 |
| 대상 화살표 | 기존 `AttackArrowView`는 일반 공격용 그대로 유지. 방어용은 같은 Unity UI 계층에 별도 베지어 Graphic 사용 |
| 자원 / 존 카운터 | `BattleSpecialCostsView`, `BattleHudView`, `PublicZoneButton`. 기존 요력·덱·묘지·제외 값 유지 |
| 폰트 | `BattleText`, `LocalizedBattleFont`, `Resources/UI/BattleFontPalette` 재사용 |
| 현지화 | `Assets/Resources/ProjectC_Data - Localize.json`에 KO/EN/JA 키 추가 |
| 기존 선택 / 입력 | `EffectTargetPicker`, `CardGalleryView`, `GameplayInputCoordinator`, `ModalBlocker` 재사용 |
| 실제 선택 DTO | `Assets/Scripts/Network/Protocol.cs`의 `ActionChoiceView`, `ActionChoiceCandidateView`, `EffectSelectionView`. wire 필드 변경 없음 |
| 회귀 테스트 | `BattleUxPlayModeTests`, `GameplayInputPlayModeTests` 그대로 실행. 신규 검증은 `CardSelectionFrameworkTests` |

수정 전 `BattleUxPlayModeTests` 5개를 먼저 실행했다. 3개 통과, 아래 2개 기존 실패를 기록했다. 원본 테스트와 기대값은 변경하지 않았다. 선택 전 화면은 `Artifacts/BattleSelection/Before-Choice.png`에 보관했다. 이 기준 캡처는 1280×720이며 비교 이미지에서만 1920×1080으로 정규화했다.

## 구현한 표시와 상호작용

### 공용 카드 선택 바

- 하단 중앙 패널, 상단 안내 리본·가는 선·꽃 장식, 수량 칩, 취소·확인, 독립 눈 버튼.
- 이름과 숫자는 모두 TMP. 배경에 텍스트를 구워 넣지 않는다.
- 기존 손패 프리팹으로 후보를 표시한다. 카드 데이터 모델을 복제하지 않는다.
- 정확히 N장 / 범위 선택, 최대치 초과 방지, 선택 해제 시 확인 비활성 복귀, 비활성 후보 표시·클릭 차단.
- 선택된 후보에는 기존 강조와 별도 주홍 체크 배지. 이름판 아래 아트 우측에 체크를 둔다.
- 필드 직접 클릭과 미러 목록 선택이 같은 기존 후보 토큰 목록을 갱신한다. 필드에도 체크 표시를 붙인다. 카드가 다시 Bind되면 이전 선택 체크를 제거한다.
- 가로 ScrollRect + RectMask2D. 세로 휠을 가로 이동으로 변환한다. 카드 위 드래그도 스크롤에 전달하고 기존 CardWidget의 드래그 후 클릭 억제를 사용한다.
- 5개 후보가 들어가는 폭에서는 중앙 정렬 및 스크롤 비활성, 넘으면 스크롤. 좌우 페이지 버튼·페이지 점 없음.
- 같은 요청 재표시 시 스크롤·선택·눈 상태 보존. 후보가 제거되면 선택에서 정리한다. 새로운 요청에서는 숨김 상태를 초기화한다.
- 눈 버튼은 바 밖에 남는다. 목록·안내와 열려 있던 상세창을 숨겼다가 복원한다. 선택 중 기본 손패는 숨겨 중복 표시를 피한다.
- 선택 모달의 입력 차단은 유지하며 선택 모드 자체는 화면 전체를 어둡게 하지 않는다. 다른 모달의 기존 배경 색은 각 lease 해제 후 복원된다.
- 내 묘지 후보 선택에서는 기존 묘지 태그를 얇게 강조하고 `내 묘지` 라벨을 붙인다. 카운터 값은 변경하지 않는다.
- `CardGalleryView._showFieldMirrors`로 필드 미러 목록을 끌 수 있다. 기본은 켜짐이다.

### 방어 배정 틀

- `DefenseAssignmentView.Show(presenter, request)`에 표시용 요청을 전달한다. 요청은 허용된 공격자·방어자 쌍, 초기 배정, 선택적인 공격 카운터 값을 받는다.
- 공격자→방어자와 방어자→공격자 드래그를 모두 지원한다. 확정된 화살표 방향은 공격자→방어자로 통일한다.
- 카드 두 장을 차례로 클릭해도 배정할 수 있다. 같은 쌍을 다시 지정하면 해제, 다른 상대를 지정하면 재배정한다.
- 같은 방어자의 중복 배정은 요청의 `AllowSharedDefender`가 명시적으로 허용한 경우에만 가능하다. 허용 후보는 UI에서 추측하지 않는다.
- 연결선은 64구간 cubic Bézier UI Mesh다. 드래그 중 끝점이 움직이며 카드 이동·Hover·스케일·해상도 변화에 맞춰 다시 계산한다. 같은 높이를 지나도 곡선이 불연속적으로 뒤집히지 않는다.
- 확정선·드래그 미리보기는 Raycast를 받지 않는다. 카드 위 전용 핸들이 드래그를 받는다.
- 배정된 방어자에는 방패 표시. 요청으로 제공된 카운터만 표시하고, 0은 1.2초 주기로 알파 0.6~1.0을 변화시킨다. `HasAttacked`에서 숫자를 역산하지 않는다.
- 기존 큰 버튼의 아트를 유지하면서 `방어 확정`으로 표시한다. 실제 내부 폭 200px을 기준으로 임시 여백·Auto Size를 적용하고 종료 시 원래 텍스트 설정을 복원한다. 확정은 `Confirmed` 이벤트로 초안 사본을 전달하며 서버 명령을 임의로 보내지 않는다. 배정 0개도 확정 가능하다.
- 모드 중 일반 전투 행동을 입력 scope로 막는다. 화면 무효화·닫기에서 초안 UI와 입력 scope를 정리한다.

### 응전 틀

- 같은 바에서 손패 / 필드 출처 라벨, 단일 선택, 비활성 후보, 확인을 지원한다.
- `AllowPass`와 패스 콜백을 지정하면 기존 큰 버튼에 `패스`를 표시한다. 선택 0개에서도 패스 가능하며 실제 UI Raycast가 ModalBlocker에 가로막히지 않도록 모달 foreground를 지정한다.
- 현재 온라인 응전 이벤트·우선권·상대 발동 효과 정보 DTO가 없으므로 자동 진입 및 실제 응전 명령은 연결하지 않았다. 임시 체인 번호나 가짜 상대 정보를 만들지 않았다.

### 기존 중앙 표시 정리

방어선과 겹치던 `CenterTurnOwnerText`, `CenterTurnNumberText`는 기존 HUD의 `_showCenterStatus` 옵션으로 상시 표시되고 있었다. 이 옵션과 두 고정 라벨만 비활성화했다. 실제 턴 전환의 일시적인 `TurnTransitionImage` 연출 및 타이머 코드는 유지했다.

## 실제 1920×1080 측정

원점은 화면 좌측 상단. Unity 카메라 렌더 좌표를 캡처 시 직접 측정했다. 전체 측정은 `Artifacts/BattleSelection/Captures.txt` 참조.

| 요소 | 실제 X / Y / W / H(px) | 목표와의 관계 |
|---|---|---|
| 하단 패널 | 403 / 778 / 1114 / 249 | 명세의 폭·높이·하단 53px와 일치 |
| 가로 후보 뷰포트 | 421 / 790 / 870 / 225 | 5장 중앙 정렬, 6장 이상 스크롤 |
| 확인 버튼 | 1313 / 940 / 187 / 63 | 명세의 컨트롤 폭·확인 높이와 일치 |
| 안내 리본 | 580 / 734 / 760 / 40 | 추정 637×45보다 넓음. KO/EN/JA 안내와 기존 필드 하단을 함께 피하도록 조정 |
| 눈 버튼 | 1463 / 740 / 54 / 34 | 패널 외부. 숨김 중에도 조작 가능 |
| 후보 카드 슬롯 | 155×215, 중심 간 167 | 원본 카드 프레임 비율 유지. 실제 프레임은 원본 프리팹 내부 여백만큼 작게 보임 |

기존 필드 카드 크기·필드 최대 수·필드 행 배치는 이 작업에서 변경하지 않았다. 리본은 현재 아군 카드 하단을 피하도록 배치했다. 와이드에서는 기존 Expand 방식에 따라 16:9 기준 UI 공간을 유지하면서 중앙 정렬한다.

## 확인 방법

1. 편집 중인 씬의 미저장 변경을 먼저 저장한 뒤 Unity 메뉴 `ProjectC → UI → Selection and Defense Preview`를 연다.
2. `Open preview scene`으로 `Assets/Scenes/Previews/BattleSelectionPreview.unity`를 열고 Play한다.
3. Game View에 포커스를 두고 1=폐기, 2=강령, 3=포식, 4=응전, 5=방어를 누른다. 같은 모드는 메뉴 창 버튼으로도 바꿀 수 있다.
4. 방어 카드에서 다른 진영 카드로 드래그한다. 반대 방향의 드래그와 같은 쌍 재지정도 확인한다.

프리뷰는 Editor 전용 코드와 가상 상태를 사용한다. 운영 전투 씬에 자동 실행되지 않고 서버에 연결하거나 게임 명령을 보내지 않는다. `확인`, `패스`는 로컬 확인 메시지만 기록한다. 이 씬은 Build Settings에 추가하지 않았다.

## 파일과 에셋

수정:
- `Assets/Prefabs/Battle/BattleScreen.prefab`: 선택 바·방어 UI 인스턴스, 필드 미러 옵션, 고정 중앙 중복 라벨 비활성화.
- `Assets/Scripts/Battle/CardGalleryView.cs`, `EffectTargetPicker.cs`: 기존 선택 흐름을 새 바로 표시, 일반 공개존 열람 유지.
- `Assets/Scripts/Battle/BattleInteractionController.cs`: 선택 상태 동기화, 같은 요청의 후보 갱신 보존.
- `Assets/Scripts/Battle/GameplayInputCoordinator.cs`: 같은 요청의 후보 갱신에서 제출 잠금을 유지하면서 허용 후보를 갱신.
- `Assets/Scripts/Battle/BattlePresenter.cs`: 표시 모드 입력 차단 및 큰 버튼의 임시 문구 표시.
- `Assets/Scripts/Battle/CardWidget.cs`: 재바인딩 시 이전 필드 선택 체크 정리.
- `Assets/Scripts/CommonUI/ModalBlocker.cs`: 선택 UI 전용 backdrop tint override. 기존 모달 기본값 유지.
- `Assets/Resources/ProjectC_Data - Localize.json`: 신규 KO/EN/JA 선택·방어 안내.

추가:
- `Assets/Prefabs/Battle/CardSelectionBar.prefab`, `DefenseAssignment.prefab` 및 `.meta`.
- `Assets/Scenes/Previews/BattleSelectionPreview.unity` 및 `.meta`.
- `Assets/Scripts/Battle/Selection/`: 요청·초안 모델, 선택 바, 수평 스크롤, UI 도형, 필드 체크, 방어 핸들·베지어선·표시, 큰 버튼 override, Editor 전용 프리뷰 드라이버.
- `Assets/Editor/AuthorBattleSelection.cs`, `BattleSelectionPreviewWindow.cs`: 프리팹 작성과 프리뷰 진입.
- `Assets/Tests/PlayMode/CardSelectionFrameworkTests.cs`.

신규 래스터 아트·Shader·Material은 추가하지 않았다. 기존 카드/캐릭터/배경/턴 버튼을 재사용하고 패널·체크·눈·방패·간단한 꽃은 해상도 독립적인 Unity UI Mesh로 만들었다. 텍스트와 장식선은 별도 노드다. 원본 카드 프리팹과 Sprite/Material 참조를 교체하지 않았다.

작업 시작 전 이미 변경되어 있던 `NotoSerifKR-Bold.asset`, `Pretendard-SemiBold.asset`, `ConnectionScene.unity`는 이번 작업에서 덮어쓰지 않았다.

## 검증과 결과물

별도 APFS 복사본 `/tmp/projectc-selection-work/ProjectC.Client`에서 실행했다. 원본 Unity 프로젝트의 작업 중 씬·폰트 파일을 시험 실행으로 저장하지 않기 위한 분리다. 실제 Unity PlayMode와 Unity UI 카메라 렌더로 PNG를 캡처했으며 그림 합성으로 존재하지 않는 UI를 추가하지 않았다. 프리뷰 캡처는 실제 온라인 경기 화면이라는 의미가 아니다.

- PlayMode 총 19개: 신규 7개 + 기존 12개. 17개 통과, 수정 전과 동일한 기존 실패 2개.
- 신규 검증: 정확 수량/범위/초과/비활성/항목 소멸/초안 보존, 동일 요청 갱신 중 제출 잠금, 카드 스크롤 후 클릭 억제, 눈 토글과 스크롤 보존, 필드 직접 선택↔미러/체크 동기화, 묘지 강조와 닫기, 양방향 방어 드래그/재배정/해제, 허용 쌍 제한, 명시적 다중 방어 옵션, 실제 UI Raycast, 패스의 모달 관통, 프리뷰 씬 실행, 상태 불변성.
- pull 후 서버 HEAD `4122189`에서 `ProjectCNetworkServer`를 다시 빌드한 뒤 같은 UI 회귀 테스트를 실행했다. 서버 소스 변경은 없다.
- 기존 실제 로컬 WebSocket 서버 테스트 `RealSpellRejectReselectAndDelayedSnapshotKeepCommandAndUiLocks` 통과. 기존 선택 제출·거절·재선택·지연 스냅샷의 잠금 유지 확인.
- 1920×1080, 1280×720, 2560×1440, 2400×1080(20:9), 2560×1080에서 선택 바를 캡처하고 화면 안에 들어오는 Rect를 검사했다. 방어는 1920×1080, 1280×720, 2560×1440, 2400×1080 캡처.
- KO/EN/JA의 새 선택 바와 방어 확정 텍스트 overflow 검사 및 캡처.
- C#/Markdown/JSON 변경에 대한 `git diff --check` 통과. Unity가 작성한 prefab YAML의 공백은 소스 포매팅 검사 대상에서 제외했다.

기존 실패:
1. `InspectorIsClickPinnedAndTimerUsesServerCorrection`: “Hover must not inspect” 기대와 기존 Hover 상세 표시 동작이 충돌한다. 수정 전부터 실패했다.
2. `OverlapUsesRotatedAndScaledCardAreaInEveryCanvasMode`: ScreenSpaceOverlay에서 0.9 초과 기대에 실제 약 0.59542. 수정 전과 동일하다.

캡처 / 증거: `Artifacts/BattleSelection/`
- `BasicHUD.png`, `Discard.png`, `Graveyard.png`, `FieldCost.png`: 현재 기존 선택 DTO를 사용한 표시 fixture. 실제 비용 종류가 DTO에 없는 점 때문에 라이브와 같은 일반 안내를 표시한다.
- `Response-InteractivePrototype.png`: 프리뷰 씬의 응전 표시·패스. 실제 응전 규칙 동작을 주장하지 않는다.
- `Response-Prototype.png`, `Response-korean/japanese/english.png`: 혼합 출처와 비활성 후보 fixture.
- `Defense.png`, `Defense-japanese/english.png`: 제공된 허용 쌍과 카운터로 표시한 방어 프리뷰.
- `Reference-vs-Unity-Defense.png`: 왼쪽 레퍼런스 / 오른쪽 Unity. 원본 레퍼런스만 1920×1080으로 정규화했다.
- `Before-vs-After-Choice.png`: 왼쪽 수정 전 1280×720 캡처를 정규화 / 오른쪽 수정 후 1920×1080.
- `Verification.xml`, `Verification.log`, `Baseline-BattleUx.xml`, `Captures.txt`: 실행 결과 및 좌표.

## 남은 차이와 다음 연결 작업

- 새 화면의 패널·리본·체크·선은 그래픽 틀이다. 레퍼런스의 정교한 매듭·금속·벚꽃 도안 전체를 재제작하지 않았다. 공격 카운터 태그에 모래시계 아이콘은 아직 없다.
- 기존 카드·HUD 위치와 실제 원본 아트를 유지했으므로 레퍼런스의 카드 간격, 빈 필드 슬롯 장식, 기력 패널까지 동일하지는 않다. 기력을 기존 요력으로 대체하거나 3/3을 하드코딩하지 않았다.
- 기존 카드 이름 번역과 상단 턴 HUD 등 새 선택 바 밖의 기존 다국어 레이아웃은 이번 신규 UI overflow 검사에 포함되지 않는다.
- 선택 상한 도달 시 추가 선택은 막지만 짧은 흔들림 연출은 넣지 않았다. 비활성 후보의 `DisabledReason` 표시 툴팁은 후속 연결 사항이다.
- 서버가 비용 종류·응전 우선권/허용 행동·상대 공개 효과·습격자/방어 후보·공격 카운터·기력·일반 소환 가능 여부·요청 식별자/제출 명령을 공개 DTO로 제공해야 한다. 서버 변경 범위로 별도 구현해야 한다.
- 그 뒤 UI adapter에서 `CardSelectionRequest`와 `DefenseAssignmentRequest`를 구성하고 확인/패스 콜백을 실제 명령에 연결한다. 카운트·선택 목록·배정은 초안이며 서버 수락 전에는 권위 상태나 자원 카운터를 갱신하지 않는다.
- 새 Yakou 응전·방어의 실전 경기, 전용 재접속/리플레이, 비공개 정보 필터링까지 완료됐다고 보고하지 않는다. 현재 공개 프로토콜의 기존 회귀 테스트와 신규 UI fixture 검증을 구분했다.
