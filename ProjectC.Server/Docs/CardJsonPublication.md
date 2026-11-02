# 카드 JSON 배포와 신규 카드군

게임 실행 데이터는 JSON으로 관리한다. 외부 `ProjectC_Data.xlsx`의 관리와 JSON 내보내기는 저장소 밖의 작업이며, 서버와 Unity는 XLSX를 읽지 않는다.

## 배포 절차

외부에서 내보낸 다음 파일을 `ProjectC.Client/Assets/Resources/`에 반영한다.

- `ProjectC_Data - CardData.json`: 카드 규칙, NameKey, DescriptionKey, DescriptionArgs.
- `ProjectC_Data - Localize.json`: 카드 및 UI 번역, 키워드 마크업.
- `ProjectC_Data - DeckData.json`: 견본 덱의 기존 행 구조.

저장소 루트에서 실행한다. Python 표준 라이브러리만 필요하다.

```powershell
python Tools/ExportCardData.py
python Tools/ExportCardData.py --check
cmake --build build --config Debug --parallel 6
ctest --test-dir build -C Debug --output-on-failure
```

첫 명령은 전체 입력을 검증한 다음 아래 네 파일을 생성한다. `--check`는 생성 결과와 저장된 결과의 차이가 있으면 실패하며 파일을 쓰지 않는다.

- `ProjectC.Server/Data/HyakkiYagyo/Cards.json`
- `ProjectC.Server/Data/HyakkiYagyo/Decks.json`
- `ProjectC.Client/Assets/Resources/CardData/HyakkiCards.json`
- `ProjectC.Client/Assets/Resources/CardData/HyakkiDecks.json`

서버/Unity 카드 파일은 동일하며 규칙 내용 해시를 CatalogVersion에 포함한다. DeckData에 나오는 덱 ID만 갱신하며, 기존 Config와 나머지 덱은 보존한다. 배포 서버의 JSON도 함께 갱신해야 한다. 기존 기본/Legacy 카탈로그는 이 작업의 대상이 아니다.

검증은 중복 ID, Enum/Operation/Trigger, 비용과 대상 규칙, 카드 참조, 현현 참조, 설명 경로, Localize 키/키워드, 설명 인덱스, 덱 매수/편입 가능 여부를 검사한다. 비용·대상 선택은 기존 서버 명령 처리에 맡긴다. UI의 비용 표시는 제출할 규칙이나 비용의 근거가 되지 않는다.

## 추가 데이터

기존 17종을 유지하며 CrimsonForge 3001–3006, FrostArchive 4001–4006을 추가해 총 29종이다. 기존 `HyakkiYagyo` 덱을 유지하고 다음 견본 덱을 추가한다. 모두 30장, 동일 카드 최대 3장이다.

- `HyakkiSample`
- `CrimsonSample`
- `FrostSample`

신규 효과는 기존 `ModifyStats`, `Search`, `DealDamage`, `Destroy`, `Salvage`, `Exile`, `Draw`와 `OnNormalSummoned`, `OnSummoned`, `OnRevived`, 요력 비용, 임시 공격력, `PerTurnLimit`으로 처리한다. 신규 GameCore Operation이나 카드 ID별 런타임 분기는 없다. 4006의 횟수 제한은 기존 정의 ID/효과 ID 단위이고, 3003에는 원본 JSON에 횟수 제한이 없다.

로비는 서버가 내려주는 덱 목록을 사용한다. 덱 편집 화면 상단의 견본 덱 선택 → **견본 덱 복사**로 새 개인 덱을 만든다. 템플릿 ID와 이름은 원본을 유지하고 개인 덱에는 새 ID를 부여한다. 신규 카드도 기존 카드 프레임, 스탯 배지, 일러스트 로더를 사용한다. `Resources/CardSprite/CardSprite_{Id}`가 없으면 기존 Placeholder를 사용한다.

`DeckEditorScreen.prefab`에는 견본 덱 드롭다운과 상세 비용을 위한 공간이 저장되어 있다. 추가 수동 연결은 필요 없다. 기존 자산을 재생성해야 할 때만 `ProjectC/Upgrade Sample Deck Selection` 메뉴를 실행한다. 위치와 크기는 Prefab의 RectTransform으로 편집한다.

## 원본 데이터의 남은 제약

1. `ProjectC_Data - CardData.json`의 3001–3006/4001–4006은 모두 `DescriptionArgs = []`이며, `ProjectC_Data - Localize.json`의 `card_3001_desc`–`card_3006_desc`, `card_4001_desc`–`card_4006_desc`에 숫자가 직접 적혀 있다. 현재 수치는 효과와 일치하지만 밸런스 변경 시 자동 치환되지 않는다. 요청대로 원본은 재작성하지 않았다. 외부 데이터에서 `{0}`와 `DescriptionArgs` 경로를 함께 내보내면 기존 포매터가 처리한다. 예: 3003의 피해량은 `Effects[0].Actions[0].Amount`, 비용은 `Effects[0].Costs[0].Amount`이다.
2. 현재 부활 주문 1101과 1103은 백귀야행 카드군 제한이 있다. 신규 카드 3004/4004의 OnRevived 효과는 구현되어 있으나 현 카탈로그/견본 덱에는 이를 부활시킬 수단이 없다. 규칙을 임의로 완화하지 않았다. 회귀 테스트는 범용 테스트 전용 부활 효과로 트리거와 원본 HP 복구를 검증한다. 이 테스트용 정의는 실행 JSON에 포함하지 않는다.
3. 제공된 Localize 내보내기에 기존 전투 UI 키 26개(`ui.phase.*`, `ui.effect.*`, `ui.card.*`, `ui.turn.*`, `ui.choice.*`, `ui.drop.cancelled`)가 누락되어 기존 저장소의 번역을 복원했다. 신규 UI 키는 `ui.deck.import_sample`, `ui.card.yokai_cost`이다. 향후 외부 내보내기에도 UI 키를 보존해야 한다.
4. 신규 카드 일러스트는 포함되어 있지 않다. 파일을 추가하면 기존 로더가 자동 적용한다. RP/응전 체인은 현재 GameCore에 구현되어 있지 않으며 이번 카드 통합에서 추가하지 않았다.

## 검증 범위

`NewFactionTests.cpp`는 실제 JSON의 신규 12종 효과, 임시 능력치 원복, 카드군/스탯 필터, 비용 부족/잘못된 대상의 원자성, 주문 반복 사용, 횟수 제한 초기화, 부활 HP와 트리거, 명령 재생 일치를 검사한다. `MulliganWebSocketIntegration.py`는 기존 재접속/비공개 정보/시간 초과 검사를 유지하고 세 견본 덱으로 매치를 시작해 멀리건 완료와 replay 검증까지 수행한다.

Unity 테스트에는 신규 카드 Localize/스탯/비용, 견본 덱 원본 이름·구성, 개인 덱 복사의 독립성, 실제 DeckEditorScene의 선택·상세·Placeholder 검사가 추가되어 있다. PlayMode 자동 검증과 별개로 사용 환경에서 두 클라이언트로 견본 덱 매치, 드로우/드래그/공격, 실제 입력으로 효과 선택과 견본 드롭다운 스크롤을 확인한다.

2026-11-03 검증 결과:

- Windows MSVC Debug 전체 C++ 빌드 성공, CTest 9/9 통과. Python 데이터 검증 테스트 6개 포함.
- `python Tools/ExportCardData.py --check` 통과. 서버/Unity 실행 데이터 일치.
- Unity 6000.0.81f1 C# 컴파일 및 PlayMode 37/37 통과: CardLocalizationTests, DeckStorageTests, FrontEndPlayModeTests. 사용자가 열어 둔 프로젝트를 보호하기 위해 동일 Assets/Packages/ProjectSettings를 복사한 격리 프로젝트에서 실행했다. Unity 전체 테스트 모음을 실행한 것은 아니다.
- 세 견본 덱의 실제 WebSocket 매치/멀리건과 기존 세 시나리오, 총 6개 리플레이 검증 통과.
- 기존 개인 덱 Unity 테스트는 멀리건이 활성화된 설정에서 멀리건을 완료하지 않고 EndTurn을 요청하던 절차상 문제가 있었다. 멀리건 완료를 앞 단계에서 명시하도록 수정했다. 격리 프로젝트 실행 시 서버 경로를 재정의하지 못하던 기존 자동 매칭 테스트에도 다른 테스트와 같은 인자 처리를 추가했다.
- 초기 실행의 그래픽 장치 없는 캡처 실패는 그래픽 장치를 사용하는 batchmode 재실행으로 해결했다. 초기 신규 부활 테스트의 백귀야행 전용 주문 오용도 위 테스트 전용 enabler로 수정했다. 최종 실패는 없다.

최종 결과 파일은 `ProjectC.Client/Artifacts/CardIntegration/ctest-final.xml`, `unity-results.xml`이며, 화면 캡처는 같은 폴더의 `NewFactionDeckEditor.png`이다. 실제 마우스/두 클라이언트를 이용한 수동 플레이 검증과 Standalone Player 빌드는 수행하지 않았다.

## 변경 파일

- 데이터: 위 네 실행 JSON, 원래 제공된 CardData/DeckData 내보내기 파일, Localize 내보내기 파일. CardData/DeckData 원본 행은 이번 구현에서 재작성하지 않았다.
- 배포/검증: `Tools/ExportCardData.py`, `Tools/CardDataValidation.py`, `Tools/Tests/TestExportCardData.py`.
- 서버: `Network/CardCatalog.cpp`(엄격한 대상·비용 검증과 카드/효과 위치 진단), `Network.cmake`(테스트 등록).
- 서버 테스트: `Tests/NewFactionTests.cpp`, `CardDataTests.cpp`, `HyakkiComboTests.cpp`, `NetworkTests.cpp`, `MulliganWebSocketIntegration.py`. 기존 테스트는 유지하고 고정 전체 카드 수 기대값만 확장 가능한 검증으로 바꿨다.
- Unity 데이터/상세 표시: `Assets/Scripts/Decks/CardLibrary.cs`, `CardPresentation.cs`, `Assets/Scripts/Battle/CardDetailView.cs`.
- Unity 견본 덱: `Assets/Scripts/FrontEnd/ClientApp.cs`, `DeckEditorPresenter.cs`, `Assets/Editor/AuthorFrontEndAssets.cs`, `UpgradeSampleDeckAssets.cs`와 meta, `Assets/Prefabs/FrontEnd/DeckEditorScreen.prefab`.
- Unity 테스트: `Assets/Tests/PlayMode/CardLocalizationTests.cs`, `FrontEndPlayModeTests.cs`.
- 문서: 이 파일. 전투 상태/명령 DTO, 프로토콜 버전, 효과 실행기, 기존 애니메이션 코드는 변경하지 않았다.
