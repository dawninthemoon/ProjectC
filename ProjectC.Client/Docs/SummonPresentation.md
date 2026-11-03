# 소환 연출 1차 구현

2026-11-03. 첨부 `baekgwi_battle_presentation_spec.md`의 C04를 참고했다. 이번 구현은 소환과 재사용할 UI VFX 기반에 한정한다. 문서의 신규 Director/별도 게임 상태/쇼케이스/공격·피해·파괴·현현 연출은 추가하지 않았다.

## 화면 동작

기본 길이는 **0.66초**이며 기존 `BattleVisualSettings`의 Summon Duration(0.5~0.8) 하나로 조절한다. 각 단계는 겹쳐 재생된다.

| 기본 시간 | 연출 |
| --- | --- |
| 0~0.26초 | 주변 8개 영적 광점이 소환 슬롯 중앙으로 모인다. 중심 청록빛이 따뜻한 금빛으로 바뀐다. |
| 0.12~0.59초 | 청록색 에너지 링이 바닥 방향으로 확장하며 사라진다. |
| 0.25~0.50초 | 짧은 황금빛 섬광과 바깥으로 퍼지는 광점. 전체 화면 플래시나 Bloom은 없다. |
| 0.25~0.54초 | 카드 alpha 0→1, 크기 0.72→1로 등장한다. 기존 일러스트·프레임·공격력·체력은 그대로다. |
| 0.54~0.66초 | 정상 크기로 정착하고 잔광이 끝난다. 입력/Raycast와 레이아웃 제어를 기존 카드 뷰로 돌려준다. |

대기 중인 기존 배치가 많으면 배치당 +0.2의 재생 속도를 적용하며 최대 2배다. 일반 소환은 0.66초, 밀린 연출은 최단 0.33초다. 같은 이벤트 묶음에서 연속으로 필드에 들어온 카드는 동시에 재생할 수 있다. 뒤따르는 스탯 변경·제거 등 상태 변화는 앞의 소환이 완료된 뒤 반영한다.

## 기존 구조와 상태 수렴

기존 `BattlePresenter._batches`와 `BattlePresentationState`를 그대로 사용한다. 서버 DTO와 게임 규칙, 패킷, 전송 순서는 수정하지 않았다. 별도의 상태 관리/게임 규칙 추론/명령 큐를 만들지 않았다.

소환 시작 기준은 서버의 기존 **CardMoved(4), ToZone=Field(2), FromZone≠Field**다. 표시 투영에 필드 진입을 적용하고 같은 Unity 프레임 안에서 카드를 비행/등장 상태로 바꿔 완성된 카드가 먼저 번쩍 나타나는 일을 막는다. 바로 뒤의 CardSummoned(11)는 기존 소환 사운드와 동기화하며 두 번째 VFX를 만들지 않는다. 기존 `SUMMONED` 설명 팝업 대신 슬롯에서 직접 소환을 보여준다.

재생 중에도 서버 이벤트와 스냅샷은 기존 큐에 수신된다. 소환 메타데이터 외의 다음 상태 변경은 ActiveSummons가 0이 될 때까지 기다린다. 마지막에 해당 배치의 서버 스냅샷으로 기존 투영을 다시 맞춘다. 서버의 선택 창 때문에 명령 확정이 보류된 경우에는 확정 이벤트를 받기 전 소환하지 않는다.

**Space** 또는 `BattlePresenter.SkipPresentation()`으로 현재/대기 연출을 스킵할 수 있다. 재접속과 같은 정리 경로를 재사용하여 코루틴·VFX·드로우를 중단하고, 최신 `Client.State`를 표시 투영에 복사한 뒤 즉시 다시 그린다. 타이머 실행 여부는 최신 서버 상태에서 복구한다. 비활성화/씬 종료/재동기화에서도 같은 정리 경로가 작동한다. 카드의 alpha, Scale, IsInFlight, Raycast, 슬롯 위치와 풀 활성 상태를 복원한다.

## VFX와 렌더링

현재 전장은 URP 17.0.4, 카드는 Screen Space Overlay다. 추가 카메라나 World Space 카드 전환 없이 `Table/BattleVfxLayer`를 양쪽 필드 카드보다 먼저 그린다. 따라서 효과는 3D 전장 위·카드 뒤에 위치한다. 레이어 CanvasGroup과 모든 RawImage는 입력을 차단하지 않는다.

Magic Effects FREE(`Assets/Hovl Studio/Magic effects pack/`)의 다음 텍스처를 원본 수정 없이 참조한다.

| 텍스처 | 용도 |
| --- | --- |
| `Textures/Circle2.png` | 확장 링 |
| `Textures/GlowFree1.png` | 집약되는 중심 광원 표현 |
| `Textures/FlashFree1.png` | 짧은 방사형 섬광 |
| `Textures/Flare.png` | 모였다 흩어지는 광점 8개 |

기존 패키지의 월드 Particle System 프리팹을 Overlay 위에 억지로 렌더링하지 않고, 필요한 마스크만 UI용 Additive 셰이더에 재사용했다. 이는 UI 입자 표현이며 Unity ParticleSystem이나 실제 광원을 추가한 것이 아니다. URP 호환 UI 셰이더는 stencil/Clip Rect를 지원하고 하나의 공유 머티리얼을 사용한다. 개별 머티리얼을 매 프레임 생성하지 않는다. 패키지 에셋은 원래 배포 라이선스의 적용을 받으며 CC0라고 재표기하지 않았다. Shadowverse 리소스는 사용하지 않았다.

`BattleVfxPlayer`는 최대 4개 Cue를 처음 사용할 때만 만들고 이후 재사용한다. Cue는 정규화된 진행률을 샘플링하며 게임 상태를 소유하지 않는다. 풀 반환/전체 중단 기능은 추후 UI VFX에도 재사용할 수 있다. 용량 초과나 누락 시에도 카드 등장/정착과 서버 상태 반영은 계속된다.

## 변경 파일

- `Assets/Scripts/Battle/BattlePresenter.cs`: 기존 큐에 필드 진입 연출 연결, 다음 상태 변화 대기, 스킵/예외 복구.
- `BattleEffectsView.cs`: 소환 코루틴, 카드 등장/정착, 기존 비행 정리 재사용.
- `BattlePresentationState.cs`: 상태를 계산하지 않는 필드 진입 이벤트 분류 헬퍼.
- `BattleInteractionController.cs`: Space 스킵 입력.
- `BattleVisualSettings.cs`, `Assets/Prefabs/Battle/BattleVisualSettings.asset`: 소환 길이 0.66초. 기존 Ease 기본값도 명시적으로 직렬화됐다.
- `CardTableLayout.cs`: 요청대로 손패의 슬롯 회전 0도. 기존 손패 위치·크기·확대/축소·커브 위치·선택 강조는 유지했다. 드로우 시스템은 재구현하지 않았다.
- 신규 `BattleVfxPlayer.cs`, `BattleVfxCue.cs`와 meta.
- `Assets/Presentation/Vfx/SummonSpirit.prefab`, `SpiritAdditive.mat`, `SpiritAdditive.shader` 및 폴더/meta.
- `Assets/Prefabs/Battle/BattleScreen.prefab`: 입력을 막지 않는 VFX 레이어와 프리팹 참조. 수동 Inspector 연결 불필요.
- 신규 `Assets/Tests/PlayMode/SummonPresentationPlayModeTests.cs`와 meta.
- 본 문서와 `Artifacts/SummonPresentation/`.

사용자가 별도로 수정한 Editor 제작 도구와 임포트 패키지 원본은 되돌리거나 편집하지 않았다.

## 검증

열린 원본 에디터를 방해하지 않도록 `/tmp/ProjectC-BattlefieldValidation` 복사본에서 실행하고 필요한 소스/프리팹/머티리얼만 원본에 반영했다.

신규 PlayMode **5개 모두 성공**:

1. 세 번의 빠른 소환 중 나중 스탯 이벤트 수신: 권위 상태는 즉시 갱신되고 표시 투영은 순서대로 반영.
2. 한 묶음의 동시 소환 3개와 뒤따르는 제거: 제거가 등장 완료를 기다리며 최종 필드·묘지가 서버 스냅샷과 일치.
3. 대기 배치를 포함한 스킵 및 중간 비활성화/재활성화: 카드와 VFX 상태 복구.
4. 100회 반복: 풀 오브젝트 4개 유지, 활성 잔류 없음. VFX 레이어 누락에도 카드 정상 복원.
5. 실제 C++ 서버 + 두 클라이언트 + `Data/HyakkiYagyo`: 실제 소환, 필요한 서버 선택 응답, 1920×1080 Game View의 단계별 캡처와 소환 시간 측정. 카드 스탯은 서버 값과 일치.

드로우·전장 Raycast/손패 선택·실제 서버 드래그 소환/공격/재접속·효과음 관련 기존 검증 **22개 중 21개 성공, 1개 실패**였다. 실패는 `BattleAudioPlayModeTests.RealServerEffectsAndTransformationProduceTheirCues`가 재사용하는 오래된 입력 시나리오에서 새 소환이 끝나기 전에 카드 선택을 호출해 ContextAction을 찾지 못한 경우다. 테스트 원본은 수정하지 않았다. 복사본에만 `yield return Until(() => !ui.PresentationBusy)` 한 줄을 선택 전에 넣어 같은 효과 대상/현현/사운드 시나리오를 실행하면 성공했다. `Validation/wait-probe.xml`에 기록했다. 이 기다림을 원본 테스트에 반영하는 작업은 남아 있다.

전체 PlayMode 묶음은 실행하지 않았다. 테스트 프레임 캡처는 검증 코드에만 있으며 실제 게임의 매 프레임 동작에는 포함되지 않는다.

## 화면 자료와 플레이에서 확인할 부분

- `Summon-Preview.mp4`: 실제 C++ 서버 검증 Game View 프레임을 시간 간격대로 연결한 무음 영상.
- `01-Gather-1920.png` ~ `04-Settled-1920.png`: 같은 실제 서버 소환의 단계별 화면.
- `Timing.txt`: 실측 소환 길이와 캡처 프레임 수.
- `Validation/summon-verified.xml`: 신규 5개 결과.
- `Validation/final-tests.xml`: 앞서 실행한 관련 26개 결과(신규 4개 포함, 기존 시나리오 1개 실패).

실제 플레이에서 잔광의 밝기·색감, 양쪽 필드가 가득 찼을 때 인접 카드와의 시각적 간격, 1080p 이외 해상도 및 저사양 장치의 성능은 추가 확인/취향 조정할 수 있다. 추가 공격·피해·파괴·현현·쇼케이스 구현은 포함하지 않는다.

## macOS Player 확인과 빌드

실제 macOS Metal Player에서 별도의 공개 이벤트 fixture로 링/섬광/카드 정착을 재생했다. shader supported=True, 종료 후 활성 효과 0이며 Player 측 소환 실측은 약 0.73초였다. `PlayerProof/`의 캡처는 합성 공개 이벤트를 넣은 실제 실행 파일이고, 위 Game View 영상/캡처는 실제 C++ 서버와의 대전이다. 둘을 구분해 기록한다. Player fixture의 초기 턴 배너는 기존 연출이다.

검증 전용 컴포넌트는 복사본에만 넣었으며 제거한 뒤 동일 소스·씬·VFX로 일반 macOS Development Player를 다시 클린 빌드했다. 정상 실행용 앱은 `Build/Multiplayer/ProjectC.Client-Summon.app`이며 기존 앱을 덮어쓰지 않는다. 원본 프로젝트에서 직접 다시 빌드해도 저장된 프리팹 연결이 사용된다. Windows Player는 이번에 실행하지 않았다.
