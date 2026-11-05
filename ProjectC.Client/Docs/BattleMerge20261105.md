# 2026-11-05 병합 검증

로컬 `1d4ed64` 전투 UI 작업에 `origin/main`의 `b5c224d`까지 병합했다.

- 전투 프리팹과 씬은 로컬 버전을 보존했다. 자동 병합된 카드 프리팹의 원격 폰트·크기 변경도 로컬 설정으로 되돌렸다.
- 양쪽 요력 패널은 로컬의 실제 값 표시와 최대 요력 0 처리, 기존 Inspector 바인딩을 유지했다. 원격의 글자 스타일 처리를 결합했다.
- 상세창은 로컬 크기·스크롤·안전 영역을 유지하면서 원격의 빈 보조 설명 숨김과 스타일 처리를 반영했다.
- 필드 글로우는 서버의 행동 가능 여부와 현재 입력 권한을 함께 사용한다. 손패는 로컬의 서버 기반 사용 가능 표시를 유지한다.
- 턴 버튼은 원격의 현지화 및 상대 턴 상태 처리를 포함한다. 추가 대기 문구는 프리팹에 전용 슬롯이 있을 때 표시하도록 해 로컬 장식·텍스트와의 겹침을 방지했다.
- 원격 카드 설명 서식 수정과 서버 테스트 타임아웃 수정은 포함했다. GameCore와 프로토콜은 변경하지 않았다.

Unity 6000.0.81f1 검증:

| 테스트 | 결과 |
|---|---:|
| ReferenceLayoutMeasurementPlayModeTests | 1/1 통과 |
| BattleMergeCompatibilityTests | 1/1 통과 |
| GameplayInputPlayModeTests | 7/7 통과 |
| DrawPresentationPlayModeTests | 12/12 통과 |
| MultiplayerPlayModeTests.DrawPresentationPreservesServerDragAttackAndRematch | 1/1 통과 |
| CardLocalizationTests | 24/25 통과 |

현지화의 `NewFactionsUsePublishedStatsTextCostsAndExistingArtworkFallback`은 설명 서식 원문 `{0}`과 치환된 `1`을 직접 비교해서 실패한다. 병합 전에도 확인된 항목이며 테스트 기대값은 변경하지 않았다.

검증 로그와 결과 XML은 `/tmp/projectc-merge-*.log`, `/tmp/projectc-merge-*.xml`에 기록했다. 리베이스 이전 원본은 `rescue/pre-rebase-20261105-112443` 브랜치에 보존되어 있다.
