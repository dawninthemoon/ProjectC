# 백귀야행 실험 카드 세트

`Data/HyakkiYagyo/Cards.json`과 `Decks.json`은 제공된 연구 보고서의 첫 30장 구성으로 만든 **별도 실험 데이터**다. 서버를 다음과 같이 실행하면 클라이언트 로비에서 `HyakkiYagyo` 덱을 선택할 수 있다.

```powershell
cmake --build build --config Debug --parallel 4
./build/Debug/ProjectCNetworkServer.exe --port 7777 --data ./ProjectC.Server/Data/HyakkiYagyo
```

기존 `Data`와 회귀 테스트용 `Tests/LegacyData`는 별개다. 기본 `Data/Decks.json`에 로컬에서 변경된 `DrawPerTurn=1`은 그대로 보존했다. 기존 284개 GameCore 테스트 동작은 LegacyData로 검증한다.

## 현재 실험값

`Decks.json`의 `Config`에서 조정한다. 초기 Life 20, 시작 손패 5, 매 턴 드로우 1, 필드 상한 5, 손패 상한 10, 덱 정확히 30장, 동일 카드 최대 3장이다. 요력은 **초기 2**이며 자기 두 번째 턴부터 매 시작 시 1 증가하고 상한 10이다. `InitialYokaiPower`, `YokaiPowerPerTurn`, `MaxYokaiPower`가 설정 키다. `SummoningSickness=false`인 이 실험에서 소환된 몬스터는 같은 턴 Battle 단계부터 공격할 수 있다. 일반 소환 횟수는 여전히 턴당 1회다. 첫 번째 플레이어도 첫 `StartTurn`에서 `DrawPerTurn`만큼 드로우한다. 이 값과 카드 수치는 최종 규칙이 아니다.

## 카드 JSON 형식

`Cards.json`은 `Cards` 배열이다. 각 원소의 `Id`, `Name`, `Type` (`Monster` 또는 `Spell`), 몬스터의 `Attack`·`MaxHp`, `Archetypes` 문자열 배열, `Rarity`, `ArtKey`, `MainDeckEligible`, `AllowedManifestations`, `Effects`를 정의한다. `MainDeckEligible=false`인 현현 형태는 덱에 넣을 수 없다. `Effects`의 `Type` (`Activated`/`Triggered`), `Trigger`, `ActivationZone`, `Costs`, `PerTurnLimit`, 묘지 조건, 순서 있는 `Actions`를 사용한다. 대상 필터는 `Category`, `Zones`, `Type`, `RequiredArchetype`, `MaxAttack`, `MinCount`, `MaxCount`로 구성한다. 비용은 `YokaiPower`, `Life`, `Discard`, `Tribute`, `HiddenExileTop`이고, 효과는 기존 원시 연산에 `Discard`, `SendToGraveyard`, `SummonFromHand`, `Exile`, `GainYokaiPower`를 추가했다. 임의 스크립트 실행은 지원하지 않는다. 카드 정의 ID와 예시는 실제 JSON을 기준으로 한다.

덱 검증은 `Decks.json`의 `Decks[].Cards`에서 정의 존재, 메인 덱 허용 여부, 장수, 동일 카드 상한을 확인한다. 서버 권위 시드와 Fisher–Yates 셔플을 사용하고, 확정된 `PlayerActionCommand`의 선택 결과를 Replay에 기록한다. 23장 백귀야행 카드와 7장 공용 카드로 구성했다. `구미호 현현`은 현현 형태이므로 덱 수량 0이다.

## 실전 동작과 프로토콜

주문은 손패에서 드래그하거나 클릭하여 발동한다. 몬스터의 발동 효과와 현현도 서버가 `Activations`에 제공한 경우에만 표시한다. 명령은 기존 `SubmitCommand` 봉투 안에 `{"Type":"PlayerAction","CardId":12,"EffectId":1}`로 전송한다. `EffectId=0`은 일반 소환이다. 서버가 대상 선택을 요청하면 `CommandPending`과 **선택하는 플레이어에게만** `ActionChoiceRequired`를 보낸다. 응답은 `{"Type":"ResolveChoice","ChoiceId":"...","Choices":["option token"]}`이다. 비용 단계와 효과 단계는 `IsCost`로 구분한다. `CancelAction`은 아직 비공개 새 카드나 덱 검색을 공개하지 않은 선택에만 허용된다. 공개 후에는 `CanCancel=false`이며 끝까지 선택해야 한다.

선택 후보 토큰은 서버 발급이고 임의 카드 ID로 다른 매치나 상대 손패를 조작할 수 없다. 덱 검색 후보는 해당 플레이어에게만 정의 정보를 보여 주며 카드 인스턴스 ID와 덱 순서는 내보내지 않는다. 드로우 후 선택은 확정 전의 가상 상태를 본인에게만 보여 준다. 모든 비용과 연속 효과는 후보 상태에서 계산하고 마지막 선택이 유효할 때 GameState를 한 번에 확정한다. 연결 해제 시 원본 GameState와 선택 토큰을 매치에 유지하며 재접속하면 동일 선택을 복구한다. 공개 스냅샷은 `YokaiPower`, 공개 `Exile`, `HiddenExileCount`만 제공하고 숨긴 제외 카드와 덱 순서는 제공하지 않는다.

## 구현 범위와 남은 결정

17개 카드 정의와 첫 30장 덱 중 `봉마의 칼바람`은 데이터에 등록하지 않았다. 현재 필드에는 지속/세트 주문과 함정 존이 없어 이 카드의 유효 대상이 없다. 계정용 덱 편집·멀리건, RP 응전 스택, 연출용 일러스트도 아직 없다. 연구 보고서의 “황천의 초혼부로 오니 문지기 부활” 예시는 카드의 `ATK≤3` 제한과 충돌하므로 그대로 재현하지 않았다. 오니는 제한 없는 `귀환의 부적`으로 부활시키거나 대상 조건을 별도로 확정해야 한다. 기존 기본 규칙의 소환 후유증과 무비용 현현은 변경하지 않았으며 이 파일의 수치는 실험 데이터에서만 활성화된다.

검증: Windows MSVC Debug CTest 4/4, Ubuntu 24.04 WSL GCC Release CTest 4/4, 308개 GameCore 그룹, 실제 WebSocket 두 매치 격리·비공개 선택·재접속·Replay, Unity PlayMode 6/6, Windows Development 빌드 성공. Docker Desktop 엔진이 실행되지 않아 이번 변경의 로컬 Docker 이미지·Compose 검증은 수행하지 못했다. CI에는 기존 Docker 이미지 빌드·CTest·컨테이너 수명 검증 단계가 남아 있다. 두 개의 Windows 그래픽 창을 사람이 조작하는 수동 검증도 별도로 필요하다.
