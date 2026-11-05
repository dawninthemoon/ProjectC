# 전투 UI: Compact Costs 목업 적용

기준: `ProjectC_Compact_Costs_Tooltip_v4.png`, 1920×1080. 배경, 영웅, 필드 카드, 카드 일러스트와 기존 전투 입력 경로를 유지했다. 목업 전체 이미지를 UI로 사용하지 않았다.

## 실제 적용 파일

- `Assets/Prefabs/Battle/BattleScreen.prefab`: 턴/시간 HUD, 좌측 아이콘 메뉴, 특수 코스트 목록, 우측 카운터, 턴 버튼 내부 배치.
- `Assets/Prefabs/Battle/BattleCard.prefab`: 손패 이름판과 이름 텍스트의 부모·영역·그리기 순서.
- `Assets/Prefabs/Battle/BattleCostItem.prefab`: 동적으로 생성하는 코스트 행.
- `BattleScene.unity`는 기존 BattleScreen 프리팹 참조를 통해 변경 사항을 사용한다. 씬 파일 자체와 카메라는 변경하지 않았다.
- `Assets/Scripts/Battle/BattleTurnHudView.cs`, `BattleSpecialCostsView.cs`, `BattleCostItemView.cs`, `BattleCostDefinition.cs`: 표현 컴포넌트.
- `BattleHudView.cs`, `BattlePresenter.cs`: 기존 스냅샷·타이머와 새 표현 컴포넌트 연결.
- `Assets/Editor/ApplyCompactBattleHud.cs`: 프리팹 적용 도구. 자동 실행하지 않는다. 기존 프리팹을 다시 변경하는 명시적 제작 도구이므로 이후 수동 편집을 보존해야 할 때 무심코 재실행하지 않는다.
- 지역화 JSON: 초 단위와 요력 설명의 한국어·일본어·영어 추가.

## 레이아웃 및 데이터

좌표는 1920×1080 기준 좌상단 좌표다. 기존 CanvasScaler와 앵커를 사용한다.

| 영역 | 적용 내용 |
| --- | --- |
| 턴/시간 | (20, 8), 320×118. 왼쪽 턴 번호, 오른쪽 남은 초와 Radial360 Filled Image |
| 전투 기록 / 메뉴 | (44,127) / (44,219), 각각 78×78. 두루마리·햄버거 아이콘, 기존 Button 이벤트 유지 |
| 특수 코스트 | (25,313), 폭126. 토글32, 행46, 간격6. 4행을 넘으면 ScrollRect |
| 코스트 툴팁 | 목록 오른쪽, 폭300, 내용에 따라 높이150~280. 화면 경계에서 안쪽으로 보정 |
| 고정 카드 상세 | (174,260), 300×480. 새 목록 옆에 놓고 기존 상세 기능 유지 |
| 턴 버튼 | (1620,335), 260×400. 원본 종횡비 유지, 실제 그림 내부 기준으로 페이즈·본문·화살표 분리 |
| 손패 이름판 | 원본 카드 프리팹 좌표 (36,8), 148×30. 텍스트 (46,10),128×26 + 내부 여백 |

타이머는 기존 서버 `TurnEndsAtUnixMs`와 동기화된 남은 시간을 사용한다. UI가 게임 진행이나 턴 종료를 제어하지 않는다. 정보가 없으면 `--`, 게이지0으로 표시한다. 프로토콜에 전체 제한시간이 없으므로 게이지 분모는 턴 변경/재접속 뒤 처음 관측한 남은 시간이다. 서버 시간 보정으로 시간이 증가하면 분모도 보정한다. 재접속 시 원래 제한시간 대비 진행률과는 다를 수 있다.

코스트 행은 `BattleCostDefinition`(이름/설명 지역화 키·아이콘)과 `BattleCostDisplay`(현재 값·선택적 최대값)를 받는다. 실제 게임 어댑터는 서버에 존재하는 요력만 전달한다. 최대 요력0이면 목록을 숨긴다. RP의 규칙이나 수치를 생산 코드에 추가하지 않았다. 새 코스트의 서버 데이터가 생기면 어댑터가 항목을 추가하면 되고, 목록 자체는 종류를 가정하지 않는다.

자신의 기존 YokaiPanel은 비활성 상태로 보존했다. 상대 요력 표시는 목업처럼 상단에 유지한다. 0개→여러 개 전환 시 비활성 오브젝트의 Awake 순서에 의존하지 않도록 초기화한다. 접기/펼치기는 표시만 변경한다.

우측 덱·묘지·제외 수치는 기존 데이터와 공개/비공개 열람 제한을 그대로 사용한다. 보라색 보석 대신 카드가 소용돌이로 들어가는 제외존 아이콘을 사용한다. 카운터 프레임에는 숫자를 가로지르는 내부 선이 없다.

손패 이름은 프레임·광택 위에 그리는 불투명 이름판의 중앙에 놓는다. 장식과 분리하고 Auto Size(6~18, 프리팹 좌표 기준)를 적용했다. 매우 긴 제목은 작아지며 전체 정보는 기존 Hover 상세에서 읽을 수 있다. 기존 TMP 역할별 한/일 폰트와 Cinzel 숫자 폰트를 재사용한다.

## 리소스

`Assets/Art/UI/BattleCompact/`에 편집 가능한 SVG와 표시 크기의 2배 PNG를 저장했다.

- TurnPanel, CircleFrame, TimerRing
- MenuFrame, MenuIcon, LogIcon
- CostRow, CostToggle, Chevron, YokaiIcon
- CounterFrame, ExileIcon
- HandNamePlate, TooltipFrame
- 실제 요력의 표현 정의 `YokaiCost.asset`

PNG는 투명 Sprite로 임포트했다. TooltipFrame은 Border64/source PPU200의 9-slice를 사용한다. 고정 실루엣 리소스는 억지로 늘리지 않는다. 기존 턴 버튼 고해상도 배경·광택 리소스와 기능은 재사용한다.

## 캡처

`Artifacts/CompactBattleHud/`에 실제 Unity Game View 캡처를 저장했다.

- `battle-1920.png`: 실제 카드 정의와 스냅샷 형태의 검증 상태. 운영 서버 경기 캡처는 아니다.
- `reference-vs-unity.png`: 목업과 Unity 캡처를 각각1920×1080으로 맞춘 좌우 비교.
- `costs-1.png`, `costs-2.png`, `costs-6.png`, `costs-collapsed.png`, `costs-scrolled.png`: 동적 목록·접기·스크롤.
- `two-types-preview-only.png`: 요력 + RP0의 **테스트 전용** 두 종류 표시. RP는 런타임 테스트 픽스처에만 존재하며 실제 게임에 노출하지 않는다.
- `language-korean.png`, `language-japanese.png`, `language-english.png`: 긴 제목·턴 문구·툴팁.
- `hand10-hover.png`: 10장 손패와 Hover 상세.
- `ratio-1920x1200.png`, `ratio-2560x1080.png`, `ratio-2560x1440.png`: 화면 비율 검증.

## 검증 범위와 차이

새 PlayMode 검증은 타이머 감소/새 턴 초기화/시간 미제공 상태, 코스트0·1·2·6개, 최대값 없는 별도 종류, 툴팁·스크롤·접기, 비활성 풀 초기화, 다국어 긴 이름·턴 문구의 넘침, 이름판 내 영역, 메뉴 Raycast·기록 열기/닫기, 10장 Hover, 네 해상도에서 HUD 경계를 확인한다.

2026-11-05 Unity 6000.0.81f1에서 실행한 결과다. 서버 시나리오는 로컬 `ProjectCNetworkServer` 프로세스를 실제 실행했다. XML은 `Artifacts/CompactBattleHud/Verification/`에 보관했다.

| 실행 항목 | 결과 |
| --- | --- |
| CompactBattleHudPlayModeTests | 1/1 통과. 실제 Game View 캡처 포함 |
| GameplayInputPlayModeTests | 7/7 통과 |
| DrawPresentationPlayModeTests | 12/12 통과 |
| DrawPresentationPreservesServerDragAttackAndRematch | 1/1 통과. 서버 드래그 소환·공격·재경기 |
| SpellDragPrivateChoiceReconnectAndCommit | 1/1 통과. 비공개 선택·재접속·확정 |
| ServerDefinedEffectsUseClickTargetsAndManifestContext (기존 테스트 그대로) | 0/1 실패. 547행에서 활성 ContextActionView가 null |
| CompactHudEffectsWaitForPresentationBeforeTargetingAndManifest (새 검증) | 1/1 통과. 연출 큐와 입력 권한을 기다린 뒤 효과 대상 선택·서버 체력 갱신·현현 확인 |

합계23개 통과,1개 실패. 실패한 기존 테스트는 서버 시퀀스 수신 뒤 한 프레임만 기다려 클릭한다. 같은 시나리오에서 `PresentationBusy == false`와 실제 입력 권한을 기다리는 별도 검증은 통과했다. 실패를 숨기거나 기존 테스트의 기대값을 바꾸지 않았다. 기존 테스트 전체가 통과했다고 보고하지 않는다.

남는 시각적 차이: 새 소형 벡터 프레임/아이콘은 목업의 수작업 금속 곡면·세밀한 장식과 동일하지 않다. 목업의 영웅·필드 크기를 이번 변경에서 다시 조정하지 않았다. 모든 카드의 번역 데이터를 새로 작성하지 않았으며, 언어별 UI와 긴 제목은 검증 픽스처로 확인했다. 매우 긴 코스트 설명은 최대 높이280에서 말줄임 처리한다. 실제 제공된 요력 설명은 세 언어 모두 온전히 표시된다.

데스크톱 Unity Editor에서 검증했으며 기기별 빌드와 전체 카드 카탈로그의 모든 번역 조합은 검증하지 않았다. GameCore, 서버, 통신 프로토콜, 카드 규칙과 원본 일러스트는 변경하지 않았다. 커밋/푸시는 하지 않았다.
