# 통합 전투 UI — 2026-11-03

PDF의 UI 사양과 첨부 화면을 기존 전투 프리팹에 적용했다. 서버의 게임 규칙, 프로토콜 버전, 명령 형식은 그대로다. 이미 존재하던 신규 12종 카드와 견본 덱을 다시 추가하지 않았다.

## 조작과 배치

- 기본 손패는 우하단의 작은 수평 배열이다. 카드 클릭 시 하단 중앙의 부채꼴 배열로 이동한다. 확장 상태에서 다른 카드에 호버하면 해당 카드가 정면으로 올라오고 왼쪽 설명과 발동 액션의 소스가 바뀐다. 대상 선택 중 호버는 설명만 갱신하며 확정 대상은 클릭과 확인으로 결정한다.
- 빈 필드 클릭, 취소, 입력 초기화 시 손패를 접는다. 필수 서버 선택과 응답 대기 중에는 배경 클릭으로 선택을 취소하지 않는다. 드래그 드롭의 기존 화면상 카드 면적 30% 판정은 보존한다.
- 상단 중앙은 Barbaros, 하단 중앙은 Megachiyo 초상화다. 이 연결은 임시 표시 매핑이며 덱/플레이어의 서버 정체성으로 추정하지 않는다. 기존 플레이어 hit area와 Life 텍스트 참조는 유지한다. 서버 DTO에 최대 Life가 없으므로 최대 체력을 임의로 표시하지 않는다.
- 우상단 두 줄에 상대/자신의 덱·묘지·제외 수를 표시한다. 제외 수에는 비공개 제외 수량도 포함하지만 갤러리에는 공개 DTO만 표시한다. 묘지·제외 아이콘은 양쪽 공개 카드를 소유자 표시와 함께 하단 가로 스크롤 갤러리로 연다. 덱 아이콘은 개수만 표시한다.
- 효과의 필드 대상은 전장 카드와 영웅을 직접 클릭한다. 덱·묘지 등 별도 존의 선택은 같은 카드 갤러리를 사용한다. 서버의 허가된 후보 DTO와 불투명 토큰만 사용하며 로컬 카탈로그로 덱 목록을 재구성하지 않는다. 갤러리가 열리면 원래 손패는 숨긴다.
- 오른쪽 원형 버튼에 페이즈, 다음 동작, 서버 기준 남은 시간을 모았다. 0.15초 색상 애니메이션은 표시 효과이며 턴을 강제로 종료하지 않는다. 아래에는 요력 값과 게이지가 있다.
- 피해·회복·공격력/HP 변화는 서버 이벤트 직전의 표시 상태와 비교해 대상 위치에 표시한다. 요력 증가도 실제 서버 이벤트 값의 차이만 표시한다. 숫자는 Animator로 0.7초 동안 상승·페이드한다. 서버 스냅샷이나 수치를 수정하지 않는다.
- 멀리건의 기존 서버 선택/확인/대기/타임아웃 흐름을 유지한다. 전용 불투명 화면에서 기존 5장 UI를 사용하고 양쪽 확정 전에는 교체 결과를 보내지 않는다. 종료 후 같은 멀리건 화면에서 바꾼 카드만 제자리 뒤집기 연출을 적용하고, 총 1초 후 전투 화면으로 넘어간다. 확인 버튼 하나만 표시하며 별도의 새 손패 안내 문구는 없다. 그동안 로컬 명령 입력을 막고 이후 펼친 손패로 돌아간다. 서버의 Main 1 시작 시각이나 턴 타이머는 지연시키지 않는다.

## 에셋과 유지보수

`Assets/Prefabs/Battle/BattleScreen.prefab`과 `BattleCard.prefab`에 최종 배치를 저장했다. 일반 실행 전에 에디터 도구를 실행할 필요가 없다. `NumberPopup.prefab`, `NumberPopup.anim/.controller`, `PhaseChange.anim/.controller`를 추가했다. 회전하는 카드의 일러스트에는 stencil `Mask`를 사용한다.

`ProjectC/Upgrade Integrated Battle UI`는 명시적인 재배치 도구다. 기존 플레이어 패널의 중첩 프리팹 인스턴스를 전투 프리팹 내부에서 풀고 Life를 새 배지 밑으로 옮긴다. 직렬화 필드·기존 참조·씬 카메라는 보존한다. 수동 배치 변경 후 도구를 다시 실행하면 도구의 위치로 돌아간다.

`ProjectC/Capture Integrated Battle Preview`는 별도 임시 Preview Scene에서 합성 카드 상태를 렌더링한다. 온라인 경기나 PlayMode 테스트가 아니다. 결과는 `Artifacts/IntegratedBattle/Compact.png`, `Expanded.png`, `Gallery.png`다.

신규 카드 3001–3006, 4001–4006의 설명 숫자를 `DescriptionArgs` 경로와 `{0}` 템플릿으로 바꿨다. 생략된 대상 수량은 기존 기본값 1을 사용한다. 능력치, 효과, 비용, 덱 구성은 바꾸지 않았다. `Tools/ExportCardData.py`로 서버와 Unity 카탈로그를 함께 재생성했다. 변경된 카탈로그 버전을 사용하는 서버와 클라이언트는 함께 사용해야 한다.

## 아이콘 생성 기록

imagegen 스킬의 내장 이미지 생성 도구를 사용했다. 파일은 모두 `Assets/Resources/Icons/`에 저장했다.

- `Icon_Deck.png`: three stacked playing cards
- `Icon_Grave.png`: a small gravestone with a spirit wisp
- `Icon_Exile.png`: a card dissolving into a crescent portal
- `Icon_Life.png`: a heart
- `Icon_Yokai.png`: a Japanese spirit flame

각 파일에 사용한 최종 프롬프트는 아래 공통 문장의 `{subject}`를 위 주제로 치환한 것이다. 생성 요청에는 `transparent_background: true`를 설정했다.

> Use case: logo-brand. Asset type: small Unity game HUD icon PNG. Create one centered icon of {subject}. Matching set for dark teal Japanese yokai card game interface. Simple bold ivory and warm gold silhouette, restrained turquoise accent, crisp clear edges, readable at 32 pixels, flat graphic design, generous transparent margins. Transparent background. No text, no letters, no numbers, no border, no additional icons.

`UI_Circle.png`는 에디터 도구가 생성한 원형 UI 마스크다. 초상화는 사용자가 제공한 `Resources/Portraits/Barbaros.png`, `Megachiyo.png`를 사용한다.

## 이번 실행 범위

Unity 6000.0.81f1 배치 실행으로 C#을 컴파일하고 프리팹을 저장했으며 위의 정적 화면을 렌더링했다. `python3 Tools/ExportCardData.py`로 데이터 산출물을 생성했다. 자동 테스트 추가·실행, C++ 빌드, 온라인 대전 및 OS 마우스 조작 검증은 수행하지 않았다. PDF 안의 기존 검증 완료 서술을 이번 실행 결과로 간주하지 않았다.

## 손패 선택 및 사용 가능 표시 보완

확장 손패의 호버 선택은 상대 턴의 Idle 상태에서도 이동한다. 새 카드로 커서를 옮기면 이전 카드의 선택은 해제되며, 명령 전송 대기·드래그·효과 대상 선택 중에는 액션 소스를 바꾸지 않는다. 사용 가능한 카드는 서버의 CanSummon/CanAttack/CanActivate에 따라 선택 없이도 민트색 전체 테두리로 표시하고, 선택은 금색으로 구분한다. 별도 대상 선택 중의 후보/불가 강조는 우선한다.
