# 원본 목업 기준 BattleScene UI 적용·검수 보고

2026-11-06. Unity **6000.0.81f1**, Game View **1920×1080**. 원본 에디터를 종료하지 않고 `build/UnityMatchValidation` 복제본에서 적용·Play Mode 검증·촬영한 뒤, 검증한 에셋을 원본 프로젝트에 반영했다.

## 사용한 자료와 적용 범위

- 지정한 `ProjectC_UI_ResourcePack_v3_OriginalPixel/ProjectC_UI_ResourcePack_v3_exact`의 `README_KO.md`, PNG 25개, 원본 `reference/source_mock.png`를 확인했다.
- 추가로 전달된 `Downloads/Unity_Setup.md`의 v4 가이드를 읽었다. **v4 ZIP과 그 PNG는 확인되지 않아 v4 리페인트를 적용했다고 주장하지 않는다.** 실제 스프라이트는 v3 원본 추출본이다.
- 다운로드 폴더의 UI 없는 `벚꽃 노을 아래의 일본식 결투장.png`를 별도 배경으로 사용했다. 원본은 **1672×941**이며 PNG를 리사이즈하거나 보정하지 않고 `BattleBackground_Original.png`라는 파일명으로 복사해 전체 화면에 표시했다. 목업의 배경과 완전히 같은 그림은 아니다.
- `source_mock.png`는 비교 아티팩트에만 있으며 런타임 배경으로 사용하지 않는다.
- 작업 시작 시 로컬 HEAD는 `4de65f3`, GitHub HEAD는 `4d76e8e`였다. 로컬의 이후 UI 조정 내용을 기준으로 작업했고 GitHub의 요청된 프리팹 6개와 `BattleSpecialCostsView.cs`도 조회했다. 로컬 `BattleScene`의 프리팹 참조, RectTransform 및 기존 에디터 도구도 확인했다.
- **PNG 26개 모두 원본과 SHA-256 일치**: [png-integrity.json](../Artifacts/MockupExact/png-integrity.json). PNG 재도색, 색 보정, 그림자·광택 추가는 하지 않았다. 기존 `Apply*` 도구도 재실행하지 않았다.

## 변경 파일

| 파일 | 변경 내용 |
|---|---|
| `Assets/Prefabs/Battle/BattleScreen.prefab` | 원본 목업 좌표 배치, 배경, 턴·타이머, 영웅 링/HP, 카운터, 페이즈, 코스트 패널, 좌측 버튼, 알림 정리 |
| `Assets/Prefabs/Battle/BattleCard.prefab` | v3 손패 프레임·마스크, 중앙 제목, 왼쪽 공격/오른쪽 수비 숫자, v3 배지 부품, 기존 오오라 |
| `Assets/Prefabs/Battle/BattleFieldCard.prefab` | 아치 프레임·마스크와 숫자. 프레임에 포함된 배지 위에 기존 배경 Image가 중복되지 않도록 비활성화 |
| `Assets/Prefabs/Battle/CardBack.prefab` | 상대 패의 원본 벚꽃 뒷면 |
| `Assets/Prefabs/Battle/PlayerPanel.prefab` | 기존 템플릿의 분리된 초상화 마스크·링·HP 구조 |
| `Assets/Prefabs/Battle/BattleCostItem.prefab` | 동적 아이콘/숫자 행, 고정 배경 중복 제거, 현재 값 표시 |
| `Assets/Prefabs/Battle/BattleVisualSettings.asset` | 목업의 손패 카드 크기·간격·약한 회전/곡선 |
| `Assets/Art/UI/BattleCompact/YokaiCost.asset` | 실제 요력 코스트의 불 아이콘을 원본 영역 Sprite로 연결 |
| `Assets/Scripts/Battle/BattlePresenter.cs` | 새 배치에서 프리팹 손패 좌표를 유지하는 옵션. 기존 경계 보정·선택·드래그 유지 |
| `Assets/Scripts/Battle/CardTableLayout.cs` | 양쪽 필드의 간격을 프리팹에서 지정하는 옵션. 기본값은 기존 값 유지 |
| `Assets/Scripts/Battle/BattleTurnHudView.cs` | 붉은 눈금에 흰색 tint 유지, 중앙의 실제 턴 주체·턴 번호 갱신 |
| `Assets/Scripts/Battle/BattleSpecialCostsView.cs` | 접을 때 본문 숨김, 헤더/하단 여백 및 화살표 방향 설정. 동적 생성·스크롤 유지 |
| `Assets/Scripts/Battle/BattleCostItemView.cs` | 배경 tint 및 최대치 표시에 대한 프레젠테이션 옵션. tooltip 데이터는 유지 |
| `Assets/Editor/ApplyMockupExactBattleUi.cs` | 이번 배치를 저장하는 명시적 적용 도구. PNG 생성/수정 기능 없음 |
| `Assets/Tests/PlayMode/MockupExactBattleUiPlayModeTests.cs` | UI 상태·프라이버시·스크롤·필수 캡처 검증 |
| `Assets/Tests/PlayMode/MockupExactOnlinePlayModeTests.cs` | 실제 서버 소환·공격·페이즈 연속 클릭 방어·재접속 검증 |

서버·게임 규칙·프로토콜·카탈로그의 코스트 종류는 변경하지 않았다. `BattleScene.unity`, 기존 테스트 파일과 기존 에디터 적용 스크립트도 수정하지 않았다. 생성된 폰트 캐시 변경은 되돌렸다. 커밋·푸시·배포 빌드는 하지 않았다.

## 리소스 매핑

신규 위치는 `Assets/Art/UI/Battle/MockupExact/`이다. 원본 README와 전달된 가이드도 함께 보존했다. PNG는 Sprite / Single / Full Rect / Bilinear / Alpha Is Transparency / Mip Maps Off / 무압축으로 임포트했다.

| 원본 PNG | 적용 |
|---|---|
| `HandCardFrame_Exact`, `Mask_HandArtwork` | 손패의 별도 일러스트·프레임·마스크 |
| `FieldMonsterFrame_Exact`, `Mask_FieldArtwork` | 세워진 필드 카드. 빈 슬롯 없음 |
| `CardAttackFlower_Exact`, `CardDefenseShield_Exact` | v3 손패의 독립 배지 부품. 공격 왼쪽/수비 오른쪽. 숫자는 기존 TMP |
| `CardBack_Exact` | 상대 패. 덱 앵커에도 동일 Sprite 참조를 유지하고 카운터 뒤 중복 그림은 숨김 |
| `HeroRing_Player_Exact`, `HeroRing_Opponent_Exact` | 초상화 마스크 밖의 별도 링/꽃 장식 |
| `Mask_PlayerPortrait`, `Mask_OpponentPortrait` | 기존 영웅 이미지의 stencil 마스크 |
| `HeroHealthBadge_Exact` | 양쪽 영웅 HP, 별도 동적 TMP |
| `TurnHUD_Exact` | 좌측 상단 턴 배너와 고정 타이머 housing의 원본 영역 |
| `TimerTicks_Off_Exact`, `TimerTicks_On_Exact` | 별도 Off/On 눈금. On만 Filled / Radial360 |
| `SpecialCost_Expanded_Exact` | 상단/본문/하단/아이콘의 원본 영역 Sprite |
| `SpecialCost_Collapsed_Exact` | 접기 핸들 및 회전하는 화살표의 원본 영역 |
| `SideButton_Log_Exact`, `SideButton_Menu_Exact` | 기존 좌측 Button의 graphic |
| `PhaseButton_Exact`, `PhaseArrow_Exact` | 우측 페이즈 버튼과 별도 화살표 |
| `ZoneTag_Deck_Exact`, `ZoneTag_Grave_Exact`, `ZoneTag_Exile_Exact` | 양쪽 카운터. 기존 별도 아이콘 숨김, TMP 숫자 유지 |
| 별도 배경 `BattleBackground_Original` | 전체 화면 배경, 원본 픽셀 유지 |

`CardBottomJewel_Exact`도 원본과 함께 보존했지만 프레임에 이미 있는 장식 위에 다시 겹치지 않았다. v3 손패는 독립 배지 부품을 요구하는 추출본이므로 v4의 '완성 프레임에 배지를 겹치지 말 것'과 동일한 자산 구조가 아니다. v3 README의 매핑을 따르며 필드의 기존 배지 배경은 숨겼다.

## 구조상 수정한 부분

**손패:** 기존 런타임은 프리팹 위치와 별도로 `ExpandedHandTop=803` 등의 고정 좌표를 다시 적용했다. 새 프리팹에 `_useAuthoredHandLayout`을 활성화해 목업 좌표를 유지하고, 화면 경계 보정 및 hover/선택/드래그는 계속 사용한다. 기존 다른 프리팹은 기본값 false다.

**타이머:** v3의 배너와 housing은 한 PNG에 합쳐져 있다. PNG를 수정하는 대신 Unity의 원본 영역 Sprite 에셋으로 `TurnBannerArt`와 `TimerHousing`을 분리했다. `TimerHousing`은 Simple, `TimeTrack`은 Off, `TimeRing`은 Filled/Radial360, `TurnTimer`는 동적 TMP다. 배너나 원판 전체는 회전하거나 fill로 사라지지 않는다. 실제 deadline과 서버 시간 보정은 기존 로직을 사용한다.

**코스트:** v3 확장 패널에는 아이콘 3개가 합성돼 있어 전체 PNG를 가변 행 뒤에 깔면 중복된다. `Slices/*.asset`은 원본 Texture의 영역을 참조하는 Unity Sprite이며 새 PNG가 아니다. 이를 사용해 고정 헤더·하단, 늘어나는 본문·양쪽 테두리, 별도 화살표·아이콘으로 구성했다. `CostBody`는 접으면 숨고 `CostToggle`은 남는다. 펼침 화살표는 위, 접힘 화살표는 아래다. `RowHeight=46`, `RowSpacing=6`, `VisibleRows=4`를 유지하고 헤더/하단 여백만 원본 핸들 비율에 맞췄다. 4행을 넘으면 같은 ScrollRect/RectMask2D로 스크롤한다.

**영웅:** 초상화·마스크·링·HP를 분리했다. 장식 Image는 raycast를 받지 않으며 영웅 루트의 투명 hit Image와 HP 클릭 영역은 유지했다. 원본 링을 분리한다는 이유로 공격 대상 영역이 없어지지 않도록 했다.

**턴·로그:** 목업 중앙의 턴 주체와 번호는 실제 상태에 연결했다. 상단 카드 이벤트 알림의 Graphic만 숨겨 HUD를 가리지 않도록 했고, 이벤트 데이터와 로그 기록은 유지한다. 기존 로그 버튼에서 여는 모달, X/ESC 닫기, dim, 카드 상세, 상대 비공개 정보 차단을 그대로 사용한다. 중앙의 과거 대형 턴 알림도 중복 표시하지 않는다.

## 최종 기능 검증

[최종 XML: results-delivery.xml](../Artifacts/MockupExact/results-delivery.xml) — **2/2 통과**, 약 18.7초. Unity에서 실제 `BattleScene`을 열고 실행한 결과이며 컴파일만 확인한 결과가 아니다.

| 최종 테스트 | 결과 |
|---|---|
| `MockupExactBattleUiPlayModeTests.OriginalMockupLiveUiAndRequiredCaptures` | 통과. 내/상대 턴 및 중앙 표기, 타이머 감소·절반·0, 고정 housing, 손패 5장/추가·제거/오오라, 양쪽 필드 2+3 및 5+5, 빈 필드, 공격·수비 및 영웅 HP 변경, 카운터 갱신, 뒷면·비공개 정보, 네 페이즈 문구, 클릭 raycast, 코스트 0/3/6종·값 변경·접기·펼치기·휠 스크롤, 로그 X/ESC/dim/상세·스크롤·재대전 초기화 |
| `MockupExactOnlinePlayModeTests.MockupUiPreservesServerCommandsAndReconnect` | 통과. 기존 `ProjectCNetworkServer.exe` 및 TestData로 실제 WebSocket 연결·준비, 잘못된 드롭 복구, 드래그 소환, 상대 소환, 페이즈 버튼 연속 클릭이 한 번만 진행됨, 드래그 공격/체력 감소, 턴 종료, 재접속 후 카드 복원 |

최종 테스트 실행에서 예상하지 않은 Console Error/Exception은 없었다. 기존 일본어 로그 일부 글리프 누락 경고는 남는다. 기존 테스트 전체를 통과했다고 주장하지 않으며 이번에는 위 두 테스트를 실행했다. 초기 실패 및 중간 검증 XML도 같은 아티팩트 폴더에 보존했다.

1920×1080이 주 검수 해상도다. 추가로 1280×720, 1920×1200, 2560×1080에서 HUD/로그 팝업 경계도 확인했다. 사람이 두 독립 클라이언트를 OS 마우스로 끝까지 플레이한 검증은 수행하지 않았다.

## 필수 캡처와 비교

| 상태 | Unity 캡처 |
|---|---|
| A. 전투 시작 | [A-battle-start.png](../Artifacts/MockupExact/A-battle-start.png) — 실제 로컬 서버 |
| B. 손패 5장 | [B-hand-five.png](../Artifacts/MockupExact/B-hand-five.png) |
| C. 양쪽 필드 | [C-both-fields.png](../Artifacts/MockupExact/C-both-fields.png) |
| D. 코스트 펼침 | [D-cost-expanded.png](../Artifacts/MockupExact/D-cost-expanded.png) |
| E. 코스트 접힘 | [E-cost-collapsed.png](../Artifacts/MockupExact/E-cost-collapsed.png) |
| F. 타이머 진행 | [F-timer-half.png](../Artifacts/MockupExact/F-timer-half.png), [0초](../Artifacts/MockupExact/timer-zero.png) |
| G. 전투 기록 | [G-log-card-detail.png](../Artifacts/MockupExact/G-log-card-detail.png) |

비교 이미지의 왼쪽은 원본 목업, 오른쪽은 실제 1920×1080 Play Mode 캡처다. 이미지를 나란히 검토하면서 런타임 손패 좌표, 영웅 hit 영역, 배지 크기, 중앙 턴 표기, 코스트 화살표 및 배경을 수정하고 재촬영했다.

- [목업 ↔ 실제 전체 화면](../Artifacts/MockupExact/compare-C-both-fields.png)
- [목업 ↔ 코스트 펼침](../Artifacts/MockupExact/compare-D-cost-expanded.png)
- [목업 ↔ 코스트 접힘](../Artifacts/MockupExact/compare-E-cost-collapsed.png)
- [실제 서버 소환·공격](../Artifacts/MockupExact/online-summon-attack.png)

B~G의 UI 검증은 합성 서버 메시지/DTO를 사용한 실제 Play Mode다. 코스트 3종/6종은 기존 동적 UI의 검증용 표시 데이터이며 게임에 새 코스트 종류를 추가한 것이 아니다. 실제 게임은 현재 서버가 제공하는 요력과 최대치에 따라 기존대로 표시한다. 카드 그림·포트레이트도 기존 프로젝트의 데이터 리소스를 사용하므로 목업에 합성된 그림과 동일하지 않을 수 있다.

## 남은 시각적 차이와 미완료 범위

1. **v4 PNG는 미제공 상태다.** v4의 `BattleBackground_1920x1080`, 완성형 카드 프레임, 독립 `TurnBanner`/`TimerHousing`/빈 코스트 패널을 적용한 결과가 아니다. v3에서 동일한 동작 구조를 구현했다.
2. 별도 클린 배경은 목업과 바닥 재질·수평선·성·난간 구도가 다르다. 이전의 밝고 복잡한 배경보다 목업 색감에 가깝지만 **배경 일치 항목은 완전 충족하지 못했다.** 원본 픽셀을 유지하기 위해 재도색/색 보정으로 감추지 않았다.
3. v3 자체에 숫자 제거 패치, 일부 꽃/링 가장자리와 배지의 추출 흔적이 있다. 손패 독립 배지의 일부 가장자리 품질과 영웅 링 주변의 추출 흔적은 PNG를 수정하지 않는 조건에서 남아 있다.
4. 글꼴과 실제 카드/영웅 일러스트는 목업과 차이가 있다. 목업 중앙의 가는 선·꽃 장식은 독립 리소스가 없어 새로 그려 추가하지 않았다.
5. 좌측 로그 팝업·상세 패널은 제공 팩에 교체 자산이 없어 기존 디자인을 유지한다. 전체 화면 픽셀 일치나 모든 완료 조건 충족으로 보고하지 않는다.

## Unity에서 필요한 설정

프리팹, Sprite 메타, 원본 영역 Sprite 에셋, 설정은 모두 저장되어 있어 **추가 수동 배치/스프라이트 지정은 필요 없다.** 원본 에디터가 새 파일을 임포트한 뒤 `BattleScene`을 실행하면 적용된다. 열어 둔 Prefab Mode에 저장하지 않은 수동 변경이 있다면 해당 변경과 디스크 에셋을 확인해야 한다.

명시적 재적용 메뉴는 **Project C → Apply original mockup battle UI**다. 일반 플레이에는 실행하지 않아도 된다. 기존 `ApplySunsetCelBattleUi`, `ApplyReferenceBattleLayout` 등 이전 적용 도구는 새 배치를 덮어쓸 수 있으므로 재실행하지 않는다.

검증 재현 명령은 다음과 같다. 원본 에디터와 충돌하지 않도록 복제본을 사용했고, 캡처를 위해 batchmode 없이 실행했다.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.0.81f1\Editor\Unity.exe' `
  -projectPath C:\Dev\ProjectC\build\UnityMatchValidation `
  -runTests -testPlatform PlayMode `
  -testFilter 'ProjectC.Tests.MockupExactBattleUiPlayModeTests;ProjectC.Tests.MockupExactOnlinePlayModeTests' `
  -projectcServerPath C:\Dev\ProjectC\build\Debug\ProjectCNetworkServer.exe `
  -testResults C:\Dev\ProjectC\ProjectC.Client\Artifacts\MockupExact\results-delivery.xml `
  -logFile C:\Dev\ProjectC\ProjectC.Client\Artifacts\MockupExact\playmode-delivery.log
```
