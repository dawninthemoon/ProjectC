# 개발용 다중 계정 프로필

Windows Credential Manager의 게스트 세션을 Website API URL과 AccountProfileId로 구분한다.
서버가 발급한 Website PlayerId(UUID)를 그대로 사용하며, 프로필 이름으로 플레이어 UUID를 만들지 않는다.
GameCore, C++ 게임 규칙, WebSocket 프로토콜 버전 2는 변경하지 않았다.

## 실행 방법

지원하는 프로필은 `default`, `dev-editor`, `dev-client-1`, `dev-client-2`다.
첫 실행에서 프로필을 지정하지 않으면 기존과 동일한 `default`를 사용한다.

Unity Editor:

1. Play 모드에 들어가기 전에 **Project C → Development → Account profile → dev-editor**를 선택한다.
2. 이 선택은 프로젝트 경로별 EditorPrefs에 프로필 이름만 저장한다. JWT나 Refresh Token은 넣지 않는다.
3. Play 모드에서는 화면의 **개발 계정 프로필 → 계정 프로필 관리**에서 변경할 수 있다.
   현재 AccountProfileId와 Website PlayerId를 표시한다. Play 중 선택은 다음 Play 실행에도 사용한다.

Windows 빌드:

1. Unity Build Profiles에서 Windows용 **Development Build**를 켜고 빌드한다.
2. 서로 다른 프로필을 지정해 실행한다.

```powershell
.\ProjectC.exe --account-profile=dev-client-1
.\ProjectC.exe --account-profile=dev-client-2
```

개발 빌드도 인자가 없으면 `default`다. 인자는 정확히 한 번만 지정할 수 있다.
알 수 없는 ID와 중복 인자는 개발 빌드에서 설정 오류다.

일반 **Release 빌드는 항상 default**를 사용한다. 프로필 실행 인자와 Editor 설정을 무시하며,
개발 프로필 패널 타입과 표시 코드도 플레이어 컴파일에서 제외한다.

로컬 검증 시 `ServerConnectionSettings`에서 Local 환경의 API/WebSocket 주소와
`Local Website Enabled`를 설정한다. 이 옵션이 꺼진 기존 로컬 견본 덱 모드에서는 Website PlayerId가 없다.
운영 주소로 실행하면 해당 API에서 계정을 복원/생성하므로, 자동 테스트는 아래의 로컬 환경만 사용한다.

## 저장 키와 호환성

API URL은 기존 WebsiteSession과 같이 끝의 `/`를 제거한다.

| 프로필 | Credential Manager Target |
|---|---|
| `default` | `ProjectC.Session/<API URL>` — 기존 키 그대로 |
| 개발 프로필 | `ProjectC.Session.Profile/<AccountProfileId>/<API URL의 SHA-256>` |

개발 프로필은 별도 네임스페이스를 사용한다. URL 해시는 다른 URL/프로필 조합의 문자열 경계가
모호해지지 않도록 한다. 기존 default 값을 복사·이동·삭제하는 마이그레이션은 없다.
Access/Refresh Token 쌍은 기존처럼 OS 보안 저장소에 원자적으로 저장한다.
같은 API URL과 같은 프로필로 실행한 두 프로세스는 같은 계정을 사용한다. 동시에 대전할 프로세스에는
**서로 다른 프로필**을 지정한다. 이 기능은 동일 프로필의 여러 프로세스 간 Refresh Token 동시 회전을 조정하지 않는다.

## 전환과 로그아웃

프로필 변경은 다음 순서로 진행한다.

1. 기존 WebSocket을 종료하고 매칭 대기, 게임 화면 상태, 요청 ID와 기존 SessionToken을 정리한다.
2. 이전 세션은 새 HTTP 요청을 받지 않는다. 이미 진행 중인 Refresh Token 회전은 취소하지 않고
   완료 및 보안 저장을 기다린다. 이전 프로필의 자격 증명은 유지한다.
3. 선택한 프로필의 세션을 복구한다. 저장 정보가 없을 때만 새 게스트를 생성한다.
4. 사용자 정보·Website 덱 목록·랭크를 다시 조회하고 ConnectionScene에서 새 계정으로 연결한다.

전환 번호와 세션 객체를 확인하여 이전 프로필의 늦은 로그인/덱/랭크 응답이나 매칭 요청이 새 프로필에
적용되지 않게 한다. 프로필 전환 자체는 다른 프로필의 토큰을 revoke하거나 덱·랭크를 삭제하지 않는다.
일반적인 연결 끊김에서는 기존 SessionToken 재접속이 유지된다. 프로필을 바꿀 때만 이전 계정의
재접속 토큰을 버린다. 진행 중 대전에서 전환하면 기존 서버의 연결 종료 유예가 적용되므로,
이전 계정으로 즉시 돌아가면 유예가 끝날 때까지 AccountBusy가 발생할 수 있다.

**현재 프로필 로그아웃**은 해당 프로필의 refresh family를 revoke하고 해당 Credential Manager 항목만
삭제한다. 로그아웃 상태에서는 새 게스트를 자동 생성하지 않는다. **현재 프로필로 로그인**을 누르거나
다른 프로필을 선택하여 로그인한다. 다른 프로필의 인증 정보와 Website 덱/랭크는 그대로다.
로그아웃한 게스트의 DB 계정/덱/랭크도 삭제하지 않지만, 복구 수단 없이 유일한 토큰을 삭제하면
같은 게스트에 다시 접근하지 못할 수 있다. 다시 로그인하면 새로운 게스트가 생성될 수 있다.
API 오류로 revoke를 완료하지 못한 경우 로그인 정보를 보존하고 오류 팝업을 표시한다.

Website 덱과 랭크는 사용자 UUID별로 API가 관리한다. 기존 로컬 DeckEditor 파일의 저장 위치는 변경하지
않았다. 프로필 전환/로그아웃은 로컬 덱 파일도 삭제하지 않는다.

## 검증

Unity PlayMode에서 다음 세 클래스를 실행한다.

- `AccountProfilePlayModeTests`: 새 프로필 기능 5개 테스트
- `WebsiteSessionPlayModeTests`: 기존 세션/보안 저장/토큰 회전 테스트
- `MatchmakingPlayModeTests`: 기존 매칭/취소/재접속 메시지 상태 테스트

이번 작업에서 총 **17/17 통과**했다. 테스트는 고유한 loopback API URL의 Windows Credential Manager
항목만 생성·제거한다. 로컬 모의 Website API와 실제 C++ Battle Server를 실행하여 서로 다른 두 프로필이
다른 PlayerId와 소유 덱을 복원하고 같은 매치에 들어가 자동 시작하는 것을 검증한다.
또한 실제 ClientApp의 프로필 전환, 이전 프로필 복원, 로그아웃 후 자동 게스트 생성 방지,
다른 프로필의 토큰·덱·랭크 보존, 진행 중 회전 완료 대기와 SessionToken 정리를 검사한다.
운영 API/DB에는 요청을 보내지 않는다.

```powershell
Unity.exe -batchmode -nographics -projectPath C:/Dev/ProjectC/ProjectC.Client `
  -runTests -testPlatform PlayMode `
  -testFilter "ProjectC.Tests.AccountProfilePlayModeTests;ProjectC.Tests.WebsiteSessionPlayModeTests;ProjectC.Tests.MatchmakingPlayModeTests" `
  -projectcServerPath C:/Dev/ProjectC/ProjectC.Server/out/build/x64-Debug/ProjectCNetworkServer.exe `
  -testResults C:/Dev/ProjectC/ProjectC.Client/Logs/AccountProfileTests.xml
```

Editor 전용 `VerifyAccountProfilePlayers.Release`와 `VerifyAccountProfilePlayers.Development`는 Windows
플레이어 스크립트를 각각 컴파일하고 개발 기능 상수, 패널 타입의 포함 여부 및 Editor 설정의 제외를
검사한다. 두 검증은 각각 별도 batchmode 프로세스에서 실행한다. 전체 그래픽 플레이어 빌드와 수동 UI
조작 테스트를 대신하는 검증은 아니다.

Unity 6000.0.81f1의 Release 및 Development Windows 플레이어 스크립트 컴파일과 위 포함/제외 검증도
각각 통과했다. Release에서 개발 패널 타입과 Editor 전용 설정이 없고, Development에서만 패널이
포함되는 것을 컴파일 결과의 어셈블리 메타데이터로 확인했다.
