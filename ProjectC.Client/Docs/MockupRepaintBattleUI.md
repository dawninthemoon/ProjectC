# v4 목업 리페인트 적용·실행 검수

2026-11-06. `ProjectC_UI_Repaint_v4_UnityPack/ProjectC_UI_Repaint_v4`의 **Unity용 PNG 21개**를 확인하고 실제 전투 UI에 적용했다. 패키지의 README와 `Docs/Unity_Setup.md`를 읽었으며, 이전 v3 결과를 기반으로 배치·이벤트·동적 UI 구조를 유지했다.

## 결과와 범위

- Unity **6000.0.81f1**, **1920×1080 Game View**에서 실제 `BattleScene` 실행·캡처·목업 비교를 수행했다.
- PNG는 재도색, 리사이즈, 색 보정하지 않았다. **21/21개 SHA-256 일치**: [원본 무결성 기록](../Artifacts/MockupRepaint/png-integrity.json).
- 최종 Play Mode 및 실제 WebSocket 서버 테스트 **2/2 통과**: [results-final.xml](../Artifacts/MockupRepaint/results-final.xml).
- `build/UnityMatchValidation` 복제본에서 검증한 프리팹을 원본 프로젝트에 반영했다. 원본 에디터를 강제 종료하지 않았다. 시작 시 존재하던 폰트 캐시 변경은 건드리지 않았고, 원본 에셋에 다른 편집이 생기지 않았는지 해시를 확인한 뒤 반영했다.
- 서버·규칙·프로토콜·카탈로그와 런타임 스크립트는 이번 v4 교체에서 수정하지 않았다. 기존 테스트 파일도 유지했다. 커밋·푸시·배포 빌드는 하지 않았다.

## 수정 파일

| 파일 | 변경 |
|---|---|
| `Assets/Prefabs/Battle/BattleScreen.prefab` | v4 전체 배경, 턴 배너/타이머, 영웅 링/HP, 카운터, 페이즈, 좌측 버튼, 가변 코스트 패널 |
| `Assets/Prefabs/Battle/BattleCard.prefab` | v4 프레임, 새 창에 맞춘 마스크 정렬, 중앙 카드명, 내장 배지 위 동적 숫자 |
| `Assets/Prefabs/Battle/BattleFieldCard.prefab` | v4 아치 프레임, 마스크 및 동적 숫자. 세워진 형태 유지 |
| `Assets/Prefabs/Battle/CardBack.prefab` | 새 벚꽃 뒷면 |
| `Assets/Prefabs/Battle/PlayerPanel.prefab` | v4 링·HP 및 초상화 마스크 |
| `Assets/Editor/ApplyMockupRepaintBattleUi.cs` | v4만 적용하는 명시적 에디터 도구 |
| `Assets/Tests/PlayMode/MockupRepaintBattleUiPlayModeTests.cs` | v4 상태·레이아웃·동작·필수 화면 캡처 검증 |
| `Assets/Tests/PlayMode/MockupRepaintOnlinePlayModeTests.cs` | 실제 서버 소환·공격·페이즈·중복 클릭 방어·재접속 검증 |

신규 리소스는 `Assets/Art/UI/Battle/MockupRepaint/`에 있다. 패키지 README/가이드와 Unity 원본 영역 Sprite 에셋 3개도 포함한다. `BattleScene.unity`, `BattleCostItem.prefab`, 기존 코스트 정의·시각 설정의 연결은 유지했다.

## 적용 리소스

| v4 PNG | 실제 연결 |
|---|---|
| `BattleBackground_1920x1080` | 전체 화면 배경. 검은 여백 없이 표시 |
| `TurnBanner` | 상단 왼쪽 턴 배너. 턴 번호/주체 TMP는 별도 |
| `TimerHousing` | 고정 원판 Simple Image |
| `TimerTicks_On` | Off/On 두 Image에 동일 Sprite. Off는 적갈색, On은 흰색 tint + Filled/Radial360 |
| `HandCardFrame`, `FieldMonsterFrame` | 실제 손패·필드 프레임 및 기존 선택/사용 가능 테두리 강조 |
| `CardBack` | 상대 비공개 손패, 양쪽 덱 앵커의 공통 뒷면 참조 |
| `HeroRing_Player`, `HeroRing_Opponent`, `HeroHealthBadge` | 별도 초상화 마스크 위 링, 별도 HP/TMP |
| `ZoneTag_Deck`, `ZoneTag_Grave`, `ZoneTag_Exile` | 양쪽 세 영역. PNG 내 아이콘을 사용하고 기존 별도 아이콘은 숨김 |
| `PhaseButton`, `PhaseArrow` | 기존 페이즈 버튼과 별도 화살표. 문구는 TMP |
| `SideButton_Log`, `SideButton_Menu` | 기존 Button graphic. 빈 투명 여백은 Unity 영역 Sprite로 제외해 형태를 유지 |
| `SpecialCostPanel_Expandable`, `CostChevron` | 가변 패널과 회전 화살표 |
| `CardAttackFlower`, `CardDefenseShield` | 정상 임포트·보존. v4 프레임 안에 이미 배지가 있어 추가로 겹치지 않음 |

모든 PNG는 Sprite / Single / Full Rect / Bilinear / Alpha Is Transparency / Mip Maps Off / 무압축이다. 고해상도 작업 소스나 Preview의 정적 코스트 그림은 런타임 스프라이트로 사용하지 않았다.

## 구조·정렬 및 기능 유지

**카드:** 프레임은 원본 비율로 기존 슬롯 안에 맞췄다. 일러스트는 기존 `AspectFillImage`와 stencil 마스크를 유지하면서 v4의 투명 창 좌표에 맞게 RectTransform을 조정했다. PNG 마스크를 다시 그리지는 않았다. 손패 이름은 중앙이며 마나 공간은 없다. 양쪽 카드 모두 기존 배지 배경 Image를 끄고 기존 공격력/체력 TMP만 내장 배지에 맞췄다. 공격은 왼쪽, 수비/체력은 오른쪽이다. 빈 슬롯 가이드는 없고 실제 카드 위젯이 없으면 프레임도 없다.

**타이머:** `TimerHousing` → `TimeTrack`(Off) → `TimeRing`(On) → `TurnTimer` 순서다. housing/숫자는 고정이고 On만 실제 남은 시간에 따라 fill이 감소한다. Off/On의 위치·크기는 동일하다. 이전 단계에서 수정한 흰색 On tint와 서버 시간 보정 로직을 재사용한다.

**영웅:** 기존 초상화 리소스 자체에 예전 링이 포함되어 있어 기존 UV crop을 조정해 새 링과의 중복을 줄였다. 원본 초상화 PNG도 수정하지 않았다. 초상화와 새 링/HP는 별도 요소이고 공격 대상 hit 영역은 유지한다.

**코스트:** 고정 아이콘이 없는 새 패널을 `CostBody`에 9-slice로 적용했다. Border는 좌/우 16, 위 165, 아래 80 원본 픽셀이다. `pixelsPerUnitMultiplier`를 표시 너비에 맞춰 상·하 장식이 행 수에 따라 늘어나지 않게 했다. 접힘 헤더는 같은 PNG의 원본 상단 영역을 참조하는 Sprite다. 별도 화살표와 `CostToggle`, `CostViewport`의 ScrollRect/RectMask2D, 동적 `BattleCostItem`을 유지했다. RowHeight 46 / 간격 6 / 최대 표시 4행을 재사용하며 4행 초과는 스크롤한다. 아이콘은 기존 `BattleCostDefinition.Icon`을 유지한다.

**로그·입력:** 기존 이벤트 기반 모달 로그, X/ESC 닫기, 배경 dim, 상세 카드, 상대 비공개 정보 차단, 드래그/공격/선택/오오라와 페이즈 대기 방어를 유지했다. 상단 이벤트 알림으로 HUD를 덮지 않는 이전 구성도 유지했다. 공개 영역 버튼과 카운터 TMP도 기존 바인딩이다.

## 실제 검증 결과

| 최종 실행 | 결과 |
|---|---|
| `MockupRepaintBattleUiPlayModeTests.V4RepaintLiveUiAndRequiredCaptures` | 통과. 내/상대 턴·중앙 턴 표기, 시간 감소/절반/0, 고정 원판과 동일 눈금 Rect, 손패 추가/제거·5장·오오라, 양쪽 필드 2+3/5+5, 공격/수비 및 영웅 HP 갱신, 빈 필드, 뒷면·비공개 정보, 양쪽 카운터, 네 페이즈 문구, 실제 raycast, 코스트 0/3/6종·값 갱신·접기·펼치기·휠 스크롤, 로그 X/ESC/dim/상세·스크롤·재대전 초기화 |
| `MockupRepaintOnlinePlayModeTests.V4UiPreservesServerCommandsAndReconnect` | 통과. 기존 서버/TestData로 실제 WebSocket 매치, 잘못된 드롭 복구, 드래그 소환, 상대 소환, 페이즈 버튼 연속 클릭이 한 번만 진행됨, 드래그 공격·체력 감소, 턴 종료, 재접속 후 필드 복원 |

최종 **2/2**, 약 16.6초. 예상하지 않은 Console Error/Exception은 없었다. 전체 기존 테스트 스위트를 실행했다고 주장하지 않는다. 추가 해상도 검사는 1280×720, 1920×1200, 2560×1080의 HUD/로그 화면 경계다. 기존 일본어 로그 일부 글리프 경고는 남는다.

## 실제 Game View 캡처

| 요구 상태 | 캡처 |
|---|---|
| A. 전투 시작 | [A-battle-start.png](../Artifacts/MockupRepaint/A-battle-start.png) — 실제 서버 |
| B. 손패 5장 | [B-hand-five.png](../Artifacts/MockupRepaint/B-hand-five.png) |
| C. 양쪽 필드 | [C-both-fields.png](../Artifacts/MockupRepaint/C-both-fields.png) |
| D. 코스트 펼침 | [D-cost-expanded.png](../Artifacts/MockupRepaint/D-cost-expanded.png) |
| E. 코스트 접힘 | [E-cost-collapsed.png](../Artifacts/MockupRepaint/E-cost-collapsed.png) |
| F. 타이머 진행 | [F-timer-half.png](../Artifacts/MockupRepaint/F-timer-half.png), [0초](../Artifacts/MockupRepaint/timer-zero.png) |
| G. 전투 기록 | [G-log-card-detail.png](../Artifacts/MockupRepaint/G-log-card-detail.png) |

- [목업 ↔ 최종 전체 화면 비교](../Artifacts/MockupRepaint/compare-C-both-fields.png)
- [코스트 펼침 비교](../Artifacts/MockupRepaint/compare-D-cost-expanded.png)
- [코스트 접힘 비교](../Artifacts/MockupRepaint/compare-E-cost-collapsed.png)
- [실제 서버 소환·공격](../Artifacts/MockupRepaint/online-summon-attack.png)

첫 실행 캡처를 검토한 뒤 타이머 크기와 초상화의 예전 링 crop을 조정하고 다시 실행·촬영했다. B~G는 합성 서버 메시지/DTO로 상태를 구성한 실제 Play Mode 캡처이며, 코스트 3종/6종은 UI 검증 데이터다. 실제 게임에 새 코스트 종류를 추가한 것은 아니다.

## 남은 차이·미검증 및 수동 설정

- v4 자체가 목업의 재해석 리페인트여서 프레임·매듭·꽃·아이콘·배경 세부 형태는 원본과 픽셀 단위로 같지 않다. PNG를 다시 그려 그 차이를 감추지 않았다.
- 카드·영웅 일러스트와 글꼴은 기존 프로젝트 데이터를 사용하므로 목업과 다르다. 중앙의 선/꽃 장식과 로그 모달/상세 패널의 별도 v4 자산은 제공되지 않아 기존 표기·디자인을 유지했다.
- 초상화 crop은 기존 그림 안의 옛 장식을 숨기는 표시 조정이므로 완전히 분리된 새 포트레이트 원본을 사용하는 것과는 차이가 있다.
- 사람이 두 독립 클라이언트를 OS 마우스로 끝까지 플레이한 검증은 하지 않았다.
- 프리팹·Sprite 메타·영역 Sprite 에셋을 모두 저장해 **추가 수동 Sprite 지정이나 배치는 필요 없다.** 일반 플레이에서 적용 도구를 실행할 필요도 없다. 명시적 재적용 메뉴는 **Project C → Apply v4 mockup repaint UI**다. 이전 v3/기타 `Apply*` 도구는 새 구성을 덮어쓸 수 있으므로 재실행하지 않는다.

검증 명령:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.0.81f1\Editor\Unity.exe' `
  -projectPath C:\Dev\ProjectC\build\UnityMatchValidation `
  -runTests -testPlatform PlayMode `
  -testFilter 'ProjectC.Tests.MockupRepaintBattleUiPlayModeTests;ProjectC.Tests.MockupRepaintOnlinePlayModeTests' `
  -projectcServerPath C:\Dev\ProjectC\build\Debug\ProjectCNetworkServer.exe `
  -testResults C:\Dev\ProjectC\ProjectC.Client\Artifacts\MockupRepaint\results-final.xml `
  -logFile C:\Dev\ProjectC\ProjectC.Client\Artifacts\MockupRepaint\playmode-final.log
```
