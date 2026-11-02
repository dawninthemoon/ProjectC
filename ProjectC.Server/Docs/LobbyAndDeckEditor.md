# LobbyScene / DeckEditorScene 1차 구현

2026-11-01. Unity **6000.0.81f1** 유지. 서버와 GameCore 규칙은 변경하지 않았다.

## 실행과 화면 흐름

`ProjectC.Client/Assets/Scenes/LobbyScene.unity`를 열고 Play한다. Windows 빌드도 LobbyScene에서 시작한다. `Project C/Build Windows Multiplayer Client` 메뉴로 빌드하며 출력은 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe`다.

- 로비: 대전 시작 → 서버 주소/접속 → 매치 생성 또는 ID 참가 → 개인 덱 또는 서버 제공 덱 선택 → 양쪽 준비 → BattleScene 자동 이동.
- 덱 관리: DeckEditorScene으로 이동. 최초 실행 시 **백귀야행 복사**로 기본 30장 개인 덱을 만든다.
- 전투의 로비 버튼은 연결을 유지한 채 로비로 이동한다. 진행 중에는 전투 복귀 버튼이 표시된다. 종료 결과에서 나가기를 누르면 매치를 떠난 뒤 로비로 돌아온다.
- 설정: 음량과 전체 화면. 이번 구현에서는 세션 중 적용하며 별도 설정 파일에는 저장하지 않는다.

2026-11-02 업데이트: 개인 덱을 서버에 제출하여 온라인 대전에 사용할 수 있다. 준비 버튼은 로컬 검증과 서버 제출 승인을 거친다. 덱 버튼으로 개인 덱과 기존 서버 제공 덱을 전환한다. 프로토콜·검증·실행 절차는 [PersonalDeckOnline.md](PersonalDeckOnline.md)를 참조한다.

## 저장된 씬과 프리팹

- `Assets/Scenes/LobbyScene.unity`, `DeckEditorScene.unity`
- `Assets/Prefabs/FrontEnd/LobbyScreen.prefab`, `DeckEditorScreen.prefab`
- `Assets/Prefabs/FrontEnd/DeckTile.prefab`, `ClientServices.prefab`

먹색·주홍색·금색과 신사 실루엣을 사용한 독립적인 UI다. 덱 편집은 좌측 상세 / 중앙 덱 / 우측 라이브러리 구성이다. 카드 그림이 없는 항목에는 프레임, 이름, 종류, 능력치와 문양을 표시한다. 화면 전체를 런타임 코드로 생성하지 않는다. 카드 목록만 저장된 DeckTile 프리팹을 인스턴스화한다.

`AuthorFrontEndAssets.Build`와 `Project C/Author lobby and deck editor (explicit)`는 초기 자산 제작용 **명시적 에디터 명령**이다. 실행하면 해당 FrontEnd 씬/프리팹을 다시 작성하므로 수동 레이아웃 수정 후에는 주의한다. 자동 Import/Play 시에는 실행하지 않는다. 기존 BattleScene 파일은 다시 작성하지 않는다.

## 데이터 및 API

| 책임 | 파일/API |
|---|---|
| 개인 덱 | `PersonalDeck`: DeckId, Name, Cards(정의 ID 목록), Copy |
| 편집 버퍼 | `DeckEditSession`: Begin, Draft, Dirty, Cancel, Accept |
| 카드 정의 | `CardLibrary.Load/Find/Filter/Starter` |
| 검증 | `DeckValidator.Validate`, `CannotAdd` |
| 디스크 저장 | `DeckRepository.Load/Save/Select/Delete/Find` |
| 씬 간 서비스 | `ClientApp`: Network, Library, Decks, Navigate |
| UI | `LobbyScenePresenter`, `DeckEditorPresenter`, `DeckTileView`, `ConfirmationDialog` |

UI 참조는 기존 `UnityBindingExtensions.Bind/BindButton`으로 연결한다. `ClientApp`의 ClientServices 루트만 DontDestroyOnLoad로 보존한다. BattlePresenter는 해당 네트워크를 사용하며 BattleScene 단독 실행 시 기존 컴포넌트를 사용하는 경로도 유지한다.

서버 `Data/HyakkiYagyo/Cards.json`, `Decks.json`을 그대로 `Assets/Resources/CardData/HyakkiCards.json`, `HyakkiDecks.json`으로 복사했다. 카드 정의 ID를 재사용하며 17개 백귀야행 정의를 제공한다. 예전 테스트용 4개 카드 카탈로그는 개인 덱 라이브러리에 섞지 않았다. 서버 데이터 변경 시 두 Resources 파일을 함께 갱신한다. 명시적 자산 제작 명령도 원본을 복사한다.

JSON 파싱에는 Unity 공식 `com.unity.nuget.newtonsoft-json` **3.2.2**를 고정했다([공식 패키지 문서](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html)).

개인 덱 저장 위치는 `Application.persistentDataPath/PersonalDecks.json`이다. 카드 정의, 네트워크 토큰, 비공개 게임 상태는 저장하지 않는다.

```json
{
  "Version": 1,
  "SelectedDeckId": "example-deck-id",
  "Decks": [
    { "DeckId": "example-deck-id", "Name": "나의 행렬", "Cards": [1001, 1001, 2001] }
  ]
}
```

빈 덱과 미완성 덱도 저장한다. 대전 구성 검증은 정확히 30장, 동일 정의 최대 3장, MainDeckEligible을 검사한다. UI에서 31번째/4번째 카드와 현현 전용 카드 추가를 차단한다. MainDeckEligible 생략 시 서버 데이터 관례대로 true다. 알 수 없는 카드 ID는 삭제하지 않고 보존하며 오류를 표시하고 덱에서 제거할 수 있다.

파일 크기·항목 수·정수 범위·필수 타입·JSON 중복 속성을 검사한다. 중복 DeckId는 새 ID로 복구하여 두 덱을 보존한다. 잘못된 JSON은 경고하고 가능하면 `.bak`을 읽는다. 다음 저장에서 원본을 `.preserved-*`로 보존한다. 임시 파일과 File.Replace로 교체하며 저장 실패 시 메모리 상태를 성공한 것처럼 갱신하지 않는다. 두 실행 파일에서 같은 개인 덱 파일을 동시에 편집하는 충돌 병합은 구현 범위가 아니다.

이름 변경과 카드 편집은 Draft에 반영한다. 이동·덱 전환·취소·종료 시 미저장 변경이 있으면 저장 후 계속/버리고 계속/돌아가기 선택을 제공한다. 삭제에는 별도 확인을 표시한다. 마지막 선택 덱은 파일에 저장한다.

## 검증과 수동 확인

2026-11-01 검증 결과: Unity PlayMode **12/12** 통과(기존 6 + 신규 6), Windows CTest **4/4** 통과 및 GameCore **308개 그룹** 회귀 없음. Unity 6000.0.81f1 Windows Development 빌드 성공. 실행 결과는 `build/FrontEndPlayMode.xml`, `build/FrontEnd-test.log`, `build/FrontEnd-build.log`에 기록했다. 사용자 에디터와 충돌하지 않도록 별도 검증 프로젝트에서 실행했고, 생성된 씬/프리팹 및 Windows 빌드는 원본 프로젝트에 반영했다. Linux/Docker는 이번 클라이언트 변경에서 재검증하지 않았다. 새 테스트는 임시 저장 경로를 사용하여 실제 개인 덱을 건드리지 않는다.

- DeckStorageTests: 생성/이름 변경/복제/삭제/디스크 재로드, 검증, 알 수 없는 정의 보존, 편집 취소, 잘못된 JSON/선택 ID/중복 속성, 백업 복구, 중복 DeckId 복구.
- FrontEndPlayModeTests: 실제 씬에서 저장·편집·경고·삭제·복원 및 실제 WebSocket 서버의 접속/생성/준비→전투→승리→나가기→로비 복귀. 씬을 넘어 같은 네트워크 인스턴스가 유지되는지 확인한다.
- 기존 Battle PlayMode 테스트도 함께 실행한다. 저장 복원은 Repository 및 ClientApp 재생성으로 검증하며 별도 Windows 프로세스 재실행은 아래 수동 확인에 포함한다.

수동 확인 절차:

1. Windows 실행 파일에서 덱 관리 → 기본 덱 복사 → 이름 변경 → 카드 제거/추가 → 저장. 프로그램을 종료하고 다시 실행해 이름·카드·선택 덱 복원을 확인한다.
2. 새 빈 덱을 저장하고 미완성 표시 확인. 같은 카드를 4번 추가하거나 31장째 추가할 수 없는지, 현현 전용 1007 추가가 차단되는지 확인한다.
3. 검색과 몬스터/주문·카드군·희귀도 필터를 조합한다. 카드 호버 상세와 양쪽 카드 목록 스크롤을 확인한다.
4. 편집 후 뒤로/다른 덱 선택/취소/프로그램 종료를 시도해 저장·버림·돌아가기 각각 확인한다. 덱 복제와 삭제 확인/취소도 확인한다.
5. 서버를 실행하고 클라이언트 두 개에서 같은 매치에 참가·준비한다. 소환·공격·재접속·재대전과 종료 후 로비 복귀를 확인한다. 두 창에서 개인 덱 파일을 동시에 편집하지 않는다.
6. 1920×1080 및 다른 창 크기에서 카드/버튼/드롭다운·모달 가독성과 입력을 확인하고 전체 화면·음량 설정을 확인한다.

개인 덱 온라인 연결은 후속 1단계 구현에 포함되었다. 새 카드 효과나 게임 규칙은 추가하지 않았다.
