# 전투 UI 리뉴얼 3단계: 손패·필드 카드 분리

## 변경 내용

- 기존 서버 `CardView`와 `CardWidget`을 그대로 사용한다. `BattleScreen.prefab`의 손패는 `BattleCard.prefab`, 필드는 `BattleFieldCard.prefab`을 사용한다. Scene은 변경하지 않았다.
- 손패는 180×244의 긴 곡선형 프레임, 상단 이름 띠와 별도 원형 비용 배지, 하단 능력치 배지를 사용한다. 필드는 180×228의 절단형 프레임, 작은 이름 띠, 넓은 일러스트와 독립된 청색 공격력·적색 체력 배지를 사용한다. 필드 중앙 하단의 작은 장식 프레임은 후속 상태 아이콘을 위한 공간이며, 현재 특정 상태를 의미하는 아이콘은 아니다.
- 이름과 수치는 TMP 컴포넌트이고 일러스트는 기존 `AspectFillImage`와 마스크를 사용한다. 배지의 부모 계층을 정리해 카드 외곽선이 숫자 위에 그려지지 않게 했다. 입력·드로우 로직과 동일 카드 데이터 모델은 유지한다.
- 비용 배지는 기존 로직의 `ActivationYokaiCosts`가 존재할 때만 표시한다. 이는 효과 발동의 요력 비용이며, 카드 사용·소환 비용이 있다는 뜻이 아니다. 없는 비용이나 0을 만들어 표시하지 않았다.
- `CardTableLayout`은 필드 카드 수가 영역을 초과하면 간격과 크기를 함께 균일하게 축소한다. 5장 기본 배치의 크기는 유지하며 10장의 공격력/체력 배지가 서로 겹치지 않는다. 극단적인 32장에서는 카드가 매우 작아져 가독성이 제한된다.

## 아트 및 광택

`Assets/Art/UI/BattleRenewalStage3/`의 HandCardFrame, FieldCardFrame, HandTitle, HandCardCostBadge, FieldCardAttackBadge, FieldCardHealthBadge, FieldStatusFrame은 각각 SVG 원본과 투명 PNG가 있다. 별도의 HandCardGlowMask, FieldCardGlowMask는 각 프레임 PNG의 알파에서, TurnGlowMask는 턴 프레임 알파의 외곽에서 생성했다. 글자와 수치는 아트에 포함하지 않는다.

`Assets/Shaders/UI/BattleFrameGlow.shader`와 공유 머티리얼 `BattleFrameGlow.mat`를 사용한다. uGUI 스텐실 및 RectMask 클리핑을 지원하고, 프레임별 마스크 위에 대각선으로 움직이는 광택을 그린다. 카드의 기존 서버 기반 사용 가능/선택/대상 강조와 펄스에 색과 불투명도를 연결했다. 턴 프레임은 기존 Button의 interactable 상태에서만 표시한다. 이 광택은 마스크를 가로지르는 기본 스윕이며 윤곽을 따라 움직이는 정밀한 거리 기반 하이라이트나 Bloom은 아니다. 빌드 플랫폼별 GPU 성능 및 렌더 호환성은 미측정이다.

`Assets/Editor/ApplyBattleRenewalStage3.cs`는 명시적 프리팹 적용 도구다. 재실행 시 이 단계의 프레임/배지 배치가 다시 저장된다. 1·2단계의 이전 적용 도구를 재실행하면 이 단계 배치가 덮일 수 있다.

## 검증

- Unity 6000.0.81f1 PlayMode에서 실제 BattleScene과 합성 공개 스냅샷으로 손패 1/5/10장, 필드 1/5/10장, 손패 Hover, 필드 선택 및 대상 강조, 카드별 다른 프레임·마스크, 비용·공격력·체력, 셰이더 지원 여부를 검사했다. `Artifacts/BattleRenewalStage3-visual.xml`: 1/1 통과. `Artifacts/BattleRenewalStage3/`의 hand-*, field-* 캡처는 합성 상태다. `field-target`은 서버 선택 UI를 연 것이 아니라 해당 대상 시각 상태를 직접 적용한 캡처다.
- `Artifacts/BattleRenewalStage3-regression.xml`: 기존 회귀 22개 중 20 통과, 2 실패. 실제 서버를 사용하는 주문 선택·거부·재선택과 입력 잠금, 드로우 표시 관련 12개 및 2단계 화면 비율·Raycast 검사는 통과했다. 서버 전장 테스트는 본문 실행 이후 정리에서 파괴된 CardGalleryView 접근 예외로 실패했고, 별도 서버 효과 테스트는 클릭 후 ContextActionView가 null이어서 선택 동작 확인 전에 실패했다. 후자는 이 단계의 회귀 여부를 확정하지 못했다.
- `online-field.png`는 실제 서버 전장 테스트 중 카드 사용·소환 및 공격 후 캡처다. 해당 테스트는 위 정리 예외 때문에 전체 통과가 아니다. `online-play-card.png`도 기존 서버 테스트의 캡처다.
- 화면과 아트는 레퍼런스의 손패 곡선·비용, 필드 절단형·전투 배지 및 서로 다른 비율을 반영했으나, 금속 세공·원화 구도·손패 겹침 및 효과의 시각적 완성도는 아직 다르다. 플레이어 직접 조작, 모든 선택형 효과, 다양한 기기와 전체 게임 회귀는 추가 검증이 필요하다.
