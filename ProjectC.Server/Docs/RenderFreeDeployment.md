# Render Free: 임시 결과 큐 배포 안내

## 지원 범위

Free Web Service에서도 Battle Server와 기존 파일 결과 큐를 실행할 수 있다.
Persistent Disk는 실행 필수 조건이 아니다. **쓰기 가능한 디렉터리**가 필요하다.
다만 Free 컨테이너의 로컬 큐는 경기 결과를 영구 보존하는 저장소가 아니다.

[Render Free 공식 문서](https://render.com/docs/free)에 따르면 Free 서비스는 Persistent Disk를
지원하지 않으며, 재시작·재배포·절전 시 변경된 로컬 파일이 사라진다.
서비스는 유휴 상태에서 절전될 수 있고 다시 시작하는 데 지연이 있다.
이 문서의 재시작 테스트는 이러한 수명 주기를 로컬에서 재현한 것이며 실제 Render 배포는 실행하지 않았다.

## 큐 경로와 모드

| 설정 | 기본값과 의미 |
|---|---|
| `BATTLE_RESULT_OUTBOX` | 실행 파일 자체의 기본값은 상대 경로 `BattleOutbox`. 프로세스 작업 디렉터리를 기준으로 해석 |
| Docker 이미지의 기본 경로 | `/ProjectC/BattleOutbox`. `WORKDIR=/ProjectC`, 디렉터리 소유자는 UID 10001 |
| `BATTLE_RESULT_STORAGE` | `ephemeral` 기본값. `ephemeral` 또는 `persistent`만 허용 |

새 디렉터리는 자동 생성한다. 상대 경로도 사용할 수 있지만 Render에서는 절대 경로를 권장한다.
시작 시 임시 파일 생성·flush/fsync·원자적 교체·삭제를 확인한다. 빈 경로나 쓰기 불가능한 경로이면
시작에 실패하며 메모리 큐로 몰래 전환하지 않는다. `/tmp/projectc-battle-outbox` 같은 쓰기 가능한
임시 경로도 사용할 수 있다. 경로를 `/var/data`로 지정하는 것만으로 영구 디스크가 되지는 않는다.

`BATTLE_RESULT_STORAGE`는 운영자가 보존 정책을 **선언하는 로그용 설정**이다.
전송·재시도·멱등성 알고리즘은 두 모드에서 동일하다. `persistent`를 설정해도 디스크가 생성되거나
마운트의 영구성을 검증하지 않는다. 실제 보존은 호스팅 서비스의 디스크 설정에 달려 있다.
Free에서는 반드시 `ephemeral`로 설정한다.

시작 로그에는 해석된 절대 경로, 선언된 모드, 파일시스템이 남아야 복구할 수 있다는 내용이 표시된다.
키·JWT·결과 본문은 로그에 출력하지 않는다.

## 결과 유실과 멱등성의 차이

기존 UUID MatchId, 참가자/덱/snapshotId, 승자, 종료 사유, endedAt은 최초 큐 기록 후 변경하지 않는다.
재전송은 같은 본문으로 한다. Website API의 기존 MatchId 멱등성은 그대로 사용한다.
전송 실패·5xx·429는 최대 총 6회 요청하고, 재시도 간격은 4/8/16/32/64초다.
그 뒤 남은 파일은 자동 삭제하지 않지만, 이는 **해당 컨테이너 파일이 살아 있는 동안**에만 의미가 있다.

| 상황 | DB와 복구 결과 | Unity 표시 |
|---|---|---|
| API 200 응답을 확인한 정상 경기 | DB에 저장됨. 이후 로컬 큐가 사라져도 저장된 경기/랭크는 유지 | 본인 RankUpdated 수신 후 최신 랭크 조회까지 성공하면 갱신 확인 |
| API가 계속 실패하고 컨테이너는 살아 있음 | 같은 MatchId로 제한된 재시도. 한도 이후 파일 잔류 | 확인 대기, 지연 시 미확인. RP/MMR 보상을 임의 계산하지 않음 |
| 미전송 상태에서 Free 컨테이너 교체/절전 | 로컬 큐가 사라지면 서버는 결과를 재구성할 수 없음. 미저장 경기/랭크 반영이 누락될 수 있음 | 미확인. 조회한 랭크를 이번 경기 반영 완료로 간주하지 않음 |
| DB 커밋 후 HTTP 응답이 유실되고 컨테이너도 교체됨 | DB에는 이미 반영되었을 수 있음. 로컬 파일 부재만으로 미저장을 단정할 수 없음 | 미확인. 최신 랭크는 별도로 조회 가능 |
| 같은 파일시스템을 유지한 프로세스 재시작 | 남아 있는 큐를 읽어 동일 본문 재전송. 이미 반영된 MatchId는 중복 반영하지 않음 | 살아 있는 연결/경기 문맥이 없으면 변화 통지가 재생되지는 않음 |

fsync와 원자적 rename은 파일 쓰기와 프로세스 장애에 대한 보호다.
**컨테이너 파일시스템 자체의 폐기를 막지 못한다.** 정상 종료 때 큐에 기록하더라도, Render가 그
파일시스템을 폐기하면 미전송 데이터는 남지 않는다. 종료 시 모든 HTTP 요청의 성공을 보장하지 않는다.
진행 중 매치·재접속 좌석·아직 큐에 기록되지 않은 결과도 기존처럼 프로세스 재시작으로 사라진다.
SessionToken은 살아 있는 서버 프로세스의 경기 복귀를 위한 것이며, 서버 재시작 복원 기능은 아니다.

멱등성은 **이미 받은 결과의 중복 반영 방지**다. Website API가 아직 받지 못한 결과를 복원하는 기능이
아니다. Free 파일 큐만으로 결과 무유실을 보장할 수 없다. 무유실 요구가 생기면 서버 측 외부 영구 큐/
저장소 또는 실제 Persistent Disk가 필요하다. 이번 변경에서는 별도 DB나 외부 큐를 추가하지 않았다.

## Unity의 완료 판정

WebSocket 메시지 형식과 버전 2는 바꾸지 않는다. `MatchEnded`는 경기 종료이며 DB 저장 성공 통지가 아니다.

- 결과 저장 확인을 기다리는 동안 마지막 조회 랭크를 **이번 경기 반영 미확인**으로 표시한다.
- 종료 후 180초간 확인되지 않거나 결과 화면에서 연결이 끊기면 **결과 저장 미확인**으로 표시한다.
  이는 실패 확정이 아니다. 서버의 재시도가 계속될 수 있으며 늦게 온 정상 통지는 처리한다.
- 서버가 보낸 본인의 `RankUpdated`를 받아야 경기 저장 확인 상태가 된다.
- 그 뒤 `/api/rank/me`까지 성공해야 **랭크 갱신 확인**이 된다. 랭크 조회만 성공해서는 이번 경기의
  커밋을 증명할 수 없다. 결과 통지가 없는 상태의 새로고침은 미확인을 해제하지 않는다.
- 결과 통지는 받았지만 랭크 조회가 실패하면 **경기 결과 저장 확인 / 최신 랭크 조회 미완료**로 표시한다.

재시작 이후 이전 MatchId의 저장 여부를 확인하는 사용자용 경기 조회 API는 현재 계약에 없다.
클라이언트는 결과를 재제출하거나 승패/RP/MMR을 직접 수정하지 않는다.

## Render 환경 변수

Battle Server Free Web Service에 다음을 설정한다. Website API의 환경 변수/DB schema 변경은 없다.

| 변수 | 값 |
|---|---|
| `BATTLE_API_KEY` | 이미 설정된 Website API와 같은 비밀값, 환경 변수에만 유지 |
| `WEBSITE_API_URL` | `https://projectc-api.onrender.com` |
| `PROJECTC_DATA_DIRECTORY` | `/ProjectC/Data/HyakkiYagyo` |
| `BATTLE_RESULT_STORAGE` | `ephemeral` |
| `BATTLE_RESULT_OUTBOX` | `/ProjectC/BattleOutbox` |
| `PORT` | `7777` — 현재 Docker CMD의 `--port 7777`과 맞춤 |

Render Docker 설정:

1. 저장소 루트를 빌드 컨텍스트로 사용한다. Root Directory는 비워 두고 Dockerfile Path는
   `ProjectC.Server/Dockerfile`로 설정한다.
2. Free 인스턴스를 선택한다. Persistent Disk를 추가하지 않는다.
3. 위 환경 변수를 적용한다. Docker Command는 이미지의 기존 기본값을 유지한다.
   코드가 실제로 listen하는 포트는 기존 `--port 7777`이 결정한다.
4. 이번 코드를 검토한 뒤 운영자가 수동 배포한다. 시작 로그의 `storage=ephemeral`과
   `/ProjectC/BattleOutbox`, 카탈로그 오류 및 디렉터리 쓰기 오류가 없는지 확인한다.
5. Unity의 운영 주소는 `wss://projectc-server.onrender.com`, Website 주소는
   `https://projectc-api.onrender.com`을 유지한다. 별도 테스트 계정으로 매칭과 결과 화면을 확인한다.

Free에서는 Shell/SSH 접근도 제공되지 않으므로, 예전 문서의 큐 JSON 수동 수정 절차를 Free의 복구
수단으로 가정하지 않는다. 재배포/재시작으로 재시도를 유도하면 오히려 대기 파일이 없어질 수 있다.
같은 컨테이너에서의 제한된 자동 재시도 이후 수동 복구가 필요하면 서버 측 영구 저장 설계를 별도로 해야 한다.

Website API도 Free에서 절전된 경우 처음 호출에 지연/실패가 날 수 있다. 현재 연결/요청 타임아웃과
제한된 재시도는 유지한다. 이를 무한 재시도나 절전 방지용 트래픽으로 바꾸지 않는다.

## 로컬 검증

```powershell
ctest --test-dir ProjectC.Server/out/build/x64-Debug --output-on-failure
```

`OutboxRecoveryIntegration`은 로컬 모의 API와 실제 Battle Server 프로세스로 다음을 재현한다.

- 영구 마운트 없이 기본 상대 경로 및 임시 경로에서 시작
- 잘못된 storage 설정 거부
- API 503 후 파일시스템을 유지한 프로세스 재시작과 동일 결과 재전송
- API 503 후 빈 파일시스템의 새 컨테이너에 해당하는 교체: 미전송 결과 복구 불가
- DB 커밋 후 응답 유실 + 파일시스템 교체: 이미 저장된 결과 유지
- 원래 본문이 남아 있는 경우 동일 MatchId 재전송의 중복 반영 방지

`WebsiteBattleIntegration`은 기존 실제 WebSocket 경기·재시도·한도 검증을 유지한다.
Unity `WebsiteSessionPlayModeTests`는 조회만 성공, 결과 미확인, 랭크 조회 실패, 늦은 확인 및
확인 시간 초과 상태를 검증한다. 실제 Render/운영 DB에는 배포하거나 쓰지 않는다.

이번 변경의 검증 결과:

- Windows MSVC 전체 재빌드 후 CTest **11/11 통과**.
- Ubuntu 24.04 Docker Release 이미지 빌드 및 CTest **11/11 통과**.
- 마운트 없는 런타임 컨테이너 2개를 교체하여 기본 큐 쓰기 권한·시작 로그·WebSocket 연결과
  이전 컨테이너의 큐 파일이 새 컨테이너에 남지 않는 동작 확인.
- Unity 6000.0.81f1 PlayMode의 세션/매칭 테스트 **12/12 통과**.
- `python Tools/TestWebsiteIntegration.py ... C:/Dev/ProjectC-Website/backend`로 별도 PostgreSQL 17에서
  Website `npm test`, `npm run test:e2e`, `npm run build` 및 실제 C++ → NestJS → DB 경기 저장/랭크/
  응답 유실·중복 제출 검증 통과. 운영 데이터는 사용하지 않았다.

참고: [Render Free](https://render.com/docs/free), [Persistent Disks](https://render.com/docs/disks).
