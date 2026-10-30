# GameCore 1차 확장 기록 — TCG 규칙 추가 전

이 문서는 187개 테스트 시점의 1차 확장 기록이다. **현재 실행 계약은
[TcgRulesAndEffects.md](TcgRulesAndEffects.md)를 따른다.** 아래의 임시 소환 설정,
덱 부족 시 거부 정책, 직접 공격/현현/효과 미구현 설명은 이후 구현으로 대체되었다.
현재는 일반 소환 턴당 1회, 별도의 소환 후유증, 직접 공격, 드로우 부족 패배,
Activated/기본 Triggered 효과 및 현현을 구현한다. Continuous/Replacement는 실행하지 않는다.

이 문서는 현재 C++20 GameCore의 실행 계약과 이후 효과 시스템의 확장 지점을 기록한다.
게임 상태의 기준은 `GameState`이며, 일반 게임 진행은 `ProcessCommand`를 사용한다.
네트워크, RP/응전 스택, 현현 실행, 야행 시너지, 범용 효과 해석기는 이번 구현에 포함하지 않는다.

## 카드 정의와 인스턴스

| 데이터 | 책임 |
|---|---|
| `CardDefinition` | 카드 정의 ID, 이름, 타입, 기본 Attack/MaxHp |
| `CardInstance` | 한 장의 고유 ID, 소유자, 현재 능력치/HP, 공격 여부, 원본/현재 정의 ID |
| `GameState::Definitions` | 해당 게임에서 사용하는 정의 스냅샷. Command 실행 중 변경하지 않는다. |

`CardDefinitionId`와 `CardInstanceId`는 서로 다른 의미의 ID다. 현재는 기존 API와의 호환성을 위해
각각 `int` 별칭을 사용하므로 컴파일러가 교차 전달을 막지는 않는다. 정의는 여러 인스턴스에서
공유할 수 있지만, 인스턴스 ID는 두 플레이어의 모든 Zone 전체에서 유일해야 한다.
0은 미지정 값이고 실제 ID는 양수다. 플레이어 ID도 양수이며 서로 달라야 한다.

초기화는 플레이어 배열 순서와 각 덱 순서대로 인스턴스 ID를 1부터 배정한다.
같은 입력은 같은 ID를 만든다. 생성/복제 효과와 그에 필요한 ID 할당기는 추후 구현한다.
`OriginalDefinitionId`는 생성 당시 정의이고 `CurrentDefinitionId`는 현재 형태의 정의다.
현현 자체를 실행하는 API는 아직 없다.

기존 전투 테스트와 호출부를 위해 정의 목록이 비어 있고 두 정의 ID가 모두 0인 인스턴스는
저수준 전투/Zone API에서 지원한다. `InitializeGame`은 반드시 덱 항목을 실제 정의에서 생성하며,
`PlayCardCommand`는 정의 없는 카드를 소환하지 않는다.

## Zone과 이동

각 `PlayerState`가 `Deck`, `Hand`, `Field`, `Graveyard` 벡터를 가진다.
카드가 위치한 벡터가 실제 Zone이며 별도의 중복 Zone 필드는 두지 않는다.
덱의 index 0이 맨 위다. 덱/손패/필드/묘지의 순서를 모두 상태 비교와 Replay에 포함한다.

`GetZone`은 명시적인 Zone 조회를 제공하고, 기존 `FindCard`는 계속 **Field만** 검색한다.
`ValidateCardIntegrity`는 전체 Zone의 인스턴스 ID 중복, 소유권 불일치, 정의 참조 오류를 검사한다.
`ValidateGameState`는 여기에 설정, 활성 플레이어, 턴/페이즈, 손패/필드 제한, Life/승패 일관성을 검사한다.

`MoveCard(state, owner, id, from, to)`는 서버 내부 이동 기본 연산이다.
출발 Zone의 소유자와 ID가 정확해야 하고 목적지의 손패/필드 제한을 지킨다.
같은 Zone 이동, 잘못된 Zone, 존재하지 않는 카드, 파괴된 카드의 필드 이동은 거부한다.
성공 시 목적지 끝에 추가한다. 실패 시 상태는 변하지 않는다.
이 API 자체는 플레이어가 임의로 덱 소환/회수/부활을 수행할 수 있다는 게임 규칙을 뜻하지 않는다.
해당 효과는 나중에 별도 검증과 이벤트를 갖는 서버 처리기로 구현해야 한다.

묘지 이동 시 `Id`, `OwnerId`, `OriginalDefinitionId`를 유지하고 `CurrentDefinitionId`를 원본 ID로 둔다.
현재 HP/능력치/공격 여부는 마지막 실행 상태의 기록으로 유지한다.
부활 시 HP, 능력치와 일시 효과를 어떻게 초기화할지는 미확정이며 이번 단계에서 부활 Command는 제공하지 않는다.

Zone 이동 또는 성공한 Command 이후에는 기존 포인터/참조가 유효하다고 가정하지 않는다.
외부 호출자는 ID를 보관하고 상태에서 다시 조회한다. 이벤트에는 포인터/참조를 넣지 않는다.

## 초기화와 설정

`InitializeGame(output, config, definitions, players, firstPlayerId)`는 두 플레이어와 덱을 생성하고
각 덱 앞쪽에서 `InitialHandSize`만큼 초기 손패를 만든다. 실패하면 기존 output을 유지한다.
첫 플레이어는 호출자가 명시한다. 셔플, 시간, 난수는 사용하지 않는다.
초기화 직후는 `Status = Running`, `TurnStarted = false`, `Phase = Main`이며 첫 턴 시작 Command가 필요하다.
초기 손패는 초기 상태 스냅샷의 일부이고 별도 초기화 이벤트는 현재 제공하지 않는다.

다음 기본값은 실행 가능한 예제를 위한 임시 설정이며 최종 게임 규칙이 아니다.

| GameConfig 필드 | 기본값 | 의미 |
|---|---:|---|
| `InitialLife` | 20 | 초기 Life. 양수 |
| `InitialHandSize` | 0 | 초기 손패 수 |
| `FieldLimit` | 5 | 플레이어별 필드 상한. 0이면 소환 불가 |
| `HandLimit` | 10 | 손패 상한. 0이면 손패 보유 불가 |
| `DrawPerTurn` | 0 | 턴 시작 자동 드로우 수 |
| `AllowManualDraw` | true | Main의 명시적 DrawCommand 허용 여부 |
| `CanAttackOnSummon` | true | 소환 직후 공격 허용 여부 |

자동 드로우와 수동 드로우는 별도 기능이다. 수동 드로우를 허용하면 현재는 횟수 제한이 없다.
정규 게임 규칙에 맞는 수치는 호출자가 설정해야 한다. 수동 드로우가 없는 게임은 false로 둔다.
덱 부족/손패 초과 시 현재는 부분 처리 없이 Command 전체를 거부한다.
덱 소진 패배, 강제 드로우 대체, 손패 초과 시 버리기 규칙은 아직 정하지 않았으며 TODO다.
초기 손패 수는 손패 제한을 초과할 수 없고, 턴 자동 드로우 실패 시 턴 시작도 반영하지 않는다.

## Command와 Event

`CommandResult::Code`가 **모든 명령의 성공/실패 기준**이다.
`Code == Success`일 때만 성공이다. `Result`는 기존 공격 API용 `BattleResult` 상세 결과로 유지한다.
다른 명령이나 전역 오류에 대해 `Result`만 읽어서 성공 여부를 판단하면 안 된다.
기존 전투 검증 실패는 `Code = BattleRejected`와 정확한 `Result`를 함께 반환한다.

| Command | 검증과 성공 동작 |
|---|---|
| `StartTurnCommand` | 활성 플레이어의 아직 시작하지 않은 턴만 허용. 공격 여부 초기화, 설정된 드로우 수행 |
| `DrawCommand` | 활성 플레이어의 시작한 Main 턴, 수동 드로우 허용, 양수 Count, 덱/손패 여유 검사 |
| `PlayCardCommand` | 활성 플레이어의 Main 턴, 자기 손패의 살아 있는 Monster, 필드 여유 검사 후 Hand → Field |
| `AdvancePhaseCommand` | 시작한 활성 턴에서 Main → Battle → End. End에서 추가 진행은 거부 |
| `EndTurnCommand` | 시작한 활성 턴의 End에서만 허용. 다음 플레이어, TurnNumber + 1, Main/미시작 상태 |
| `AttackCommand` | 기존 전투 검증과 동시 피해를 사용하고 파괴된 전투 참여 카드를 묘지로 이동 |
| `ChangeLifeCommand` | 신뢰할 수 있는 서버/향후 효과 처리기의 Life 변경 입력. 플레이어 행동 권한을 의미하지 않음 |

소환 비용, 소환 횟수, 자원 지불, RP/응전은 아직 구현하지 않는다.
턴 Command의 단계 분리는 중복 시작으로 공격 여부를 반복 초기화하는 것을 막는다.
`CanAttackOnSummon = false`이면 소환할 때 공격 여부를 true로 두고 다음 자기 턴에 초기화한다.
소환 제한과 이미 공격한 상태를 별도로 표현할 필요가 생기면 필드를 분리할 예정이다.

Command는 작업 상태 복사본에서 검증/처리한 뒤 성공 시에만 authoritative 상태를 교체한다.
전투는 기존 `ResolveBattle`이 검증과 피해 처리를 담당하며, 복사본에서 전투가 성공해도
전체 상태 무결성 검사에 실패하면 원래 상태와 빈 이벤트 목록을 그대로 반환한다.
전투 검증 순서(페이즈 → 활성 플레이어 → 존재/소유권 → 파괴 여부 → 공격 여부)는 유지한다.
게임 종료, 미시작 턴 등 추가된 전역 조건은 별도 Command 오류로 처리한다.

`GameEvent`는 값 타입이며 이동 이벤트에는 소유자, 인스턴스 ID, 출발/도착 Zone, 정의 ID가 포함된다.
이벤트 종류는 기존 전투 4종과 `CardMoved`, `TurnStarted`, `TurnEnded`, `PhaseChanged`,
`LifeChanged`, `GameEnded`다. 선택적 필드의 미사용 값은 기본값을 유지한다.

공격 성공 이벤트 순서는 다음과 같다.

```text
BattleStarted
CardDamaged(attacker)
CardDamaged(defender)
CardDestroyed(attacker)  // 해당할 때
CardDestroyed(defender)  // 해당할 때
BattleCompleted
CardMoved(attacker, Field -> Graveyard)  // 해당할 때
CardMoved(defender, Field -> Graveyard)  // 해당할 때
```

기존 이벤트 순서와 payload를 유지하고 묘지 이동 이벤트를 뒤에 추가한다.
BattleCompleted는 전투 피해/파괴 계산의 종료이며, 전체 Command는 묘지 이동까지 완료해야 성공한다.
피해는 max(0, Attack)으로 기록하며, 초과 피해는 기록하되 HP는 0으로 제한한다.

## 저수준 전투 API 호환성

`ResolveBattle`은 기존대로 파괴 직후 카드를 필드에 남긴다. 기존 전투 규칙을 재작성하지 않았다.
새 게임 진행에서 이를 직접 사용하면 Command의 묘지 정리/이벤트를 우회하므로 일반 입력에는 사용하지 않는다.
`StartTurn`, `AdvancePhase`, `EndTurn` 역시 기존 호출부를 위한 저수준 API다.
기존 `EndTurn`은 다음 턴까지 즉시 시작하지만 `EndTurnCommand`는 다음 플레이어의 명시적 시작을 기다린다.
둘을 혼용하지 않는다. 종료 상태에서 저수준 턴/전투/이동 API도 상태를 바꾸지 않는다.

기존 66개 테스트 그룹은 모두 유지했다. 그중 성공 공격 Command의 9개 경우는
이전의 '필드에 파괴 카드 보유' 기대를 '묘지 보유 + 뒤에 이동 이벤트 추가'로 수정했다.
직접 `ResolveBattle`을 테스트하는 기존 기대는 유지했다.
상태/이벤트 비교는 C++20 구조적 equality로 확장해 새 필드와 모든 Zone까지 비교한다.

## Life, 게임 종료, Replay

`ChangePlayerLife(state, playerId, delta)`는 `ChangeLifeCommand`와 같은 경로를 사용한다.
Life 계산은 넓은 정수로 수행하고 음수 결과는 0으로 제한한다.
양수 결과가 int 최대값을 넘으면 실패한다. 최대 Life 상한은 아직 정하지 않았다.
Life 0이면 `Finished`로 전환하고 살아 있는 상대 플레이어를 승자로 기록한다.
이벤트 순서는 `LifeChanged` → `GameEnded`다. 종료 이후 모든 Command는 빈 이벤트와 함께 거부된다.
승자가 없는 종료 상태의 표현은 `WinnerPlayerId = 0`으로 예약한다.
동시 Life 변경/동시 패배를 실행하는 API와 판정 순서는 아직 제공하지 않는다.

몬스터 전투는 플레이어 Life를 감소시키지 않는다. 직접 공격/관통 피해 규칙은 TODO다.
통합 테스트와 main 예제는 양쪽 소환·전투·묘지 이동을 먼저 검증하고,
별도 서버 Life 변경 입력으로 종료 판정을 검증한다.

`GameCommand`는 지원하는 Command의 std::variant이고 `ReplayCommands(initial, commands)`는
동일한 ProcessCommand 경로로 순서대로 실행한다. 실패 명령도 기록하고 다음 명령을 실행한다.
초기 상태에는 설정, 정의 스냅샷, 덱 순서를 모두 포함해야 한다.
반환값은 최종 상태와 각 명령의 상세 결과/이벤트다. 외부 저장용 직렬화 형식은 아직 없다.
모든 상태 필드, Zone 순서, 카드 필드, 이벤트의 모든 값이 비교 대상이다.

## 현현 확정 규칙과 HP 정책

현현은 기존 인스턴스의 형태 변경이다. 새 인스턴스를 만들지 않고 `CardInstanceId`와 소유자를 유지한다.
`OriginalDefinitionId`는 바꾸지 않고 `CurrentDefinitionId`를 현현 정의로 변경한다.
`HasAttackedThisTurn`은 초기화하지 않는다. 필드에서 현현 해제는 불가능하다.
현현 상태에서 파괴되면 묘지에는 원본 정의의 카드가 들어간다.

HP 정책은 사용자 최종 지시에 따라 **받은 피해량 유지(PreserveDamage)**로 확정했다.
이전의 '최대 HP 변경 시 미확정' 문구보다 이 규칙이 우선한다.
유효한 생존 카드(0 < Hp <= MaxHp)에 대해 계산은 다음과 같다.

```text
damageTaken = oldMaxHp - oldHp
newHp = max(0, newMaxHp - damageTaken)

oldMaxHp = 5, oldHp = 3 -> damageTaken = 2
newMaxHp = 8 -> newHp = 6
```

현재 Hp/MaxHp로 피해량을 계산하므로 별도 피해 누적 필드를 중복 저장하지 않는다.
현현 구현 시에는 이 계산을 독립 HP 정책 함수에 두고 공격 여부와 ID는 그대로 유지한다.
덧셈/뺄셈은 넓은 정수로 검증한다. 최대 HP가 작아져 결과 HP가 0인 경우의
즉시 파괴 시점과 대체 효과 개입, 일시 최대 HP 보정과 현현 적용 순서는 추후 확정해야 한다.
초과 회복/비정상 HP 상태의 정규화도 정책으로 결정한다. 이번 단계는 현현 명령을 구현하지 않는다.

## 효과 시스템 확장 계획

현재 Command와 Zone 기본 연산을 재사용하되, 플레이어 행동 Command와 카드 효과의 실행 권한은 분리한다.
예를 들어 효과 Draw는 상대 턴에도 가능할 수 있으므로 Main/활성 플레이어 검증이 있는
DrawCommand를 그대로 재호출하는 것으로 구현하지 않는다. 이후 효과 처리기가 대상/타이밍을 검증하고
서버 내부 이동·피해·Life 연산을 호출하도록 구성한다.

| 예정 효과 | 재사용할 상태/추가로 정해야 할 규칙 |
|---|---|
| Draw | Deck → Hand, 덱 부족/손패 초과 정책 |
| Salvage | Graveyard → Hand, 원본 정의와 회수 가능한 카드 필터 |
| Mill/Dump | Deck/Hand → Graveyard, 출발 Zone, 선택 방식과 수량 |
| Search | Deck 후보 선택 → 지정 Zone, 필터와 공개 범위, 이후 덱 순서 |
| SummonFromDeck | Deck → Field, 필드 제한과 소환 시점/조건 |
| SummonFromGraveyard | Graveyard → Field, HP/능력치/공격 여부 초기화 |
| ReturnToHand | Field → Hand, 일시 효과와 형태 정리 정책 |
| Destroy | 파괴 사실 → 묘지 이동, 대체 효과 개입 시점 |
| DealDamage / Heal | 카드/플레이어 구분, HP/Life 상한과 파괴/종료 시점 |
| ModifyStats | 기본값/일시 보정 분리, 수명과 적용 순서 |
| Transform | 현현과 일반 변신 구분, 정의/HP 정책과 허용 형태 |
| AddResource / SpendResource | 자원 종류/상한/지불 검증. 현재 자원 모델 없음 |

대상은 FriendlyMonster, EnemyMonster, AnyMonster, Self, FriendlyPlayer, EnemyPlayer를 기본 어휘로 삼는다.
이후 대상 기술은 **관계 + Zone 집합 + 카드 타입/종족/속성 필터 + 수량**을 조합한다.
단일 거대 enum에 모든 조합을 나열하지 않는다. 현재 코드는 Monster 타입만 제공하며 종족/속성 모델은 TODO다.
선택 결과는 플레이어/인스턴스 ID 값으로 보관하고, 적용 시점에 Zone/소유권/조건을 다시 확인한다.
OwnerId와 제어권은 현재 동일하게 취급한다. 제어권 변경을 추가하려면 ControllerId와 관계 판정을 별도로 설계해야 한다.

| 효과 유형 | 향후 제약과 미확정 사항 |
|---|---|
| Activated | 발동 권한, 비용, 대상 선택, 사용 횟수, 허용 타이밍 |
| Triggered | 어떤 확정 이벤트를 관찰하는지, 동시 유발 순서, 반복 방지 |
| Continuous | 기본 정의와 보정 분리, 재계산 순서, 수명/Zone 이탈, 순환 의존 |
| Replacement | 상태 변경 전 개입, 적용 순서/선택권, 중복 적용과 무한 루프 방지 |

현재 반환 이벤트는 처리 완료 사실이다. Replacement를 반환 이벤트를 사후 수정하는 방식으로 만들지 않는다.
구현 시 mutation 이전의 의도/예정 동작과 확정 이벤트를 구분해야 한다.
파괴 이벤트와 묘지 이동 사이의 replacement/trigger 타이밍도 그때 명확히 정한다.
현현, 응전 우선권/스택, 지속 효과 계층, 대체 효과 순서가 확정되기 전 범용 인터프리터나 스크립트 엔진은 도입하지 않는다.

## 검증

기존 66개 그룹을 포함한 총 187개 테스트 그룹으로 Zone 무결성, 초기화 실패 원자성,
명령별 성공/실패/경계, 전체 상태 불변, 종료 후 거부, 통합 게임 흐름을 검사한다.
Replay는 전체 결과뿐 아니라 명령 접두 시퀀스마다 모든 상태 필드와 이벤트 payload를 비교한다.
Windows Debug/MSVC와 Docker Linux Release/GCC의 CMake/CTest로 확인한다.
Docker 런타임 예제도 명령 기반 시나리오와 Replay 일치를 확인한다.

다음 단계는 게임 규칙 확정과 작은 효과 한 종류의 명시적 처리기 추가다.
그 후 직렬화 DTO, 공개 정보/비공개 정보 분리, 결정론을 유지하는 효과 타이밍을 단계적으로 추가한다.

온라인 멀티플레이 구현·실행·프로토콜·검증 절차는 [OnlineMultiplayer.md](OnlineMultiplayer.md)를 참조한다.
