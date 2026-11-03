# 빌드에서 3D 전장이 회색으로 보이는 문제 수정

2026-11-03. Unity 6000.0.81f1 / URP 17.0.4 / macOS Metal.

## 원인과 변경

기존 `UniversalRP.asset`에는 Renderer2D만 등록되어 있었고, BattleScene의 Main Camera는 Renderer Index=-1(기본 렌더러)을 사용했다. 3D 전장에 URP Lit 머티리얼을 적용했지만, 카메라와 Player 빌드의 렌더러 구성은 2D 전용으로 남아 있었다. 에디터 Game View 검증만으로는 Player의 3D 렌더링과 셰이더 패스를 보장하지 못했다.

수정 파일은 다음과 같다.

- `Assets/Settings/BattlefieldRenderer.asset` 및 `.meta`: 3D Forward용 UniversalRendererData.
- `Assets/Settings/UniversalRP.asset`: Renderer List의 인덱스 1에 위 에셋 추가. 기본 인덱스 0의 Renderer2D는 유지.
- `Assets/Scenes/BattleScene.unity`: Main Camera의 Renderer Index를 -1에서 1로 변경. 그 외 씬 설정은 그대로.

카메라의 위치/투영/크기, 모델과 머티리얼, 조명, 카드 UI, 입력, 애니메이션 및 게임/네트워크 코드는 변경하지 않았다. 로비와 덱 편집기는 기존 기본 렌더러를 계속 사용한다. 셰이더 전체 강제 포함이나 전역 stripping 해제로 우회하지 않았다.

## Player 검증

원본 프로젝트의 열린 에디터를 방해하지 않도록 `/tmp/ProjectC-BattlefieldValidation` 복사본에서 검증했다.

1. 실제 활성 Build Settings의 LobbyScene, DeckEditorScene, BattleScene으로 macOS Development Player를 CleanBuildCache 옵션과 함께 빌드했다.
2. 복사본에만 임시 스모크 컴포넌트를 넣어 실행 파일에서 BattleScene을 열고 공개 카드 상태를 주입했다. 게임 규칙이나 프로토콜을 바꾸지 않았으며 실제 온라인 대전 캡처를 가장하지 않았다.
3. **실제 macOS Player**의 1920×1080 화면에서 전장 모델, 주변 석재 바닥, 카드/초상화/손패/턴 UI 표시를 확인하고 ScreenCapture로 저장했다.
4. Player 로그에서 실제 렌더러가 `UniversalRenderer`이고, 두 MeshRenderer의 URP Lit 셰이더가 supported=True이며 각 BaseMap이 연결된 것을 확인했다. 기존 로그에 있던 async texture load 실패는 새 실행에서 나타나지 않았다.
5. 임시 검증 컴포넌트를 제거하고 동일 에셋/씬으로 **일반 실행용 macOS Player를 다시 클린 빌드**했다. 최종 Managed DLL에 `BattlefieldPlayerSmoke`가 포함되지 않은 것도 확인했다.

실행 파일은 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.app`이다. x86_64와 arm64를 포함하는 Universal binary이며 기존 `BuildFile.app`을 덮어쓰지 않았다. 새 앱은 기본 로비에서 시작하며 별도 연결 작업 없이 빌드 설정에 저장된 전장 렌더러를 사용한다. 이후 Unity의 기존 Build 메뉴에서도 같은 수정 에셋이 포함된다.

캡처와 로그는 `Artifacts/BattlefieldBuildFix/`에 있다.

- `Player-1920.png`: 실제 Player에서 UI와 카드를 포함한 전장 화면.
- `Player-Model-1920.png`: 같은 Player에서 UI만 숨긴 3D 전장/바닥.
- `Validation/build-proof.log`, `player-proof.log`: 캡처에 사용한 검증 Player 빌드/실행 로그.
- `Validation/build-final.log`: 임시 컴포넌트 제거 후 일반 Player 빌드 성공 로그.
- `Validation/playmode.xml`, `playmode.log`, `summary.json`: 기존 전장/실제 서버 상호작용 테스트 결과.

기존 전장 PlayMode 검증 2개는 모두 성공했다. Overlay Raycast·손패 선택/확대/축소와 실제 서버의 무효 드롭 복귀·드래그 소환·공격/HP 반영·페이즈 전환·재접속을 확인했다. 테스트 소스는 수정하지 않았다.

전체 테스트 묶음이나 Windows Player는 이번에 실행하지 않았다. macOS 실행 화면은 실제로 확인했으며 다른 플랫폼의 시각적 검증까지 완료했다고 보고하지 않는다.
