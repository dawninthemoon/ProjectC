# Project C Alpha 01 — 카드별 검증 매트릭스

기준: `Cards.json`의 55종을 자동 열거했다. JSON 로드 및 기존 데이터 검사는 전 종 공통으로 통과했으나, 이 사실만으로 카드 효과 검증을 뜻하지 않는다. `Partially Verified`는 아래 적힌 특정 서버 시나리오에 한정한다. `Untested`는 구현 부재라는 뜻이 아니라 카드별 실행 근거가 아직 부족하다는 뜻이다. 카드별 검증과 공통 시스템 검증은 분리한다. 카드 고유 동작의 정상·거부·예외 조건과 리플레이 근거가 충분하고, 동일한 실제 코드 경로를 이용하는 공통 WebSocket·재접속·정보 분리·Unity 입력·우선권 검증에 연결할 수 있을 때만 `Verified`로 판정한다. 코드 경로가 다르면 대표 카드 검증을 전용 근거로 전용할 수 없다. 이 매트릭스에는 아직 `Verified` 판정을 부여하지 않았다.

공통 시스템 근거: `YakouWebSocketIntegration`은 방 생성·참가·덱 조합·비공개 상태·승패 경로를, `YakouOnlinePlayModeTests`는 두 전송 컴포넌트와 선택·재접속 경로를 검증한다. 이 근거는 카드별 실제 실행을 대신하지 않으며, 아래 W/U는 해당 카드가 실제로 그 경로를 거쳤을 때만 표시한다.

열의 기호: `D` 데이터 검사, `E` 카드별 서버 실행 증거, `R` 카드별 리플레이 증거, `W` 카드별 실제 WebSocket 경기, `U` 카드별 Unity 조작. `✓`는 해당 범위 검증, `△`는 일부 시나리오만 검증, `—`는 검증 근거 없음. 비용·대상·타이밍 검증은 `E`의 설명에 포함되지만 모든 정상·오류 조합을 뜻하지 않는다.

| ID | 효과/트리거 | D | E: 확인한 시나리오 | R | W | U | 상태 |
|---:|---|:---:|---|:---:|:---:|:---:|---|
| 2001 | OnSummoned:Mill | ✓ | 소환 밀링 및 묘지 이동 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 2002 | OnNecromancySummoned:Salvage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2003 | OnSummoned:ModifyCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2004 | OnSummoned:Salvage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2005 | OnDiscarded:Mill, OnBanishedForNecromancyCost:ModifyCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2006 | OnAssignedAsBlocker:ReduceDamage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2007 | OnRaid:ModifyAttack | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2008 | OnDevourSummoned:Mill | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2009 | OnResolve:Mill | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2010 | OnResolve:SpecialSummon | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 2011 | OnResolve:ModifyAttack | ✓ | raid tactic target/cost (부분) | — | — | — | Partially Verified |
| 2012 | OnResolve:ReduceDamage | ✓ | next-damage shield and replay (부분) | △ | — | — | Partially Verified |
| 2101 | OnManifested:ModifyCountdown, OnRaid:GrantPiercing | ✓ | piercing cost/combat/replay (부분) | △ | — | — | Partially Verified |
| 2102 | OnDestroyedEnemyByCombat:SetCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3001 | OnSummoned:Mill | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3002 | OnSummoned:HealAccumulatedDamage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3003 | OnRaid:ModifyAttack | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3004 | OnAssignedAsBlocker:ReduceDamage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3005 | OnDevourSummoned:ModifyCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3006 | OnNecromancySummoned:HealAccumulatedDamage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3007 | OnSummoned:ModifyCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3008 | OnDevoured:ModifyCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3009 | OnTargetedByEnemySpell:ReduceDamage | ✓ | accepted target event and replay (부분) | △ | — | — | Partially Verified |
| 3010 | Passive:IncreaseMaxBlocks | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3011 | OnResolve:ModifyCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3012 | OnResolve:LookAndChoose | ✓ | paid spell choice and replay (부분) | △ | — | — | Partially Verified |
| 3013 | OnResolve:Salvage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3014 | OnResolve:HealAccumulatedDamage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 3015 | OnResolve:IgnoreOneManifestCondition | ✓ | condition waiver (부분) | — | — | — | Partially Verified |
| 3016 | OnResolve:ReduceDamage | ✓ | raid shield interaction (부분) | — | — | — | Partially Verified |
| 3017 | OnResolve:PreventEffectDamage | ✓ | targeted protection and replay (부분) | △ | — | — | Partially Verified |
| 3018 | OnResolve:ReassignBlocker | ✓ | blocker replacement (부분) | — | — | — | Partially Verified |
| 3019 | OnResolve:ModifyAttack | ✓ | raid tactic cost/target (부분) | — | — | — | Partially Verified |
| 3020 | OnResolve:NegateSpellEffect | ✓ | nested negation (부분) | △ | — | — | Partially Verified |
| 4001 | OnSummoned:Mill | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 4002 | OnSummoned:Salvage | ✓ | 폐기 소환 후 조건부 회수·잘못된 대상·조건 부족 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4003 | OnSummoned:Mill | ✓ | 폐기 소환·밀링·영역 이동 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4004 | OnDevourSummoned:ModifyCountdown | ✓ | 포식 소환·능력치 상속·비용 이동 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4005 | OnSummoned:Mill | ✓ | 강령 소환·비용 제외·밀링 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4006 | OnResolve:Salvage | ✓ | 강령 주문·회수 후보·잘못된 비용/대상 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4007 | OnResolve:Mill/ModifyCountdown | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 4008 | 정의된 효과 없음 | ✓ | 일반 소환·잘못된 시점/비용/중복 거부 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4009 | 정의된 효과 없음 | ✓ | 일반 소환·잘못된 시점/비용/중복 거부 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4010 | OnSummoned:ModifyCountdown | ✓ | 다른 아군 대상·거부·카운터 감소 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4011 | OnDevourSummoned:ModifyCountdown | ✓ | 포식 소환·능력치 상속·비용 이동 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4012 | OnResolve:ModifyCountdown | ✓ | multi-target spell (부분) | — | — | — | Partially Verified |
| 4013 | OnResolve:ModifyCountdown | ✓ | 비용·대상 거부·카운터 감소 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4014 | OnSummoned:ModifyCountdown | ✓ | 폐기 소환 후 카운터 감소 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4015 | OnSummoned:HealAccumulatedDamage | ✓ | 누적 피해 회복·대상 거부 (YakouMatchTests) (부분) | — | — | — | Partially Verified |
| 4016 | OnAssignedAsBlocker:ReduceDamage | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 4017 | OnResolve:ReassignBlocker/ReduceDamage | ✓ | replacement plus shield (부분) | — | — | — | Partially Verified |
| 4018 | OnResolve:ReduceDamage | ✓ | all-blocker shield (부분) | — | — | — | Partially Verified |
| 4019 | OnAssignedAsBlocker:ModifyAttack | ✓ | 카드별 실행 근거 미확인 | — | — | — | Untested |
| 4020 | OnResolve:NegateSpellEffect | ✓ | nested negation (부분) | △ | — | — | Partially Verified |
| 4021 | OnSummoned:Salvage | ✓ | 주문 묘지 회수·상대 비공개 선택 (YakouMatchTests) (부분) | — | — | — | Partially Verified |

## 판정 및 남은 검증

- Verified: **0**, Partially Verified: **29**, Unsupported: **0**, Untested: **26**. `Unsupported: 0`은 모든 카드가 지원된다는 의미가 아니다. 미지원 여부를 확정할 충분한 카드별 실행 감사를 아직 마치지 않았다.
- 여섯 덱 조합을 사용하는 기존 WebSocket 테스트는 제한된 소환/습격/승패 흐름을 검증한다. 특정 55종 각각을 사용하지 않으므로 `W`는 모두 미검증이다. Unity PlayMode도 특정 카드별 완결 경기나 독립 빌드 2개를 검증하지 않아 `U`는 모두 미검증이다.
- 3009/3017의 대상 지정·응전·비용·상태·리플레이는 실제 카드로 확인했다. 효과 피해 감소/완전 방지 판정은 현 카드 풀에 직접 효과 피해 카드가 없어 별도 판정 fixture에서만 확인했다.
- 55종 전체 Verified 판정에는 카드별 정상/거부 비용, 대상, 트리거, 응전, 선택 중 재접속, 최종 스냅샷, 리플레이 및 Unity 입력 증거가 더 필요하다. `RuntimeReady`는 false다.

## 로컬 CTest 타임아웃 조사

- `WebsiteBattleIntegration`의 CTest 제한은 120초이고 테스트는 순차 실행한다. 테스트는 동적 포트를 할당하며 종료 시 연결, 서버 프로세스, 로컬 HTTP 서버를 정리한다.
- 이전 실행에서는 테스트가 성공 문구를 출력하거나 직접 실행에서 종료 코드 0을 반환했는데도 CTest가 수천 초의 경과 시간을 기록하며 타임아웃으로 분류했다. 이번 전체 CTest에서는 같은 테스트가 23.69초, 전체 18/18 통과했다. 제한 시간을 변경하지 않았다.
- 따라서 간헐적 시간 측정 이상은 관찰했지만 원인은 확정하지 못했다. 포트 충돌이나 서버 자식 프로세스 잔류의 증거도 현재 없다. 다음 재현 시 CTest 상세 로그와 시스템 시각 변화를 함께 기록해야 한다.
