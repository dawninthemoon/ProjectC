# 카드 프레임 돌출 및 오오라 수정

2026-11-05. 턴 HUD/전투 로그/게임 규칙/서버/카드 배치 코드는 변경하지 않았다. 기존에 수정 중이던 폰트 에셋도 덮어쓰지 않았다.

## 확인한 원인

손패와 필드 프리팹의 `FrameImage/Inset/Accent`는 Sprite가 없는 폭3px, 세로 Stretch Image였다. `CardWidget.RenderStatus()`가 이 직선 이미지를 사용 가능 시 청록색, 선택 시 금색으로 바꿨다. Inset에는 이 이미지를 카드 곡선으로 제한하는 마스크가 없어, 카드의 상단·하단 모서리 곡선 밖에서도 직선이 보였다. 수정 전 Unity 캡처에서 첨부 화면과 같은 돌출을 재현했다.

오오라가 약했던 별도 원인은 `FrameGlow`의 표현이다. 기존 HandGlow/FieldGlow는 금속 테두리 위에 겹치는 단일 선으로, 바깥쪽 확산과 투명 여백이 없었다. 공유 BattleFrameGlow 셰이더는 평상시 알파를0.38까지 낮추고 위젯 알파0.65를 곱해 불투명한 마스크 부분도 약0.247 수준이 됐다. Sweep가 지나가는 순간 외에는 특히 밝은 일러스트·금속 프레임 위에서 잘 보이지 않았다.

프레임과 기존 Glow의 원본 비율 및 Face의 Anchor/Pivot/회전/균일 배율은 서로 일치했다. 기존 Outline 컴포넌트는 이미 꺼져 있었다. 압축·필터링 또는 회전이 긴 상태색 직선을 생성한 것은 아니었다. 원본 HandFrame/FieldFrame PNG와 SVG는 수정하지 않았다.

## 실제 수정

- 기존 Accent는 직선 렌더링을 종료했다. 직렬화된 Image 참조와 상태색은 호환성을 위해 남겼다. 상태 표시는 아래의 실제 윤곽 오오라로 대체했다. 원본 프레임을 자르거나 새 Mask로 돌출을 가리지 않았다.
- `HandAura.svg/.png`, `FieldAura.svg/.png`는 각각 기존 HandFrame/FieldFrame SVG의 **동일한 외곽 경로**를 사용한다. 주 프레임 안쪽과 기존 금속 두께7을 제외하고, 바깥쪽에 선명한 윤곽과 부드러운 확산을 만든다. 중앙 알파는0이다.
- 원본 좌표 기준 사방18의 투명 여백을 주고2배 해상도로 렌더링했다. 손패460×568, 필드404×438. 프리팹의 Glow RectTransform도 사방18만 늘려 원래 프레임과 UV가 일치하도록 했다. 카드 본체 크기/배치/기울기를 변경하지 않았다.
- 카드 전용 `CardFrameAura.shader`와 Material을 분리했다. 기존 BattleFrameGlow 셰이더 및 턴 버튼 Material은 변경하지 않았다. 지속적인 기본 밝기와 작은 호흡 변화만 사용해 강조가 주기적으로 사라지지 않는다. 사용 가능 상태의 불투명 윤곽 기준 최소 알파는 약0.792다.
- Glow는 같은 Face의 첫 자식으로 그린다. 프레임·일러스트·텍스트·배지 아래에 위치하고 카드와 함께 회전/확대/이동한다. 화면 전체를 덮는 별도 Canvas는 만들지 않았다. Glow의 Raycast는 꺼져 있다.
- PNG는 Uncompressed/Bilinear/Clamp, mipmap 없음, FullRect, PPU200으로 임포트했다. 9-slice로 곡선을 늘리지 않는다.

## 상태 표시

| 상태 | 표현 |
| --- | --- |
| 사용 불가능, 평상시 | 금속 프레임만 표시, 오오라 알파0 |
| 사용 가능 | 청록색 외곽선 + 확산, 위젯 알파0.9 |
| 사용 가능 Hover | 더 밝은 청록색, 알파1 + 기존 들어올림/확대 |
| 사용 불가능 Hover | 정보 열람용 옅은 회백색, 알파0.55. 사용 가능 청록색과 구분 |
| 선택한 카드 | 금색, 알파1 |
| 선택 가능한 대상 | 하늘색, 알파0.98. 상대 카드도 실제 후보일 때 표시 |
| 선택 완료/집중 대상 | 밝은 아이보리 금색, 알파1 |
| 대상 지정 중인 원본 카드 | 후보가 아니어도 선택 금색을 유지 |

기존 서버 Card.CanSummon/CanAttack/Activations와 GameplayInputCoordinator를 사용한다. Presenter가 손패에도 실제 PlayCard/ActivateEffect 입력 권한을 전달하므로, 모달·대기·연결 해제 등으로 입력이 잠기면 서버 사용 가능 플래그만으로 청록색이 남지 않는다. 독립 프리팹 미리보기에서는 기존 서버 DTO 기반 표시를 유지해 호환성을 보존했다. 게임 행동의 허용 여부를 새 규칙으로 판단하지 않는다.

Hover/선택 변경 즉시 표시를 갱신하고, 선택/Hover 카드는 기존 Zone 정렬 경로로 앞으로 올린다. 카드 순서와 슬롯 배치는 유지한다. 다른 카드에 완전히 덮인 부분을 뚫고 오오라가 보이게 하지는 않는다. 노출된 외곽을 강조하며, 선택/Hover 시 카드 전체를 앞으로 올려 읽게 한다.

풀 반환 때 이전 강조·Pulse·입력 권한을 정리하고, 비공개 카드와 드로우 비행 중에는 강조를 표시하지 않는다. 기존 소환/피해 Pulse는 별도의 짧은 피드백으로 유지했다. 비용/공격력/체력 배지 자체에 새 오오라를 추가하지 않았으며, 현재 효과는 주 카드 프레임 윤곽을 따른다.

## 변경 파일

- `Assets/Scripts/Battle/CardWidget.cs`
- `Assets/Scripts/Battle/BattlePresenter.cs`: 손패의 표시용 입력 권한 전달5행만 추가.
- `Assets/Prefabs/Battle/BattleCard.prefab`
- `Assets/Prefabs/Battle/BattleFieldCard.prefab`
- `Assets/Shaders/UI/CardFrameAura.shader`
- `Assets/Art/UI/BattleCardAura/`: HandAura/FieldAura SVG·PNG, CardFrameAura Material.
- `Assets/Editor/ApplyCardAura.cs`: 명시적 제작 도구. 자동 실행하지 않음.
- `Assets/Tests/PlayMode/CardAuraPlayModeTests.cs`

## Unity 검증 및 캡처

열려 있는 원본 Unity 에디터를 종료하지 않고, `/tmp/projectc-card-aura/ProjectC.Client` 복제본에서 Unity6000.0.81f1로 수정 전 캡처·프리팹 적용·실행 검증을 수행했다. 검증한 프리팹과 에셋/.meta를 원본에 반영했다.

`Artifacts/CardAura/Before/`, `After/`에 실제 Game View 캡처를 보관했다. 같은 카드 정의와 표시 조건을 사용한 테스트 fixture 화면이며, 운영 서버 경기 캡처는 아니다.

- 손패1·3·5·10장, 다양한 밝기의 실제 카드 일러스트.
- 서버 사용 가능/불가능, 입력 잠금, 사용 가능 Hover, 불가능 Hover, 선택, 대상 지정.
- 필드 사용 가능/불가능·Hover·선택, 상대 대상 후보/집중/불가.
- 확대/들어올림 후 복귀, 기울기와 겹침, 숨겨진 카드/드로우 비행의 강조 제거.
- 1920×1080,1280×720,2560×1080. 카드 본체가 화면 경계를 벗어나지 않는지 확인.

수정 전·후 직접 비교:

- `comparison-hand-5.png`: 전체 화면 비교.
- `comparison-hand-detail.png`: 직선 돌출과 새 윤곽 강조를 확인하는 손패 영역 확대.
- `comparison-selected.png`, `comparison-target-selection.png`.

| 실행 항목 | 결과 |
| --- | --- |
| CardAuraPlayModeTests: 수정 후 상태/복귀/해상도 | 1/1 통과 |
| BattleMergeCompatibilityTests | 1/1 통과 |
| GameplayInputPlayModeTests | 7/7 통과 |
| DrawPresentationPlayModeTests | 12/12 통과 |
| 실제 서버 드래그 소환·공격·재경기 | 1/1 통과 |
| 실제 서버 효과 대상 선택·현현 | 1/1 통과 |
| 실제 서버 비공개 선택·재접속·확정 | 1/1 통과 |
| TurnLogPlayModeTests | 1/1 통과 |
| CardPresentationPlayModeTests.BattleArtworkBadgesTransformSelectionAndHover | 0/1 실패:33행의 Artwork 부모 RectMask2D 기대값 |

수정 후25개 통과,1개 실패. 실패한 기존 테스트는 복제본의 CardWidget/Presenter/두 카드 프리팹을 모두 수정 전 HEAD 버전으로 되돌려 다시 실행했을 때도 **동일한33행**에서 실패했다. 기존 테스트 기대값은 변경하지 않았다. 수정 전 캡처용 CardAuraPlayModeTests는 별도로1/1 통과했다. XML 결과와 수정 전 실패 재현 결과는 `Artifacts/CardAura/Verification/`에 보관했다.

전체 테스트 스위트 및 배포용 Player 빌드는 실행하지 않았다. 정상 동작 확인은 위 Editor/Game View와 로컬 실제 서버 시나리오 범위다. 장치별 GPU와 모든 카드 일러스트 조합은 전수 검증하지 않았다. 커밋·푸시는 하지 않았다.
