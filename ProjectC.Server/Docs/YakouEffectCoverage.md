# 야행 카드 효과 지원 현황

기준: `YakouPrototype/Cards.json` 34종. 상태는 **서버 내부 프로토타입의 명시적 효과 처리 API** 기준이다. 현재 실제 온라인 대전의 지원을 뜻하지 않는다. JSON과 스프레드시트 데이터는 수정하지 않았다.

| ID | 이름 | 동작 | 현재 범위 |
|---:|---|---|---|
| 2001 | 등불동자 | Mill | 단일 효과 수동 처리 |
| 2002 | 망령 수집가 | Salvage | 단일 효과 수동 처리 |
| 2003 | 여우불 길잡이 | ModifyCountdown | 단일 효과 수동 처리 |
| 2004 | 곡비 | Salvage | 조건/대상 문맥 미구현 |
| 2005 | 잔영여우 | Mill, ModifyCountdown | 단일 효과 수동 처리 / 단일 효과 수동 처리 |
| 2006 | 오니 문지기 | ReduceDamage | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 2007 | 백귀의 선봉 | ModifyAttack | 조건/대상 문맥 미구현; 해당 트리거 실행 흐름 미연결 |
| 2008 | 탐식귀 | Mill | 단일 효과 수동 처리 |
| 2009 | 야행의 징 | Mill | 단일 효과 수동 처리; 해당 트리거 실행 흐름 미연결 |
| 2010 | 망자의 부름 | SpecialSummon | 조건/대상 문맥 미구현; 해당 트리거 실행 흐름 미연결 |
| 2011 | 저승길 봉쇄 | ModifyAttack | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 2012 | 망자의 비호 | ReduceDamage | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 2101 | 귀화의 길잡이 | ModifyCountdown, GrantPiercing | 단일 효과 수동 처리 / 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 2102 | 탐식의 오니왕 | SetCountdown | 단일 효과 수동 처리; 해당 트리거 실행 흐름 미연결 |
| 3001 | 길 잃은 등불지기 | Mill | 단일 효과 수동 처리 |
| 3002 | 떠돌이 약장수 | HealAccumulatedDamage | 단일 효과 수동 처리 |
| 3003 | 무명의 낭인 | ModifyAttack | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3004 | 산길의 파수꾼 | ReduceDamage | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3005 | 굶주린 아귀 | ModifyCountdown | 단일 효과 수동 처리 |
| 3006 | 묘지의 나그네 | HealAccumulatedDamage | 단일 효과 수동 처리 |
| 3007 | 까마귀 전령 | ModifyCountdown | 단일 효과 수동 처리 |
| 3008 | 종이 인형 | ModifyCountdown | 조건/대상 문맥 미구현 |
| 3009 | 황혼의 방랑자 | ReduceDamage | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3010 | 낡은 갑옷무사 | IncreaseMaxBlocks | 효과 미구현 |
| 3011 | 길잡이의 부적 | ModifyCountdown | 단일 효과 수동 처리; 해당 트리거 실행 흐름 미연결 |
| 3012 | 낡은 지도 | LookAndChoose | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3013 | 망자의 거래 | Salvage | 조건/대상 문맥 미구현; 해당 트리거 실행 흐름 미연결 |
| 3014 | 정화의 향 | HealAccumulatedDamage | 단일 효과 수동 처리; 해당 트리거 실행 흐름 미연결 |
| 3015 | 달빛의 의식 | IgnoreOneManifestCondition | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3016 | 순간의 결계 | ReduceDamage | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3017 | 봉인의 부적 | PreventEffectDamage | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3018 | 그림자 바꿔치기 | ReassignBlocker | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3019 | 저승의 속삭임 | ModifyAttack | 효과 미구현; 해당 트리거 실행 흐름 미연결 |
| 3020 | 역행의 주문 | NegateSpellEffect | 효과 미구현; 해당 트리거 실행 흐름 미연결 |

공통 미완성: 자동 트리거 스케줄링, 체인 효과 실행, 선택/우선권 및 서버 권위 비용 문맥, WebSocket DTO, Unity 연결, 재접속과 리플레이. `RuntimeReady`는 false다.

카드 2002의 한국어 설명은 선택 회수를 언급하지만 실제 JSON에는 `Optional` 속성이 없다. 데이터를 임의로 변경하지 않고 현재 JSON을 따른다.
