# 카드 데이터와 로컬라이제이션

## 현재 입력과 호환성

Google Sheets 원본은 Unity Resources에 보관한다.

- `ProjectC.Client/Assets/Resources/ProjectC_Data - CardData.json`
- `ProjectC.Client/Assets/Resources/ProjectC_Data - Localize.json`

기존 `Cards.json`은 `{CatalogVersion, Cards}` 객체, 정수와 실제 Effects 배열, 직접 표시하는 Name을 사용했다. 신규 원본은 행 배열이며 Id/Attack/MaxHp가 문자열이고 Archetypes/AllowedManifestations/Effects/DescriptionArgs가 JSON 문자열이다. Name 대신 NameKey/DescriptionKey를 사용한다. 17장 전부 비교했으며 효과와 능력치는 기존 백귀야행 데이터와 같다. 주문의 생략된 Attack/MaxHp는 기존 로더 기본값을 유지한다.

신규 원본에는 1007번의 기존 `MainDeckEligible: false`가 없다. 이 규칙은 `Data/HyakkiYagyo/CardMetadata.json`에 명시적으로 보존했다. 시트에 해당 열을 추가하면 원본의 명시적 값을 우선한다. 신규 행 배열을 서버 Cards.json에 직접 복사하지 말고 아래 명령으로 배포한다. 그래야 생략된 호환성 메타데이터도 포함된다.

C++ CardCatalog와 Unity CardDataLoader는 기존 객체 스키마와 Sheets 행 배열을 모두 지원한다. C++ CardDefinition에는 NameKey/DescriptionKey/DescriptionArgs를 별도 보관한다. Name은 기존 코드·와이어 호환성을 위해 남겨 두었지만 더 이상 신규 데이터의 필수 필드는 아니다. Name이 없으면 NameKey를 호환 값으로 사용한다. 한국어 번역은 GameCore에 하드코딩하지 않는다. 기존 기본 테스트 카탈로그는 변경하지 않는다.

## 갱신 명령

저장소 루트에서 실행한다. Python 표준 라이브러리만 사용한다.

```powershell
python Tools/ExportCardData.py
python Tools/ExportCardData.py --check
```

첫 명령은 숫자·중첩 JSON·중복 ID/속성/번역 키·번역 키 참조·인수 경로/인덱스·키워드 정의·버전을 검사하고, 동일한 정규화 JSON을 다음 두 경로에 생성한다.

- 서버: `ProjectC.Server/Data/HyakkiYagyo/Cards.json`
- Unity: `ProjectC.Client/Assets/Resources/CardData/HyakkiCards.json`

CatalogVersion은 `시트 버전-정규화 카드 데이터 SHA256 앞 12자리`다. 시트 버전을 수동으로 올리지 않아도 카드 내용이 바뀌면 배포 버전이 바뀐다. 번역 문구만 바꿀 때는 카드 규칙 버전을 바꾸지 않는다. `--check`는 쓰지 않고 산출물의 일치를 검사하며, CTest의 CardDataExportCheck에도 포함된다. 배포 전 C++ 로더/회귀 테스트로 전체 효과 스키마와 규칙도 검증한다. 이 스크립트가 효과 실행기를 대신하지 않는다.

정규화 후 서버와 Unity를 함께 빌드·배포한다. 서버 `--data`에는 기존처럼 HyakkiYagyo 디렉터리를 지정한다. 서버가 알려 준 버전과 로컬 버전이 다르면 로비에 오류를 표시하고 자동 매칭/준비를 막는다. 진행 중인 매치가 불일치 상태로 복구되면 로컬의 잘못된 효과 수치를 추측하지 않고 서버 RulesText를 표시한다. 버전 없는 기존 기본 테스트 서버는 호환 경로를 유지하므로 이 검사만으로 검증할 수 없다.

## Unity 텍스트 처리

CardLibrary는 정규화 데이터를 로드하고, LibraryCard는 NameKey/DescriptionKey와 읽기 전용 설명 데이터 사본을 보관한다. CardPresentation은 서버의 `DefinitionId`로 조회한다. 이 와이어 필드는 서버의 `CurrentDefinitionId`이므로 현현 후 이름·설명이 바뀐다. 공격력/HP, 실행 가능 상태, 명령 판정은 계속 서버 스냅샷과 GameCore가 결정한다. 알 수 없는 정의는 기존 서버 텍스트로 표시한다.

LocalizationService의 기본 언어는 korean이다. 지원 열은 korean, english, japanese, zh-tw, zh-cn, portuguese다. 조회 순서는 선택 언어 → korean → english → 키 표시다. 존재하지 않는 키/모든 번역이 비어 있는 키는 오류를 언어·키별 한 번 기록하고 키 자체를 반환한다. 중복 lmk는 로딩 오류다.

CardTextFormatter는 객체 속성과 배열 인덱스만 허용한다. 예를 들어 `Effects[0].Costs[0].Amount`가 있으면 그 값을 사용하며, 0/false/빈 문자열도 실제 값으로 취급한다. 경로가 없거나 null일 때만 default를 사용한다. default도 없으면 FormatException으로 실패한다. 수식·함수·와일드카드·음수 인덱스는 허용하지 않는다. `{0}`, `{1}`의 범위를 검사하며, `{{`/`}}`는 리터럴 중괄호다.

`{표시:@KeywordId}`는 TextSegment 메타데이터로 보존한다. KeywordLinkView는 이를 TMP 링크로 렌더링하고 `keyword_KeywordId_name/desc`를 조회한다. 덱 편집기의 설명 링크를 클릭하면 기존 상태 영역에 설명을 표시한다. 전투 카드 상세도 같은 링크 컴포넌트를 사용하며, 선택된 키워드 설명은 기존 Forms 영역에 표시한다. 기존 호버 종료 동작과 상세창 구조는 유지한다. 링크 렌더링은 임의의 번역/인수 TMP 태그를 이스케이프한다. 복잡한 툴팁 연출은 추가하지 않았다.

기존 UI 오브젝트/프리팹과 Harmonic SDF 폰트를 사용한다. 에디터에서 수동 참조 할당은 필요 없다.

## 검증 결과

- 로더/포맷터를 먼저 분리 검증: Unity 20/20 통과 (`build/CardLocalization-unit.xml`).
- UI 연결 후 전체 Unity PlayMode: 46/46 통과 (`build/CardLocalization-all.xml`). 기존 23개와 신규 23개이며, 실제 서버 개인 덱·매칭·재접속·멀리건·전투 테스트를 포함한다.
- CMake Debug 빌드 및 CTest: 8/8 통과. GameCore/Network와 기존 WebSocket 통합 4개, 신규 CardDataTests/CardDataExportCheck를 실행했다.
- Windows x64 Development 빌드 성공 (`build/CardLocalization-build.log`). 기존 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe` 경로에 갱신했다.
- `python Tools/ExportCardData.py --check`, `git diff --check` 통과.

키워드 포인터 처리의 메타데이터 연결/선택 콜백은 Unity 테스트에서 확인했다. 실제 두 Windows 창의 수동 클릭과 해상도별 시각 검사는 별도로 실행하지 않았다.
