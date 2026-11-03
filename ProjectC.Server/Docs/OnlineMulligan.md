# 온라인 멀리건 — 2단계

2026-11-02. Unity 6000.0.81f1 / C++20. 기존 개인 덱 온라인 제출 결과를 보존하며, 응전·체인 초안은 제거했다. 이번 단계에는 응전·체인 구현이 없다.

## 실행과 설정

2026-11-03부터 백귀야행 데이터는 `EnableMulligan: true`로 시작한다. 기본 소규모 테스트/Legacy 데이터는 기존대로 비활성화한다. 멀리건은 서버 설정이며 `--mulligan on|off`가 데이터 설정을 덮어쓴다. CreateMatch/JoinMatch와 QueueMatch는 동일한 MatchSession 준비 흐름을 사용한다.

```sh
cmake -S ProjectC.Server -B build -DCMAKE_BUILD_TYPE=Debug
cmake --build build --config Debug
ctest --test-dir build -C Debug --output-on-failure
./build/ProjectCNetworkServer --data ProjectC.Server/Data/HyakkiYagyo --mulligan on
```

Windows에서는 빌드 구성에 따라 `build/Debug/ProjectCNetworkServer.exe`를 사용한다.

| 설정 | 기본값 | 의미 |
|---|---|---|
| Decks.json Config.EnableMulligan | false (생략 가능) | 준비 단계 활성화 |
| --mulligan on/off | 데이터 설정 사용 | 서버 전체 매치의 데이터 설정을 명시적으로 재정의 |
| --mulligan-timeout | 60초 | 양쪽 준비 완료 시점부터 공통 마감, 허용 범위 1~3600초 |
| --mulligan-auto-keep on/off | on | 마감 시 미완료자에게 0장 교체 적용. off이면 MulliganTimeout으로 매치 종료 |

활성화 시 초기 손패는 정확히 5장이어야 하며 각 덱은 최소 10장, 손패 상한에는 기존 첫 턴 드로우를 수용할 공간이 있어야 한다. 잘못된 설정은 서버 시작/초기화 단계에서 거절한다. 기존 소형 테스트 데이터에는 `--mulligan off`를 사용한다. 카드 카탈로그 버전, 개인 덱 저장 형식과 카드 효과는 변경하지 않았다.

## 상태 전이와 GameCore

```text
WaitingForPlayers → Ready → Mulligan → Playing → Finished
                              │
                              └─ 제한 시간: 미완료자 0장 자동 확정 / 설정에 따라 Closed
```

- MatchSession의 `Mulligan`과 GameCore의 `PreparationPhase::Mulligan`을 추가했다. `TurnPhase`와 기존 Main/Battle/End 숫자는 바뀌지 않았다.
- 양쪽 덱 확정·셔플 및 초기 5장 배분 후 BattleScene에 진입한다. 이 시점에는 `Started=true`, `TurnStarted=false`다. 첫 턴 드로우는 아직 하지 않는다.
- `SubmitMulliganCommand{Player, Cards}`는 본인 손패 InstanceId를 0~5개 받는다. 정의 ID가 아니다. 중복/위조/상대 카드/이미 완료한 플레이어의 새 제출은 상태를 바꾸지 않는다.
- 후보 상태에서 선택 카드를 임시 분리 → 남은 덱 맨 위에서 같은 수를 손패로 이동 → 분리 카드를 덱으로 반환 → 덱 셔플 → 완료를 커밋한다. 0장 교체는 손패와 덱 순서를 유지한다.
- 같은 DefinitionId의 다른 복사본은 뽑힐 수 있다. **선택한 InstanceId 자체는 교체 드로우에 포함되지 않는다.**
- GameCore는 두 플레이어가 모두 완료하면 기존 `StartTurnCommand`를 정확히 한 번 실행한다. 백귀야행의 기존 규칙에 따라 선공은 이때 1장을 추가로 뽑아 6장이 되고 요력은 초기 2를 유지한다. 자기 두 번째 턴부터 요력 +1이라는 기존 동작은 바뀌지 않는다. 반환·셔플 이후의 정상 턴 드로우에서는 반환 카드가 나올 수 있다.
- 준비 중 소환·주문·공격·턴/단계 진행·직접 변형 등 일반 GameCore 명령은 거절된다. 후보 복사/검증/Commit 원칙을 유지한다.
- `MulliganRandom`은 서버의 매치 seed에서 분리한 좌석별 SplitMix64 상태다. rejection sampling을 쓰는 Fisher–Yates로 플랫폼별 분포 구현 차이를 피한다. 클라이언트가 seed나 결과 순서를 제출하지 않는다.
- 선택 ID의 제출 순서는 정규화하고, 두 좌석은 독립 RNG를 사용한다. 같은 초기 상태에서 제출 순서를 바꾸어도 최종 손패/덱/RNG 상태가 같다.
- Replay 초기 상태에 비공개 RNG 상태가 포함되고, 수동/타임아웃 제출 모두 같은 명령으로 기록된다. 서버 시간이나 새 난수를 Replay 도중 읽지 않는다.

## 네트워크 계약

ProtocolVersion 2의 추가 명령이며 기존 SubmitCommand 봉투를 재사용한다.

```json
{
  "Type": "SubmitCommand",
  "ProtocolVersion": 2,
  "RequestId": "unique-id",
  "MatchId": "M1-example",
  "GameNumber": 1,
  "SequenceNumber": 1,
  "Command": {"Type": "SubmitMulligan", "CardIds": [17, 21]}
}
```

- 플레이어는 연결에 바인딩된 좌석에서 결정한다. MatchId, GameNumber, RequestId, 시퀀스 범위와 배열/정수 형식을 검증한다.
- 준비 중 `SubmitMulligan`만 해당 게임의 준비 시퀀스 `1..현재 SequenceNumber`를 허용한다. 양쪽이 같은 초기 스냅샷을 보고 동시에 제출해도 다른 플레이어의 완료 때문에 거절되지 않는다. 미래 시퀀스/0/다른 게임 번호는 거절하며 일반 전투 명령은 기존 정확한 시퀀스 일치를 유지한다.
- 서버는 기존 단일 io_context 스레드에서 직렬 처리한다. 동일 RequestId/동일 본문 재전송은 이전 응답만 재전달하고 다시 교체하지 않는다. 새 RequestId로 재제출하면 MulliganAlreadyComplete 또는 MulliganClosed다.
- 성공/거절은 기존 CommandAccepted/CommandRejected를 사용한다. 새 규칙 오류는 MulliganPending, MulliganClosed, MulliganAlreadyComplete, InvalidMulliganSelection이다. 기존 GameRule 접두사/상세 형식을 유지한다.
- Snapshot 추가 필드: `MulliganActive`, `MulliganSecondsRemaining`, `Players[].MulliganComplete`, `Players[].MulliganSelectedIds`.
- `MulliganSelectedIds`와 손패는 본인에게만 제공한다. 양쪽 확정 전에는 제출한 본인에게도 초기 손패만 보내며 교체 결과를 보류한다. 재접속/재동기화도 같은 기준이다. GameCore의 즉시 교체 및 Replay 계산은 유지하고 Snapshot 공개 시점만 제한한다. 상대 목록은 빈 배열이며 교체 수·선택 ID·덱 순서·seed/RNG를 보내지 않는다. 공개 `MulliganCompleted` 이벤트는 플레이어 ID와 완료 사실만 담는다.
- 마감은 1초 주기 서버 관리 및 명령 수신 직전에 검사한다. 재접속해도 마감은 연장되지 않는다. 연결이 끊긴 플레이어도 기본 정책으로 완료된다.
- 마지막 완료 후 Snapshot 및 LobbyState 모두 Playing으로 갱신한다. 멀리건 중 명시적 LeaveMatch는 기존 진행 중 매치와 같이 방을 닫는다.

## Unity UI와 재접속

`MulliganPanelView`가 기존 카드 프리팹을 재사용하는 추가 오버레이를 생성한다. 기존 씬/전투 프리팹을 재생성하지 않는다.

1. BattleScene 진입 → 초기 손패 5장, 선택 수, 상대 완료 상태, 남은 시간 표시.
2. 카드 클릭으로 교체 선택/해제, 강조 테두리와 ‘교체 예정/유지’ 표시.
3. ‘확인’ 하나로 선택 목록을 제출한다. 아무 카드도 선택하지 않으면 0장 교체다.
4. 제출 처리 중과 완료 후 확인 버튼을 잠그고 기존 손패와 상대방 대기를 표시한다. 교체 결과는 아직 보이지 않는다.
5. 양쪽 완료 후 같은 화면에서 교체한 카드만 원래 슬롯에 뒤집어 보여준다. 유지한 카드는 움직이지 않고, 첫 턴 추가 드로우는 이 5장 연출에서 제외한다. 별도 안내 문구 없이 총 1초 후 오버레이를 닫는다.
6. 네트워크 오류 메시지를 제공하며 재접속은 기존 연결 메뉴를 사용한다. 확정 손패·완료 상태·본인 제출 ID는 서버 스냅샷에서 복원한다. 미제출 선택은 동일 클라이언트 프로세스의 메모리에 보존하고 다시 받은 손패로 필터링한다.

기존 세션 토큰과 매치 보존 범위를 유지한다. **클라이언트 프로세스 종료 후 미제출 선택 복구, 서버 재시작 이후 매치 복구는 추가하지 않았다.** 제출하지 않은 클릭을 상대나 서버로 실시간 전송하지 않는다. 화면의 카운트다운은 안내용이고 마감 판정은 서버만 한다.

## 자동 테스트

- `Tests/MulliganTests.cpp`: 0/1/5장, 같은 InstanceId 재추첨 방지, 중복/위조/상대 ID, 모든 전투 명령 차단, 단 한 번의 첫 턴, 요력/드로우, 제출 순서 및 선택 순서와 무관한 결과, Replay, 비활성화 회귀.
- `Tests/NetworkTests.cpp`: 같은 시퀀스의 두 제출, 완료 전/후 재접속, 손패·선택 은닉, 자동 keep/timeout close, Replay.
- `Tests/MulliganWebSocketIntegration.py`: 실제 두 WebSocket 클라이언트의 동시 제출, 재접속, 정보 은닉, 연결 종료 상태에서 실제 타이머 완료, 3개 매치 Replay 검증.
- Unity `FrontEndPlayModeTests`: 개인 덱 제출로 시작하는 실제 서버/로비/전투, 5장 오버레이, 클릭/해제/강조, 두 버튼, 제출 전/후 재접속, 5장 상대 교체, 전투 진입 및 턴 종료. 오버레이 렌더 캡처도 생성한다.

## 실제 2인 수동 검증 절차

1. 위 명령으로 백귀야행 서버를 `--mulligan on`으로 실행한다. Unity 6000.0.81f1 에디터 + 별도 클라이언트 빌드/프로젝트 복사본을 준비한다. 개인 덱 파일은 독립 저장 경로를 사용한다.
2. 각자 다른 유효한 30장 개인 덱을 선택하고 같은 방에서 준비한다. 전투 씬 진입 시 두 사람 모두 정확히 5장이고 일반 전투 입력이 잠기는지 확인한다.
3. A는 1장 선택/해제 후 1장을 확정한다. B가 확정하기 전에는 첫 턴이 시작되지 않고 A의 재제출 버튼은 잠겨야 한다. B는 5장을 교체한다. 선공의 정상 첫 턴 드로우 후 전투 입력이 활성화되어야 한다.
4. 다음 매치에서는 선택 없이 ‘확인’을 눌러 0장 제출을 확인하고, 또 다른 매치에서는 동시에 확정한다.
5. 제출 전 및 제출 후 각각 네트워크를 끊었다가 재접속한다. 미제출 선택, 확정 손패와 완료 상태가 유지되고 상대 손패/선택은 보이지 않아야 한다.
6. `--mulligan-timeout 5`로 실행하여 한 명이 연결을 끊어도 미완료자가 0장으로 자동 완료되는지 확인한다. `--mulligan-auto-keep off`에서는 대신 MulliganTimeout으로 종료되어야 한다.
7. 일반 소환·주문·현현·공격·턴 종료·재대전을 확인한다. 재대전에는 새 멀리건 기회가 1회 제공된다. 종료 로그의 `replayVerified=true`를 확인한다.
8. `--mulligan off`로 실행하면 오버레이 없이 기존 초기 드로우 및 전투 흐름이어야 한다.

## 변경 파일

- GameCore: `Include/ProjectC/{GameConfig,GameState,PlayerState,GameCommand,GameEvent,GameInitializer}.hpp`, `Source/{Mulligan,GameInitializer,GameFlow,GameCommand,CardEffects}.cpp`.
- 서버: `Network/{CardCatalog,MatchSession,MatchManager,main}.cpp`, `Network/{MatchSession,MatchManager}.hpp`, `CMakeLists.txt`, `Network.cmake`.
- Unity: `Scripts/Battle/{MulliganPanelView,BattlePresenter}.cs`, `Scripts/Network/{Protocol,MultiplayerDebugScreen}.cs` 및 새 스크립트 meta.
- 테스트: 위 C++/Python/Unity 파일과 이 문서. 1단계 결과(현재 커밋 ea22781)는 보존했다.


## 최종 검증 결과

- C++20 Debug 빌드 성공 (macOS AppleClang 17).
- CTest **6/6 통과**: GameStateTests, NetworkTests, WebSocketIntegration, ContentWebSocketIntegration, PersonalDeckWebSocketIntegration, MulliganWebSocketIntegration.
- Unity **6000.0.81f1 PlayMode 15/15 통과**. 기존 13개와 신규 멀리건 UI 2개를 모두 실행했다. 사용자 에디터를 방해하지 않도록 임시 프로젝트 복사본에서 실행했다.
- Unity 결과: `/tmp/projectc-mulligan-unity-tests.xml`, 로그: `/tmp/projectc-mulligan-unity.log`. 1920×1080 오버레이 캡처 `/tmp/projectc-unity-validation/Artifacts/MulliganOverlay.png`에서 카드 선택, 버튼 글자, 상대 상태와 카운트다운 표시를 확인했다.
- 초기 테스트에서 발견한 테스트 대기 조건(재접속의 일시적인 null 상태), 매치 시작 전 빈 Players 배열 취급, UI 버튼 레이블을 수정한 후 최종 실행은 모두 통과했다. 실패한 테스트를 삭제하거나 무시하지 않았다.
- 실제 C++ 서버와 두 Unity 네트워크 컨트롤러, 두 WebSocket 연결을 통한 기능 검증은 자동화했다. 사람이 별도 GUI 창 두 개를 조작하는 수동 대전은 수행하지 않았으며 위 절차로 확인할 수 있다.
- `git diff --check` 통과. 이 작업에서 commit/push는 수행하지 않았다.
