# 3D 전장 모델 적용 — 1차

2026-11-03, Unity 6000.0.81f1 / URP 17.0.4. 이 단계는 모델·머티리얼·조명·카메라와 배경 가림 처리만 적용한다. 기존 카드/손패 프리팹, 런타임 C# 코드, 레이아웃, 애니메이션, GameState와 서버 코드는 변경하지 않았다. 적용 전부터 있던 BattleScene의 활성화 오버라이드도 보존했다.

## 변경 파일

- `Assets/Scenes/BattleScene.unity`: 전장 프리팹 인스턴스, 기존 Main Camera의 전장 구도, Scene 조명 환경, UI 배경의 투명도 오버라이드.
- `Assets/Art/Environment/BattleField/YokaiShrineStage/YokaiShrineStage.prefab`: 원본 FBX 인스턴스와 조명 두 개. Collider/Rigidbody와 별도 런타임 컴포넌트는 없다.
- 같은 폴더의 `ShrineStone.mat`, `ShrineEmission.png`, `ShrineMetallicSmoothness.png` 및 `.meta`: URP 머티리얼과 비파괴 파생 텍스처.
- 제공된 FBX의 `.meta`: 카메라·조명·애니메이션 import 비활성화, Collider 생성 비활성화. 원본 FBX의 형상/UV/바이트는 보존했다.
- 원본 basecolor/metallic/roughness/normal 텍스처의 `.meta`: sRGB/선형/Normal Map 구분과 mipmap, 최대 2048, 압축 설정. 원본 이미지 바이트는 보존했다.
- `Assets/Tests/PlayMode/BattlefieldScenePlayModeTests.cs`, `BattlefieldOnlinePlayModeTests.cs` 및 `.meta`: 실제 씬 검증과 실제 서버 입력 검증. 기존 테스트는 수정하지 않았다.
- `Artifacts/Battlefield3D/`: Game View 캡처 및 `Validation/` 실행 기록.

에디터 제작 스크립트는 검증 복사본에서만 사용했다. 일반 실행이나 배포에 추가 도구 실행 또는 수동 연결이 필요 없다.

## 적용 방식과 좌표

전장은 별도의 `YokaiShrineStage` 프리팹으로 관리한다. FBX의 기존 import 회전 X=-90°가 이미 Z-up 모델을 수평 XZ 평면으로 변환하므로 추가 회전은 하지 않았다. 균일 스케일은 22이고, 모델 인스턴스의 위치는 대략 `(0, -2.1694, 0)`이다. 원래 비율을 유지하며 상단 외곽 높이를 Y=0.4에 맞췄다. 추후 장식은 이 프리팹 아래에 추가할 수 있다.

기존 Main Camera는 Perspective, FOV=34°, 위치 `(0, 24, -18)`, 회전 약 `(55.04, 0, 0)`, Near=0.1, Far=100이다. `(0, 0.4, -1.5)`를 향한다. 1920×1080의 실제 Game View 캡처에서 석판 내부가 기존 상대/자기 필드 카드 영역을 포함하도록 조정했다. 보조 카메라, RenderTexture 배경이나 카메라 스택은 추가하지 않았다.

카드는 기존 Screen Space Overlay Canvas를 그대로 사용한다. 필드 RectTransform, 카드 크기·간격, 좌표 변환, GraphicRaycaster, 드래그의 30% 판정은 변경하지 않았다. 이 단계에서 카드를 World Space로 옮기거나 실제 바닥에 눕히지 않는다. 모델과 카드 사이의 원근 차이는 후속 범위다.

모델을 가리는 Background/Table/Battlefield/Midline Image의 alpha만 씬 인스턴스에서 0으로 설정했다. 컴포넌트나 입력 영역을 비활성화하지 않았다. OwnFieldDropZone은 컨트롤러가 런타임에 색을 덮어쓰므로 해당 오브젝트에만 alpha=0.04인 CanvasGroup을 추가했다. 입력과 Raycast는 계속 켜져 있고, 기존 소환 후보 틴트는 약하게 남는다. 카드, 영웅, HUD, 상세/선택 UI에는 이 CanvasGroup이 적용되지 않는다.

## 머티리얼과 조명

`Universal Render Pipeline/Lit`를 사용한다. 원본 basecolor를 sRGB로, metallic/roughness를 선형 데이터로, normal을 Normal Map으로 가져온다. 별도 파생 맵에서 R=원본 metallic, A=1−원본 roughness를 패킹해 URP의 Metallic/Smoothness 슬롯에 연결했다. 노멀 강도는 0.65, Smoothness 배율은 0.7이다.

Emission 맵은 원본 basecolor에서 청록색 픽셀만 추출했다. 석재 전체를 밝히지 않고 원래 문양을 약하게 유지한다. 별도 Bloom, Volume 또는 VFX를 추가하지 않았다.

- Moonlight: 차가운 청색 Directional Light, intensity 1.15, 부드러운 그림자 strength 0.35.
- SpiritFill: 청록색 Directional Light, intensity 0.28, 그림자 없음.
- Scene ambient: Flat `(0.19, 0.23, 0.30)`, reflection intensity 0.35, Skybox 없음.

Render Pipeline/Quality의 전역 설정은 바꾸지 않았다. 조명은 전장 프리팹과 BattleScene에 한정된다.

## 실행한 검증

원본 프로젝트가 Unity에 열려 있어 `/tmp/ProjectC-BattlefieldValidation`에 Assets/Packages/ProjectSettings/Library를 복사해 검증했다. 원본 씬과 에셋을 반영할 때 기존 런타임 C#·UI 프리팹의 바이트가 복사본과 동일하며 원본 FBX/이미지 바이트도 동일한지 확인했다.

1. 적용 전 기존 PlayMode 전체: **69개 중 60개 성공, 9개 실패**.
2. 실제 새 BattleScene 검증 2개: **2/2 성공**. 씬 로드, Perspective 카메라, 정상 URP Shader/4종 텍스처, Collider/Rigidbody 부재, Overlay 유지, 양쪽 필드 표시, 실제 UI Raycast, 손패 확대·호버 선택 이동·배경 클릭 축소를 확인했다.
3. 위 2개 중 온라인 검증은 실제 C++ WebSocket 서버와 두 클라이언트를 사용한다. 무효 드롭의 손패 복귀 및 Sequence 유지, 유효한 드래그 소환, 공격 드래그와 서버 피해 반영, UI 페이즈 전환, 재접속 스냅샷 복구를 확인했다.
4. 적용 후 PlayMode 전체: **71개 중 62개 성공, 9개 실패**. 기존 69개 각각의 성공/실패 결과가 적용 전과 동일하다. 새 회귀는 발견하지 않았다. 기존 실패는 고치거나 기대값을 바꾸지 않았다.
5. Unity C# 컴파일과 FBX/텍스처 import 완료. 실제 그래픽 장치로 렌더링했고 Shader.isSupported도 확인했다. 별도 C++ 변경이나 빌드는 수행하지 않았다.

실행 명령의 핵심 옵션:

```sh
Unity -batchmode -projectPath /tmp/ProjectC-BattlefieldValidation \
  -runTests -testPlatform PlayMode \
  -projectcServerPath /Users/hanjunseo/Dev/ProjectC/build/macos-server/ProjectCNetworkServer \
  -projectcInteractionData /Users/hanjunseo/Dev/ProjectC/ProjectC.Server/Tests/InteractionData
```

전체 XML: `Artifacts/Battlefield3D/Validation/baseline-playmode.xml`, `final-playmode.xml`. 별도 씬/온라인 실행: `stage-integration.xml`. 테스트별 비교와 실패 메시지: `summary.json`. Unity log도 같은 폴더에 보관한다.

### 변경 전부터 남아 있는 실패

- BattlePrefabPlayModeTests.AuthoredPrefabHoverSelectionHiddenCardsAndFeedback: hover scale 기대값.
- BattleUxPlayModeTests.ChoicePreviewSingleReplacementAndMultipleSelectionRemainTemporary: 정보/선택 상태 기대값.
- BattleUxPlayModeTests.OverlapUsesRotatedAndScaledCardAreaInEveryCanvasMode: 겹침 비율 fixture 기대값.
- CardLocalizationTests.NewFactionsUsePublishedStatsTextCostsAndExistingArtworkFallback: 설명 템플릿과 치환된 텍스트 기대값.
- CardPresentationPlayModeTests.BattleArtworkBadgesTransformSelectionAndHover: 이전 카드 마스크 컴포넌트 기대값.
- FrontEndPlayModeTests.AutomaticQueueMulliganReconnectAndTimeoutStartsBattle: 멀리건 종료 직후 화면 상태 기대값.
- FrontEndPlayModeTests.MulliganKeepClearsSelectionAndStarts, MulliganSelectConfirmReconnectAndStart: 이전 확인 버튼 문구 기대값.
- MultiplayerPlayModeTests.FourClientsTwoMatchesPlayRematchAndReconnect: 이전 턴 문구 기대값.

## 캡처와 한계

모두 실제 Play Mode의 ScreenCapture로 저장한 **1920×1080 Game View**다. 기존 Canvas를 캡처용 World/Camera Space로 바꾸지 않았다.

- `Shrine-Compact-1920.png`: 실제 3D 씬과 UI, 합성 공개 카드 상태를 사용한 축소 손패.
- `Shrine-Expanded-1920.png`: 같은 상태에서 손패를 클릭하고 다른 카드로 호버한 화면.
- `Shrine-Model-1920.png`: 같은 카메라에서 UI를 숨긴 모델/라이팅 화면.
- `Shrine-Online-1920.png`: 실제 서버의 Legacy 테스트 덱으로 소환·공격한 화면. 이 덱의 일부 카드 그림은 기존 fallback이다.

전체 테스트는 실제 UI 이벤트와 서버를 사용하지만 OS 마우스로 사람이 플레이한 검증은 아니다. 멀리건의 일부 기존 테스트는 위 기대값에서 중단되므로 멀리건 전체 검증 성공으로 보고하지 않는다. 초광폭/세로 화면과 장시간 실플레이, 저사양 기기의 GPU 성능 측정은 수행하지 않았다.

제공 모델은 약 388,506 triangles의 단일 메쉬다. 원본 형태를 보존했고 이번에는 감면/LOD를 만들지 않았다. 후속 작업은 모델 LOD·성능 측정, 카드와 바닥의 원근/접지감, 별도 제공될 신사 장식, 기존 테스트 기대값 정비다. 이 단계에서 카드 배치나 애니메이션을 바꾸지 않았다.
