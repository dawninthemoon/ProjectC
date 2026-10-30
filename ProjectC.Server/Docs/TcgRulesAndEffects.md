# Project C — 현재 TCG 규칙과 효과 실행 계약

현재 GameCore는 기존 카드/Zone/Command/Event/Replay 구조 위에 확정 TCG 규칙을 추가한다.
이 문서는 GameCore 규칙 계약을 다룬다. 현재 네트워크·Unity 구현은 [OnlineMultiplayer.md](OnlineMultiplayer.md)에 별도로 문서화한다. RP/응전 스택, Continuous/Replacement 실행, 귀화/RP 자원은 여전히 제외한다.

## 확정 소환 규칙

- 패에서 내는 일반 소환은 무료이며 **자신의 턴당 1회**다.
- 성공한 PlayCardCommand만 NormalSummonsThisTurn을 증가시킨다. 실패하면 횟수를 소비하지 않는다.
- 다음 자신의 턴 시작에 횟수를 초기화한다. 특수 소환은 이 횟수를 사용하지 않는다.
- 일반/특수 소환 모두 소환한 턴 공격할 수 없으며, 다음 자신의 턴이 시작되면 공격할 수 있다.
- 필드에서 나갔다 들어오면 새로운 소환이다. 현현은 소환이 아니다.
- HasAttackedThisTurn과 소환 후유증을 별도로 판정한다.

소환 후유증은 백귀야행의 '소환한 턴 공격 불가'를 위한 상태 추적이다.
다른 게임의 소유/조종 기간 규칙을 가져온 것이 아니다. 현재 제어권 변경은 지원하지 않는다.

PlayerState는 TurnsStarted(자신의 턴 시작 횟수)와 NormalSummonsThisTurn을 가진다.
CardInstance는 EnteredFieldTurnNumber, EnteredFieldActivePlayerId, OwnerTurnAtEntry를 기록한다.
현재 소유자의 TurnsStarted가 OwnerTurnAtEntry보다 커져야 공격할 수 있다.
따라서 상대 턴 특수 소환도 다음 자신의 턴 시작에 준비된다. 전역 TurnNumber만 비교하지 않는다.
IgnoreSummoningSickness는 미래의 예외 효과용 서버 상태이며 현재 플레이어 명령으로 설정할 수 없다.

기존 CanAttackOnSummon 임시 설정은 제거했다. 새 소환은 HasAttackedThisTurn=false로 두되 후유증으로 공격을 거부한다.
정의 없는 과거 전투 fixture의 EnteredFieldTurnNumber=0은 이미 준비된 몬스터를 나타낸다.
저수준 StartTurn을 같은 턴에 재호출해 공격 플래그를 지워도 턴 시작 횟수나 소환 횟수를 초기화하지 않는다.

## 직접 공격

DirectAttackCommand는 기존 몬스터 간 AttackCommand와 구분한다.
두 공격 모두 ValidateAttackSource/ValidateAttackReadiness를 사용해 공통 검증을 공유한다.
Battle 페이즈, 활성 플레이어, 필드 존재, 소유권, 생존, 이미 공격했는지, 소환 후유증을 검증한다.
직접 공격은 상대 필드에 살아 있는 몬스터가 없을 때만 가능하다.
파괴되어 남아 있는 저수준 fixture 카드는 방어 몬스터로 세지 않는다.

직접 공격 피해는 max(0, Attack)이며 상대 Life를 최소 0으로 제한한다.
공격자 HP는 유지하고 HasAttackedThisTurn=true로 기록한다.
이벤트 순서는 PlayerDamaged → LifeChanged → GameFinished(치명타일 때)다.
기존 GameEnded와 새 GameFinished 이름은 같은 enum 값의 별칭이며 중복 종료 이벤트를 발생시키지 않는다.
잘못된 직접 공격은 상태 불변과 빈 이벤트 목록을 보장한다.

## 드로우와 게임 종료

덱이 비어 있다는 사실만으로 패배하지 않는다. 실제 드로우가 필요한데 뽑지 못할 때 패배한다.
다중 드로우는 가능한 카드를 덱 앞에서 순서대로 뽑고 최초 부족 시 종료한다.
기본 DrawCommand, 턴 자동 드로우, 카드 효과와 Triggered Draw가 같은 서버 내부 연산을 사용한다.
Mill/Search/특수 소환 같은 비드로우 연산은 덱 소진 패배를 일으키지 않는다.

패배는 CommandError가 아닌 정상 처리 결과다. Code=Success로 이미 처리한 드로우/소환/효과와 패배를 확정한다.
GameState.EndReason은 None, LifeDepleted, DeckExhausted를 구분한다.
덱 소진 패배에서는 Life를 인위적으로 0으로 바꾸지 않는다.
Life 0 또는 드로우 부족 시 승자를 기록하고 GameFinished를 발생시킨다. 이후 모든 Command를 거부한다.
효과 처리 중 종료되면 나머지 action과 자동 Trigger를 실행하지 않는다.
EffectResolved는 마지막 GameFinished 바로 앞에 기록하여 종료 이벤트를 최종 이벤트로 유지한다.

손패 제한 충돌은 아직 별도 게임 규칙이 확정되지 않았다. 현재는 실제 뽑을 수 있는 수만큼의
손패 공간을 먼저 검사하고 부족하면 HandFull로 전체 명령을 거부한다. 버리기/초과 손패 정책은 TODO다.
초기 손패 구성은 게임 시작 전 초기화 단계이므로 덱 부족을 InitializationResult 실패로 처리한다.

## 효과 데이터와 권한

GameTypes.hpp는 PlayerId/CardInstanceId/CardDefinitionId, CardType과 Zone을 정의한다.
CardDefinition에는 Traits, AllowedManifestations, Effects를 추가했다.
효과 데이터는 초기화 및 상태 무결성 검사에서 검증하며 Command 처리 중 정의 목록은 변경하지 않는다.

EffectDefinition은 카드 정의 내부의 고유 Id, Type, Trigger와 순서가 있는 Actions를 가진다.
EffectAction은 Operation, TargetRule, Amount, AttackDelta, MaxHpDelta, TransformDefinitionId를 정의한다.
클라이언트는 ActivateEffectCommand에 자신의 Player, 필드 SourceCard, EffectId,
action별 대상 ID 목록만 보낸다. 피해량·연산·변신 목적지는 서버 CardDefinition에서 읽는다.
선택 목록의 순서도 명시적 입력이므로 Replay에서 동일하게 재현한다.

Activated는 현재 **자신의 시작한 Main 턴의 살아 있는 자기 필드 카드**에서만 발동한다.
발동 비용/횟수, 상대 턴 발동, 패/묘지 발동은 미확정이므로 추가하지 않았다.
현재 Activated 효과에는 별도 사용 횟수 제한이 없다. 이 정책은 일반 소환 1회 제한과 다르다.
특수 소환/회수 등 효과는 플레이어 행동 Command를 우회 호출하지 않고 서버 내부 기본 연산을 사용한다.

| 연산 | 현재 동작 |
|---|---|
| Draw | 지정 플레이어 드로우. 부족하면 해당 플레이어 패배 |
| Mill | 덱 맨 위부터 min(Amount, 남은 카드 수)만큼 묘지로 이동. 덱 부족 패배 없음 |
| Salvage | 선택한 묘지 카드를 해당 소유자의 손패로 회수 |
| Search | 조건을 만족하는 선택한 덱 카드를 소유자의 손패로 이동. 남은 덱 순서 유지 |
| SummonFromDeck | 선택한 덱 카드를 소유자의 필드로 특수 소환 |
| SummonFromGraveyard | 선택한 묘지 카드를 원본 정의의 HP/능력치로 복구 후 소유자의 필드로 특수 소환 |
| ReturnToHand | 선택한 필드 카드를 해당 소유자의 손패로 이동 |
| Destroy | HP=0, CardDestroyed, Field → Graveyard |
| DealDamage | 필드 몬스터 HP 또는 지정 플레이어 Life 감소 |
| Heal | 몬스터는 MaxHp까지 회복. 플레이어 Life의 별도 상한은 미확정 |
| ModifyStats | Attack/MaxHp 증감. MaxHp 변경은 받은 피해량 유지, HP=0이면 파괴 |
| Transform | 허용된 정의로 현현. 아래 현현 계약 적용 |

묘지 회수/부활은 원본 정의의 능력치와 HP를 복구하는 현재 기본 정책이다.
묘지에서 회수한 파괴 카드를 다시 정상 소환할 수 있게 한다. 향후 카드별 다른 부활 HP 정책은 별도 데이터가 필요하다.
ReturnToHand는 현재 형태와 능력치를 유지한다. 새 필드 진입에서는 공격 여부와 소환 후유증을 새 소환으로 기록한다.
ModifyStats는 이번 구현에서 지속 기간 없는 즉시 변경이다. Continuous 효과의 보정 계층을 의미하지 않는다.
자원 AddResource/SpendResource는 구현하지 않았다.

## 공통 대상 검증과 비공개 정보

TargetRule은 Category, 허용 Zones, CardType, RequiredTrait, MinCount/MaxCount를 조합한다.
Self, FriendlyMonster, EnemyMonster, AnyMonster, FriendlyPlayer, EnemyPlayer,
FriendlyDeck, FriendlyHand, FriendlyGraveyard, EnemyGraveyard를 지원한다.
현재 CardType은 Monster만 있다. Traits 문자열로 종족/속성 등 후보 필터를 표현할 수 있으며 정식 분류 체계는 TODO다.

EffectTarget은 Player, Card, Location 값이며 Card=0은 플레이어 대상이다.
ValidateTargets는 개수·중복·Zone·소유권·관계·타입/특성·필드 생존 여부를 검사한다.
각 action/대상 적용 전에 재검증하므로 앞선 action으로 이동/파괴된 대상을 잘못 사용할 수 없다.
상대 Deck/Hand의 카드 ID 선택은 허용하지 않는다. 상대 플레이어 대상의 서버 정의 Draw/Mill은 개별 비공개 ID 선택과 구분한다.

CommandResult.Events는 **서버 내부 authoritative journal**이다. Replay와 트리거 처리에는 원본을 사용한다.
EventsForPlayer(events, viewer)는 상대의 Deck/Hand 출입 CardMoved에서 인스턴스 ID와 정의 ID를 제거한다.
공개 소환/파괴/현현 이벤트는 필드/묘지에서 공개된 정보를 제공한다.
CardDrawn은 중복 이벤트 방지를 위해 현재 별도 발생시키지 않고 기존 CardMoved(Deck→Hand)를 사용한다.
실제 네트워크는 원본 journal을 전송하지 말고 viewer별 projection/공개 DTO를 사용해야 한다.
카드 뒷면 토큰, 공개 이력/패 추적, 관전자, 특수 공개 효과의 네트워크 정책은 TODO다.

## 현현

서버용 TransformCommand/TransformCard와 정의에 연결된 Transform action을 구현했다.
TransformCommand는 신뢰할 수 있는 내부 입력이다. 네트워크에서 플레이어의 자유 변신 명령으로 허용하면 안 된다.
실제 현현 카드/비용/사용 Zone이 정해지면 해당 권한 검증을 추가한다.

- 필드의 살아 있는 자기 소유 인스턴스만 변경한다.
- 현재 정의의 AllowedManifestations에 등록된 유효한 형태만 허용한다.
- Id, OwnerId, OriginalDefinitionId, 공격 여부와 필드 진입 정보는 유지한다.
- 현재 정의와 기본 Attack/MaxHp를 목적 정의로 변경하고 HasManifested=true로 기록한다.
- 받은 피해량을 유지한다. oldMaxHp=5, oldHp=3 → newMaxHp=8이면 newHp=6이다.
- 계산은 넓은 정수로 수행한다. newHp가 0이면 파괴와 묘지 이동까지 처리한다.
- 묘지에는 원본 정의를 기록한다. 필드에서 원본으로 돌아가는 현현 해제는 거부한다.
- 재현현은 GameConfig.AllowRemanifestation=false가 기본이다. true여도 허용 형태 검증과 해제 금지는 유지한다.

현현은 소환이 아니므로 공격 여부와 후유증을 새로 설정하거나 제거하지 않는다.
재현현의 최종 게임 정책, 일시 보정과 형태 변경의 적용 순서는 TODO다.

## 기본 Triggered 처리

OnSummoned, OnDestroyed, OnDamaged, OnTurnStarted, OnTurnEnded를 지원한다.
앞의 세 조건은 **해당 카드 자신의 사건**, 턴 조건은 **소유자의 턴 사건**에 반응한다.
다른 카드의 사건 관찰, 선택적 Trigger와 RP/응전은 아직 구현하지 않는다.
0 피해 CardDamaged는 OnDamaged를 발생시키지 않는다.

처리 순서는 확정 이벤트 순서 → 플레이어 배열/필드 순서 → 카드 정의 Effects 등록 순서의 FIFO다.
숫자 EffectId 정렬이나 임의 우선권을 부여하지 않는다. 이는 동시 발동 우선순위 확정 전의 명시적 임시 정책이다.
자동 대상은 플레이어 배열, Deck/Hand/Field/Graveyard, 각 Zone의 벡터 순서에서 첫 MinCount개의 합법 대상을 선택한다.
플레이어의 대상 선택 창을 대신하는 최종 게임 규칙은 아니며 선택적 발동이 필요하면 별도 프로토콜을 추가해야 한다.

이벤트에는 당시 CurrentDefinitionId를 기록하므로 현현 카드가 원본 정의로 묘지에 가더라도
파괴 당시 형태의 OnDestroyed 효과를 실행할 수 있다.
기본 Command/action 목록 완료 후 자동 큐를 처리한다. 효과 사이 응전 창은 없다.
자동 효과의 대상이 사라지거나 실행 조건을 만족하지 못하면 해당 효과만 취소하고
EffectResolved.EffectOutcome에 실패 코드를 기록한다. 원래 소환/파괴는 되돌리지 않는다.

재귀 호출 대신 큐를 사용하고 TriggerResolutionLimit(기본 64)로 순환을 제한한다.
한도를 넘으면 TriggerLimitReached로 원래 Command 전체를 거부해 중간 상태가 남지 않도록 한다.
이는 잘못된 순환 카드 데이터에 대한 구현상 안전장치이며 정상 드로우 패배는 한도 실패로 취급하지 않는다.
정상 게임 종료가 발생하면 그 시점까지의 정상 결과를 확정하고 큐 처리를 중단한다.

Continuous/Replacement는 타입을 예약했지만 실행하지 않는다. 발동 시 UnsupportedEffect를 반환한다.
Continuous의 계층/재계산/수명과 Replacement의 mutation 이전 의도·적용 순서·중복 방지는 미확정이다.
이미 반환한 이벤트를 사후 수정하는 방식의 Replacement는 도입하지 않는다.

## 원자성, 호환성과 Replay

모든 명령은 Code로 성공 여부를 판단한다. Result는 기존 공격 BattleResult 상세 결과다.
일반적인 검증/효과 실행 실패는 상태 복사본 전체와 이벤트를 폐기한다.
정상적인 덱 부족 패배와 치명적 공격은 Code=Success로 확정된다.
최종 상태 무결성을 검증한 뒤에만 원본 상태를 교체한다.

기존 ResolveBattle API와 전투 이벤트 순서는 유지한다. CardDamaged/CardDestroyed에는
Trigger가 당시 형태를 확인할 수 있도록 CurrentDefinitionId 값을 추가한다.
일반 소환 성공에는 기존 이동 이벤트 뒤에 CardSummoned가 추가된다.
GameCommand variant에는 DirectAttackCommand, ActivateEffectCommand, TransformCommand를 추가했다.
Replay는 신뢰된 전체 입력 기록을 순서대로 실행하고 실패 명령도 같은 결과로 기록한다.
서버용 Transform/ChangeLife 입력은 네트워크 플레이어 입력과 구분해야 한다.

기존 187개 그룹을 삭제하지 않고 새 규칙에 맞게 기대값을 변경했다.
주요 변경은 덱 부족이 실패 코드에서 정상 패배로 바뀐 점, 소환 이벤트/필드 진입/턴 카운터,
종료 사유, 소환 공격 플래그와 후유증 분리다. 기존 저수준 전투 검증 기대는 유지했다.
추가 테스트는 소환·직접 공격·덱 소진, 12개 연산, 대상 10범주, 현현, Trigger 순서/순환,
비공개 이벤트 projection, 전체 게임 시나리오와 명령 접두 시퀀스별 Replay를 검사한다.

현재 총 **284개 테스트 그룹**이 Windows MSVC Debug 빌드에서 통과했다.
Linux Docker GCC Release 빌드 및 내부 CTest도 통과했으며 컨테이너 실행은
몬스터 전투 → 묘지 이동 → 다음 자기 턴의 직접 공격 승리 → Replay 일치를 확인한다.
Docker 빌드의 최신 네트워크/매치 검증 결과는 OnlineMultiplayer.md를 기준으로 한다.

## 네트워크 계층과 현재 개발 상태

다중 MatchManager, 로비/준비/재대전, 외부 카드·덱 정의, 서버 셔플, 매치별 Replay/재접속, Unity 클라이언트를 별도 계층으로 구현했다. 기존 GameCore Command와 규칙 처리는 유지한다. GameConfig의 MinDeckSize/MaxDeckSize/MaxCardCopies는 현재 임시 서버 덱 구성 제약이며 CardCatalog에서 검사한다.

온라인 EndTurn 요청은 기존 AdvancePhase/EndTurn/StartTurn 명령을 순서대로 실행해 상대 턴을 자동 시작한다. 원격 플레이어의 Transform/ChangeLife 직접 호출은 계속 차단한다. 소환 Trigger는 기존 자동 처리 경로를 사용한다. 구체적인 API·토큰/수명 정책·테스트 결과는 [OnlineMultiplayer.md](OnlineMultiplayer.md)를 참조한다.
