# ProjectC 목업 기반 UI 리페인트 — Unity 적용 가이드 (v4)

## 목표와 범위

- 기준: 사용자가 제공한 1672×941 전투 목업. 1920×1080 캔버스에서도 동일한 배치 비율을 사용한다.
- 미술 방향: 셀채색, 산호색/주홍·먹색·아이보리·남청, 명확한 외곽선, 절제된 1~2단 면 음영. 과도한 유광, 플라스틱 반사광, 발광, 검게 죽은 톤 금지.
- `Unity/Assets/Art/UI/Battle/MockupRepaint/` 아래 파일은 모두 개별 Unity UI 스프라이트다. 배경만 불투명 1920×1080이고, 다른 파일은 알파 투명 PNG다.
- 이미지 자체에 가변 텍스트/수치/카드 일러스트를 넣지 않았다. 영웅 초상화와 카드 일러스트는 현재 카드/영웅 데이터의 기존 이미지를 사용한다.

## 실제 프로젝트 구조에 따른 매핑

GitHub `dawninthemoon/ProjectC`의 `BattleScreen.prefab`, `BattleCard.prefab`, `BattleFieldCard.prefab`, `CardBack.prefab`, `BattleCostItem.prefab` 및 전투 에디터 구성 스크립트를 기준으로 정리했다. 실제 파일 이름/필드가 다른 브랜치에서는 바인딩 검증이 필요하다.

| 스프라이트 | ProjectC 대상/역할 | 동적 요소와 주의점 |
|---|---|---|
| `BattleBackground_1920x1080.png` | `BattleScreen` 배경 `Image` | 전체 Stretch, 채우기. 배경 위에 UI를 추가. |
| `TurnBanner.png` | `TurnHud`의 턴 장식 패널 | 턴 번호·`내 턴`/`상대 턴`은 별도 TMP. 우측 타이머를 배너 위에 겹침. |
| `TimerHousing.png` | `TimeTrack`의 고정 원판 | 숫자는 기존 `TurnTimer` TMP. 고정 바깥 테두리와 중앙 원판. |
| `TimerTicks_On.png` | `TimeRing` 눈금 레이어 | 같은 스프라이트를 2장 배치: 아래 `Off`는 `Image.Type=Simple`, 어두운 적갈색 틴트; 위 `On`은 `Image.Type=Filled`, `Fill Method=Radial 360`, 반투명 없는 원본 빨강, `fillAmount=remaining/duration`. 두 장 모두 같은 RectTransform. 원본 원판까지 Radial로 지우지 말 것. |
| `HeroRing_Player.png`, `HeroRing_Opponent.png` | 양쪽 `PlayerPanel` 초상화 위 프레임 | **초상화는 별도 `Image`** + 원형 마스크; 테두리 PNG는 초상화 위. |
| `HeroHealthBadge.png` | 양쪽 영웅 HP | 숫자 TMP 유지. 목업처럼 초상화 바로 옆 작은 붉은 육각형. |
| `HandCardFrame.png` | `BattleCard.prefab`의 `RenewalFrame` | 일러스트는 기존 `ArtworkMask`/`AspectFillImage` 그대로. 카드 이름 TMP는 투명한 상단 명패 위. 공격/방어 배지 모양은 그림 안에 들어 있어 기존 배지 배경 `Image`를 **비활성화**하고 TMP 숫자만 유지해야 중복되지 않는다. |
| `FieldMonsterFrame.png` | `BattleFieldCard.prefab`의 `RenewalFrame` | 카드 아트/동적 능력치 텍스트 유지; 기존 배지 배경은 중복 금지. |
| `CardAttackFlower.png`, `CardDefenseShield.png` | 독립 배지 부품 | 향후 배지를 프레임에서 분리하거나 스탯 HUD에 재사용할 경우 사용. 이 v4의 카드 프레임엔 배지 그림이 포함되어 있으므로 **같은 프레임에 다시 겹치면 안 됨**. |
| `CardBack.png` | `CardBack.prefab` | 별도 카드뒷면 스프라이트. |
| `PhaseButton.png`, `PhaseArrow.png` | 우측 페이즈/배틀 버튼 | 텍스트/화살표는 동적으로 위에 올림. `메인 1` 탭은 비워 둠. |
| `ZoneTag_Deck.png`, `ZoneTag_Grave.png`, `ZoneTag_Exile.png` | 상·하단 양측 덱/묘지/제외 영역 | 태그 PNG 안에 아이콘이 포함됨. 기존 `DeckIcon`/`GraveIcon`/`ExileIcon` 이미지 숨기고 숫자 TMP만 올려야 아이콘이 중복되지 않음. |
| `SpecialCostPanel_Expandable.png`, `CostChevron.png` | `CostToggle` + `CostViewport` 배경 | 패널 본체는 9-slice 또는 상단/하단 고정+중앙 늘림, 화살표 별도, 가변 행은 실제 `BattleCostItem` 프리팹 사용. |
| `SideButton_Log.png`, `SideButton_Menu.png` | 좌측 두 개 버튼 | 기존 Unity Button component/Click handler 유지하고 graphic만 교체. |

## 특수 코스트는 정적인 3줄 그림이 아님

현재 `BattleSpecialCostsView.cs`는 `CostToggle`, `CostChevron`, `CostViewport`(`ScrollRect`/`RectMask2D`) 및 재사용 `BattleCostItem` 행을 갖는다. `RowHeight=46`, `RowSpacing=6`, `VisibleRows=4`, `HeaderHeight=32` 구조를 유지한다.

1. 새 검정 패널을 `CostViewport` 뒤에 놓고, 뷰포트를 패널 내부에 배치한다.
2. `CostToggle` 클릭 시 패널 높이와 `CostViewport` 표시 높이를 갱신한다. 화살표는 기존처럼 180° 회전.
3. 행마다 `BattleCostDefinition.Icon`과 `_value` TMP를 따로 표시한다. 코스트 종류·개수가 바뀌어도 판넬 배경에는 **고정 아이콘/수치가 없다**.
4. 늘어나는 패널의 최소/최대 높이를 확인한다. 현재 `SpecialCostPanel_Expandable` PNG의 위·아래 장식은 고정, 가운데 직선 구간만 늘리도록 **Sprite Editor에서 9-slice Border**를 지정한다. 내보낸 266×750 스프라이트에서는 우선 좌우 16 px, 위 150~175 px, 아래 65~90 px 범위를 시각 확인하며 설정한다. 가운데의 긴 직선 구간만 늘어나야 한다.

## 타이머 단계 및 색상

`BattleTurnHudView.cs`의 `TimeRing`은 이미 `Image.fillAmount`를 사용한다. 다음처럼 역할을 재정의한다:

```text
TimeRoot (same RectTransform, center/pivot 0.5)
 ├─ TimerHousing       Image.Simple, white tint
 ├─ TimerTicksOff      Image.Simple, TimerTicks_On sprite, color #55252B
 ├─ TimeRing           Image.Filled Radial360, TimerTicks_On sprite,
 │                     white tint, clockwise true, fillAmount 0..1
 └─ TurnTimer          TMP, center, ivory text
```

현재 스크립트가 시간이 줄 때 `TimeRing.color`를 청록/황색으로 바꾸는 부분은 제거하거나 `Color.white`로 고정해야 원본 붉은 눈금이 유지된다. 바뀌는 것은 **켜진 눈금의 노출량**이고 원판/숫자는 그대로 유지된다.

## Unity 가져오기

- Texture Type: `Sprite (2D and UI)`
- Sprite Mode: `Single`
- Alpha Is Transparency: `On`
- Generate Mip Maps: `Off`
- Filter Mode: `Bilinear`
- Compression: `None`(PC 시각 점검 시), 이후 플랫폼별 아틀라스로 최적화
- Mesh Type: `Full Rect`; Pivot은 중앙으로 시작하고 프리팹 배치에 맞춰 조절
- Canvas Scaler: `Scale With Screen Size`, Reference Resolution `1920×1080`, `Match Width Or Height`는 프로젝트 배치에 맞춤
- 명패, 수치, 초상화, 카드 일러스트는 `Image`/TMP 실시간 데이터 요소로 남겨둔다.

## 적용 순서 및 확인

1. 기존 프리팹/아트 보존용 Git 작업 브랜치를 만든다.
2. 배경/턴/타이머/영웅/하단 카드/우측 버튼/족자/특수 코스트 순서로 각각 Sprite만 교체한다.
3. 1920×1080 Game View에서 기본 상태, 카드 5장 손패, 필드 카드 2+3, 타이머 68→0, 특수 코스트 0/3/5종, 축소 해상도까지 확인한다.
4. 기존 `ApplyReferenceBattleLayout`, `ApplyBattleRenewalStage3`, `ApplyTurnLogArtwork` 같은 이전 버전 자동 적용 스크립트를 새 구성 후 다시 실행하면 일부 Sprite/RectTransform이 덮일 수 있다. 배포용 새 적용 스크립트 하나로 고정해야 한다.

## 정확도에 관한 주의

이 팩은 첨부한 목업을 직접 참조해 각 요소를 **새로 그린 것**으로, 원본 PSD/레이어가 없어 픽셀 단위 복제는 아니다. 개별 자산은 임의 빛 반사와 3D 광택을 배제한 근사 리페인트이며, 스프라이트 바인딩과 Unity Game View의 최종 시각 QA는 별도로 필요하다. 제공되는 `Preview` 이미지는 검수용이고 실제 게임 스프라이트로 사용하지 않는다.
