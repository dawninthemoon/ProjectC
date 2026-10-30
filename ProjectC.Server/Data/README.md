# CardCatalog 데이터 형식

Cards.json과 Decks.json은 서버가 시작할 때 읽는 UTF-8 JSON이다. 실행 중 변경은 반영하지 않는다. `--data <directory>`로 경로를 지정한다. 각 파일은 1 MiB 이하, JSON 중첩 깊이는 32 이하여야 한다. 중복/미등록 키, 잘못된 타입·범위·참조·효과 정의는 서버 시작 전에 거부한다.

## Cards.json

최상위는 `{"Cards":[...]}`이다. 최대 1024개 정의를 지원한다.

| 필드 | 형식 | 설명 |
|---|---|---|
| Id | 양의 정수, 최대 1000000 | 전역 고유 정의 ID |
| Name | UTF-8 문자열, 1..256바이트 | 표시 이름, 제어문자 금지 |
| Attack | 정수 0..100000 | 기본 공격력 |
| MaxHp | 정수 1..100000 | 기본 최대 HP |
| Traits | 선택 문자열 배열, 최대 16 | 종족/특성 임시 문자열 |
| AllowedManifestations | 선택 ID 배열, 최대 16 | 유효한 다른 정의 참조 |
| Effects | 선택 배열, 최대 16 | 아래 EffectDefinition |

현재 GameCore CardType은 Monster 하나이므로 Type을 임의로 확장하지 않는다.

EffectDefinition 필드는 `Id`, `Type`, 선택 `Trigger`, `Actions`다. Id는 카드 안에서 고유하다. Type은 Activated 또는 Triggered다. Continuous/Replacement는 아직 실행되지 않으므로 로더에서 거부한다. Triggered는 OnSummoned/OnDestroyed/OnDamaged/OnTurnStarted/OnTurnEnded를 사용하고 Activated는 None 또는 생략한다.

각 Action은 `Operation`, `Targets`, 선택 `Amount`(0..100000, 기본 1), `AttackDelta`/`MaxHpDelta`(-100000..100000, 기본 0), `TransformDefinitionId`를 사용한다. Actions는 최대 16개다. 실제 의미/유효성은 기존 GameCore 효과 검증을 따른다.

Operation은 Draw, Mill, Salvage, Search, SummonFromDeck, SummonFromGraveyard, ReturnToHand, Destroy, DealDamage, Heal, ModifyStats, Transform이다. Targets는 Category, 선택 Zones/MinCount/MaxCount/RequiredTrait다. Category는 기존 Self/FriendlyMonster/EnemyMonster/AnyMonster/FriendlyPlayer/EnemyPlayer/FriendlyDeck/FriendlyHand/FriendlyGraveyard/EnemyGraveyard 중 하나다. Zones는 Deck/Hand/Field/Graveyard다. 대상 타입은 현재 Monster다.

```json
{"Id":20,"Name":"Test Warden","Attack":2,"MaxHp":3,"Effects":[
  {"Id":1,"Type":"Triggered","Trigger":"OnSummoned","Actions":[
    {"Operation":"ModifyStats","Targets":{"Category":"Self","Zones":["Field"]},"AttackDelta":1}
  ]}
]}
```

수동 Activated 효과와 Transform 목적 정의는 데이터로 검증/보관할 수 있지만 현재 원격 명령/UI에는 노출하지 않는다. 실제 예제 덱은 자동 소환 Trigger만 사용한다. 예제 10 -> 12는 허용 현현 형태 연결을 보여주기 위한 데이터다.

## Decks.json

최상위는 `{"Config":{...},"Decks":[...]}`이다. Config의 모든 필드는 필수다.

| Config 필드 | 예제 값 | 범위 |
|---|---|---|
| InitialLife | 3 | 1..100000 |
| InitialHandSize | 2 | 0..128, HandLimit 이하 |
| DrawPerTurn | 0 | 0..128 |
| FieldLimit | 5 | 1..32 |
| HandLimit | 10 | 0..128 |
| MinDeckSize | 4 | 1..128 |
| MaxDeckSize | 40 | MinDeckSize..128 |
| MaxCardCopies | 4 | 1..128 |

덱은 최소 두 종류, 최대 32종이다. 각 덱은 고유 ASCII `Id`(1..64자), UTF-8 `Name`(1..256바이트), `Cards`(정의 ID 배열)를 가진다. 각 Cards는 크기/동일 ID 최대 투입 수/정의 존재/초기 손패 조건을 만족해야 한다. 모든 정의와 각 덱의 게임 초기화 및 첫 StartTurn도 GameCore로 검증한다.

```json
{"Id":"Strikers","Name":"Strikers - combat test","Cards":[10,11,10,11]}
```

서버 시작 후 정의와 덱은 불변이다. 플레이어는 덱 ID만 선택하며 개별 덱 내용을 클라이언트가 업로드하는 기능은 없다. DeckCatalog는 이름/ID/장수만 전송한다. 상대가 선택한 덱 ID와 실제 카드 구성은 LobbyState에서 노출하지 않는다.

덱 크기와 중복 수는 GameConfig의 새 구성 값이다. 기존 저수준 InitializeGame 계약을 변경하지 않고 네트워크 시작 전 CardCatalog::ValidateDeck에서 검사한다. 미확정 실제 카드 구성 규칙이나 밸런스를 확정한 것이 아니다.

## 난수와 재현

서버가 OS 난수에서 64비트 시드를 만들고, 각 플레이어 덱 복사본을 같은 mt19937_64 스트림으로 Fisher-Yates 셔플한다. 인덱스 선택은 rejection sampling을 직접 구현해 MSVC/GCC의 std::shuffle 구현 차이를 피한다. 시드/셔플 순서는 네트워크와 로그에 공개하지 않는다. Replay에는 시드와 셔플 후 완전한 InitialState를 별도로 보관하므로 정확히 재현할 수 있다.
