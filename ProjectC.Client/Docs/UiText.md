# UI 텍스트

LobbyScene, DeckEditorScene, BattleScene의 공용 UI 프리팹은 TextMeshPro를 사용한다. SampleScene에는 UI 텍스트가 없다. 씬의 프리팹 인스턴스 참조와 기존 오브젝트 이름/컴포넌트 fileID는 유지했다.

- 표시 텍스트: `TextMeshProUGUI`, 스크립트 참조: `TMP_Text`
- 입력창: `TMP_InputField` (텍스트/플레이스홀더, 입력 제한, 이벤트, viewport 연결 유지)
- 덱 선택 목록: `TMP_Dropdown` (caption/item/template 연결 유지)
- 모든 UI 폰트 및 TMP 기본 폰트: `Assets/Fonts/Harmonic SDF.asset`
- 런타임 멀리건 UI와 명시적 에디터 UI 제작 명령도 동일한 TMP 폰트를 사용한다.

기존 Text 151개, InputField 6개, Dropdown 1개를 변환했다. TMP 필수 셰이더·설정·줄바꿈 리소스는 Unity UI 패키지에서 제공하는 기본 리소스를 사용한다. 게임 규칙과 네트워크 프로토콜은 이 전환으로 변경하지 않았다.

`Project C/Validate TMP scene fonts`로 저장된 프리팹과 모든 씬의 레거시 컴포넌트 잔존 여부, Harmonic SDF 지정, 입력창/드롭다운 연결을 확인할 수 있다. 원본 프로젝트에서 Unity 6000.0.81f1 컴파일과 해당 확인 명령을 실행했다. 로그: `build/TMP-original-validate.log`. PlayMode 테스트는 이번 작업에서 실행하지 않았다.

기존 실행 경로 `ProjectC.Client/Build/Multiplayer/ProjectC.Client.exe`의 Windows x64 Development 빌드도 갱신했다. 빌드 로그: `build/TMP-original-build.log`.
