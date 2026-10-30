# 추가 요구사항 및 Astra 인계

2026-10-30. 기준 commit은 `016e9fa`이며, 이 작업은 사용자의 마지막 정정에 따라 **영상 분석과 요구사항 정리까지만** 수행한다. 화면 구성·게임 코드 구현은 Astra의 다음 작업이다. 구현 완료 보고 문서가 아니다.

## 우선순위와 출처

1. 사용자의 마지막 작업 범위 정정: 이 작업에서는 분석/정리만 한다.
2. 사용자의 최신 추가 지시: Bind 우선 조사, 필드 자동 배치, 최신 로그 아래, 클릭 효과 대상, 현현 버튼, 서버 권한 유지 등이 우선한다.
3. 이전 사용자 요구: 실제 프리팹/씬, 게임에 연결된 턴·드로우·상세·팝업, 기존 규칙과 비공개 정보 유지.
4. 첨부 PDF: 카드 드래그·공격 화살표·카드 인접 액션·숨김 UI 등의 추가 제안 자료. 사용자 메시지와 충돌하면 사용자 메시지를 따른다. PDF의 모델 지정, 긴 보고 형식, '시각적 완료 기준' 섹션은 별도 요구로 적용하지 않는다.
5. 영상/스크린샷: 직접 확인한 상호작용과 정보 구조의 근거. 관찰할 수 없는 내부 동작을 요구로 확정하지 않는다.

특히 PDF의 '로그 방향 미지정', '필드 위치 선택 미지정', '효과 클릭/드래그 미지정', '현현 명칭 미지정'은 최신 사용자 메시지로 해결되었다. 반면 현현 비용/발동 조건과 일부 특수 Target UI 규칙은 여전히 미결정이다.

## 1. 레퍼런스에서 확인한 UI/UX

구체적인 근거와 관찰 한계는 [ReferenceVideoAnalysis.md](ReferenceVideoAnalysis.md)에 있다.

- 중앙 TURN CHANGE / PHASE 배너가 잠시 표시된다.
- 상하 플레이어·필드, 하단 손패, 상황별 카드 설명 패널과 세로 기록 패널이 보인다.
- 카드 확대/발광, 소환 정착, 피해 수치가 관련 행동을 강조한다.
- V4에서는 별도 후보 카드 창에서 클릭 선택과 결정 후 닫힘을 확인했다.
- 실제 드로우의 전체 경로, hover 지연, 로그 자동 스크롤, 입력 차단, 서버 승인 시점은 영상만으로 확정하지 않는다.

## 2. 사용자가 명시적으로 확정한 요구사항

| ID | 확정 사항 | 구현 시 지켜야 할 조건 |
|---|---|---|
| U01 | 실제 Unity Scene/프리팹 | 에디터에서 편집 가능한 Canvas·패널·카드 에셋. 런타임 코드로 화면을 그리는 구조 금지 |
| U02 | Bind 조사와 우선 사용 | 실제 UnityBindingExtensions.Bind API와 사용 패턴 확인. 가능한 UI 자식 참조는 Bind로 연결 |
| U03 | 필드 자동 배치 | 서버 GameState의 Field 순서 유지. 슬롯 지정/필드 카드 재정렬 드래그 금지 |
| U04 | 최신 로그 아래 | ScrollRect. 최신 위치를 보고 있을 때만 새 로그를 따라가며, 과거 읽기 위치는 보존 |
| U05 | 로그 왼쪽 슬라이드 | 토글 및 닫기 버튼. 숨겨진 패널은 게임 입력을 가로채지 않아야 함 |
| U06 | 효과 대상 클릭 | 선택 가능/불가능 피드백, 취소, 서버의 최종 유효성 검사. 없는 규칙 생성 금지 |
| U07 | 현현 액션 | 자신의 필드 몬스터 선택 → 가능한 경우 '현현' 표시 → 네트워크 전송 → 서버 승인 후 카드 갱신. 불가능하면 기본 숨김 |
| U08 | 턴·드로우·Hover·소환/효과 정보 | 실제 서버 상태/이벤트에 연결. 상대 손패·덱 정보를 공개하지 않음 |
| U09 | 미확정 특수 Target UI | 기본 클릭부터 구현하고, 새 선택 규칙/게임 규칙을 임의 확정하지 않음 |
| U10 | 기존 환경 유지 | Unity 버전/프로젝트 설정 보존. 기존 프리팹·네트워크 구조 재사용. commit/push 없음 |
| U11 | 정직한 보고 | 관찰/추정, 구현/미구현, 실제 검증/수동 필요를 구분. 별도 '시각적 완료 기준' 섹션 작성 안 함 |

## PDF에서 제안한 추가 UX

다음 항목은 영상에서 확인한 사실과 별개인 **PDF의 구현 사양**이다. 최신 사용자 확정 사항과 구분해서 Astra가 참고한다.

- 손패 카드 드래그 → 내 필드 전체 Drop Zone → PlayCard. 드롭 x좌표는 카드 위치/순서에 영향 없음.
- 공격자 카드는 고정하고, 카드에서 상대 카드/리더로 화살표 드래그 → Attack/DirectAttack.
- 전역 SUMMON/ATTACK/DIRECT ATTACK 기본 버튼을 카드 중심 조작으로 대체.
- 카드 옆 Context Action, 지연 Hover 상세, 빈 곳 클릭/Esc 취소.
- 손패 fan/overlap, 실제 카드 수에 따른 필드 중앙 정렬. 큰 빈 슬롯 상시 표시 제거.
- 로그·묘지·Connection/Debug는 기본 화면에서 숨기고 필요한 때 열기.
- 승인 전에는 드래그 시각 요소만 임시 표시. 거절 시 복귀 및 이유, 요청 중 중복 입력 방지.
- 표현 큐와 권위 상태를 분리. 제안된 시간 수치는 게임 규칙이 아니며 조정 가능.

## 3. 저장소를 확인해야 결정할 구현 사항 — 조사 결과

### 현재 에셋/버전

- Unity는 실제 `6000.0.81f1`이다. 6.3으로 바꾸지 않는다.
- `Assets/Scenes/BattleScene.unity`와 `Assets/Prefabs/Battle/{BattleScreen,BattleCard,CardBack,PlayerPanel}.prefab`가 존재한다.
- `Assets/Editor/AuthorBattleAssets.cs`는 기존 에셋 생성 도구이며, 실행하면 씬까지 다시 작성한다. 사용자가 현재 Scene의 Main Camera에 추가한 URP 컴포넌트 변경을 보존해야 하므로 도구 전체 재실행을 피하거나 기존 씬 보존 업그레이드 방식이 필요하다.
- 사용자 기존 변경인 `ProjectSettings/SceneTemplateSettings.json`과 `ProjectC.Client/Scripts/Utils`를 삭제/덮어쓰지 않는다.

### Bind의 실제 API

원본은 `ProjectC.Client/Scripts/Utils/UnityBindingExtensions.cs`, namespace는 `Game.Utils`다. 실제 제공하는 오버로드는 다음과 같다.

```csharp
bool Bind(this GameObject gameObject, string objectName, out GameObject target)
bool Bind(this Transform transform, string objectName, out GameObject target)
bool Bind(this MonoBehaviour behaviour, string objectName, out GameObject target)
bool Bind<T>(this GameObject gameObject, string objectName, out T component) where T : Component
bool Bind<T>(this Transform transform, string objectName, out T component) where T : Component
bool Bind<T>(this MonoBehaviour behaviour, string objectName, out T component) where T : Component
bool BindButton(this MonoBehaviour behaviour, string objectName, out Button button, UnityAction callback)
```

`BindNoRecursive<T>`도 있으며 직계 자식만 검사한다. 일반 Bind는 **현재 root 자신부터 이름으로 재귀 검색하여 최초 일치**를 반환하고 그 오브젝트에서 GetComponent한다. 경로 검색·캐시·누락 시 예외를 자동 제공하는 API가 아니다. BindButton은 기존 리스너를 모두 제거한다. `Component_Base`는 Bind 래퍼를 사용하지만, 현재 Assets 내 배틀 코드에서 이 유틸을 쓰는 패턴은 확인되지 않았다.

현재 원본이 **Assets 밖**이라 Unity에서 컴파일되지 않는다. 또한 TMPro 및 현재 저장소에 없는 `Game.StaticData.LocalizeDataManager`를 참조한다. 따라서 파일을 그대로 전부 Assets로 옮기면 컴파일 문제가 생길 수 있다. 공통 Bind 부분과 지역화 의존 부분을 분리하거나 기존 의존성을 적절하게 연결해야 한다. 이름이 반복되는 `Name`, `Stats` 등은 카드/패널 Transform 범위에서 Bind해야 한다. 외부 프리팹 자산 참조는 계층 검색으로 찾을 수 없으므로 직렬화 참조를 유지할 타당한 사례다.

### 이미 구현된 기능

| 영역 | 기존 파일/기능 | 현재 한계 |
|---|---|---|
| 네트워크 | WebSocketTransport, MultiplayerDebugScreen | 송수신 큐, 순번/epoch, 매치·재접속은 이미 존재. 화면 입력과 역할 분리는 필요 |
| 스냅샷 표시 | BattlePresenter | 손패·필드·Life·묘지·턴 표시, 카드 클릭 선택은 있으나 전역 버튼 중심 |
| 카드 | CardWidget, CardZoneView | hover 확대/선택, 상태 문구 존재. 드래그/화살표/FSM 없음 |
| 공개 정보 | CardView/PlayerView | 자신의 Hand만 상세 수신, 상대 Hand는 빈 배열·장수만. RulesText/ManifestationText/SummoningSick 있음 |
| 연출 | BattleEffectsView | 턴 배너, 뒷면 드로우 이동, 순차 효과/소환 팝업 존재. 앞면 드로우·정착/롤백 보강 필요 |
| 상세 | CardDetailView | 상시 고정 Inspector. 지연 hover·팝업 위치/긴 설명 처리 부족 |
| 로그 | BattleLogView | 최근 5개·전체 최근 40개. 최신 아래 텍스트지만 조건부 자동 스크롤/왼쪽 drawer 없음 |
| 테스트 | PlayMode 4개와 CTest | 이전 구현 검증 이력은 있음. 다음 변경의 통과를 뜻하지 않음 |

### UI 연결이 부족한 기능

- 공개 이벤트에 Source/Target 카드·플레이어, FromZone/ToZone, 효과/전투 종류가 있어 연출 연결에 사용할 수 있다.
- `CanSummon`, `CanAttack`, `SummoningSick`, `HasAttacked`는 표시 힌트로 쓸 수 있다. 최종 행동 허용은 서버가 판단한다.
- EndTurn 네트워크 경로는 서버에서 페이즈 진행·턴 종료·상대 StartTurn을 처리한다. 별도 원격 StartTurn 버튼은 필요 없다.
- GameCore에는 ActivateEffectCommand, ValidateTargets, TransformCard/TransformCommand와 Replay가 존재하지만, 이것이 그대로 원격 호출 권한을 뜻하지 않는다.

### 새로 구현할 UI 기능

- 입력 FSM: Idle / hand drag / attack drag / action selection / effect target selection / awaiting server, 취소와 reset.
- 프리팹 기반 drag visual·placeholder·rollback, 화살표, 리더 hit area, 대상 강조.
- 서버 순서를 유지하는 필드 정렬과 손패 레이아웃.
- 카드 인접 액션과 상세 팝업, 숨김 drawer, 로그 조건부 스크롤.
- 서버 승인/거절·재동기화·재접속·새 게임 시 선택/드래그/표현 큐 정리.

### 서버/프로토콜 변경이 필요한 기능

기준 서버 Submit 허용 명령은 `PlayCard`, `AdvancePhase`, `EndTurn`, `Attack`, `DirectAttack`뿐이다. **현재 DTO에 CanManifest/현현 액션 목록/효과 대상 후보가 없고, 원격 ActivateEffect도 허용하지 않는다.** 따라서 현현 버튼이나 효과 대상 UI만 추가해 곧바로 동작한다고 보고할 수 없다.

기존 GameCore의 `ActivateEffectCommand`는 Player·SourceCard·EffectId·액션별 Targets를 받는다. active player / Main phase / turn started / 소유자 / 효과 타입 / TargetRule을 검사한다. 대상별 카테고리·Zone·개수·중복·Trait과 상태 변화 후 재검증도 존재한다. 이 API를 활용한 제한된 네트워크 경로와 서버 발급 후보 DTO가 검토할 수 있는 방향이다. 클라이언트가 EffectOperation·Amount·목적 정의 ID를 임의 전송하게 해서는 안 된다.

`TransformCommand`는 신뢰된 서버 입력이다. `AllowedManifestations`는 허용 형태 목록이며 **자유 현현 권한/코스트/발동 조건을 뜻하지 않는다**. 기본 `Test Striker`에는 허용 형태 12가 있지만 Activated Transform 효과가 정의되어 있지 않다. 이 카드에 현현 버튼을 무조건 붙이려면 규칙을 새로 정하는 셈이다.

안전한 구현 후보는 '실제로 정의된 Activated Transform 효과를 현현 액션으로 표시하고 ActivateEffect로 처리'하는 방식이다. 이는 **조사 기반 제안**이지 이번 작업에서 확정/구현한 사양이 아니다. 기본 카드의 자유 현현 조건은 별도 결정이 필요하다.

원격 대상 DTO를 설계할 때는 상대 Hand·Deck과 자신의 Deck 순서/구성 ID가 새 후보 목록으로 누출되지 않게 해야 한다. 공개 Field/Graveyard, 자신의 Hand, 공개 Player 대상과 숨긴 Deck 검색을 같은 일반 목록으로 처리하지 않는다. 모든 요청은 세션에 바인딩한 PlayerId와 MatchId/epoch/sequence로 재검증해야 한다.

## 아직 결정되지 않은 사항

| 사항 | 결정 없이 진행할 수 있는 범위 |
|---|---|
| 기본 카드 현현의 비용·횟수·발동 시점 | UI/후보 DTO 구조까지 준비 가능. 기존 AllowedManifestations만으로 승인 기능을 만들지 않음 |
| 숨긴 덱 검색·선택, 효과별 특수 창 | 공개 대상 클릭부터 구현. 덱 ID 공개나 새 검색 규칙 생성 금지 |
| 다중 대상/액션별 선택 확인 방식 | 기존 MinCount/MaxCount는 서버 기준. 자동 확정/확인 버튼은 UX 선택으로 문서화 |
| 턴 안내 중 입력 차단 | 영상으로 확인 불가. 상태·입력 정책을 명시적으로 택해야 함 |
| 드로우 앞면 공개 시점·중첩·대량 이벤트 처리 | 공개 스냅샷에 있는 자신의 카드만 사용. 시간은 표현 설정으로 분리 |
| 한국어 '현현' 폰트 | 현재 기본 폰트의 한글 렌더링 확인 필요. 적법한 폰트/라이선스 선택 필요 |

## Astra의 권장 작업 순서

1. 이 문서와 영상 분석을 읽고 실제 프리팹·사용자 Scene 변경을 다시 확인한다.
2. **게임 화면 Scene/프리팹을 먼저 구성**한다. 기본 화면은 카드·전장·Life·턴 진행 중심으로 두고 상세·로그·설정은 숨김 패널로 둔다.
3. 실제 Bind API의 컴파일 경로/의존성을 해결하고 패널별 바인딩을 연결한다.
4. 기본 PlayCard/Attack/DirectAttack을 기존 네트워크에 연결해 드래그·화살표·취소·거절·재접속을 먼저 검증한다.
5. 효과/현현 권한·후보 DTO를 필요한 범위로 추가하고 GameCore 검증과 Replay를 보존한다. 미결정 규칙은 사용자 판단 항목으로 남긴다.
6. 로그 정책·연출·클릭 대상 선택을 검증하고 기존 테스트/Windows 빌드를 실행한다. 실제 Windows 창 수동 플레이는 별도 결과로 구분한다.

## 이번 작업의 정리 상태

- 이 작업에서 잠시 시작했던 미완성 코드 수정은 `build/InteractionDraft`로 보관하고 작업 소스에서 원상복구했다. **완성품이나 권장 구현이 아니므로 그대로 적용하지 않는다.**
- 작업 전부터 있던 BattleScene의 URP Camera 변경과 외부 유틸 파일은 보존했다.
- 중간에 서버 빌드가 한 번 실행되었으므로, 다음 실행 전에는 복구된 소스로 다시 빌드해야 한다. 이번 분석 작업의 테스트 통과 결과로 인용하지 않는다.
- Unity/Windows/새 UI 테스트는 이번 범위에서 완료했다고 보고하지 않는다. commit/push하지 않았다.
