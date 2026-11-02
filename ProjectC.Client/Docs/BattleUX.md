# 전투 UI/UX 변경 — 2026-11-03

## 조사 결과와 경계

- 실제 직접 공격 오류는 `GameRule:DirectAttackRestricted:Battle:0`이다. GameCore는 공격 횟수를 `CardInstance.HasAttackedThisTurn`에 카드별로 저장한다. 다른 몬스터가 공격하면 모든 몬스터를 금지하는 로직은 발견되지 않았다.
- `CardMutations.cpp`의 `PreventDirectAttackThisTurn` 처리가 부활 대상 카드에 `DirectAttackRestrictedTurn`을 기록한다. 백귀야행 카드 1103과 2005의 명시적인 효과다. 이 제한을 삭제하지 않았다. 기존 `CanAttack`은 전투 준비 상태만 표시하여 이 카드를 직접 공격 가능한 것처럼 보일 수 있었다.
- 페이즈는 기존 Main/Battle/End뿐이었다. Main 2를 정식 GameCore 페이즈로 추가했다. 기존 wire 값 Main=0, Battle=1, End=2는 보존하고 Main2=3을 추가했다.
- 멀리건 처리·개인화 스냅샷·재접속 복원·자동 유지 기능은 이미 있었지만 백귀야행 서버 덱 설정에도 EnableMulligan이 없었다. 백귀야행 프로필에서 활성화했다. 기본/Legacy 테스트 규칙은 유지한다.
- 기존 소환 명령은 `PlayCard { CardId }`이고 서버 필드는 카드 벡터다. 독립적인 슬롯 번호나 빈 슬롯 객체는 없다. 따라서 슬롯 ID나 클라이언트 전용 배치 상태를 발명하지 않고 기존 유효 아군 필드 영역과의 겹침을 판정한다.
- 일반 턴의 남은 시간/종료 시각 DTO는 없었다. 서버가 설정하는 표시용 턴 시간을 추가했다. 기존에 없는 강제 턴 종료 규칙은 추가하지 않았다.

## 구현

| 영역 | 파일 | 변경 이유 |
|---|---|---|
| 직접 공격 | 서버 `Network/MatchSession.cpp`, 클라이언트 `Network/Protocol.cs`, `BattleInteractionController.cs` | 서버가 `CanDirectAttack`을 별도로 계산한다. 직접 공격만 제한된 카드는 몬스터 공격을 할 수 있지만, 상대 필드가 비었으면 공격 가능한 것처럼 강조하지 않는다. 클라이언트는 이 권한으로 직접 공격 후보를 표시하고 서버가 최종 검증한다. |
| 페이즈 | 서버 `GameState.hpp`, `GameState.cpp`, `GameFlow.cpp`, `GameInitializer.cpp`, `CardEffects.cpp` | Main 1 → Battle → Main 2 → End. 두 Main에서 기존 메인 행동을 허용한다. 소환/효과 횟수는 페이즈 전환 때 초기화하지 않는다. |
| 고정 정보 패널 | `BattlePresenter.cs`, `CardDetailView.cs`, `BattleScreen.prefab` | 왼쪽 300px 예약 영역에 클릭 정보를 고정한다. 같은 카드 재클릭, Hover 종료, 입력 정리, 드래그와 일반 버튼은 정보를 닫지 않는다. 빈 필드 클릭은 정보만 숨긴다. |
| 후보 미리보기/선택 | `EffectTargetPicker.cs`, `BattleInteractionController.cs` | 클릭은 미리보기와 UI 임시 선택, 확인 버튼은 제출이다. 단일 선택은 즉시 교체하고 복수 선택은 제한 내에서 토글한다. 서버 선택 토큰과 기존 허가된 Card DTO만 사용한다. |
| 화면 겹침 | `ScreenRectOverlap.cs`, `BattleInteractionController.cs` | 각 Canvas의 카메라로 실제 카드/필드 사각형을 화면에 투영하고 볼록 다각형 교집합을 계산한다. 카드 면적의 30% 이상인 유효 영역 중 가장 큰 것을 선택한다. 회전·확대된 카드에도 경계 상자 근사를 사용하지 않는다. |
| 문구 | `BattleText.cs`, `ProjectC_Data - Localize.json`, `ContextActionView.cs` | 다음 페이즈, 효과 발동, 유형/스탯, 후보 안내와 시간 문구를 Localize 키로 관리한다. `effect_{DefinitionId}_{EffectId}_name`이 있으면 사용하고 없으면 `ui.effect.activate`로 표시한다. 내부 EffectId는 그대로다. |
| 턴 시계 | 서버 `MatchSession.*`, `MatchManager.*`, `main.cpp`, 클라이언트 `MultiplayerDebugScreen.cs`, `BattlePresenter.cs` | 서버의 시간 쌍으로 보정한 뒤 단조 로컬 시계로 매 프레임 표시한다. 매초 요청하지 않는다. 턴 변경·재접속·재동기화 때 다시 보정한다. |

기존 EndTurn 명령의 서버 측 빠른 종료 호환 동작은 유지한다. 이 명령도 후보 상태에서 필요한 모든 AdvancePhase/EndTurn/StartTurn 명령을 검증하고 성공할 때만 커밋한다. 새 UI는 기존 EndTurn 버튼 한 곳에서 단계별 AdvancePhase, 마지막 EndTurn을 제출한다. 효과 선택·요청 대기·드래그 중에는 버튼을 비활성화한다. 응전/체인 시스템은 추가하지 않았다.

후보 버튼과 효과 버튼은 풀에서 재사용한다. 정보 패널 일러스트는 기존 CardArtProvider/AspectFillImage와 RectMask2D를 재사용한다. 카드 프레임, 배지, 손패 정렬과 드로우 투영 모델은 변경하지 않는다.

## 프로토콜과 실행 설정

프로토콜 버전은 2를 유지한다. 추가 필드는 다음과 같다. 클라이언트와 서버는 함께 갱신해야 한다.

- 카드 DTO: `CanDirectAttack`.
- StateSnapshot envelope: `ServerTimeUnixMs`.
- StateSnapshot payload: `TurnEndsAtUnixMs`, `TurnTimerRunning`.
- Phase 값: `3`은 Main 2. 기존 `0`, `1`, `2`의 의미는 그대로다.

`--turn-duration 180`으로 표시 시간을 설정한다. 기본 180초이며 멀리건 완료 이후 시작한다. 표시는 `mm:ss`이고 0에 도달해도 클라이언트가 명령을 만들지 않는다. 이 변경은 표시용 시계이며, 시간 초과에 의한 강제 턴 종료는 구현하지 않는다. 기존 경기 정리 타임아웃과 멀리건 타임아웃 정책은 유지한다.

백귀야행 온라인 실행 예:

```powershell
..\build\Debug\ProjectCNetworkServer.exe --data ..\ProjectC.Server\Data\HyakkiYagyo --turn-duration 180
```

멀리건은 서버 설정이다. 기본 소규모 테스트 카탈로그에서 클라이언트가 임의로 켜지 않는다. 백귀야행에서는 기본 활성화되며 `--mulligan off`로 명시적으로 끌 수 있다. 기존 개인 덱·효과 테스트의 준비 단계를 고정하려는 fixture에는 명시적으로 off를 지정했다.

## Prefab 및 수동 확인

`UpgradeBattleUxAssets`를 실행한 결과가 `BattleScreen.prefab`에 저장되어 있다. 일반 사용에 추가 에디터 설정은 필요 없다. 기존 직렬화 필드와 참조를 보존하며 기존 BattlePhase 버튼은 호환성을 위해 비활성 상태로 유지한다.

- `CardInfoSpace/CardInspector`: 고정 정보 내용과 ArtworkMask, 이름, 유형/스탯, 스크롤 본문.
- `OwnFieldDropZone`: 정보 패널을 침범하지 않도록 필드와 함께 이동한 실제 드롭 영역.
- `EndTurn`: 한 곳의 다음 페이즈 버튼.
- `TurnTimer`: 시간 표시.

`ProjectC/Upgrade Battle UX`는 명시적 배치 마이그레이션 도구다. 수동 배치 후 재실행하면 해당 배치 값이 도구의 값으로 재설정된다.

자동 검증 결과는 `Artifacts/BattleUX/ctest.xml`, `Artifacts/BattleUX/playmode.xml`에 기록한다. `BattleUX-Inspector.png`와 `BattleUX-ChoicePreview.png`는 PlayMode에서 실제 Canvas를 렌더링한 결과다.

## 실행한 검증

- `cmake --build ../build --config Debug -j 6`: 전체 C++ 타깃 빌드 성공.
- `ctest --test-dir ../build -C Debug --output-on-failure`: 8/8 성공. GameStateTests, NetworkTests, CardDataTests, CardDataExportCheck, WebSocketIntegration, ContentWebSocketIntegration, PersonalDeckWebSocketIntegration, MulliganWebSocketIntegration.
- Unity 6000.0.81f1 실제 PlayMode 선택 실행: 51/51 성공. BattleUxPlayModeTests, DrawPresentationPlayModeTests, CardLocalizationTests, CardPresentationPlayModeTests, BattlePrefabPlayModeTests, MultiplayerPlayModeTests 전체와 FrontEnd의 MulliganSelectConfirmReconnectAndStart, MulliganKeepClearsSelectionAndStarts, AutomaticQueueMulliganReconnectAndTimeoutStartsBattle.
- 실제 WebSocket 소환 테스트에서는 포인터가 필드 밖에 있어도 카드가 30% 이상 겹치는 위치에서 소환을 성공시킨다. 기존 무효 드롭, 서버가 거절한 소환의 복귀, 네 클라이언트/두 경기 격리, 드로우, 현현, 공격, 재대전 검사도 유지했다.
- C#/C++/문서/Python 변경에 대한 `git diff --check` 통과. C++ 변경 범위는 저장소 clang-format 설정으로 정리했다.

최종 실행에서 남은 테스트 실패는 없다. 새 Main 2 단계 때문에 필요한 페이즈 명령을 기존 테스트 흐름에 추가했고, 기존 검증은 삭제하지 않았다. 기존 테스트가 현재 Prefab에서 없는 LobbyOverlay를 직접 참조하던 문제는 null을 허용하는 동일 목적의 검사로 수정했다. 제거된 카드 상태 문구/호버 정보 표시를 기대하던 과거 UI 검사는 현재 카드 UI 및 클릭 고정 계약에 맞췄다. 초기에 발생한 서버 경로 인자 누락과 잘못된 재접속 테스트 순서도 테스트 실행/fixture 문제로 구분해 수정했다.

OS 마우스로 장시간 플레이하는 검증, 고지연·패킷 손실 환경, 초광폭·세로 모니터의 수동 시각 검증은 수행하지 않았다. 자동 테스트의 포인터/드래그와 세 Canvas Render Mode 검증을 실제 사용자 수동 플레이와 동일하다고 주장하지 않는다.
