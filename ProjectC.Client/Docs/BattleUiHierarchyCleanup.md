# Battle UI 계층 정리

2026-11-06. 현재 사용자가 배치한 UI를 기준으로 이름과 계층을 정리했다. PNG, 화면상 크기·위치, 폰트 설정 및 게임 규칙은 변경하지 않았다. 기존 `BattleScene.unity`의 상대 HP 위치 오버라이드와 작업 전부터 있던 폰트 변경도 보존했다.

## 변경 범위

- `Assets/Prefabs/Battle/`의 9개 프리팹: BattleScreen, BattleCard, BattleFieldCard, CardBack, PlayerPanel, BattleCostItem, BattleLogItem, CardAction, NumberPopup.
- GameObject 총 82개 제거. BattleScreen은 207 → 145개, 손패 카드는 18 → 13개, 필드 카드는 20 → 14개다.
- 이전 디자인의 꺼진 장식, 레거시 디버그 버튼, 숨겨진 중복 로그 텍스트, 사용하지 않는 턴 단위 텍스트·타이머 장식·리더 정보·자원 게이지를 제거했다.
- 카드의 꺼진 Accent Image 대신 동일한 상태 색상을 지역 변수로 계산한다. FrameGlow의 사용 가능·선택·대상 표시 로직은 유지했다.
- BattleHudView, BattleTurnHudView, PlayerPanelView, CardWidget, BattleLogView, BattlePresenter의 제거된 요소에 대한 Bind·갱신·이벤트 등록과 직렬화 필드를 함께 정리했다.
- 자원 획득 숫자 피드백은 숨겨진 옛 게이지 대신 실제 특수 코스트 행을 기준으로 표시한다.
- 이름에 의존하는 나머지 Battle 뷰와 기존 테스트의 조회를 함께 갱신했다. 과거 에디터 도구는 제거된 런타임 필드에 의존하지 않도록 컴파일 참조만 정리했으며 실행하지 않았다.

## 이름과 부모 관계

텍스트는 `Text`, 이미지는 `Image` 또는 기존 `Icon`, 버튼은 `Button`으로 끝나도록 정리했다. `RenewalZone0/1/2`는 소유자와 영역을 명시하는 이름으로 바꿨다. 전체 변경 기록과 이름 대응표는 [changes.txt](../Artifacts/UiHierarchy/changes.txt), [names.tsv](../Artifacts/UiHierarchy/names.tsv)에 있다.

```text
TurnHudImage
  TurnBannerImage
    TurnText
    TurnOwnerText
  TimerHousingImage
    TimerTicksOffImage
    TimerTicksOnImage
    TimerText

SpecialCosts
  CostBodyImage
    CostViewportImage
      CostContent
  CostToggleButton
    CostChevronImage
  CostTooltipImage

FrameImage
  FrameGlowImage
  ArtworkMask
    ArtworkImage
  CardFrameImage
    NameText
    AttackValueContainer / AttackBadgeText
    HealthValueContainer / HealthBadgeText
    CostBadgeImage / CostBadgeText
```

리더 HP 텍스트는 LifeBadgeImage 아래에 유지했다. 초상화 마스크와 HeroRingImage는 그림이 테두리를 덮지 않도록 같은 리더 아래의 형제로 유지했다. 제외 영역의 클릭 버튼은 실제 ExileTagImage 아래로 옮겼다. 카드 능력치 컨테이너의 중복 배경 Image만 제거하고 수치별 표시/숨김 컨테이너는 유지했다.

## 배치 보존 및 검증

복제 프로젝트 `build/UnityMatchValidation`에서 검증한 뒤 프리팹을 반영했다. 기존 에디터를 종료하거나 기존 Apply 도구를 재실행하지 않았다.

- 프리팹을 기준 크기로 평가한 뒤 생존한 모든 RectTransform의 네 모서리를 변경 전후 비교했다. 1920×1080, 1280×720, 2560×1080, 1920×1200 모두 0.05 UI 단위 이내로 일치했다. 직렬화된 Canvas의 0 배율·0 크기는 검사 동안만 정규화하고 저장 전 원래 값으로 복원했다.
- 변경 전후 1920×1080 Game View의 양측 필드 각 5장 화면을 비교했다. 총 2,073,600픽셀 중 값이 다른 픽셀은 8개이며, 이미지·텍스트의 배치 차이는 관찰되지 않았다. [나란히 비교 이미지](../Artifacts/UiHierarchy/compare-before-after.png).
- Unity Play Mode **2/2 통과**: `MockupRepaintBattleUiPlayModeTests.V4RepaintLiveUiAndRequiredCaptures`, `MockupRepaintOnlinePlayModeTests.V4UiPreservesServerCommandsAndReconnect`. [결과 XML](../Artifacts/UiHierarchy/results.xml).
- 타이머 감소, 내/상대 턴, 손패 추가·제거, 양측 필드, 능력치/HP/카운터, 오오라, 비공개 카드 차단, 코스트 접기·펼치기·6행 스크롤, 로그 팝업·카드 상세·ESC/X, 언어 변경 및 해상도별 HUD 경계를 검증했다.
- 실제 서버로 드래그 소환·공격·턴 진행·중복 페이즈 명령 방지·재접속 복원을 검증했다.
- 프리팹의 로컬 fileID 참조 누락이 없고 기존 GUID와 생존 오브젝트의 fileID는 유지했다. 전달한 프리팹은 YAML 블록 순서와 줄 끝 공백을 제외하고 검증본과 동일하다.

실행 캡처: [필드 전체](../Artifacts/UiHierarchy/both-fields-full.png), [코스트 펼침](../Artifacts/UiHierarchy/D-cost-expanded.png), [코스트 접힘](../Artifacts/UiHierarchy/E-cost-collapsed.png), [로그/상세](../Artifacts/UiHierarchy/G-log-card-detail.png), [선택 오오라](../Artifacts/UiHierarchy/hand-selected.png), [서버 소환/공격](../Artifacts/UiHierarchy/online-summon-attack.png).

전체 과거 테스트 모음과 두 운영체제 창에서의 수동 마우스 플레이는 실행하지 않았다. 기존 일본어 글리프 경고는 남아 있다. 새 Console 오류는 검증 테스트에서 발생하지 않았다. 추가 Unity 수동 설정, 커밋·푸시·배포 빌드는 수행하지 않았다.

이전 디자인의 Apply 도구는 수동 배치를 재작성하는 일회성 도구다. 정리된 UI는 저장된 프리팹을 그대로 사용하며, 해당 도구를 다시 실행할 필요가 없다. 이번 구조 마이그레이션의 검토용 소스는 [CleanupBattleUiHierarchy.cs](../Artifacts/UiHierarchy/CleanupBattleUiHierarchy.cs)에 보관했다.
