# Project C Alpha 01 — 카드별 검증 매트릭스

기준: `Cards.json`의 55종을 자동 열거했다. JSON 로드 및 기존 데이터 검사는 전 종 공통으로 통과했으나, 이 사실만으로 카드 효과 검증을 뜻하지 않는다. `Partially Verified`는 아래 적힌 특정 서버 시나리오에 한정한다. `Untested`는 구현 부재라는 뜻이 아니라 카드별 실행 근거가 아직 부족하다는 뜻이다. 카드별 검증과 공통 시스템 검증은 분리한다. 카드 고유 동작의 정상·거부·예외 조건과 리플레이 근거가 충분하고, 동일한 실제 코드 경로를 이용하는 공통 WebSocket·재접속·정보 분리·Unity 입력·우선권 검증에 연결할 수 있을 때만 `Verified`로 판정한다. 코드 경로가 다르면 대표 카드 검증을 전용 근거로 전용할 수 없다. 이 매트릭스에는 아직 `Verified` 판정을 부여하지 않았다.

공통 시스템 근거: `YakouWebSocketIntegration`은 방 생성·참가·덱 조합·비공개 상태·승패 경로를, `YakouOnlinePlayModeTests`는 두 전송 컴포넌트와 선택·재접속 경로를 검증한다. 이 근거는 카드별 실제 실행을 대신하지 않으며, 아래 W/U는 해당 카드가 실제로 그 경로를 거쳤을 때만 표시한다.

열의 기호: `D` 데이터 검사, `E` 카드별 서버 실행 증거, `R` 카드별 리플레이 증거, `W` 카드별 실제 WebSocket 경기, `U` 카드별 Unity 조작. `✓`는 해당 범위 검증, `△`는 일부 시나리오만 검증, `—`는 검증 근거 없음. 비용·대상·타이밍 검증은 `E`의 설명에 포함되지만 모든 정상·오류 조합을 뜻하지 않는다.

| ID | 효과/트리거 | D | E: 확인한 시나리오 | R | W | U | 상태 |
|---:|---|:---:|---|:---:|:---:|:---:|---|
| 2001 | OnSummoned:Mill | ✓ | 소환 밀링 및 묘지 이동 (YakouMatchTests) (부분) | — | △ | — | Partially Verified |
| 2002 | OnNecromancySummoned:Salvage | ✓ | YakouMatchTests: 강령 소환 후 묘지 회수 후보·잘못된 대상 거부 (부분) | — | — | — | Partially Verified |
| 2003 | OnSummoned:ModifyCountdown | ✓ | YakouMatchTests: 강령 비용 2·다른 아군 카운터 감소·비용/대상 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 2004 | OnSummoned:Salvage | ✓ | YakouMatchTests: 묘지 조건 충족/미충족·선택 회수 (부분) | — | — | — | Partially Verified |
| 2005 | OnDiscarded:Mill, OnBanishedForNecromancyCost:ModifyCountdown | ✓ | YakouMatchTests: 폐기 비용 이동·밀링 1·잘못된 비용 거부·리플레이; 4020의 강령 비용으로 제외 후 응전 종료 시 선택형 카운터 감소·잘못된 대상 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 2006 | OnAssignedAsBlocker:ReduceDamage | ✓ | YakouMatchTests: 실전 방어 배정·전투 피해 1 감소·권한 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 2007 | OnRaid:ModifyAttack | ✓ | YakouMatchTests: 강령 비용·묘지 조건 습격 공격력 +1·중복 비용 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 2008 | OnDevourSummoned:Mill | ✓ | YakouMatchTests: 포식 소환·잘못된 재료 거부·밀링 1·리플레이 (부분) | △ | — | — | Partially Verified |
| 2009 | OnResolve:Mill | ✓ | YakouMatchTests: 주문 응전 후 밀링 2·우선권/중복 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 2010 | OnResolve:SpecialSummon | ✓ | YakouCommandGatewayTests: 묘지 특수 소환 비용·비공개 후보·잘못된 칸/대상·리플레이 (부분) | △ | — | — | Partially Verified |
| 2011 | OnResolve:ModifyAttack | ✓ | raid tactic target/cost (부분) | — | — | — | Partially Verified |
| 2012 | OnResolve:ReduceDamage | ✓ | next-damage shield and replay (부분) | △ | — | — | Partially Verified |
| 2101 | OnManifested:ModifyCountdown, OnRaid:GrantPiercing | ✓ | piercing cost/combat/replay (부분) | △ | — | — | Partially Verified |
| 2102 | OnDestroyedEnemyByCombat:SetCountdown | ✓ | YakouMatchTests: 현현 조건·기력·중복 현현 거부; 전투 파괴 트리거 fixture (부분) | — | — | — | Partially Verified |
| 3001 | OnSummoned:Mill | ✓ | YakouMatchTests: 소환 밀링 1·소유자 거부·공개 묘지·리플레이 (부분) | △ | — | — | Partially Verified |
| 3002 | OnSummoned:HealAccumulatedDamage | ✓ | YakouMatchTests: 실제 전투 부상 후 2 회복·잘못된 대상 거부 (부분) | — | — | — | Partially Verified |
| 3003 | OnRaid:ModifyAttack | ✓ | YakouWebSocketIntegration: 습격 시 공격력 3+1과 플레이어 피해 4 (부분) | — | △ | — | Partially Verified |
| 3004 | OnAssignedAsBlocker:ReduceDamage | ✓ | YakouWebSocketIntegration: Grave 대 Guard의 방어 피해 1 감소 (부분) | — | △ | — | Partially Verified |
| 3005 | OnDevourSummoned:ModifyCountdown | ✓ | YakouMatchTests: 포식 소환·잘못된 재료 거부·자신 카운터 감소·리플레이 (부분) | △ | — | — | Partially Verified |
| 3006 | OnNecromancySummoned:HealAccumulatedDamage | ✓ | YakouMatchTests: 실제 전투 부상·강령 비용·다른 아군 회복·잘못된 비용/대상·리플레이 (부분) | △ | — | — | Partially Verified |
| 3007 | OnSummoned:ModifyCountdown | ✓ | YakouMatchTests: 폐기 소환·다른 아군 카운터 감소·잘못된 비용 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 3008 | OnDevoured:ModifyCountdown | ✓ | YakouMatchTests: 포식당함 이벤트 문맥·소비자 카운터 감소·잘못된 대상 거부 (부분) | — | — | — | Partially Verified |
| 3009 | OnTargetedByEnemySpell:ReduceDamage | ✓ | accepted target event and replay (부분) | △ | — | — | Partially Verified |
| 3010 | Passive:IncreaseMaxBlocks | ✓ | YakouMatchTests: 지속 최대 방어 횟수 2·중복 공격자 배정 거부 (부분) | — | — | — | Partially Verified |
| 3011 | OnResolve:ModifyCountdown | ✓ | YakouMatchTests: 주문 응전·아군 카운터 감소·잘못된 대상/권한 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 3012 | OnResolve:LookAndChoose | ✓ | paid spell choice and replay (부분) | △ | — | — | Partially Verified |
| 3013 | OnResolve:Salvage | ✓ | YakouMatchTests: 강령 비용 2·응전·비공개 묘지 회수·비용/대상 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 3014 | OnResolve:HealAccumulatedDamage | ✓ | YakouMatchTests: 전투 부상 대상 주문 회복 3·잘못된 대상 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 3015 | OnResolve:IgnoreOneManifestCondition | ✓ | condition waiver (부분) | — | — | — | Partially Verified |
| 3016 | OnResolve:ReduceDamage | ✓ | raid shield interaction (부분) | — | — | — | Partially Verified |
| 3017 | OnResolve:PreventEffectDamage | ✓ | targeted protection and replay (부분) | △ | — | — | Partially Verified |
| 3018 | OnResolve:ReassignBlocker | ✓ | blocker replacement (부분) | — | — | — | Partially Verified |
| 3019 | OnResolve:ModifyAttack | ✓ | raid tactic cost/target (부분) | — | — | — | Partially Verified |
| 3020 | OnResolve:NegateSpellEffect | ✓ | nested negation (부분) | △ | — | — | Partially Verified |
| 4001 | OnSummoned:Mill | ✓ | YakouMatchTests: 소환 밀링 1·소유자 거부·공개 묘지·리플레이 (부분) | △ | — | — | Partially Verified |
| 4002 | OnSummoned:Salvage | ✓ | 폐기 소환 후 조건부 회수·잘못된 대상·조건 부족 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4003 | OnSummoned:Mill | ✓ | 폐기 소환·밀링·영역 이동 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4004 | OnDevourSummoned:ModifyCountdown | ✓ | 포식 소환·능력치 상속·비용 이동 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4005 | OnSummoned:Mill | ✓ | 강령 소환·비용 제외·밀링 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4006 | OnResolve:Salvage | ✓ | 강령 주문·회수 후보·잘못된 비용/대상 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4007 | OnResolve:Mill/ModifyCountdown | ✓ | YakouMatchTests: 응전 후 대상 선택·밀링 1·카운터 감소·거부/리플레이 (부분) | △ | — | — | Partially Verified |
| 4008 | 정의된 효과 없음 | ✓ | 일반 소환·잘못된 시점/비용/중복 거부 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4009 | 정의된 효과 없음 | ✓ | 일반 소환·잘못된 시점/비용/중복 거부 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4010 | OnSummoned:ModifyCountdown | ✓ | 다른 아군 대상·거부·카운터 감소 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4011 | OnDevourSummoned:ModifyCountdown | ✓ | 포식 소환·능력치 상속·비용 이동 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4012 | OnResolve:ModifyCountdown | ✓ | multi-target spell (부분) | — | — | — | Partially Verified |
| 4013 | OnResolve:ModifyCountdown | ✓ | 비용·대상 거부·카운터 감소 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4014 | OnSummoned:ModifyCountdown | ✓ | 폐기 소환 후 카운터 감소 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4015 | OnSummoned:HealAccumulatedDamage | ✓ | 누적 피해 회복·대상 거부 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4016 | OnAssignedAsBlocker:ReduceDamage | ✓ | YakouMatchTests: 실전 방어 배정·전투 피해 2 감소·권한 거부·리플레이 (부분) | △ | — | — | Partially Verified |
| 4017 | OnResolve:ReassignBlocker/ReduceDamage | ✓ | replacement plus shield (부분) | — | — | — | Partially Verified |
| 4018 | OnResolve:ReduceDamage | ✓ | all-blocker shield (부분) | — | — | — | Partially Verified |
| 4019 | OnAssignedAsBlocker:ModifyAttack | ✓ | YakouMatchTests: 강령 소환·방어 시 공격력 감소 (부분) | — | — | — | Partially Verified |
| 4020 | OnResolve:NegateSpellEffect | ✓ | nested negation (부분) | △ | — | — | Partially Verified |
| 4021 | OnSummoned:Salvage | ✓ | 주문 묘지 회수·상대 비공개 선택 (YakouMatchTests) (부분) | — | — | — | Partially Verified |

## 판정 및 남은 검증

- Verified: **0**, Partially Verified: **55**, Unsupported: **0**, Untested: **0**. `Unsupported: 0`은 모든 카드가 지원된다는 의미가 아니다. 미지원 여부를 확정할 충분한 카드별 실행 감사를 아직 마치지 않았다.
- 여섯 덱 조합을 사용하는 기존 WebSocket 테스트는 제한된 소환/습격/승패 흐름을 검증한다. 이번 확장으로 Grave 덱의 2001 밀링, Raid 덱의 3003 습격 피해, Grave 대 Guard 경기의 3004 방어 피해 감소는 두 실제 WebSocket 연결에서 검증했다. 나머지 카드의 `W`는 미검증이다. Unity PlayMode도 특정 카드별 완결 경기나 독립 빌드 2개를 검증하지 않아 `U`는 모두 미검증이다.
- 3009/3017의 대상 지정·응전·비용·상태·리플레이는 실제 카드로 확인했다. 효과 피해 감소/완전 방지 판정은 현 카드 풀에 직접 효과 피해 카드가 없어 별도 판정 fixture에서만 확인했다.
- 55종 전체 Verified 판정에는 카드별 정상/거부 비용, 대상, 트리거, 응전, 선택 중 재접속, 최종 스냅샷, 리플레이 및 Unity 입력 증거가 더 필요하다. `RuntimeReady`는 false다.

## 로컬 CTest 타임아웃 조사

- `WebsiteBattleIntegration`의 CTest 제한은 120초이고 테스트는 순차 실행한다. 테스트는 동적 포트를 할당하며 종료 시 연결, 서버 프로세스, 로컬 HTTP 서버를 정리한다.
- 이전 실행에서는 테스트가 성공 문구를 출력하거나 직접 실행에서 종료 코드 0을 반환했는데도 CTest가 수천 초의 경과 시간을 기록하며 타임아웃으로 분류했다. 이번 전체 CTest에서는 같은 테스트가 23.69초, 전체 18/18 통과했다. 제한 시간을 변경하지 않았다.
- 따라서 간헐적 시간 측정 이상은 관찰했지만 원인은 확정하지 못했다. 포트 충돌이나 서버 자식 프로세스 잔류의 증거도 현재 없다. 다음 재현 시 CTest 상세 로그와 시스템 시각 변화를 함께 기록해야 한다.
- 이후 Yakou WebSocket 테스트는 모든 경기 성공 문구를 출력한 뒤 서버 종료 대기에서 5초를 초과한 사례가 있었다. 테스트 하네스가 서버 표준 출력을 `PIPE`로 받아 시작 한 줄만 읽고 경기 중 드레인하지 않아 출력 버퍼가 찰 수 있는 구조였다. 서버 출력을 임시 로그 파일로 직접 보내고 포트 연결로 시작을 확인하도록 바꿨다. 로그 파일은 서버가 종료된 뒤에만 읽어 플랫폼별 공유 파일 위치 간섭 가능성도 제거했다. 이 방식은 파이프 버퍼와 백그라운드 드레인 스레드를 모두 피한다. 단독 CTest 3회가 각각 약 5.7초에 통과했다.
- 같은 작업 중 `OutboxRecoveryIntegration`은 성공 문구를 출력한 뒤 CTest에 197초로 기록된 사례가 있었다. 단독 재실행은 12.15초에 통과했다. 원인을 Yakou 출력 버퍼와 동일하다고 단정하지 않는다. 제한 시간은 변경하지 않았다. 마지막 전체 CTest 재실행은 18/18 통과했다.
- Windows CI의 장시간 지연은 완료된 이전 작업 로그에서 `YakouMatchTests` 시작 직후였음을 확인했다. 새 3006 테스트의 `std::find`가 `PendingEffectChoices()`를 각각 호출해 서로 다른 임시 벡터의 `begin()`/`end()`를 섞은 정의되지 않은 동작을 수정했다. 이 원인은 WebSocket 로그 수집 문제와 별개다.

## 이번 실행 검증 근거

- `YakouMatchTests`의 카드별 추가 실행: 3001, 4001, 2009, 4007. 정상 결과, 적어도 한 거부 경로의 상태 해시 불변, 적절한 공개 묘지 스냅샷, 최종 리플레이 해시를 확인했다. 4007은 대상이 적법하게 선택되기 전까지 밀링과 카운터 감소가 확정되지 않는 것도 확인했다.
- `YakouWebSocketIntegration.run_deck_selection`: 여섯 덱 조합의 기존 승패 흐름을 유지하면서 Grave 첫 소환 2001의 밀링 2와 Grave 대 Guard의 3004 소환·방어 배정·HP 보존을 확인했다. 이 온라인 시나리오는 55종 전체 카드의 대표 전략 검증이 아니다.

## 후속 카드별 실행 검증

- `YakouMatchTests`: 2006/4016은 3003의 실제 습격과 방어 배정으로 피해 감소량·공개 HP·권한 거부·리플레이를 확인했다. 2005는 폐기 비용으로 이동할 때의 밀링·잘못된 비용 거부·리플레이를 확인했다. 2005의 강령 제외 트리거는 이 시나리오에서 검증하지 않았다.

- `YakouMatchTests`: 3007은 폐기 소환과 다른 아군 카운터 감소, 3011은 주문 응전 후 대상 선택·카운터 감소를 검증했다. 양쪽 모두 적어도 하나의 거부 경로가 상태 해시를 유지하고, 전체 선택 흐름의 리플레이 해시가 일치한다.

- 새 포식 실행 테스트는 2008/3005의 비용 재료, 효과 결과, 거부 상태 해시, 결정론적 리플레이를 검증했다. 기존 테스트의 실제 카드 실행 근거를 재확인하여 2002/2004/2102/3008/3010/4019를 부분 검증으로 기록했다. 이 카드들의 모든 경계 및 리플레이가 검증됐다는 뜻은 아니다.

- 3006은 샘플 덱에 없어 복사한 테스트 카탈로그의 합법 덱 슬롯에만 넣었다. 실제 카드 정의는 유지하고, 전투에서 부상한 아군의 강령 소환 후 회복, 비용·대상 거부, 리플레이를 검증했다. 기존 3002의 전투 회복 테스트와 2010의 게이트웨이 특수 소환·리플레이 테스트도 카드별 부분 근거로 재확인했다.

- 2003/2007/3013/3014도 신규 서버 실행 fixture에서 고유 효과, 의미 있는 거부 사례, 최종 리플레이 해시를 확인했다. 이로써 카드별 실행 근거가 없는 `Untested`는 0이지만, 모든 카드의 모든 예외·WebSocket·Unity 경로가 검증된 것은 아니므로 Verified 승격과 Alpha 완료 판정은 보류한다.

## 응전 비용 트리거와 선택지 후속 검증

- 2005를 주문 응전 카드 4020의 강령 비용으로 제외했을 때, 비용 이벤트가 즉시 해결되지 않고 주문 체인이 종료된 뒤 아군 몬스터의 카운터를 선택해 감소시킨다. 테스트는 유효하지 않은 대상의 원자적 거부 및 결정론적 리플레이 일치를 확인한다.
- 체인 바깥에서 이미 대기 중이던 일반 효과와 체인 내 비용으로 새로 발생한 유발 효과를 구별한다. 방어자 교체(3018/4017)의 UI 후보는 기존 방어자, 배정되지 않은 습격, 방어 가능 횟수를 초과한 몬스터를 제외하도록 보완했다.
- 응전 비용으로 발생하는 다른 모든 복합 트리거와 독립 Unity 실행 빌드 2개를 이용한 완결 경기는 여전히 미검증이다. 현재 카드 판정은 **Verified 0 / Partially Verified 55 / Unsupported 판정 0 / Untested 0**을 유지한다.
