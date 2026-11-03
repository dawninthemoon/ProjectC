# 전투 화면 주변 석재 바닥

2026-11-03. Unity 6000.0.81f1 / URP 17.0.4 (`GraphicsSettings`의 기존 URP Asset 유지).

## 변경 파일

- `Assets/Art/Environment/BattleField/YokaiShrine/Presentation/YokaiShrineStage.prefab`: 기존 Geometry와 Lighting을 유지하고 `SurroundingStoneFloor` MeshRenderer를 마지막 자식으로 추가.
- `Assets/Art/Environment/BattleField/SurroundingStone/SurroundingStone.mat`: URP Lit 석재 PBR 머티리얼.
- 같은 폴더의 `FloorMetallicOcclusionSmoothness.png`: 원본으로부터 생성한 4096×4096 RGBA 채널 맵.
- 같은 폴더의 `PackingReport.json`: 변환식, 원본 SHA256, 거칠기 통계와 노멀 방향 검사값.
- 위 폴더/파일의 `.meta`.
- `Assets/Art/Environment/Textures/floor_pattern_01_4k/floor_pattern_01_diff_4k.jpg.meta`: Default/sRGB, Repeat, mipmap, Trilinear, Aniso 8.
- 같은 폴더의 `floor_pattern_01_nor_gl_4k.exr.meta`: Normal Map, Linear, GL +Y 유지, Green 반전 없음, Repeat/mipmap/Aniso 8.
- `Tools/PackStoneFloorMaps.py`: 오프라인 채널 변환 재현용 Python 스크립트. Unity 실행에 Python이나 추가 패키지는 필요 없다.
- 본 문서 및 `Artifacts/StoneSurroundFloor/` 캡처/검증 기록.

BattleScene, 카메라, 기존 3D 모델/머티리얼/조명, 카드·초상화·손패·턴 UI와 게임/서버 코드는 수정하지 않았다. 원본 이미지의 바이트도 변경하지 않았다. 프리팹에 머티리얼을 저장해 수동 연결이 필요 없다.

## 배치와 색상

바닥은 실제 3D Plane이며 XZ 평면에 수평으로 놓인다. 위치 `(0, -0.06, 0)`, 회전 `(0,0,0)`, Plane 스케일 `(4,1,2.6)`으로 월드 크기 **40×26**이다. 기존 모델 최저점 Y=0 바로 아래에 두어 테두리를 덮지 않고 Z-fighting을 피한다. Collider와 Rigidbody는 없으며 UI 입력에 관여하지 않는다. 그림자를 받지만 추가 그림자는 생성하지 않는다.

기존 수직 Orthographic 카메라 `(0,30,0)`, Euler `(90,0,0)`, Size=8.5를 유지한다. 16:9 시야 30.22×17보다 바닥을 넓게 만들어 가장자리까지 채운다.

텍스처 한 반복은 월드 **8×8**이다. 40×26 Plane의 BaseMap Tiling은 `(5,3.25)`, Offset은 `(0.13,0.07)`로 설정했다. 서로 다른 가로/세로 크기에도 돌이 늘어나지 않는다. URP Lit은 동일 BaseMap UV로 Normal과 패킹 맵도 샘플링한다.

BaseColor `(1,0.88,0.74,1)`로 기존 차가운 조명 아래에서 회갈색을 유지하도록 조정했다. Normal 강도 0.65, AO 강도 0.65, Smoothness 배율 1.0이다. 기존 차가운 청회색 게임판과 따뜻한 중간 밝기의 주변 석재가 구분되면서, 테두리의 이끼·석재와 이어진다. 새 광원, Bloom, Emission, 변위는 추가하지 않았다.

## PBR 채널과 Normal Y

| URP Lit 슬롯/채널 | 원본 |
| --- | --- |
| BaseMap RGB | Diffuse JPG, sRGB |
| BumpMap | `nor_gl` EXR, Normal Map importer, +Y |
| MetallicGlossMap R | ARM B (Metallic) |
| OcclusionMap G | ARM R (AO) |
| MetallicGlossMap A | `1 - clamp(Roughness EXR.Y, 0, 1)` |
| 패킹 B | 미사용 0 |

동일한 파생 RGBA 맵을 MetallicGlossMap과 OcclusionMap에 연결했다. 데이터 맵은 Linear이며 Smoothness Source는 Metallic Alpha이다. 원본 ARM G 대신 고정밀 독립 Roughness 맵을 사용했다.

**이 리소스의 Roughness/Bump EXR는 R이 아닌 Y(휘도) 채널에 데이터가 있다.** Unity RGB import에서 읽은 거칠기는 흰색으로 나왔으므로, OpenEXR로 Y 채널을 직접 읽어 패킹했다. 실제 거칠기 평균은 약 0.9503이며 석재에 맞는 낮은 광택을 유지한다. EXR overshoot는 0~1로 제한했다.

GL 노멀의 초록 채널은 뒤집지 않는다. 원본 Bump와 Normal을 256×256으로 평균 축소한 뒤 높이 기울기와 Y 방향을 대조했다. EXR 이미지 행은 아래로, UV V는 위로 증가하므로 image-row 높이 기울기와 GL Normal Y는 같은 부호여야 한다. 상관계수는 **+0.48954**였고, 반전하면 음수가 된다. 실제 Game View에서도 노멀 적용 후 홈/돌 표면을 확인했다. Bump는 이 방향 검사에만 사용하며 Displacement와 별도 AO는 렌더링에 중복 연결하지 않았다.

원본과 파생 PNG는 4K로 보존한다. 런타임 import 상한은 세 맵 모두 2048, Compressed HQ, mipmap/Aniso 8이다. 1080p에서 반복되는 주변 바닥에 필요한 디테일과 메모리를 고려한 설정이며 원본을 다운샘플 저장하지 않는다.

변환을 다시 해야 할 때만 임시 Python 환경에서 실행한다.

```sh
python3 -m venv /tmp/projectc-floor-tools
/tmp/projectc-floor-tools/bin/pip install OpenEXR numpy Pillow
/tmp/projectc-floor-tools/bin/python Tools/PackStoneFloorMaps.py
```

스크립트는 원본을 수정하지 않으며 파생 PNG와 PackingReport만 생성한다. 텍스처는 사용자가 제공한 파일을 사용했으며 별도 외부 리소스는 추가하지 않았다.

## 실제 화면 검증

원본 에디터 작업을 방해하지 않도록 `/tmp/ProjectC-BattlefieldValidation` 복사본에서 동일 에셋/씬으로 검증 후, 위 파일만 원본에 반영했다. 캡처는 실제 Play Mode BattleScene의 **1920×1080 Game View**에서 ScreenCapture로 저장했다. 캡처 이미지를 게임 배경에 사용하지 않는다.

- Unity import/C# 컴파일 완료, URP Lit 텍스처 정상 표시 및 분홍색 셰이더 없음.
- UI를 숨긴 Model 캡처에서 사방에 석재 바닥이 표시되고 게임판 테두리는 그대로 보인다. 전체 및 외곽 픽셀 중 RGB 모두 8 미만인 픽셀은 0개다. 화면 밖으로 이어지는 바닥에 검은 여백이 없다.
- `BattlefieldScenePlayModeTests.SceneBackdropPreservesOverlayRaycastsAndHandSelection`: 성공. 양쪽 카드 5장, 실제 Overlay Raycast, 손패 확대/호버 전환/축소와 기존 탑다운 카메라 검증.
- `BattlefieldOnlinePlayModeTests.SceneAllowsInvalidDropSummonAttackPhaseAndReconnect`: 성공. 실제 C++ 서버와 두 클라이언트로 무효 드롭 복귀, 드래그 소환, 공격/HP 반영, 페이즈 전환 및 재접속 검증.
- 최종 실행 **2개 중 2개 성공, 실패 0개**. 기존 테스트 소스는 수정하지 않았으며 전체 테스트 묶음은 이번에 재실행하지 않았다.

실행 명령은 다음과 같다.

```sh
Unity -batchmode -projectPath /tmp/ProjectC-BattlefieldValidation \
  -runTests -testPlatform PlayMode \
  -testFilter 'ProjectC.Tests.BattlefieldScenePlayModeTests;ProjectC.Tests.BattlefieldOnlinePlayModeTests' \
  -testResults /tmp/projectc-stone-floor/final.xml \
  -logFile /tmp/projectc-stone-floor/final.log \
  -projectcServerPath /Users/hanjunseo/Dev/ProjectC/build/macos-server/ProjectCNetworkServer \
  -projectcInteractionData /Users/hanjunseo/Dev/ProjectC/ProjectC.Server/Tests/InteractionData
```

캡처: `Artifacts/StoneSurroundFloor/Compact-1920.png`, `Expanded-1920.png`, `Model-1920.png`, `Online-1920.png`. XML/로그 및 수치 요약은 같은 디렉터리의 `Validation/`에 있다. Compact/Expanded는 합성 공개 카드 상태를 표시하고, Online은 실제 서버 테스트 상태다.

1920×1080 이외 해상도와 저사양 장치의 성능은 이번에 실측하지 않았다. 기존 Overlay 카드와 3D 바닥의 접지감 및 원본 모델의 외곽 메시 품질은 이번 바닥 추가 범위에서 수정하지 않았다.
