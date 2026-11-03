# YokaiShrine 3D 교체 및 수직 탑다운

2026-11-03. Unity 6000.0.81f1, URP 17.0.4. 신규 FBX를 MeshRenderer로 직접 렌더링한다. 모델을 2D 이미지, Screen Space Image, RenderTexture 배경으로 변환하지 않았다.

## 적용 파일

- `Assets/Scenes/BattleScene.unity`: 이전 전장 프리팹 인스턴스를 새 프리팹으로 교체, Main Camera를 수직 Orthographic으로 변경, Ambient 조정.
- `Assets/Art/Environment/BattleField/YokaiShrine/Presentation/YokaiShrineStage.prefab`: 신규 FBX 인스턴스와 Moonlight/SpiritFill 조명.
- 같은 폴더의 `ShrineStone.mat`, `ShrineMetallicSmoothness.png`, `ShrineEmission.png` 및 meta: 신규 텍스처용 URP PBR 머티리얼과 파생 맵.
- 신규 `YokaiShrine` FBX/원본 텍스처의 meta: 모델 import camera/light/animation 비활성화, normal/linear/sRGB 및 mipmap/압축 설정.
- `Assets/Tests/PlayMode/BattlefieldScenePlayModeTests.cs`: Perspective 검증을 명시적인 수직 Orthographic 검증으로 교체. 신규 FBX 사용/이전 FBX 미사용을 검증하며 양쪽 필드를 5장씩 렌더링한다.
- `Assets/Tests/PlayMode/BattlefieldOnlinePlayModeTests.cs`: 캡처 파일명 변경. 실제 서버 입력 검증은 유지한다.
- `Artifacts/YokaiShrineTopDown/`: 캡처와 Validation 결과.

이전 `Battlefield_YokaiShrine_Floor` 모델과 `BattleField/YokaiShrineStage` 프리팹/머티리얼은 삭제하거나 수정하지 않았다. 새 씬에는 이전 모델의 Renderer가 없다. 원본 신규 FBX와 이미지의 바이트도 그대로다.

## 배치와 카메라

- 전장 프리팹 루트: 위치 `(0,0,0)`, 회전 `(0,0,0)`, 스케일 `(1,1,1)`.
- 모델 인스턴스: 균일 스케일 `(22,22,22)`, 위치 사실상 `(0,0,0)`.
- FBX import의 기존 X=-90°가 바닥을 XZ로 정렬한다. 여기에 월드 Y=+90° 회전만 합성해 긴 돌바닥이 화면의 가로 방향을 향하도록 배치했다. 장식물도 전체 모델과 함께 회전하며 상대적인 위치·높이·비율은 보존한다.
- Main Camera: Orthographic, Size **8.5**, 위치 **(0,30,0)**, 회전 **(90,0,0)**, Near=0.1, Far=100. 카메라 forward는 정확히 월드 아래쪽이고 화면 위쪽은 +Z다. 원근 투영이나 추가 롤/비스듬한 시점이 없다.
- 1920×1080에서 보이는 월드 영역은 약 30.22×17이다. 전체 모델과 외곽 장식이 화면에 들어오고 기존 카드 5장씩이 중앙 평평한 돌바닥에 놓이도록 스케일을 맞췄다.

카드는 기존 Screen Space Overlay Canvas를 사용한다. 필드 RectTransform, 카드 크기/간격, 드롭 판정과 Raycast/좌표 변환은 수정하지 않았다. 수직 Orthographic 배경이 기존 카드 영역과 대응하도록 모델과 카메라만 조정했다. 이전 단계에서 적용한 배경 투명도 및 OwnFieldDropZone의 약한 틴트도 그대로 유지한다.

## 머티리얼과 조명

신규 atlas basecolor, normal, metallic, roughness를 `Universal Render Pipeline/Lit`에 연결했다. basecolor는 sRGB, metallic/roughness는 선형, normal은 Normal Map으로 가져온다. 원본 금속도 R과 거칠기의 역수(1−roughness)를 URP의 Metallic/Smoothness RGBA 맵으로 패킹했다. Normal 강도는 0.65, Smoothness 배율은 0.7이다.

파생 Emission 맵은 신규 basecolor에 존재하는 청록색 부분만 추출한다. 원본의 석재·이끼·붉은 목재·기와 질감을 유지하고 새로운 문양이나 불꽃은 그리지 않았다. Bloom이나 별도 VFX 시스템은 없다.

Moonlight는 차가운 청색 Directional intensity 1.15, soft shadow strength 0.35이다. SpiritFill은 청록 Directional intensity 0.28, 그림자 없음이다. Ambient Flat을 `(0.23,0.27,0.33)`으로 약간 밝게 조정했다. 기존 Render Pipeline/Quality 전역 설정과 다른 씬의 조명은 바꾸지 않았다.

## 검증

원본 프로젝트가 열려 있어 `/tmp/ProjectC-BattlefieldValidation` 복사본에서 실행했다. 검증한 씬과 전장 에셋만 원본 프로젝트에 반영한다. 별도 제작 도구를 프로젝트에 추가하지 않았고, 수동 연결이나 도구 실행도 필요 없다.

- Unity C# 컴파일 및 모델/텍스처 import 완료.
- 실제 BattleScene에서 신규 MeshRenderer, URP Shader 지원, base/normal/metallic/emission 슬롯, Collider/Rigidbody 없음 확인.
- 카메라 Orthographic 및 정확한 수직 방향, 신규 모델 경로, 이전 모델 미사용 확인.
- 양쪽 필드 5장씩의 이미지/스탯, 실제 UI Raycast, 손패 확대, 호버 선택 전환, 빈 영역 클릭 축소 확인.
- 실제 C++ WebSocket 서버와 두 클라이언트로 무효 드롭 복귀, 드래그 소환, 공격과 서버 HP 반영, 페이즈 버튼, 재접속 확인.

전체 PlayMode 결과는 변경 전후 모두 **71개 중 61개 성공, 10개 실패**로 동일하며, 전장 씬/온라인 검증 2개는 모두 성공했다. 새 회귀는 발견하지 않았다. 검증 숫자 및 기존 실패 목록은 `Validation/summary.json`과 baseline/final XML에 기록한다. 기존 테스트 실패를 수정하거나 기능/UI를 이전 상태로 되돌리지 않는다.

테스트 실행에는 다음 옵션을 사용했다.

```sh
Unity -batchmode -projectPath /tmp/ProjectC-BattlefieldValidation \
  -runTests -testPlatform PlayMode \
  -projectcServerPath /Users/hanjunseo/Dev/ProjectC/build/macos-server/ProjectCNetworkServer \
  -projectcInteractionData /Users/hanjunseo/Dev/ProjectC/ProjectC.Server/Tests/InteractionData
```

## 캡처와 남은 범위

캡처는 실제 Play Mode의 1920×1080 Game View를 ScreenCapture로 저장했다. 캡처를 런타임 배경으로 사용하지 않는다.

- `YokaiShrine-Compact-1920.png`: 합성 공개 카드 상태, 양쪽 필드 5장과 축소 손패.
- `YokaiShrine-Expanded-1920.png`: 같은 상태에서 실제 손패 클릭/호버로 확장한 화면.
- `YokaiShrine-Model-1920.png`: UI만 숨긴 동일 카메라의 실제 3D 모델.
- `YokaiShrine-Online-1920.png`: 실제 서버 Legacy 테스트 덱의 소환·공격 후 화면.

사용자 OS 마우스로 장시간 플레이하거나 저사양 기기에서 성능을 측정하지 않았다. 원본 단일 메쉬는 약 **426,114 triangles**이며 감면이나 LOD는 이번 범위에 포함하지 않았다. 수직 시점에서 지붕·장식의 측면이 덜 보이는 것은 요청한 90° 투영에 따른 것이며 실제 3D 높이를 제거하지 않았다. 카드와 바닥의 접지감/광원 통합은 기존 Overlay 카드를 유지하는 이번 작업 범위에서 추가하지 않았다.
