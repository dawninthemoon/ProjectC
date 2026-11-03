# 손패·초상화 확대와 직접 공격 드래그 영역

2026-11-03. 손패 축소 Scale 0.38→0.57, 확대 Scale 0.86→1.29로 각각 1.5배 늘렸다. 양쪽 초상화와 통합 라이프도 1.5배다. 필드 카드 크기는 유지한다.

변경 파일:

- `Assets/Scripts/Battle/CardTableLayout.cs`: 손패 Scale 증가, 손패 장수가 많을 때 카드 간격부터 줄여 화면 안에 유지. 축소 상태는 기존처럼 회전하지 않는다. 확대 상태의 강조 카드는 회전 0도를 유지한다.
- `Assets/Scripts/Battle/BattlePresenter.cs`: 커진 확대 손패가 화면 아래로 잘리지 않도록 중심을 위로 조정. 축소 손패는 우측 하단 유지.
- `Assets/Prefabs/Battle/BattleScreen.prefab`: 초상화 1.5배 확대, 수평 중심 유지. 상대 초상화는 위쪽 화면 안으로 맞추고 상대 필드를 20 UI 단위 내려 겹침 방지.
- `Assets/Scripts/Battle/BattleInteractionController.cs`: 공격 드래그의 마우스가 상대 초상화 Rect의 하단 Y 이상이면 화면 전체 가로 폭에서 직접 공격 후보로 인식. 해당 구간에서는 화살표가 초상화 중앙을 향한다.

직접 공격은 기존처럼 상대 필드가 비어 있고 소스 카드에 서버 제공 `CanDirectAttack`이 있을 때만 제출한다. 공격 단계/권한은 기존 CanAct와 서버가 검증한다. 카드 간 공격, 효과 대상 클릭, 소환 드롭 판정, GameState와 서버 규칙은 변경하지 않았다.

거대한 투명 Raycast 오브젝트를 추가하지 않고 실제 포인터 위치를 판정하므로 다른 UI를 가리지 않는다. 하단 Y는 커진 초상화의 월드 코너를 해당 Canvas의 화면 좌표로 변환해 구한다. 1920×1080 Overlay 기준 Y=840이며 고정 픽셀 840을 코드에 박아두지 않았다. 화면 바깥 드래그는 직접 공격으로 처리하지 않는다.

검증은 별도 복사본 `/tmp/ProjectC-BattlefieldValidation`에서 수행했다. Unity 컴파일 성공 및 PlayMode 3개 성공:

1. 기존 전장 씬 검증: Overlay Raycast, 확대/호버 선택 변경/축소, 양쪽 필드. 복사본에만 크기 수치 검증을 추가해 0.57/1.29와 양쪽 초상화 1.5를 확인했다.
2. 기존 실제 서버 전투 검증: 무효 드롭 복귀, 소환, 카드 공격, 페이즈, 재접속.
3. 복사본에만 둔 추가 실제 서버 시나리오: 상대 필드가 있을 때 기준선 위 드롭 거절, 필드가 비었어도 기준선 아래 드롭 거절, 초상화와 가로로 떨어진 X=화면 너비 10%·하단 Y+5에서 직접 공격 성공 및 서버 Life 감소 확인. Legacy fixture는 시작 Life=3이므로 해당 공격 후 승리 상태도 확인했다.

원본 테스트 소스는 수정하지 않았다. 임시 추가 시나리오는 기록용으로 `Artifacts/LargerHandAndPortraits/Validation/WideDirectAttackPlayModeTests.cs.txt`에 보관한다. 전체 테스트 묶음은 실행하지 않았다.

1920×1080 실제 Game View 캡처는 `Artifacts/LargerHandAndPortraits/Compact-1920.png`, `Expanded-1920.png`, `Online-1920.png`이다. Compact/Expanded는 공개 카드 상태를 주입한 표시 검증, Online은 실제 서버에서 직접 공격 후 화면이다. 원본 에디터를 닫거나 사용자 파일을 되돌리지 않았다.

macOS Development Player 클린 빌드도 성공했으며 `Build/Multiplayer/ProjectC.Client.app`을 갱신했다. 전장 전용 UniversalRenderer 설정을 포함한다. 이번 크기 변경의 캡처는 Game View이며 새 Player 화면을 별도로 캡처했다고 주장하지 않는다.

## 선택/호버 카드 크기 수정

확대 손패 기본 Scale이 1.29가 되었는데 CardWidget은 호버 카드의 최종 크기를 1.04로 고정하고 있었다. `CardWidget.cs`에서 절대 크기 보정을 제거하고 Face에 기본 레이아웃 크기의 1.2배를 곱하도록 수정했다. 따라서 현재 선택/호버 표시 크기는 1.548이며 주변 카드보다 20% 크다. 선택만 유지된 경우에도 적용하고, 축소 시에는 기존 크기로 돌아간다. 필드 카드의 기존 1.035배 호버는 유지한다.

복사본의 전장 씬 검증에 호버/커서 이탈 후 선택 유지/축소 복귀의 실제 Face 배율 검증을 추가했고, 실제 서버 소환·공격·재접속을 포함한 2개 검증이 성공했다. 원본 테스트 파일은 그대로이며 결과와 1920×1080 Game View 캡처는 `Artifacts/HandFocusFix/`에 있다.

선택 크기 수정 후 macOS 클린 빌드도 성공했다. 실행 중일 수 있는 기존 앱을 덮어쓰지 않고 `Build/Multiplayer/ProjectC.Client-FocusFix.app`으로 저장했다.
