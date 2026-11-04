# macOS 개발 계정 프로필

## 적용 위치와 메뉴 원인

macOS 구현을 기존 ProjectC 저장소의 `codex/integrate-macos-account-profiles` 브랜치에 통합했다.
기존 프로젝트의 `ProjectC.Client`를 Unity에서 열면 된다. 별도 macOS 전용 프로젝트를 열 필요가 없다.
통합 기준은 최신 main `e7c104b`이며 기존 로컬 미완료 변경은 커밋에서 제외하여 작업 폴더에 보존했다.

조사 당시 원래 checkout은 `09e4045`였고 Windows 프로필 구현 파일 자체가 없었다.
Windows 구현이 포함된 로컬 `origin/main` (`3defc78`)을 기반으로 작업했다.
해당 구현의 `DeveloperAccountProfileSettings`는 이미 `UNITY_EDITOR`, 개발 패널은
`UNITY_EDITOR || DEVELOPMENT_BUILD`로 보호되어 있어 macOS 메뉴를 배제하지 않았다.
따라서 확인한 checkout의 메뉴 부재 원인은 Windows 전용 메뉴 조건이 아니라 코드 버전 차이다.
실제로 Windows 전용이었던 부분은 `SecureSessionStore`의 네이티브 저장 코드와 보안 저장 테스트 조건이었다.

macOS Editor의 Play Mode 진입 전에 다음 네 메뉴를 사용할 수 있다.

- Project C → Development → Account profile → default
- Project C → Development → Account profile → dev-editor
- Project C → Development → Account profile → dev-client-1
- Project C → Development → Account profile → dev-client-2

기존 프로젝트 경로별 EditorPrefs 설정과 개발자 패널을 재사용한다. EditorPrefs에는 프로필 ID만 저장한다.
개발자 패널은 현재 AccountProfileId/Website PlayerId, 변경, 현재 프로필 로그아웃을 기존처럼 제공한다.
새로운 Inspector 연결, Scene/Prefab 설정은 필요 없다.

## 저장 방식

기존 `ISessionStore`와 `SecureSessionStore` 진입점을 유지하고 네이티브 구현만 분리했다.
Editor에서는 활성 Build Target 대신 실제 Editor OS에 맞는 구현을 선택한다.
지원하지 않는 OS는 `PlatformNotSupportedException`으로 중단한다.

| 플랫폼 | 구현 | 저장 키 |
|---|---|---|
| Windows | WindowsCredentialSessionStore / Credential Manager | 기존 Target 그대로 |
| macOS | MacKeychainSessionStore / generic-password | service=`ProjectC.Unity.Session`, account=기존 TargetFor 결과 |

공통 Target은 `default`에서 정확히 `ProjectC.Session/<끝의 슬래시를 제거한 API URL>`이다.
개발 프로필은 `ProjectC.Session.Profile/<AccountProfileId>/<API URL의 소문자 SHA-256>`이다.
Windows default 저장 키나 기존 계정에 대한 마이그레이션·복사·삭제는 없다.

Keychain은 Security.framework의 SecItemCopyMatching/Update/Add/Delete를 직접 호출한다.
Access Token, Refresh Token 및 기존 세션 메타데이터 JSON을 하나의 UTF-8 데이터 항목으로 저장하여
토큰 쌍을 함께 교체한다. 기존 Windows와 같은 2,560바이트 제한을 적용한다.
프로필과 API URL이 모두 같은 항목만 조회·갱신·삭제한다. iCloud 동기화를 사용하지 않는다.
토큰을 PlayerPrefs, 파일, 명령줄 또는 로그에 기록하지 않는다.

항목 없음(`errSecItemNotFound`)만 신규 계정 생성을 허용한다. Keychain 잠금, 접근 거부,
중복 생성 등 다른 오류는 OSStatus를 포함한 오류로 전달하고 연결 화면/계정 팝업에 표시한다.
읽기 실패를 빈 계정으로 처리하거나 다른 프로필을 지우지 않는다.
새 게스트 응답이나 회전된 토큰 저장 실패 시 응답을 메모리에 보존해 재시도하며,
재시도가 불필요한 게스트 생성이나 중복 refresh를 발생시키지 않는다.
저장 실패 상태의 프로필 전환도 완료하지 않는다. 프로세스 종료까지 견디는 평문 대체 저장소는 만들지 않았다.

## 전환과 로그아웃

기존 ClientApp의 전환 세대 번호와 WebsiteSession의 세마포어를 그대로 사용한다.

1. 기존 WebSocket, 매칭 대기, 재접속 토큰 및 계정별 화면 상태를 정리한다.
2. 이전 세션의 새 요청을 막고 진행 중인 refresh와 토큰 저장 완료를 기다린다.
3. 선택한 API URL/프로필의 저장 정보를 복원한다. 필요하면 JWT를 refresh한다.
4. Website 사용자·소유 덱·랭크를 다시 가져와 ConnectionScene에서 연결한다.

현재 프로필 로그아웃은 기존 API로 refresh family를 revoke한 뒤 해당 항목만 삭제한다.
실패하면 세션 객체와 미저장 응답을 보존하고 오류를 표시한다. 다른 프로필 항목은 유지한다.
성공 후 로그아웃 상태에서 임의로 새 게스트를 생성하지 않는 기존 동작을 유지한다.

## macOS 실행

Development Build를 켠 플레이어에서만 인자를 사용한다. 실행 파일명은 Product Name에 맞춘다.
현재 프로젝트의 실행 파일명은 `ProjectC.Client`다.

```sh
./ProjectC.app/Contents/MacOS/ProjectC.Client --account-profile=dev-client-1
./ProjectC.app/Contents/MacOS/ProjectC.Client --account-profile=dev-client-2
```

인자가 없으면 default다. 개발 빌드의 알 수 없는/중복 프로필 인자는 설정 오류다.
Release는 유효하지 않은 인자까지 모두 무시하고 default를 사용하며 개발 패널과 Editor 설정은 제외된다.
동시에 테스트하는 프로세스에는 서로 다른 프로필을 사용한다. 동일 프로필의 프로세스 간
Refresh Token 동시 회전 조정은 기존과 같이 지원하지 않는다.

## 검증 결과 — 2026-11-04

환경: macOS Apple Silicon, Unity 6000.0.81f1. 운영 API/Neon DB는 사용하지 않았다.

| 검증 | 결과 / 범위 |
|---|---|
| Editor 메뉴 | 네 항목 실제 등록 및 Play 진입 전 ExecuteMenuItem 선택 성공, 원래 설정 복원 |
| Keychain/인증 오류/기존 WebsiteSession PlayMode | 13/13 통과, 실제 Keychain 읽기·쓰기·갱신·삭제 및 API/프로필 격리 |
| 기존 AccountProfile/Matchmaking PlayMode | 12/12 통과, 실제 ClientApp 전환·복원·로그아웃 및 타 프로필 보존 |
| Windows Dev/Release 스크립트 | 각각 컴파일 성공, 저장소 선택 및 개발 기능 포함/제외 검사 통과 |
| macOS Dev/Release 스크립트 | 각각 컴파일 성공, 저장소 선택 및 개발 기능 포함/제외 검사 통과 |
| macOS Development 실행 파일 | dev-client-1, dev-client-2, 무인자 default 확인; 세 실행 모두 네이티브 Keychain round-trip 성공 |
| macOS Release 실행 파일 | 개발 인자 / 잘못된 중복 인자 모두 default, 개발 UI 없음; 두 실행 모두 Keychain round-trip 성공 |
| Windows 소스 호환 | 네이티브 메서드/선언은 플랫폼 가드·빈 줄 외 동일, TargetFor는 동일 |

총 PlayMode **25/25 통과**. AccountProfile 통합 테스트는 로컬 모의 Website API에서 별도 게스트와 덱을
만들고 실제 C++ Battle Server를 실행한다. 서로 다른 Website PlayerId 복원, 프로필 전환,
refresh 대기, 현재 항목 로그아웃과 나머지 항목 보존을 검증했다.
운영 Website에서 실제 게스트를 발급한 테스트는 아니다.
오류 주입 테스트는 읽기·쓰기·회전·삭제 실패 시 새 게스트를 생성하지 않고 복구하는지 검사한다.
Keychain 오류 상태 매핑은 검사했지만 사용자의 실제 Keychain을 잠그거나 ACL을 변경하지 않았다.

결과 XML, 프로필별 실행 결과 JSON 및 요약은
[Artifacts/MacAccountProfiles/Validation](../Artifacts/MacAccountProfiles/Validation)에 있다.
실행 프로브는 고유한 `.invalid` API URL 키에 테스트 문자열만 저장하고 정리한다.
출력 JSON에는 토큰이 없다. 프로브 타입은 `PROJECTC_ACCOUNT_PROFILE_PROBE`를 명시한 별도
검증 빌드에만 포함되며 일반 제품 빌드에는 포함되지 않는다.

재실행 도구:

- `VerifyAccountProfileMenus.Run`: macOS Editor 메뉴 등록/선택
- `VerifyAccountProfilePlayers.Development` / `.Release`: Windows 스크립트
- `VerifyAccountProfilePlayers.MacDevelopment` / `.MacRelease`: macOS 스크립트
- `BuildAccountProfileProbe.Development` / `.Release`: 별도 macOS 실행 프로브 빌드
- PlayMode 클래스: `MacKeychainPlayModeTests`, `SecureSessionFailurePlayModeTests`,
  `WebsiteSessionPlayModeTests`, `AccountProfilePlayModeTests`, `MatchmakingPlayModeTests`

각 스크립트 컴파일 도구는 별도 batchmode 프로세스에서 실행한다.
프로브는 `ProjectC.Client/Build/AccountProfilePlayers/{Development,Release}/ProjectCProfileProbe.app`에 생성된다.
프로브에 `--profile-probe-output=<절대 JSON 경로>`를 주어 결과를 남긴다.
통합 테스트는 기존 `-projectcServerPath=<로컬 서버 실행 파일>` 인자를 받는다.

### 미검증 / 플랫폼 제한

Windows 네이티브 Credential Manager 실행은 이 macOS 환경에서 검증하지 않았다.
macOS GUI에서 수동 메뉴/팝업 클릭, Keychain 잠금·권한 거부 대화상자, Intel 실행 및 IL2CPP는 미검증이다.
서명이나 실행 파일 신뢰 관계가 달라지면 macOS가 Keychain 접근 허용을 요청할 수 있다.
권한을 우회하지 않으며, 거부 시 오류 처리한다. 전체 프로젝트 테스트를 실행한 것은 아니다.

## 변경 파일

경로는 `ProjectC.Client/` 기준이다. 신규 C#에는 대응하는 `.meta`도 추가했다.

- `Assets/Scripts/Network/SecureSessionStore.cs`: 공통 facade와 OS 선택, 기존 키 유지
- `Assets/Scripts/Network/WindowsCredentialSessionStore.cs`: 기존 Windows 구현 분리
- `Assets/Scripts/Network/MacKeychainSessionStore.cs`: macOS 네이티브 저장소
- `Assets/Scripts/Network/SecureSessionStoreException.cs`: 보안 저장소 오류
- `Assets/Scripts/Network/WebsiteSession.cs`: 저장 실패 시 토큰 쌍 보존 및 재시도
- `Assets/Scripts/FrontEnd/ClientApp.cs`: 실패한 로그아웃 세션 보존, 오류 표시
- `Assets/Scripts/FrontEnd/ConnectionScenePresenter.cs`: Keychain 오류 표시
- `Assets/Scripts/FrontEnd/AccountProfilePlayerProbe.cs`: 조건부 검증 플레이어
- `Assets/Editor/VerifyAccountProfileMenus.cs`: 메뉴 검증
- `Assets/Editor/VerifyAccountProfilePlayers.cs`: 양 플랫폼 컴파일 검사
- `Assets/Editor/BuildAccountProfileProbe.cs`: macOS 검증 실행 파일 빌드
- `Assets/Tests/PlayMode/AccountProfilePlayModeTests.cs`: macOS 테스트 허용, 씬/코루틴 종료 후 서비스 정리
- `Assets/Tests/PlayMode/WebsiteSessionPlayModeTests.cs`: macOS 네이티브 테스트 허용
- `Assets/Tests/PlayMode/MacKeychainPlayModeTests.cs`: Keychain 통합 검사
- `Assets/Tests/PlayMode/SecureSessionFailurePlayModeTests.cs`: 인증 저장 실패 검사
- `Docs/DevelopmentAccountProfiles.md`, `Docs/MacAccountProfiles.md`: 사용법과 검증 기록
- `Artifacts/MacAccountProfiles/Validation/*`: 테스트 결과

GameCore, C++ 서버, WebSocket v2, Website API, Neon DB, Scene/Prefab은 변경하지 않았다.

## 기존 저장소 통합 검증

2026-11-04, main `e7c104b` 기준으로 기존 ProjectC 작업 폴더에 통합했다.
새 `.meta`의 GUID와 대응 C# 파일을 확인했으며 Windows의 TargetFor는 동일하다.
네이티브 Credential Manager 메서드와 선언도 플랫폼 가드를 제외하면 변경하지 않았다.

기존 로컬 `MultiplayerDebugScreen.cs`의 연결 종료 수정과 최신 main이 충돌했다.
로컬 transport 참조 해제·카탈로그 초기화와 최신 main의 Input.Reset을 모두 유지했다.
기존 로컬 수정은 PR에 포함하지 않았고 작업 폴더에 미커밋 상태로 보존했다.
원본 파일 백업 및 별도 Git stash도 유지한다. URP 설정 변경은 최신 main과 이미 같았다.

최종 PlayMode 실행은 다음 여섯 클래스다.
MacKeychainPlayModeTests, SecureSessionFailurePlayModeTests, WebsiteSessionPlayModeTests,
AccountProfilePlayModeTests, MatchmakingPlayModeTests, GameplayInputPlayModeTests.

- PR 범위만 적용한 상태: **32/32 통과**, skipped 0.
- 기존 로컬 변경을 함께 적용한 상태: **32/32 통과**, skipped 0.
- 비교용 main + 기존 로컬 변경의 GameplayInput 단독 실행: **7/7 통과**.

통합 중 한 프로세스에서 계정 테스트 다음 입력 UI 테스트를 실행하자 씬 연결 처리의 잔여 작업이
다음 fixture에 영향을 주었다. 계정 테스트의 종료 순서를 보완했다. 연결 코루틴 중단 및 진행 중
씬 전환 완료 후 씬을 내리고 서비스를 파괴하며, ClientApp이 남지 않았는지 검사한다.
게임 UI나 입력 규칙은 변경하지 않았다. 재실행 결과가 위 최종 결과다.

최신 XML은 Validation의 `pr-playmode-final.xml`, `combined-workspace-final.xml`,
`baseline-gameplay-input.xml`이다. macOS 보안 저장 테스트는 실제 Keychain을 사용했고
계정/서버 통합은 로컬 모의 Website API와 실제 C++ 서버를 사용했다.
Windows 네이티브 실행은 이번 macOS 통합 환경에서도 미검증이다.
