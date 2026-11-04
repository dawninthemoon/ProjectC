# 전투 UI 필드 카드 가독성 개선 (1920×1080)

## 적용 범위

- `BattleFieldCard.prefab`: 필드 전용 일러스트 크롭 프로필을 연결하고 작은 이름 띠와 의미 없는 두 점 상태 장식을 숨겼다. 이름과 효과는 필드 카드 Hover 시 기존 상세 UI에서 확인한다. 공격력과 체력 배지는 유지한다.
- `BattleScreen.prefab`: 내/상대 필드를 구분해 글로우를 표시하고, 기존 턴 버튼의 진한 청록색 바탕과 페이즈 띠, 화살표, 텍스트 대비를 높였다. 버튼의 210×310 RectTransform, 클릭 이벤트와 Raycast 영역은 그대로다.
- `AspectFillImage`, `CardWidget`, `CardZoneView`, `BattlePresenter`, `TurnFrameGlow`: 필드에만 UV 크롭을 적용하고 서버 스냅샷의 행동 가능 플래그 및 기존 입력 대상 힌트에 따라 기본/행동 가능/선택/대상 가능 상태를 그린다. 손패는 원본 이미지의 기존 표시 범위를 유지한다.

## 리소스와 상태

- `Assets/Art/UI/BattleRenewalStage3/FieldCardCropProfile.asset`: 기본 확대 1.38배, 세로 오프셋 0.12. 구도가 다른 정의 ID 1001/3001/4002는 별도 값을 둔다. 원본 카드 이미지와 카드 데이터는 수정하지 않았다.
- `Assets/Art/UI/BattleRenewal/TurnFrame.svg/.png`: 편집 가능한 원본과 Unity Sprite. 금색 곡선 및 불투명 청록색 바탕을 보강했다.
- `Assets/Art/UI/BattleRenewal/TurnArrow.svg/.png`: 버튼 안쪽의 독립 장식이다. 페이즈 띠에는 기존 `HandTitle.png`를 재사용한다.
- 기본은 금속 프레임, 행동 가능한 내 카드는 청록색, 선택된 카드는 금색, 대상 선택 가능 카드는 푸른색 글로우다. 상대 카드도 실제 대상 힌트가 있으면 푸른색이 된다. UI에서 게임 규칙을 추론해 행동 가능을 만들지 않는다.
- 기존 두 점 상태 표시는 서버 상태와 연결되지 않은 장식이어서 숨겼다. 현재 필드 프리팹에는 실제 키워드를 신뢰성 있게 도식화하는 데이터 바인딩이 없어 새 키워드 아이콘을 추측해서 추가하지 않았다.

## 레퍼런스 비교

원본 레퍼런스의 필드 카드 한 장은 1920×1080 환산 약 173×218px이며 현재 필드 카드는 180×228px이다. 필드 얼굴과 상반신을 키우고 이름 띠를 없애 레퍼런스의 일러스트 중심 정보 위계에 가까워졌다. 손패 카드 크기와 부채꼴 배치는 이번 작업에서 바꾸지 않았다. [비교 이미지](../Artifacts/BattleFieldClarity/reference-vs-unity.png)를 참조한다.

남은 차이: 레퍼런스와 실제 카드 원화의 구도 자체가 달라 모든 캐릭터 얼굴이 같은 크기로 보이진 않는다. 특히 등 뒤를 보여 주는 카드 그림은 크롭만으로 얼굴을 보이게 할 수 없다. 턴 버튼의 곡선 실루엣과 금속 질감도 개선했지만 레퍼런스의 회화적인 꽃 장식과 광원 효과까지 재현한 상태는 아니다. 영웅 프레임, 자원 패널, 전장 구획선, 보조 메뉴는 이번 범위에서 변경하지 않았다.

## 검증

- Unity 6000.0.81f1, 1920×1080 Game View: [`field-1`](../Artifacts/BattleFieldClarity/field-1.png), [`field-3`](../Artifacts/BattleFieldClarity/field-3.png), [`field-5`](../Artifacts/BattleFieldClarity/field-5.png), [`basic`](../Artifacts/BattleFieldClarity/basic.png), [`attackable`](../Artifacts/BattleFieldClarity/attackable.png), [`selected`](../Artifacts/BattleFieldClarity/selected.png), [`targetable`](../Artifacts/BattleFieldClarity/targetable.png), [`hand-5`](../Artifacts/BattleFieldClarity/hand-5.png), [`hand-10`](../Artifacts/BattleFieldClarity/hand-10.png), [`hand-hover`](../Artifacts/BattleFieldClarity/hand-hover.png), [`field-hover`](../Artifacts/BattleFieldClarity/field-hover.png). 캡처는 실제 카탈로그의 서로 다른 정의 ID 10종으로 구성한 합성 스냅샷을 사용했다. 수치는 시각 검증용이다.
- 전용 PlayMode 테스트 1/1 통과: 필드/손패 크롭 분리, 상태 글로우, 상세 UI, 필드/손패/턴 버튼 Raycast. 결과: `Artifacts/BattleFieldClarity/results.xml`.
- 실제 서버 연결 드로우·드래그·카드 사용·공격·재경기 PlayMode 테스트 1/1 통과: `drag-attack.xml`.
- 입력 차단, 모달, 효과 선택 및 스냅샷 관련 PlayMode 테스트 7/7 통과: `input-regression.xml`.
- 손패 드로우 순서, 배치, Hover, 스냅샷 관련 PlayMode 테스트 12/12 통과: `draw-regression.xml`.
- 기존 1920×1080 카드 가독성·Raycast 레이아웃 PlayMode 테스트 1/1 통과: `layout-regression.xml`.

서버 규칙, 통신 프로토콜, 원본 카드 일러스트는 변경하지 않았다.
