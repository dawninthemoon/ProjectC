# 턴 HUD / 배틀 로그 팝업

2026-11-05. 이번 변경은 새로 요청한 턴 HUD와 로그 팝업에 집중한다. 함께 첨부된 이전 종합 요구사항의 덱 API, 묘지/멀리건 재디자인, 카드 오라 변경은 이번 변경에 포함하지 않았다.

## 적용 및 원인

기존 턴 HUD는 독립된 단순 원형 프레임 두 개를 사용했고, 현재 행동 중인 플레이어를 표시하는 영역이 없었다. 제공된 3분할 프레임으로 교체하고, 각 원의 실제 내부와 오른쪽 장식판을 기준으로 TMP 영역을 배치했다.

제공된 HUD PNG에는 청록색 진행 호가 이미 그려져 있다. 원본 이미지는 수정하지 않고, 해당 호 위에 불투명한 별도 원형 트랙과 `Image.Type=Filled / Radial360` 게이지를 배치해 정적 호가 남은 시간을 잘못 나타내지 않게 했다. 모든 장식·게이지의 Raycast는 꺼져 있다.

기존 로그는 문자열 큐를 하나의 TMP에 합쳐 왼쪽에서 슬라이드하는 방식이라 턴 구분과 카드별 선택이 어려웠다. 동일한 BattleLogView와 직렬화 필드를 보존하면서, 서버 이벤트로부터 항목을 생성하는 중앙 팝업으로 바꿨다. 기존 RecentEvents 표시와 예전 Lines 텍스트는 숨겼으며, 디버그용 Add/Full 접근은 유지했다.

## 프리팹과 아트

- 수정: `Assets/Prefabs/Battle/BattleScreen.prefab`
- 추가: `Assets/Prefabs/Battle/BattleLogItem.prefab`
- BattleScene은 기존 BattleScreen 참조로 변경 사항을 사용한다. 씬·카메라·카드/영웅 프리팹은 수정하지 않았다.
- `Assets/Art/UI/BattleTurnLog/ProjectC_TurnHUD_Textless.png`: 제공된 `동양풍 금장 3분할 턴 HUD-2.png`를 원본 그대로 복사.
- `Assets/Art/UI/BattleTurnLog/ProjectC_BattleLog_PopupFrame.png`: 제공된 `청록빛 금장 전투 로그 프레임-1.png`를 원본 그대로 복사.
- `TimerAnnulus.svg/.png`: 정적 타이머 호를 덮는 트랙과 실제 진행 게이지가 공유하는 링.
- `TurnStatusWash.svg/.png`: 상태판 안쪽의 부드러운 색상 강조. 내 턴은 청록/금색, 상대 턴은 푸른색, 실제 효과 선택 중은 호박색.
- PNG는 투명 Sprite/Single/FullRect, mipmap 없음, Uncompressed, 최대4096으로 임포트했다. 비대칭 프레임은 9-slice로 늘리지 않는다. .meta/GUID를 보존했다.

1920×1080에서 HUD는 좌상단 (12,3),342×114로 원본2172×724와 같은3:1 비율이다. 별도 텍스트의 중심은 원본 이미지 좌표를 균일 배율로 변환했다. 로그 팝업은 화면 중앙640×800, 그림의 원본 비율은 preserveAspect로 유지한다. 스크롤 영역은 팝업 내부(44,146),552×598이며, 닫기 버튼은 프레임에 그려진 우상단 빈 버튼 안에 둔다. 기존 CanvasScaler를 유지하고 팝업은 중앙 앵커를 사용한다.

## 상태와 입력

- 턴 번호/턴 소유자는 실제 Client.State에서 가져온다. 상태판은 내 턴·상대 턴·대기·경기 종료·실제 선택 요구 상태를 구분한다.
- 남은 시간은 기존 서버 시간/TurnEndsAtUnixMs 연결을 유지한다. UI가 턴 종료나 제한시간을 결정하지 않는다.
- 프로토콜에 전체 제한시간이 없으므로 게이지 분모는 현재 턴/재접속 후 처음 관측한 남은 시간이다. 접속 전 경과분까지 원래 제한시간 대비 복원하지는 않는다. 시간 미제공 시 기존처럼 `--`를 표시한다.
- 내/상대 턴의 턴 종료 버튼 권한은 기존 GameplayInputCoordinator/CanAct 판단을 사용한다.
- 왼쪽 로그 버튼으로 열고, X 또는 ESC로 닫는다. 기존 ModalBlocker를 재사용해 배경을 어둡게 하고 조작을 차단한다.
- 팝업/카드 상세는 공통 ModalBlocker의 foreground Canvas로 올라간다. 기본 전투 UI보다100 높은 blocker, 그보다1 높은 팝업/상세 순서다. 별도의 전투 카메라는 추가하지 않았다.
- 팝업이 열리면 입력 scope는 카드 상세와 로그 닫기만 허용한다. ESC는 전투 선택 취소보다 먼저 처리한다. 필수 효과 선택이 도착하거나 경기 종료/뷰 무효화가 발생하면 팝업을 닫고 scope와 modal lease를 해제한다.

## 이벤트와 비공개 정보

`GameEvents`를 받고 뒤따르는 스냅샷과 묶어 카드 사용·소환·공격·피해·파괴·효과·주문·현현·턴 등의 항목을 만든다. 턴 헤더, 플레이어 구분, 스크롤바, 빈 상태, 미열람 수를 지원한다. 마지막200개 이벤트를 보관한다. 읽는 동안 새 항목이 와도 현재 스크롤 위치를 유지하고, 새로 열 때는 최신 항목을 보여준다.

카드 관련 행을 클릭하면 기존 카드 상세창에서 확인한다. 피해/파괴/이동 항목은 대상 카드, 공격/효과 항목은 발동/공격 카드가 기본 상세 대상이다. 상세는 당시 수신한 카드 DTO의 복사본이며 매치 상태를 수정하지 않는다.

카드 캐시는 자신의 손패와 공개 필드·묘지·제외 DTO로만 구성한다. 상대 손패나 덱 데이터는 읽지 않는다. 서버가 가린 카드 ID=0은 이전 캐시 또는 이벤트 DefinitionId를 이용해서도 역조회하지 않는다. 식별 가능한 DTO가 없는 항목은 비공개 카드로 표시하고 상세 버튼을 비활성화한다. 숫자 ID만으로 카탈로그의 카드를 추측하지 않는다.

같은 경기의 재접속/스냅샷 복원에서는 이 클라이언트가 받은 로그를 보존하고, 다른 경기/재경기는 초기화한다. 서버 과거 로그를 조회하는 프로토콜은 추가하지 않았다. 따라서 앱 재시작 또는 새 클라이언트 접속 전의 전체 경기 기록까지 복구하지는 않는다.

## 주요 코드

- `BattleTurnHudView.cs`: 게이지와 상태판.
- `BattleLogView.cs`: 이벤트 버퍼·턴 구분·팝업·스크롤·모달 수명.
- `BattleLogEntry.cs`: 수신 가능한 카드 정보의 복사본과 지역화된 이벤트 문구.
- `BattleLogItemView.cs`: 동적 행과 카드 상세 선택.
- `BattlePresenter.cs`: 기존 네트워크 이벤트/스냅샷 및 타이머 연결.
- `BattleInteractionController.cs`: 로그가 열린 동안 ESC/단축키 처리.
- `BattlePresentationState.cs`: 기존 프로토콜 이벤트 번호를 표현용 enum에 명명.
- `ApplyTurnLogArtwork.cs`: 명시적 프리팹 제작 도구. 자동 실행하지 않는다.
- 지역화 JSON: 한국어·일본어·영어 UI 문구. 기존 카드 번역 데이터는 변경하지 않았다.
- PlayMode 테스트 asmdef에 기존 설치된 Unity.InputSystem 참조만 추가했다. 새 패키지는 설치하지 않았다.

## 검증

원본 Unity 에디터가 열려 있어 종료하지 않았다. Assets/Packages/ProjectSettings/Library 복제본(`/tmp/projectc-turnlog-work/ProjectC.Client`)에서 동일 버전 Unity6000.0.81f1로 적용·컴파일·실행한 뒤, 검증된 프리팹/에셋/.meta를 원본에 반영했다. 원래 수정 중이던 폰트 에셋 두 개는 덮어쓰지 않았다.

| 실행 테스트 | 결과 |
| --- | --- |
| TurnLogPlayModeTests | 1/1 통과 |
| CompactBattleHudPlayModeTests | 1/1 통과 |
| GameplayInputPlayModeTests | 7/7 통과 |
| DrawPresentationPlayModeTests | 12/12 통과 |
| DrawPresentationPreservesServerDragAttackAndRematch | 1/1 통과 |
| CompactHudEffectsWaitForPresentationBeforeTargetingAndManifest | 1/1 통과 |
| SpellDragPrivateChoiceReconnectAndCommit | 1/1 통과 |

총24/24 통과. 새 로그 테스트는 타이머 감소/절반 진행률, 턴 상태 전환, 빈 상태, 이벤트·턴 헤더, 카드 상세, ESC/X, 배경 Raycast 차단, 입력 권한 복구, 스크롤 위치 유지, 동일 경기 기록 보존과 재경기 초기화를 확인했다. 가려진 ID에 DefinitionId를 넣거나 상대 손패 DTO가 실수로 존재하는 경우에도 정보를 표시하지 않는지 검사했다. 서버 시나리오3개는 로컬 실제 서버 프로세스를 사용했다.

캡처/결과: `Artifacts/TurnLogHud/` 및 하위 `Verification/`.

- `own-turn.png`, `opponent-turn.png`
- `log-empty.png`, `log-card-detail.png`, `log-scrolled.png`
- `locale-korean.png`, `locale-japanese.png`, `locale-english.png`
- `ratio-1920x1200.png`, `ratio-2560x1080.png`, `ratio-1280x720.png`

캡처는 실제 Game View에 검증용 이벤트/스냅샷을 공급한 화면이다. 운영 서버 경기 캡처는 아니다. 1920×1080,16:10,21:9,1280×720에서 프레임 경계와 상태 텍스트 넘침을 확인했다. 전체 테스트 스위트, 배포용 Player 빌드, 실제 원격 서비스 환경의 종합 테스트는 실행하지 않았다. 게임 규칙/서버/프로토콜은 수정하지 않았으며 커밋/푸시는 하지 않았다.
