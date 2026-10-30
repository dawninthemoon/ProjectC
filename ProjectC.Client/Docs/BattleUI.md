# Project C 카드 중심 배틀 UI

2026-10-30. 실제 프로젝트 버전 **Unity 6000.0.81f1**을 유지한다. 구현은 uGUI Scene/Prefab과 서버 권위 스냅샷을 사용한다. 분석 자료는 [ReferenceVideoAnalysis.md](ReferenceVideoAnalysis.md), [AdditionalInteractionRequirements.md](AdditionalInteractionRequirements.md)에 있으며, 이번 작업은 그 다음 구현 단계다.

## 실행과 조작

1. 저장소 루트에서 `cmake --build build --config Debug --parallel 4` 후 `./build/Debug/ProjectCNetworkServer.exe --port 7777` 실행.
2. Unity에서 `Assets/Scenes/BattleScene.unity`를 열어 Play하거나 `Build/Multiplayer/ProjectC.Client.exe`를 두 개 실행한다.
3. 각각 CONNECT. 한쪽 CREATE MATCH, 다른 쪽은 MatchId를 입력하고 JOIN MATCH ID. 덱을 선택한 후 양쪽 READY TO PLAY.
4. 자신의 메인 페이즈에 손패 카드를 **자신의 필드 영역으로 드래그**한다. 소환 위치는 서버의 필드 순서로 자동 정렬된다. 위치 지정·카드 순서 변경 기능은 없다.
5. END TURN은 서버에서 남은 페이즈와 턴 종료, 다음 플레이어 턴 시작까지 처리한다. Start turn 입력은 필요 없다.
6. 소환한 몬스터는 다음 자기 턴부터 공격한다. BATTLE PHASE를 누른 뒤 **자신의 몬스터에서 상대 몬스터까지 드래그**한다. 공격자는 자리에 남고 화살표가 따라간다. 상대 필드가 비면 상대 플레이어의 Life 패널로 드래그하여 직접 공격한다.
7. 잘못된 드롭이나 Esc는 취소한다. 서버 거부는 이유를 표시하고 임시 카드를 복구한다. 서버 응답 대기 중에는 추가 게임 명령을 보내지 않는다. 이미 전송한 명령을 Esc로 취소할 수는 없다.
8. 카드 위에 잠시 머무르면 상세 패널이 표시된다. 카드 이름·ATK/HP·서버 효과 설명·현현 형태 설명을 확인한다. 긴 설명은 스크롤 가능하며 화면 경계에서 패널 위치를 보정한다.
9. BATTLE LOG는 왼쪽에서 열리며 같은 버튼 또는 X로 닫는다. 최신 항목은 아래쪽이다. 아래를 보고 있을 때만 새 로그를 따라가고, 과거 로그를 읽는 동안에는 위치를 유지한다. 닫힌 패널은 클릭을 가로채지 않는다.
10. GRAVEYARD로 양쪽 공개 묘지를 확인한다. MENU / LOBBY에서 접속 해제·재접속·재동기화를 수행한다. 결과 화면에서 양쪽 REQUEST REMATCH로 다시 시작한다.

기본 덱은 Life 3, 초기 손패 2, DrawPerTurn 0이다. 기본 게임에 매 턴 드로우가 없는 것은 서버 데이터에 따른 동작이다.

## 실제 에셋과 코드

| 파일 | 역할 |
|---|---|
| `Assets/Scenes/BattleScene.unity` | 기존 카메라·URP 설정·Input System EventSystem과 BattleScreen 프리팹 인스턴스. 사용자 카메라 변경 유지 |
| `Assets/Prefabs/Battle/BattleScreen.prefab` | 상단 상대 정보·숨긴 손패·필드, 중앙 전장, 하단 자신의 필드·손패, Life·덱·턴, 로비·묘지·결과·로그 패널 |
| `BattleCard.prefab`, `CardBack.prefab` | 서버 카드 DTO 또는 비공개 뒷면을 표시하는 재사용 카드 |
| `CardAction.prefab` | 현현/발동 효과와 대상 후보 버튼 템플릿 |
| `BattleVisualSettings.asset` | Hover 지연, 복귀·드로우·턴·로그 패널 연출 시간 |
| `BattleInteractionController.cs` | 드래그, 실제 UI raycast 드롭 판정, 클릭 대상 선택, 임시 카드, 공격 화살표, 입력 상태와 취소 |
| `CardWidget.cs`, `CardTableLayout.cs`, `CardZoneView.cs` | Hover·선택·후보 표시, 겹친 손패와 필드 자동 배치 |
| `BattlePresenter.cs` | 공개 스냅샷 바인딩, 로비/결과, 서버 이벤트와 연출 연결 |
| `BattleEffectsView.cs`, `BattleLogView.cs`, `CardDetailView.cs` | FIFO 연출, 왼쪽 로그 서랍, 문맥 상세 정보 |
| `ContextActionView.cs`, `EffectTargetPicker.cs` | 선택 카드 액션, 클릭 대상 후보와 최소/최대 개수 확인 |
| `MultiplayerDebugScreen.cs`, `Protocol.cs` | 기존 전송 경로, RequestId/Sequence 기반 승인 추적, 공개 액션 DTO |

화면과 연출 오브젝트는 프리팹에 저장되어 있다. 실행 중에는 저장된 카드/버튼 프리팹을 Instantiate하고 데이터·위치·색상만 갱신한다. 런타임 화면 생성기나 IMGUI 배틀 화면은 사용하지 않는다. 기존 전역 SUMMON/ATTACK/DIRECT/CLEAR 버튼은 비활성 `LegacyDebugControls` 안에 보존하며 플레이 화면에 노출하지 않는다.

`Assets/Editor/UpgradeBattleInteractionAssets.cs`는 이번 변경을 저장한 **명시적 에디터 도구**다. 기존 BattleScreen을 수정하고 기존 BattleScene을 열어 저장한다. 일반 실행·빌드에는 실행할 필요가 없다. 수동 프리팹 배치를 편집한 뒤에는 다시 실행하면 배치가 도구의 값으로 돌아가므로 주의한다. 기존 `AuthorBattleAssets.Build`는 Scene을 다시 만드는 이전 도구이며 이번 작업에서 실행하지 않았다.

UI 참조는 프로젝트의 `Game.Utils.UnityBindingExtensions.Bind` 실제 API를 사용한다. 원본 `ProjectC.Client/Scripts/Utils`는 Assets 밖에 있고 일부 텍스트 확장이 없는 현지화 모듈에 의존하므로, 공통 Bind/BindButton/BindNoRecursive만 `Assets/Scripts/Utils`로 이식했다. 원본 파일은 수정하지 않았다. 중복 이름은 카드·플레이어·로비 등의 하위 루트로 범위를 제한한다. 프리팹/설정 에셋 참조는 인스펙터에 저장한다.

## 서버 승인과 입력 상태

`Idle → DraggingHandCard / DraggingAttackTarget → AwaitingServerResponse → Idle` 흐름과 `SelectingCardAction / SelectingEffectTarget` 상태를 분리한다. 취소·스냅샷 교체·재접속·resync·새 게임 시 선택과 임시 표시를 정리한다. 드래그 중에는 실제 손패·필드·HP를 변경하지 않는다.

`CommandAccepted`의 RequestId를 보관한 후 해당 SequenceNumber 이상의 스냅샷을 수신해야 승인 처리가 끝난다. 관련 없는 스냅샷만으로 대기를 해제하지 않는다. 거부 시 이유와 복귀 연출을 제공한다. 최종 GameState는 언제나 서버가 결정한다.

상대 손패에는 CardView가 없고 수량만큼 CardBack을 표시한다. Hover·드래그·드로우 연출이 숨긴 카드의 이름이나 정의를 생성하지 않는다. 덱은 개수만 표시한다.

## 효과·현현 확장 경계

**현현의 비용·횟수·턴 조건 등 새로운 규칙을 확정하지 않았다.** 기본 Striker의 AllowedManifestations만으로 버튼을 만들지 않는다. 서버에 정의된 Activated 효과 중 Transform 동작이 있으며 현재 발동 가능할 때만 카드 옆에 **현현** 버튼이 나타난다. 단일 자기 자신 후보만 있으면 버튼으로 전송하고, 그 외에는 서버 후보의 클릭 선택을 사용한다. 승인 후 새 카드 정의·능력치·설명과 연출이 반영된다.

프로토콜 v2에 추가한 명령:

```json
{"Type":"ActivateEffect","CardId":1,"EffectId":2,"Targets":[[{"PlayerId":1,"CardId":1,"Zone":2}]]}
```

이 객체는 기존 SubmitCommand의 Command다. MatchId, GameNumber, SequenceNumber, RequestId 검증을 그대로 거친다. 바깥 명령 주체는 인증된 ClientSession에서 결정한다. Targets의 PlayerId는 대상 소유자를 표현할 뿐 명령 권한을 부여하지 않는다. Operation/Amount/TransformDefinitionId 등 효과 자체를 클라이언트가 지정할 수 없다. 내부 TransformCommand는 계속 원격 허용 목록에서 제외한다.

자신의 필드 카드에만 `Activations[]`를 제공한다. 각 항목은 `EffectId`, `CanActivate`, `IsManifest`, `Selections[]`이며 각 선택은 `MinCount`, `MaxCount`, `Candidates[{PlayerId,CardId,Zone}]`다. 후보는 GameCore ValidateTargets로 계산한 공개 대상이다. 클릭으로 선택/해제하고 CONFIRM으로 다음 선택 또는 전송한다. 최소·최대 개수와 중복, 대상 소유권·Zone·효과 규칙은 서버가 다시 검증한다. 여러 동작은 서버에서 순서대로 재검증·적용한다.

자신의 덱 카드 검색 등 **비공개 덱의 특정 카드 선택 UI는 미지원**이다. FriendlyDeck 선택이 필요한 액션은 후보 UI에 노출하지 않고 원격 Deck 타깃도 거부한다. 별도의 비공개 검색 프로토콜/특수 선택 화면은 후속 결정 사항이다. 현재 확정되지 않은 효과별 특수 UI는 추가하지 않았다.

기본 카드/덱은 바꾸지 않았다. `ProjectC.Server/Tests/InteractionData`는 다중 대상 피해·자기 현현을 검증하기 위한 **별도 테스트 데이터**이며 일반 실행의 기본 데이터가 아니다.

## 연출과 폰트

턴 안내는 YOUR TURN/OPPONENT TURN 및 TURN X, 중앙 dim/fade로 표시한다. 과거 턴 안내가 뒤늦게 나타나지 않도록 새 턴이 도착하면 오래된 턴 안내를 생략한다. 페이즈·소환·효과·피해·파괴·현현 팝업과 드로우는 FIFO로 재생하며 네트워크 수신과 실제 상태 갱신을 지연하지 않는다. 연출은 raycast를 차단하지 않는다.

드로우는 덱에서 손패로 이동·회전·크기 변화를 적용한다. 자기 카드는 공개 스냅샷에 있을 때 앞면을 사용하고 상대는 뒷면만 사용한다. 여러 드로우는 순서대로 표시한다. 소환 카드는 서버 배치 후 짧게 강조한다. 연출 시간은 Project C용 설정값이며 레퍼런스 영상의 정확한 타이밍을 측정해 복제했다고 주장하지 않는다.

`Assets/Fonts/NotoSansKR-Regular.otf`는 [Noto CJK 공식 저장소](https://github.com/notofonts/noto-cjk/tree/main/Sans/SubsetOTF/KR)의 폰트다. 라이선스는 함께 저장한 `Assets/Fonts/OFL.txt`(SIL Open Font License 1.1)를 따른다. 현현 텍스트에 사용할 한글 글리프를 포함한다. 카드 그림과 리더 초상화는 플레이스홀더다.

## 검증과 재현

- Windows MSVC: 서버 빌드 성공, CTest **3/3**. GameCore 기존 **284개 그룹** 회귀 통과.
- NetworkTests: 대상 후보·상대 액션 비공개·덱 타깃 거부·다중 대상·중복 타깃·타인 소스·효과 수치 주입 거부·현현·Replay 검증 추가.
- 실제 WebSocket: 네 플레이어/두 매치, 전투·직접 공격·재접속·재대전·비공개 정보 및 매치 격리 통과.
- Unity 전송 코드의 별도 C# 네 클라이언트 통합 테스트 통과.
- Linux Docker 이미지 빌드 및 내부 CTest **3/3**. Compose 실행 후 네 활성 연결/두 매치에서 graceful shutdown, Replay 보관, 세션 0, 종료 코드 0 확인.
- Unity PlayMode **5/5**: 실제 서버 드래그 소환/공격/직접 공격, 잘못된 드롭, 중복 전송 차단, 서버 거부 복귀, 재대전·재접속, 드로우 앞/뒷면, 클릭 효과 대상과 서버 승인 현현, 로그 자동 스크롤/과거 위치 유지, Hover, 비공개 손패, 이전 매치/epoch 무시.
- Unity Windows Standalone64 빌드 검증. 실제 실행 파일은 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe`.
- 1920×1080/1280×720/10장 손패 Canvas 캡처, 실제 서버 드래그 소환/공격 중 캡처를 확인했다. 일반 배치 캡처는 합성 DTO이고, Interaction 캡처는 실제 서버 테스트 상태다.

Unity 자동화는 `build/UnityMatchValidation` 복사본에서 수행했다. 로그: `build/InteractionPlayMode.xml`, `build/Interaction-test.log`, `build/Interaction-build.log`, 캡처: `build/UnityMatchValidation/Artifacts`. Unity Test Runner의 PlayMode 전체 실행으로 재현할 수 있다. 서버 위치가 다르면 `-projectcServerPath <exe>`를 전달한다. 효과 fixture 위치가 다르면 `-projectcInteractionData <Tests/InteractionData 절대 경로>`를 전달한다.

## 남은 수동 확인과 다음 작업

두 독립 Windows 창에서 사람이 끝까지 마우스/키보드로 플레이한 검증은 별도로 남아 있다. 자동 테스트는 실제 uGUI 드래그 핸들러와 UI raycast, 서버 연결을 사용하지만 OS 마우스 장치 조작을 대체했다고 보고하지 않는다.

수동 확인: 두 창에서 생성/참가/준비 → P1 드래그 소환/턴 종료 → P2 드래그 소환/턴 종료 → P1 Battle에서 상대 카드로 드래그 → 다음 자기 턴에 상대 Life로 드래그 → 승패/재대전. 추가로 Esc 취소, 손패 가장자리 Hover, 실제 마우스 휠로 상세/로그/묘지 스크롤, 로그 과거 위치 유지, 비활성 창 수신과 재접속을 확인한다.

다음 단계는 실제 카드 아트와 문구 정리, 확정된 효과별 특수 선택 화면, 덱 검색의 비공개 선택 프로토콜이다. 현현 규칙 합의가 이뤄지면 서버 카드 정의와 검증을 먼저 확장한다. 극단적으로 많은 필드 카드(기본 5장 초과 설정)의 별도 레이아웃은 후속 범위다. Git commit/push는 수행하지 않았다.
