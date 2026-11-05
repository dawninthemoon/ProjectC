# ProjectC 전투 UI — 목업 직접 추출본 (v3)

## 이 패키지가 무엇인지

- **새로 생성하거나 화려하게 재해석한 디자인이 아닙니다.** 첨부된 `1672 × 941` 목업의 실제 색·선·무늬를 픽셀 단위로 잘라 투명 PNG로 만든 것입니다.
- 셀 채색/무광 계열의 **원본 픽셀을 가능한 범위에서 유지**했습니다. 따로 광택, 광원, 베벨을 추가하지 않았습니다.
- 원본은 **레이어가 없는 한 장의 이미지**입니다. 텍스트·숫자·일러스트에 가렸던 배경 픽셀은 알 수 없으므로, 그 부분만 색을 덮거나 투명하게 비운 재구성입니다. 따라서 **모든 PNG가 수학적으로 원본과 픽셀 완전 동일하다고 주장하지 않습니다.**
- 숫자, 턴 이름, 시간, 카드 이름, 게이지 값, 포트레이트, 일러스트는 **런타임에서 채우세요.** 일부 비워진 부분은 원본과 동일한 색을 추정한 무광 패치입니다.
- `reference/source_mock.png`는 위치·외관 검사용 원본 목업이지, 런타임 배경으로 사용하면 안 됩니다. UI/카드가 이미 들어 있는 이미지이기 때문입니다.

## 주요 PNG 및 역할

| PNG (`png/`) | 원본 크기(px) | Unity 사용법 |
| --- | ---: | --- |
| `CardBack_Exact.png` | 88×134 | **상대 패와 덱 뒷면에 동일하게 사용**. 원본 벚꽃 문양. |
| `HandCardFrame_Exact.png` | 180×262 | 손패 아트 위에 얹을 투명 프레임. 이름과 능력치 TMP 별도. |
| `FieldMonsterFrame_Exact.png` | 139×156 | 필드 몬스터용 **세로형, 상단 둥근 아치**. 일러스트는 안쪽에 넣음. |
| `CardAttackFlower_Exact.png` | 75×68 | **공격력 = 왼쪽 붉은 꽃**. 숫자는 TMP 별도. |
| `CardDefenseShield_Exact.png` | 74×71 | **수비력 = 오른쪽 남색 방패**. 숫자는 TMP 별도. |
| `CardBottomJewel_Exact.png` | 39×38 | 카드 하단 중앙 장식. |
| `TurnHUD_Exact.png` | 355×143 | 상단 왼쪽 턴/시간 장식. 숫자와 턴 제목은 TMP 별도. |
| `TimerTicks_Off_Exact.png` | 118×137 | 타이머의 어두운 LED/눈금. |
| `TimerTicks_On_Exact.png` | 118×137 | **같은 원본 눈금의 켜진 상태**. Radial 360 Filled Image 사용. |
| `SpecialCost_Expanded_Exact.png` | 84×181 | **스크롤형 특수 코스트 확장 패널**. 불·초승달·별 아이콘 원본 그대로. |
| `SpecialCost_Collapsed_Exact.png` | 84×34 | 축소 상태의 화살표 핸들. 버튼으로 전환. |
| `SideButton_Log_Exact.png` | 83×68 | 좌측 배틀 로그/기록 버튼. |
| `SideButton_Menu_Exact.png` | 83×74 | 좌측 메뉴 버튼. |
| `PhaseButton_Exact.png` | 278×322 | 오른쪽 부채/매듭/물결 페이즈 장식. 현재 페이즈, 다음 페이즈 TMP로 표시. |
| `PhaseArrow_Exact.png` | 81×36 | 페이즈 버튼 내 오른쪽 화살표. |
| `ZoneTag_Deck_Exact.png` | 103×170 | 덱 부적. 아이콘/장식 원본 그대로, 숫자는 TMP로. |
| `ZoneTag_Grave_Exact.png` | 103×170 | 묘지 부적. 숫자 TMP로. |
| `ZoneTag_Exile_Exact.png` | 110×170 | 제외 부적. 숫자 TMP로. |
| `HeroRing_Opponent_Exact.png` | 221×199 | 상대 영웅 프로필 테두리/벚꽃. 내부 일러스트는 별도. |
| `HeroRing_Player_Exact.png` | 256×300 | 내 영웅 프로필 테두리/벚꽃. 내부 일러스트는 별도. |
| `HeroHealthBadge_Exact.png` | 83×109 | 영웅 체력 육각형. 숫자 TMP로. |
| `Mask_*.png` (4종) | 각 원본 대응 | 투명 아트 삽입 범위를 제한하기 위한 흰색 마스크. |

## 타이머: 눈금이 실제로 켜졌다 꺼지도록

타이머는 **한 장에 빛을 구워 넣으면 안 됩니다.** `TurnHUD_Exact` 위에 눈금 두 이미지를 정확히 겹치세요.

1. `TurnHUD_Exact`을 배치합니다. 해당 이미지 내부에는 이미 **꺼진** 눈금이 포함되어 있습니다.
2. 자식 `TimerLit`에 `TimerTicks_On_Exact` 설정. 위치는 TurnHUD 좌상단에서 **(+234, +0)**, 크기는 **118 × 137**입니다. `Off`는 비교용/별도 연출용이고, TurnHUD와 동시에 깔면 중복될 수 있습니다.
3. `Image.Type = Filled`, `Fill Method = Radial 360`, `Fill Origin = Top`을 기본으로 설정하고 시계방향을 화면에서 테스트하세요. `fillAmount = Mathf.Clamp01(remainingSeconds / maxSeconds)`.
4. 원본처럼 개별 LED가 단계적으로 점등되는 느낌은 `fillAmount = Mathf.Ceil(remainingSeconds / maxSeconds * segmentCount) / segmentCount`로 **단계 양자화**합니다. `segmentCount`는 실제 게임 룰/눈금 개수에 맞게 지정하고 Play Mode에서 보정하세요.
5. 중앙의 남은 시간 숫자는 `TMP_Text`로 별도 표시합니다. `TurnHUD` 전체를 회전시키거나 확대해 애니메이션하지 마세요.

## 특수 코스트: 스크롤 + 확장형

- 같은 부모 RectTransform에서 `Expanded`와 `Collapsed` 이미지 게임오브젝트를 **상호 배타적으로 활성화**합니다.
- 펼침: `SpecialCost_Expanded_Exact` (84×181), 아이콘별 숫자 3개를 그 위치에 맞춰 TMP 별도 표시.
- 접힘: `SpecialCost_Collapsed_Exact` (84×34). 화살표는 그대로 보여야 하며 입력 가능 영역이 남아야 합니다.
- **패널을 181px에서 34px로 찌그러뜨리지 마세요.** 각각 원본에서 잘라낸 별도 상태입니다.
- 좌측 기존 버튼/특수 코스트 UI 위치와 각 자원 타입/동작은 바꾸지 마세요.

## 카드·필드·HUD 배치

- 손패 카드는 `Artwork (Mask)` → `HandCardFrame` → `CardAttackFlower + ATK TMP`(왼쪽) → `CardDefenseShield + DEF TMP`(오른쪽) → `Name TMP` 순서.
- 필드 몬스터는 `Artwork (Mask_FieldArtwork)` → `FieldMonsterFrame` → `ATK/DEF 텍스트`. **카드를 눕히거나 곡면 원근으로 휘지 말고 세워서** 보여주세요. 빈 슬롯은 그리지 마세요.
- 상대 손패 `CardBack_Exact`과 덱 뒷면 `CardBack_Exact`을 동일 Sprite로 참조하세요.
- 공격력/수비력 좌우 위치는 **공격력(붉은 꽃) 왼쪽, 수비력(남색 방패) 오른쪽**입니다.
- 상대/내 필드 몬스터가 **동시에** 표시될 수 있도록 두 전장 행의 캔버스를 따로 유지하세요.
- `ZoneTag_*` 숫자는 런타임의 덱/묘지/제외 count를 각각 연결하세요. 텍스트/값을 PNG에 박아 넣지 마세요.
- 상대 패, 양측 덱/묘지/제외, 배틀 기록 버튼, 턴 UI, 시간, 내 패, 영웅은 **기존 정보 구성/위치를 유지**해야 합니다.

## 해상도 및 소스 좌표

이 파일들은 **원본 목업 1672×941 좌표의 1배율**입니다. 해당 화면은 16:9에 가깝습니다. 1920×1080 Canvas라면 초기 스케일 비율은 약 **1.1483배**입니다. `Canvas Scaler / Scale With Screen Size / Reference Resolution 1672×941`로 원본 기준 좌표를 맞출 수 있습니다. 이후 실제 게임 화면 크기에 맞춰 앵커를 지정하세요.

| 요소 | 원본 목업 좌상단 (x,y) |
| --- | ---: |
| 턴·시간 | (0, 0) |
| 배틀 기록 | (20, 136) |
| 메뉴 | (20, 204) |
| 특수 코스트 펼침 | (22, 280) |
| 첫 상대 패 뒷면 | (354, 12) |
| 상대 프로필 테두리 | (765, 0) |
| 상대 덱/묘지/제외 | (1355, 0) / (1451, 0) / (1545, 0) |
| 오른쪽 페이즈 UI | (1374, 316) |
| 중앙 예시 필드 몬스터 | (767, 266) |
| 중앙 예시 손패 프레임 | (776, 656) |
| 내 프로필 테두리 | (0, 641) |

### 임포트 권장값

`Texture Type: Sprite (2D and UI)` / `Sprite Mode: Single` / `Alpha Is Transparency: On` / `Mesh Type: Full Rect` / `Compression: None` / `Max Size: 2048 이상` (필요한 경우). UI Image에는 대개 `Preserve Aspect`를 활성화하고, `Filter Mode`는 픽셀 느낌을 선호하면 `Point`, 일반 1080p에서는 `Bilinear`를 비교하세요.

`png_2x/`는 **원본 이미지를 2배로 리샘플링한 편의본**입니다. 원본에 없던 디테일을 새로 만든 것이 아닙니다. 1배율과 혼합 사용하지 마세요.

## 꼭 알아둘 제작 한계

1. 원본이 한 장으로 합쳐져 있어서 **카드 내부 미공개 일러스트 아래 프레임**, 일부 영웅 링에 가려진 부분, 텍스트가 가렸던 무늬는 완벽하게 되살릴 수 없습니다. 이 부분의 패치는 원본 픽셀과 다를 수 있습니다.
2. 배경도 캐릭터·손패·UI가 이미 합성된 한 장이므로, **UI 없는 완벽한 순수 배경**을 픽셀 그대로 추출하는 것은 불가능합니다. 이 패키지의 `source_mock.png`를 게임 배경이라고 오인하지 마세요.
3. 원본 품질보다 더 선명한 4K 텍스처를 픽셀 그대로 생성할 수는 없습니다. 추후 진짜 마스터 리소스가 있으면 같은 레이아웃을 기준으로 고해상도 원본 레이어를 교체해야 합니다.

목표는 **스타일을 재해석한 생성형 디자인이 아니라, 목업에서 관찰되는 원형/선/문양을 최대한 그대로 게임 UI 파츠로 전달**하는 것입니다.
