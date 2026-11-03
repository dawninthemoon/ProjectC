# 전투 효과음 — 2026-11-03

Unity 6000.0.81f1. 일본 요괴·주술 테마에 맞춘 짧은 종이·북·종 계열의 효과음 14종을 연결했다. 기존에 실행 경로의 AudioManager/AudioSource 재생 시스템은 없었고, 로비의 `AudioListener.volume` 전체 음량 슬라이더와 로비/덱 에디터 카메라의 AudioListener만 있었다. 별도의 네트워크 구독자나 중복 AudioManager는 추가하지 않았다. 서버·GameCore·프로토콜·카드 규칙은 변경하지 않는다.

## 리소스와 라이선스

사용자가 `Assets/Audios`에 내려받은 두 팩을 확인했다. 동봉 License.txt와 2026-11-03 공식 페이지 확인 결과 모두 CC0다.

- 사용 팩: [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds), 원본 라이선스 `Assets/Audios/kenney_interface-sounds/License.txt`.
- 조사한 보조 팩: [Kenney UI Audio](https://kenney.nl/assets/ui-audio), 원본 라이선스 `Assets/Audios/kenney_ui-audio/License.txt`. 이번 기본 라이브러리에서는 이 팩의 클립을 참조하지 않는다.
- [Kenney의 상업적 이용/크레딧 안내](https://kenney.nl/support): CC0로 상업적 이용 가능하며 크레딧은 필수가 아니다. 동봉 출처·라이선스 파일은 그대로 보존했다.
- CC0 문서: <https://creativecommons.org/publicdomain/zero/1.0/>.

클릭·선택용으로 기존 Interface 팩 3종을 재사용한다. 원본 OGG 데이터는 변경하지 않고 재생 pitch/gain만 조정한다. 두 팩은 GUI 중심이므로 종이 움직임, 낮은 북, 주술 종소리와 짧은 승패 동기에 필요한 11종은 Python으로 합성했다. 외부 녹음·샘플·유료 API·유료 서비스는 사용하지 않았다.

합성 WAV의 라이선스는 `Assets/Audios/ProjectC_Synth/LICENSE.txt`에 기록한 **CC0-1.0**이다. 실제 전통 악기 녹음이 아닌 판타지 합성음이다. 생성 스크립트 `Tools/GenerateBattleSounds.py`는 Python 표준 라이브러리만 사용하며 seed를 고정한다.

```sh
python3 Tools/GenerateBattleSounds.py
# 별도 경로에 재현하려면:
python3 Tools/GenerateBattleSounds.py --output /tmp/projectc-sfx
```

합성 파일은 mono, PCM16, 44,100 Hz이며 피크는 0.55이다. 시작/끝 샘플은 0으로 페이드하고 DC를 제거한다. `manifest.json`에 길이·seed를 기록했다. 11종 모두 새 임시 폴더로 재생성한 결과 바이트가 일치했다.

| 상황 | SoundCue | 기본 음원 | 길이 |
|---|---|---|---|
| 카드 선택 | CardSelect | Kenney `select_001.ogg` | 원본 0.040초, pitch 0.9 |
| 카드 선택 해제 | CardDeselect | Kenney `back_001.ogg` | 원본 0.061초, pitch 0.9 |
| 버튼 누르기/키보드 제출 | ButtonClick | Kenney `click_001.ogg` | 원본 0.097초, pitch 0.95 |
| 드로우 | Draw | 합성 `Draw.wav`, 짧은 종이 움직임 | 0.22초 |
| 주문 사용 | CardUse | 합성 `CardUse.wav`, 종이·목질 타격 | 0.28초 |
| 소환 | Summon | 합성 `Summon.wav`, 낮은 북·종 | 0.45초 |
| 공격 선언 | Attack | 합성 `Attack.wav`, 짧은 바람 | 0.22초 |
| 적중/피해 | Hit | 합성 `Hit.wav`, 낮고 짧은 타격 | 0.25초 |
| 카드 파괴 | Destroy | 합성 `Destroy.wav`, 부서짐·소멸 | 0.40초 |
| 현현 | Transform | 합성 `Transform.wav`, 상승하는 종 계열 음 | 0.65초 |
| 효과 발동 | Effect | 합성 `Effect.wav`, 주술 종·종이 | 0.40초 |
| 턴 시작 | TurnStart | 합성 `TurnStart.wav`, 북·종 안내음 | 0.55초 |
| 승리 | Victory | 합성 `Victory.wav`, 짧은 상승 동기 | 1.00초 |
| 패배 | Defeat | 합성 `Defeat.wav`, 하강·낮은 잔향 | 0.90초 |

## 재생 구조와 음량

`ClientAudio` 하나가 `BeforeSceneLoad`에 생성되고 Scene 전환 중 유지된다. `Resources/Audio/ClientSoundLibrary.asset`에 원본 AudioClip의 직접 참조와 gain/pitch/cooldown을 저장했다. 원본 패키지 전체를 Resources에 복사하지 않았고 사용 클립만 라이브러리에서 참조한다.

고정 풀은 UI 3, 전투 6, 결과 1의 AudioSource 총 10개다. 재생마다 GameObject/AudioSource를 생성하지 않는다. 해당 풀의 빈 voice를 사용하고 모두 재생 중이면 가장 먼저 끝날 voice를 재사용한다. UI와 전투는 서로의 voice를 빼앗지 않으며 결과음은 별도 voice를 사용한다. 전부 2D 재생이어서 탑다운 카메라와 전장 높이로 감쇠하지 않는다.

로비 → 설정에 기존 전체 음량과 함께 **UI 효과음**, **전투 효과음** 슬라이더를 추가했다. 기본값은 UI 0.65, 전투 0.8이다. 선택·해제·버튼은 UI 볼륨, 나머지는 전투 볼륨을 사용한다. 기존 전체 `AudioListener.volume`도 계속 곱해진다. 별도 AudioMixer 에셋 없이 그룹별 `AudioSource.volume`을 조절하며 재생 중 voice에도 즉시 적용한다.

UI/전투 설정은 `ProjectC.Audio.UiSfxVolume`, `ProjectC.Audio.BattleSfxVolume` PlayerPrefs에 보관한다. 값 변경 시 설정하고 앱 중지/종료에 저장한다. 테스트는 원래 키/값을 복원한다.

BattleScene에는 기존 AudioListener가 없었으므로 서비스의 fallback listener를 켠다. 로비/덱 에디터처럼 카메라 listener가 있는 Scene에서는 fallback만 끈다. 일반 Scene 전환에서 AudioListener를 중복으로 켜지 않으며 로비 설정 테스트에서 활성 listener 1개를 확인했다.

## 재생 시점과 중복 방지

- 선택음: `BattlePresenter.SelectCard`의 실제 선택 ID 변경, 대상 클릭 선택/해제, 멀리건 클릭, 공개 카드 갤러리 클릭. 데이터 Bind나 일반 snapshot 갱신은 선택음을 내지 않는다.
- 해제음: 사용자의 배경 클릭/취소/대상 해제. 네트워크의 자동 입력 초기화나 명령 전송 시 해제음은 추가하지 않는다.
- 버튼음: `UiButtonSound`의 유효한 왼쪽 누르기/키보드 submit. 풀의 `onClick.RemoveAllListeners()`로 연결이 사라지지 않게 입력 인터페이스를 사용한다. 비활성/상호작용 불가 버튼은 소리를 내지 않는다.
- 드로우: 기존 `BattleEffectsView.Draw`의 실제 비행 시작. 상대는 숨긴 카드의 정체성에 관계없이 같은 소리를 사용한다. 멀리건은 양쪽 확정 후 바꾼 카드가 공개되는 뒤집기 중간에 한 번만 재생한다.
- 전투: 기존 이벤트 배치를 재생하는 `PlayBatches`에서 승인된 `GameEvents`에 `BattleSoundEvents`를 연결한다. Attack=0, Damage=1, Destroy=2, Life=8, PlayerDamage=10, Summon=11, Effect=13, Transform=15, Spell=19는 기존 protocol-v2 값이다. 발신 command나 드래그 시작은 전투 소리를 내지 않으므로 취소/거부된 행동은 소환·타격음을 내지 않는다.
- 직접 공격에는 BattleStarted가 없으므로 PlayerDamaged에서 선언+피해를 재생한다. 이어지는 LifeChanged의 같은 피해는 이벤트 직전 표시 상태와 비교해 중복 재생하지 않는다.
- 기본 동일 cue cooldown은 0.12초, 선택/버튼 0.07초, 드로우/타격 0.08초다. 공격의 양쪽 피해나 한 프레임의 여러 동일 이벤트를 묶는다. SpellActivated와 EffectActivated는 같은 cast gate를 사용한다. 매우 빠른 자동 턴 진행에도 동일 cue cooldown이 적용된다.
- 적중은 DSP 시각 기준 0.065초 뒤, 결과음은 0.18초 뒤에 예약한다. 게임 상태와 애니메이션의 진행을 기다리게 하지 않는다.
- 턴음은 match/game/turn, 결과음은 match/game으로 서비스에서 중복을 막는다. 같은 게임의 재접속·resync·Scene 재진입으로 같은 턴/결과를 다시 재생하지 않는다. 결과음은 서버 WinnerPlayerId에 따른다.
- ViewInvalidated/Scene 종료 때 전투 voice와 예약음을 중지한다. 턴/결과의 중복 방지 키는 유지한다.
- 누락된 라이브러리/클립은 진단을 남기고 재생만 생략한다. 동일 누락 cue 경고는 한 번만 출력하며 규칙·명령·화면 갱신은 계속된다.

라이브러리의 gain은 UI 0.28(버튼 0.32), 전투 0.65이며 공통 출력 headroom 0.7을 둔다. pitch는 합성음 1.0이다. Inspector에서 라이브러리의 클립/레벨을 수정할 수 있다.

## 변경 파일

- 신규 `Assets/Scripts/Audio/{ClientAudio,SoundLibrary,BattleSoundEvents,UiButtonSound}.cs` 및 meta.
- 기존 `BattlePresenter`, `BattleEffectsView`, `BattleInteractionController`, `MulliganPanelView`, `CardGalleryView`: 표시/입력 시점에 cue 호출만 추가.
- `LobbyScenePresenter`: 별도 두 슬라이더 바인딩.
- `BattleScreen`, `CardAction`, `LobbyScreen`, `DeckEditorScreen`, `DeckTile` 프리팹: 버튼 입력음 컴포넌트. 로비 설정에 슬라이더 2개와 레이블을 저장했다.
- `Assets/Resources/Audio/ClientSoundLibrary.asset`, 합성 WAV/manifest/LICENSE와 참조한 Kenney OGG 3개의 import meta.
- `Tools/GenerateBattleSounds.py`, `Assets/Tests/PlayMode/BattleAudioPlayModeTests.cs`.
- `Artifacts/BattleAudio/`의 미리듣기·UI 캡처·검증 기록과 이 문서.

BattleScene, 전장 모델/카메라, 게임 데이터, 서버 소스와 기존 테스트 내용은 이번 오디오 작업에서 수정하지 않았다. 앞선 작업과 사용자 변경으로 이미 수정되어 있던 파일은 보존했다. 제작용 에디터 스크립트는 검증 복사본에서만 사용했으며 일반 실행에 수동 연결이나 도구 실행은 필요 없다.

## 검증과 확인할 부분

원본 Unity가 열려 있어 `/tmp/ProjectC-BattlefieldValidation` 복사본에서 Unity 6000.0.81f1로 실행했다. 최종 테스트 결과, 변경 전후 비교와 실패 목록은 `Artifacts/BattleAudio/Validation/summary.json`, baseline/final XML에 기록한다.

**최종 결과:** 새 오디오 테스트 **9/9 성공**. 변경 전 기존 **71개 중 61개 성공, 10개 실패**, 적용 후 전체 **80개 중 70개 성공, 10개 실패**다. 기존 71개 각각의 성공/실패 결과가 동일하며 새 회귀는 발견하지 않았다.

새 오디오 검증은 다음을 확인한다.

- 14종 클립의 연결/길이, 고정 voice 수, 단일 ClientAudio, 즉시 중복 억제.
- UI/전투/전체 음량과 PlayerPrefs, 누락 클립의 경고 1회·안전한 생략.
- 공격/직접 피해/주문 발동의 중복 억제, 턴과 결과의 중복 방지.
- 실제 BattleScene의 클릭 선택/해제, 드로우 표시, 거부된 명령·snapshot 복원에서 불필요한 전투음 없음.
- 기존 실제 서버 시나리오를 재사용해 소환/공격/피해/파괴/드로우/효과/현현/승리/재대전/재접속의 cue 확인.
- 별도 임시 테스트 덱으로 실제 주문 드래그와 드로우, 실제 패배 및 결과 snapshot 재요청에서 패배음 1회 확인. 테스트 데이터는 임시 폴더만 사용하며 서버/운영 카드 데이터는 바꾸지 않는다.
- 로비 설정 슬라이더와 listener 1개, Unity DSP의 무음이 아닌 실제 출력.

Unity mixer 출력 검사에서는 DSP 시간이 진행했고 48,000 Hz에서 단일 소환 cue의 샘플 피크 약 0.14를 검출했다. `DspOutput.json`에 기록했다. 합성 WAV 11종은 44,100 Hz/mono/PCM16, endpoint=0, peak=0.55와 바이트 재현성을 별도로 검사했다.

미리듣기: `Artifacts/BattleAudio/SoundPreview.wav` (약 8초). 순서는 `SoundPreviewTimeline.json`에 기록했다. 기본 gain/pitch/bus 레벨을 적용한 클립별 샘플이며 실제 경기 전체를 녹음한 파일은 아니다. `Settings-1920.png`는 실행 중 로비 Canvas를 임시 캡처 카메라로 렌더링한 UI 확인용 이미지다.

자동 검증은 실제 서버·uGUI·Unity 오디오 출력을 사용하지만 사람이 OS 마우스로 플레이하며 헤드폰/스피커로 청감 평가한 것과 같지는 않다. 실제 장치에서 북의 저역, 종소리의 고역, 연속 효과의 상대 음량은 미리듣기와 대전을 통해 최종 조정할 필요가 있다. 기존 회귀 테스트 실패는 결과 기록에 남겼고 오디오 작업 범위에서 기대값을 바꾸지 않았다.
