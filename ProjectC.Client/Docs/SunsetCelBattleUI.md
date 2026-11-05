# Sunset Cel 전투 UI 적용 보고

2026-11-05. Unity **6000.0.81f1**에서 실제 프리팹에 적용하고 Play Mode와 로컬 WebSocket 서버로 검증했다. 작업 시작 시 작업 트리는 깨끗했고 로컬 HEAD와 GitHub HEAD는 `4d76e8ec1f0b28618808460c0d2c3a5577da12d8`로 일치했다. GitHub의 현재 턴 HUD 코드도 조회했다. 제공된 README, manifest와 SVG의 투명 창/곡선 좌표를 확인했다.

## 변경 파일

| 파일 | 변경 내용 |
|---|---|
| `Assets/Prefabs/Battle/BattleScreen.prefab` | 배경, 턴 패널, 정적 타이머 장식, 양쪽 영역 베이스/아이콘, 페이즈 버튼/화살표 및 텍스트 정렬 |
| `Assets/Prefabs/Battle/BattleCard.prefab` | 손패 프레임, 중앙 제목판, 일러스트 마스크, 체력/공격력 뱃지, 오오라 |
| `Assets/Prefabs/Battle/BattleFieldCard.prefab` | 세워진 필드 프레임과 곡선 마스크, 뱃지, 오오라, 예전 모노그램 숨김 |
| `Assets/Prefabs/Battle/CardBack.prefab` | 비공개 카드 뒷면 교체 |
| `Assets/Scripts/Battle/LocalizedBattleFont.cs` | 밝은 제목판/부적 위에 사용할 선택적 어두운 글자색 보존. 기존 컴포넌트의 기본 동작은 유지 |
| `Assets/Scripts/Battle/CardDetailView.cs` | 기존에 누락됐던 상세 그림 바인딩 복구, 그림이 있으면 물음표 숨김 |
| `Assets/Editor/ApplySunsetCelBattleUi.cs` | 명시적으로 실행하는 프리팹 마이그레이션 및 SVG 윤곽에 맞춘 마스크 생성 도구 |
| `Assets/Tests/PlayMode/SunsetCelBattleUiPlayModeTests.cs` | 실제 BattleScene의 상태 메시지, 동적 UI, 모달, 로그, 비공개 정보 및 캡처 검증 |
| `Assets/Tests/PlayMode/SunsetCelOnlinePlayModeTests.cs` | 실제 서버 소환/공격/페이즈/연속 클릭 방어/재접속 검증 |

`BattleScene.unity`, 서버, 규칙, 카드 정의, 프로토콜, 기존 테스트 파일은 수정하지 않았다. 기존 리소스는 다른 UI의 참조가 남아 있어 삭제하지 않았다. Unity가 자동 변경한 동적 폰트 에셋과 기존 캡처 파일은 원상복구했다. 커밋·푸시·배포 빌드는 실행하지 않았다. 시작 시 실행 중인 Unity 에디터는 없었고 기존 에디터를 강제 종료하지 않았다.

## 신규 리소스

`Assets/Art/UI/SunsetCel/`에 원본 README/manifest와 SVG를 보존하고 `png/`의 PNG를 Sprite/Single/FullRect, 알파 투명, 밉맵 없음, 무압축으로 임포트했다.

| PNG | 실제 적용 대상 |
|---|---|
| HandCardFrame | 손패 FrameImage/RenewalFrame 및 오오라 실루엣 |
| FieldMonsterFrame | 필드 FrameImage/RenewalFrame 및 오오라 실루엣 |
| HandTitlePlate | 손패 RenewalTitle |
| HealthBadge | 손패/필드 왼쪽 아래 HealthBadgeFrame |
| AttackBadge | 손패/필드 오른쪽 아래 AttackBadgeFrame |
| FloralCardBack | CardBack, YourDeck, OpponentDeck |
| TurnStatusBase | TurnHud |
| TimerRingFrame | 별도 정적 TimerRingFrame Image |
| ZoneOmamoriBase | 양쪽 RenewalZone0/1/2 |
| PhaseButtonBase | 기존 EndTurn 버튼 Image |
| PhaseArrow | TurnArrow 및 클릭 가능 강조 |
| ZoneIconDeck | 양쪽 덱 아이콘 |
| ZoneIconGrave | 양쪽 묘지 아이콘 |
| ZoneIconExile | 양쪽 제외 아이콘 |

전달받은 `BattleBackground_SunsetCel_1920x1080.png`를 기존 전체 배경 Image에 적용했다. 제공 SVG 외곽선을 따라 생성한 `HandArtworkMask.png`, `FieldArtworkMask.png`도 추가했다. 새 필드 슬롯 가이드는 만들지 않았다.

## 유지한 연결과 레이아웃

- HUD, 리더, 손패, 양쪽 필드, 영역 카운터, 페이즈 버튼, 왼쪽 메뉴/로그의 루트 위치와 앵커를 유지했다. 기존 카드 슬롯 크기도 손패 194×248, 필드 166×183 그대로다. 내부 FrameImage만 새 프레임 비율로 맞춰 손패 약 183×248, 필드 약 144×183으로 표시한다.
- 이름/공격력/체력 TMP와 CardWidget 바인딩, CardZoneView 풀링, 손패 펼침/선택/드래그, 필드 상호작용을 재사용한다. 카드명은 중앙이며 상단 코스트 공간은 없다. 기존 선택적 발동비용 바인딩은 보존하되 해당 카드 프리팹의 코스트 그래픽은 숨긴다. 상세 패널의 실제 효과비용 정보는 유지한다.
- 기존 Inset Image는 비활성화하고 그 RectTransform을 프레임에 맞췄다. ArtworkMask는 SVG 외곽을 따르는 stencil Mask, 프레임 장식은 위 레이어로 분리했다. AspectFillImage와 필드별 crop profile은 유지했다. 카드 외곽 오오라는 새 프레임의 알파와 기존 glow shader를 사용한다.
- TurnHud의 실제 턴/시간/주체 바인딩과 TimeRing의 Filled/Radial360을 유지한다. TimerRingFrame은 Simple Image이며 시간에 따라 회전하거나 fill로 잘리지 않는다.
- 양쪽 세 영역의 기존 TMP와 공개 영역 버튼을 유지한다. 베이스 위치는 유지하고 아이콘/숫자만 새 베이스의 빈 공간 안에 맞췄다. 장식은 raycast를 받지 않는다.
- EndTurn의 기존 이벤트, 서버 대기 방어, 상태별 텍스트와 TurnFrameGlow를 유지한다. ColorTint로 Hover/Pressed를 구분하고 기존 런타임 tint로 잠금/상대 턴 상태를 구분한다.
- 로그 팝업, X/ESC 닫기, 배경 dim, 서버 이벤트 기반 로그, 비공개 카드 정보 차단, 로그에서 상세 열기와 언어 변경을 유지한다. 새 리소스에 없는 로그/메뉴/리더 장식은 기존 디자인을 유지한다.

## 실제 검증 결과

최종 [verified-results.xml](../Artifacts/SunsetCel/verified-results.xml): **3/3 통과**. 배포 빌드는 수행하지 않았다.

| 최종 실행 테스트 | 결과 및 확인 내용 |
|---|---|
| `SunsetCelBattleUiPlayModeTests.SunsetArtworkPreservesLiveTurnLogPrivacyAndCardDetails` | 통과. 내/상대 턴, 시간 감소/보정, 정적 장식, 손패 추가/제거, 선택/사용 가능 오오라, 양쪽 5장 필드, 빈 필드, 스냅샷의 공격/체력 변경, 뒷면과 상대 손패 비공개, 양쪽 영역 아이콘/동적 카운터, 네 페이즈 텍스트, UI raycast, 로그 X/ESC/dim/스크롤/상세, 재대전 로그 초기화 |
| `SunsetCelOnlinePlayModeTests.SunsetUiPreservesServerCommandsDuplicateGuardAndReconnect` | 통과. 기존 `build/Debug/ProjectCNetworkServer.exe`와 TestData로 실제 연결/매치/준비, 잘못된 드롭 복구, 드래그 소환, 상대 소환, 연속 페이즈 클릭이 한 번만 진행됨, 드래그 공격/체력 감소, 재접속 후 필드 복원 |
| 기존 `BattleUxPlayModeTests.PinnedInspectorRefreshesFromUpdatedServerSnapshot` | 통과. 상세를 고정한 뒤 카드 정의/공격력 변경 시 텍스트와 실제 그림 갱신 |

첫 실행은 기존 `BattleUiRenewalCaptureTests`, `BattleUxPlayModeTests` 전체와 신규 UI 테스트로 **7개 중 3 통과/4 실패**였다. 이후 변경 전 HEAD의 프리팹과 폰트 코드로 동일한 기존 테스트를 재실행해 **6개 중 2 통과/4 실패**가 동일하게 재현됨을 확인했다. 기존 테스트는 수정하지 않았다.

| 기존 테스트의 실패 | 변경 전 확인 및 처리 |
|---|---|
| `CaptureSyntheticPresentationStates` | 변경 전에도 없는 현지화 키 `ui.phase.main1`에서 실패. 원본 유지 |
| `InspectorIsClickPinnedAndTimerUsesServerCorrection` | 변경 전에도 `Hover must not inspect` 기대에서 실패. 원본 유지 |
| `OverlapUsesRotatedAndScaledCardAreaInEveryCanvasMode` | 변경 전에도 겹침 비율 약 0.8204가 기대 0.9에 미달. 원본 유지 |
| `PinnedInspectorRefreshesFromUpdatedServerSnapshot` | 변경 전에도 상세 그림이 null. 상세 그림 바인딩을 복구한 뒤 최종 재실행 통과 |

기존 `BattlefieldOnlinePlayModeTests`도 실행했다. 소환/공격/페이즈/재접속 이후 `YokaiShrineStage` 이름의 3D 오브젝트를 요구하는 마지막 검사에서 실패했고 변경 전 HEAD에서도 같은 줄에서 실패했다. 새 온라인 검증은 실제 적용한 배경을 검사하며 위 기능들과 중복 명령 방어까지 통과했다. 초기/중간/기준선 XML은 `Artifacts/SunsetCel/`에 보존했다.

UI 캡처 테스트는 합성 서버 메시지/DTO를 사용한 실제 Unity Play Mode 화면이며, 온라인 캡처는 실제 로컬 WebSocket 서버 전투 화면이다. OS 마우스로 두 독립 클라이언트를 사람이 끝까지 플레이한 검증은 수행하지 않았다. 일본어 로그 일부 글자에서 기존 폰트의 글리프 누락 경고가 남는다. 일반적인 전체 화면은 1920×1080에서 확인했으며 로그/HUD 화면 경계는 1280×720, 1920×1200, 2560×1080도 검증했다.

## Unity 캡처

- [내 턴 전체](../Artifacts/SunsetCel/own-turn.png)
- [상대 턴 전체](../Artifacts/SunsetCel/opponent-turn.png)
- [양쪽 필드 5장씩](../Artifacts/SunsetCel/both-fields-full.png)
- [필드 선택 오오라](../Artifacts/SunsetCel/both-fields-selected.png)
- [손패 사용 가능/선택 오오라](../Artifacts/SunsetCel/hand-selected.png)
- [전투 기록과 카드 상세](../Artifacts/SunsetCel/log-card-detail.png)
- [실제 서버 소환/공격](../Artifacts/SunsetCel/online-summon-attack.png)

## 재실행

변경 프리팹은 이미 저장되어 있으므로 플레이를 위해 별도 적용 작업은 필요 없다. 에셋 재적용은 Unity 메뉴 **Project C → Apply Sunset Cel battle UI**에서 명시적으로 수행한다. 수동으로 내부 장식 배치를 조절했다면 이 도구는 그 값을 덮어쓰므로 재실행 전에 확인한다.

최종 검증은 저장소 루트에서 다음과 같이 실행했다. 테스트가 캡처를 저장하도록 batchmode는 사용하지 않았다.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.0.81f1\Editor\Unity.exe' `
  -projectPath C:\Dev\ProjectC\ProjectC.Client `
  -runTests -testPlatform PlayMode `
  -testFilter 'ProjectC.Tests.SunsetCelBattleUiPlayModeTests;ProjectC.Tests.SunsetCelOnlinePlayModeTests;ProjectC.Tests.BattleUxPlayModeTests.PinnedInspectorRefreshesFromUpdatedServerSnapshot' `
  -projectcServerPath C:\Dev\ProjectC\build\Debug\ProjectCNetworkServer.exe `
  -testResults C:\Dev\ProjectC\ProjectC.Client\Artifacts\SunsetCel\verified-results.xml `
  -logFile C:\Dev\ProjectC\ProjectC.Client\Artifacts\SunsetCel\verified-playmode.log
```
