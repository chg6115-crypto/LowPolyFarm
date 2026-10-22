# Block the Well — Development Log

## 2026-10-22 / 요청과 평가 기준
- 사용자 요청: 기존 LowPolyFarm 애셋을 사용해 자유 이동·물리 밀기 방식의 소코반 퍼즐을 3스테이지까지 제작. Stage 1부터 작업, 기존 씬과 원본 애셋 보존, New Input System 사용. 선택적 연출 생략.
- 참고: `LowPolyFarm_BlockTheWell_Codex_GDD.md`, `2차_Unity_평가지.hwp`.
- HWP 본문을 OLE 스트림과 HWP 레코드로 추출해 확인: 씬 20점, 게임플레이 30점, UI 10점, AI 활용 20점, 빌드·영상 10점, 포트폴리오 10점. 요구 제출물은 PC 빌드, 1~2분 영상, PDF, GitHub 링크, 대표 이미지.

## 조사와 설계
- Unity 6000.6.0f1, URP 17.6.0, Input System 1.20.0. 프로젝트 입력 설정은 New Input System.
- 사용 원본: `03_Prefabs/Player.prefab`, `04_Models/Soil/Prefabs/Soil_Base.prefab`, `04_Models/Rock/Prefabs/Rock_3k.prefab`, `04_Models/StoneWell/Prefabs/StoneWell_Low5k.prefab`.
- PlayerMovement의 CharacterController 이동과 Pushable 접촉 밀기를 재사용. 원본 코드 수정 없음. 새로운 씬의 Player 인스턴스에서만 네트워크/디버그 컴포넌트 비활성화.
- 새 씬: `Assets/01_Scene/BlockTheWell.unity`. StageRoot 3개를 씬에 미리 배치. 간단한 상태 전환만으로 R 재시작 및 다음 단계 구현이 가능해 선택.
- 타일은 맵 구성에만 사용하며 이동 좌표를 격자에 강제하지 않음.
- 우물 입구를 바닥 높이로 묻어 바위가 실제로 입구를 덮을 수 있게 함. 원본 모델/머티리얼 변경 없음. 장식 타일과 연속 바닥 충돌체를 분리해 이음새 걸림 방지.
- UI는 uGUI와 InputSystemUIInputModule. 내장 폰트에 있는 영문만 사용해 빌드에서 한글 폰트 누락 방지.

## 핵심 코드 설명
- `WellRock`: 바위 식별, Rigidbody 초기 상태 복원, 성공 후 고정.
- `WellBlockZone`: Trigger 안의 바위만 후보로 수집. 중심 XZ 거리, 높이, 속도를 검사하며 같은 바위가 0.5초 유지된 경우만 고정·성공. 캐릭터나 스치는 바위는 성공하지 않음.
- `WellStage`: 시작 지점, 바위와 우물 참조, 봉쇄 수 집계.
- `WellGame`: 시작 → 플레이 → 스테이지 성공 → 최종 성공/실패 상태. New Input System R 키와 UI 버튼으로 복구.
- `WellProjectBuilder`: 에디터에서만 씬·물리 설정·UI 생성 및 Windows 빌드. 실행 시 맵 생성하지 않음. 기존 Main/Single은 보존.

## 실제 오류와 해결
1. HWP 읽기 라이브러리 `olefile` 없음: 외부 패키지 설치 대신 OLE FAT/MiniFAT 및 압축 Section 스트림을 직접 읽어 평가 본문 확인.
2. HWP 출력 CP949 인코딩 오류: 추출 내용을 UTF-8 파일로 저장 후 PowerShell로 읽음.
3. 기존 우물의 높은 벽은 평면 물리 밀기로 입구를 덮을 수 없음: 모델 Renderer bounds를 측정해 윗면이 지면 위 0.025m가 되도록 묻고, 전체 입구를 가리는 고체 콜라이더를 제외함.
4. New Input System 전용 UI: 구 StandaloneInputModule 대신 InputSystemUIInputModule 적용.
5. 실제 Unity 씬 생성 중 UI 루트 `MissingReferenceException`: 일반 Transform에 Canvas를 추가하면 RectTransform으로 교체되어 이전 Transform 참조가 파괴됨. UI GameObject를 처음부터 RectTransform으로 생성하도록 수정. 실패한 새 임시 씬만 정리하고 재생성하도록 복구 처리 추가.
6. 자동 검증 재실행 시 Phase 1 시간 초과: 프로젝트에서 Domain Reload/Scene Reload가 꺼져 있어 검증용 정적 상태와 게임 시작 상태가 재진입 때 달라질 수 있음. Play 진입 이벤트에서 검증 상태를 초기화하고 시작 시 Advance 대신 명시적인 Restart로 초기화하도록 수정. 상태 확인 파일도 추가.
7. 실제 Windows 빌드 실패: 기존 VARCO의 `05.Editor` 폴더는 Unity의 특수 `Editor` 폴더가 아니어서 EditorToolbar/Overlay 코드가 플레이어 빌드에 포함됨. `Lab_Varco3D`도 UnityEditor 참조를 포함함. 해당 두 폴더에 Editor 전용 asmdef만 추가해 분리. 기존 원본 C#·모델·씬은 수정하지 않음.
8. 대표 이미지 캡처 직후 스테이지 전환으로 다음 화면이 찍히는 문제: 스크린샷은 프레임 끝에서 저장되므로 0.5초 대기 후 다음 단계로 이동하도록 검증 도구 수정.

## 검증 범위와 제출 준비
- 자동 Play 검증은 Stage 1에 실제 KeyboardState W 입력을 공급. 이후 단계는 Rigidbody를 합법 경로로 이동시켜 물리 충돌과 목표·전환을 검사. 수동 전 스테이지 플레이나 PC 실행 테스트와 동일하지 않음.
- 아래 실행 기록에 PASS가 없는 항목은 아직 검증 완료로 간주하지 않음.
- 플레이 영상/PDF/GitHub 게시 여부는 별도 확인 필요. 사용자 이름과 저장소 링크는 임의 작성하지 않음.

- Unity 에디터 씬 생성 완료: 2026-10-22 오전 9:41:47. 에셋 실제 Renderer bounds를 측정해 배치 크기를 맞춤. 기존 씬은 덮어쓰지 않음.

- 2026-10-22 오전 9:41:55 [자동 Play 검증] PASS Stage 1: New Input System W moved player, pushed rock physically, and blocked well.

- 2026-10-22 오전 9:41:56 [자동 Play 검증] PASS restart: rock unlocked and goals reset.

- 2026-10-22 오전 9:41:56 [자동 Play 검증] PASS player alone does not block well.

- 2026-10-22 오전 9:44:07 [자동 Play 검증] FAIL: Timed out in verification phase 1

- 2026-10-22 오전 9:45:34 [자동 Play 검증] PASS Stage 1: New Input System W moved player, pushed rock physically, and blocked well.

- 2026-10-22 오전 9:45:34 [자동 Play 검증] PASS restart: rock unlocked and goals reset.

- 2026-10-22 오전 9:45:35 [자동 Play 검증] PASS player alone does not block well.

- 2026-10-22 오전 9:46:04 [자동 Play 검증] PASS Stage 3 requires both wells.

- 2026-10-22 오전 9:46:11 [자동 Play 검증] PASS all three stages and final victory.

## 2026-10-22 사용자 지침 변경
- 게임 검증을 중단하고 시스템 구현에만 집중하도록 명시적으로 요청함.
- 자동 검증 도구와 자동 생성/빌드 요청 감시를 비활성화하고 대기 요청을 제거함. 이후 검증·테스트는 별도 요청 시에만 수행.
- Windows 빌드는 완료되지 않음. 마지막 시도는 스크립트 컴파일 중 실행되어 실패했으며, 사용자 지시에 따라 재시도하지 않음.


## 2026-10-22 — 사용자 이미지 기반 Stage 1~4 에셋 재배치
- 요청 범위: 성공/실패 기능 변경 없이 에셋 배치만 수정. 게임 실행·검증·테스트·빌드는 수행하지 않음.
- Assets/01_Scene/BlockTheWell.unity의 임시 Stage 1~3을 이미지 텍스트 격자 Stage 1~4로 교체하고 Unity 에디터에서 저장.
- Soil 실제 폭 1 기준 격자 간격 1. Soil/Rock/StoneWell/Player 인스턴스 Scale (1,1,1), 프리팹 고유 회전 유지. 바닥 표면 높이에 배치.
- 각 Stage 아래 Tiles/Walls/Rocks/Wells/Player Spawn을 Hierarchy에 미리 배치. Stage 1만 활성화, Stage 2~4 비활성화. WellGame의 stages 참조를 새 4개 루트로 연결.
- 바위/우물 수: Stage 1=4/4, Stage 2=3/3, Stage 3=4/4, Stage 4=5/5. Stage 4의 *는 같은 칸에 우물과 바위를 배치.
- 벽은 Rock_3k 원본 프리팹 인스턴스와 별도 WallRock_Black 머티리얼 사용. 고정 충돌체 추가. 원본 에셋, 다른 씬, 카메라/조명/UI와 런타임 성공실패 코드는 변경하지 않음.
- 기존 바위 물리 컴포넌트 구성은 유지. 격자 이동 시스템 구현은 이번 배치 작업에 포함하지 않음.
- 변경 전 씬은 Temp/BlockWell/BlockTheWell_before_image_layout_날짜시간.unity에 백업.

## 2026-10-22 — 바위 Cube 콜라이더
- 원본/멀티플레이 영향 방지를 위해 Assets/03_Prefabs/BlockTheWell/Rock_Grid.prefab 변형 프리팹 생성. Cube Collider 자식에 1×1×1 BoxCollider, 바닥에서 높이 0.5 중심으로 설정. 모델 크기 및 원본 회전 유지.
- BlockTheWell Stage 1~4 이동 바위와 검정 벽 바위에 적용하고 기존 중복 콜라이더 제거, 씬 저장. 게임 실행 검증 없음.

## 2026-10-22 — 회색 바위 크기 및 캐릭터 시작점
- 사용자 요청으로 Stage 1~4 회색 바위 모델 인스턴스 Scale을 (0.9,0.9,0.9)로 설정. 자식 Cube Collider도 함께 0.9 크기로 적용.
- 검정 바위는 Scale 1 유지, BoxCollider 1×1×1, 바닥 위 중심 높이 0.5 설정.
- 이미지 격자 @ 위치 (0부터 열/행): Stage 1 (4,4), Stage 2 (1,1), Stage 3 (2,3), Stage 4 (2,2)에 Player Spawn 배치. Soil 표면과 CharacterController 바닥 높이를 반영.
- 기존 WellGame.ShowStage의 시작/재시작/스테이지 전환 시 spawn 이동 연결을 유지. 에디터의 공용 Player도 Stage 1 시작점으로 배치하고 씬 저장.
- 원본 프리팹/다른 씬 변경 및 게임 실행 검증 없음.

## 2026-10-22 — 우물 높이와 밀기 코드 조사
- Stage 1~4 우물 루트 local Y=-0.5, StoneWell 자식 local position=0으로 저장. 기존 BlockZone은 월드 높이 유지.
- PlayerMovement.OnControllerColliderHit는 WASD 이동 중 Pushable Rigidbody와 접촉하면 별도 밀기 버튼 없이 속도를 계속 부여한다. 아직 격자 한 칸 이동이 아닌 기존 물리 밀기 방식이다.
- WellBlockZone은 우물 중앙에 바위가 0.5초 안정되면 WellRock.Lock으로 isKinematic=true 처리하여 이후 밀기를 차단한다.
- 게임 실행 검증 없이 코드/에디터 설정만 확인. 밀기 증상의 정확한 의미는 사용자 확인 필요, 관련 로직은 변경하지 않음.

## 2026-10-22 — 플레이 상태 원인 조사 및 격자 밀기 전환
- 사용자 요청에 따라 진행 중 Play의 상태를 읽음: Stage 1 바위 4개 중 3개 Locked=true/isKinematic=true. 기존 우물 성공 고정이 재밀기를 막고 있었음. 다른 바위는 물리 이동으로 격자 중심 이탈.
- Play 종료 후 편집. 우물 루트 Y=-0.3 저장, 감지 영역 높이 유지.
- WellRock을 kinematic 격자 이동으로 변경: 한 번에 상하좌우 1칸, 목적지 바닥/벽/다른 바위 검사, 이동 중 목적지 예약, 초기 위치로 재시작. 원본 PlayerMovement/멀티플레이 코드는 변경하지 않음.
- 전용 WellGridPusher를 WellGame 시작 시 Player에 연결. WASD로 바위 면 중앙 쪽에서 접근하여 충돌하면 한 칸 밀기. 캐릭터 자유 이동 유지.
- WellBlockZone은 바위 고정을 제거하고 현재 점유를 계속 갱신. 바위를 우물에서 빼면 Blocked 해제. 전체 우물 클리어 흐름은 유지.
- 변경 이후 자동 플레이, 입력 테스트, 빌드 검증은 진행하지 않음.

## 2026-10-22 — 걷기/달리기 속도 및 밀기 애니메이션
- BlockTheWell Player 걷기 2.2→3.3 (1.5배), runMultiplier 1.5 (Shift 속도 4.95)로 씬 인스턴스에 저장.
- 회색 바위 16개 moveSpeed 3→1로 변경, 한 칸 이동 약 1초. 스크립트 기본값도 1로 설정.
- PlayerMovement에 외부 밀기 애니메이션 요청 메서드 추가. 기존 행동은 유지하며 BlockTheWell 전용 WellGridPusher만 호출. 충돌 밀기 시작 및 가까이서 바위가 이동하는 동안 기존 Push 상태/애니메이션 우선순위 사용.
- 기존 원본 Player 프리팹 속도 및 멀티플레이 씬 설정은 변경하지 않음. 게임 실행/자동 입력 검증 없음.

## 2026-10-22 — Walking FBX 교체와 달리기 모션 제거
- 걷기 3.3→2.64 (0.8배). 요청은 달리기 모션 제거로 해석하여 Shift 속도 배율 1.5는 유지하고 runState를 Walk로 연결, 시작 가속 애니메이션 배율 1 설정.
- 사용자 추가 Assets/07_Animations/Walking.fbx를 Humanoid로 임포트, Loop Time/Loop Pose와 루트 위치/회전 고정 설정.
- 원본 Character_Animator를 보존하고 BlockTheWell 전용 컨트롤러를 복사하여 Walk만 Walking.fbx 클립으로 교체, Jogging 상태 제거. Push 상태 및 클립 유지.
- BlockTheWell Player 인스턴스에 전용 컨트롤러 연결. 다른 씬/멀티플레이 설정 변경 없음. 플레이 검증 없음.

## 2026-10-22 — Player Humanoid 전환
- 진단 당시 실제 Player Avatar isHuman=false(Generic), Walking clip humanMotion=true로 불일치 확인.
- 최초 Generic 변환 클립을 만들었으나 사용자가 Humanoid 전환 지시하여 최종 연결에서 제외.
- 원본/멀티 영향 방지를 위해 Character_Rigged.fbx를 BlockTheWell/Player_Humanoid.fbx로 복사, Humanoid Avatar 생성. isValid=true/isHuman=true 임포트 결과 확인.
- 싱글 씬 Player Animator에 해당 Avatar 연결, Walk에는 Walking.fbx Humanoid 클립 직접 연결. Push 클립/상태 유지. 플레이 실행 검증 없음.

## 2026-10-22 — Idle 침하 / Push 방향 문제
- 진단: Player와 Walk는 Humanoid지만 Idle/Push/Crawl/Hi/Scratching은 Generic .anim 상태. 기존 FBX는 루트 회전/Y/XZ Bake Into Pose도 꺼져 있었음.
- 이전 Humanoid 전환 시 기존 클립까지 함께 변환하지 않아 리그 혼용을 남긴 것이 구성 오류였음.
- 싱글 전용 FBX 복사본 5개를 만들어 같은 원본 동작을 Player_Humanoid Avatar에 맞춰 Humanoid로 임포트하고 상태에 연결. 원본 Generic 클립과 멀티 설정 보존.
- 루트 회전/Y/XZ Bake Into Pose, 방향 Body Orientation 기준, 높이 Feet 기준 및 offset 0 설정. Walking도 동일 높이 기준 적용. Idle/Push/Crawl 루프 활성화.
- 씬/애니메이션 에셋 저장. 게임 플레이나 자동 입력 검증은 수행하지 않아 실제 시각적 결과는 미확인.

## 2026-10-22 — 휠 감도와 시점 복귀
- BlockTheWell 씬 카메라 zoomSpeed 0.3→1.5 (5배). 회전 감도 및 줌 범위 유지.
- WellGame Awake에서 초기 yaw/pitch/distance 저장, 휠클릭 시 초기 각도와 거리로 복귀. 현재 씬 기준 yaw 0/pitch 48/distance 13. 플레이어 현재 위치를 중심으로 복귀.
- OrbitFollowCamera에 RestoreView 메서드 추가하여 위치 보간 속도도 초기화. 이 기능 호출 및 입력 처리는 싱글 WellGame에서만 사용. 스테이지 시작도 같은 초기 시점 사용.
- 조작 안내에 Middle mouse reset view 추가. 씬 저장, 게임 실행 검증 없음.

## 2026-10-22 — 기본 카메라와 좌우 텍스트
- 사용자 Transform 위치 (0,8.38,-5.85), Quaternion (0.520807,0,0,0.8536744), Scale 1 적용. pitch 약 62.7728, distance 약 9.193465. 지정 Transform이 추적 시 즉시 틀어지지 않도록 pivotOffset 계산하고 시작 yaw 자동 재계산 비활성화. 시작/재시작/휠클릭 복귀 모두 이 설정 사용.
- 휠 감도 1.5→7.5 (추가 5배).
- 우측 Stage1~4 mm:ss 초 텍스트 생성, 배경 패널 없음. 현재 라운드는 진행 시간 갱신, 클리어 후 유지. R 재시작은 현재 라운드 시간 초기화, 새 게임은 전체 초기화. 시간 단위 분:초. 한글 표기를 위해 Windows Malgun Gothic 동적 글꼴 사용.
- 조작 안내를 왼쪽 중앙 세로 텍스트로 이동. 스테이지 수 표시 3→4 수정. 플레이 검증 없음.

## 2026-10-22 — 스테이지 설명 제거 및 시간 텍스트 글꼴 수정
- Stage Times 오브젝트 활성화, 텍스트 내용, WellGame.stageTimes 참조는 정상 저장된 상태였음. 원인은 이전 StageText.fontsettings의 font.material이 null이어서 렌더링되지 않는 설정 오류.
- 실제 Malgun Gothic TTF를 StageTextKorean.ttf로 임포트하고 시간 텍스트에 연결. 임포트된 Font material 존재 확인, 분:초 4행 저장. 자동 게임 검증 없음.
- WellGame은 일반 스테이지에서 instruction을 빈 문자열로 표시하고 각 Stage hint 제거. 좌측 조작 안내는 유지. 실패/최종 클리어 메시지는 유지.

## 2026-10-22 — 스테이지 디버그 메뉴
- New Input System numpad0Key로 좌상단 디버그 패널 열기/닫기. UI/Debug Menu를 씬 Hierarchy에 미리 배치하여 기본 비활성화 상태로 저장.
- 이전/다음 스테이지 버튼은 목적지의 퍼즐과 시간을 초기화하고 플레이 상태로 이동. Stage 1 이전과 Stage 4 다음 버튼은 비활성화.
- 스테이지 클리어 버튼은 플레이 중에만 활성화, 퍼즐 배치를 변경하지 않고 기존 Clear/Won 결과 화면 호출. 마지막 Stage 4에서는 최종 클리어 표시.
- 창에 기능 설명과 현재 스테이지 표시. 메뉴 개방 중 게임/시간은 계속 진행. 자동 Play/빌드 검증 없음.
- 에디터 배치 명령의 Image 이름이 Unity 도구 네임스페이스와 충돌하여 첫 시도 컴파일 실패. UnityEngine.UI.Image로 명시하여 재실행 후 메뉴 생성/참조 저장 완료.

## 2026-10-22 — Quit / 숫자패드 단축키 / 영어 UI
- Quit 버튼 기존 연결 유지, 에디터에서는 Play 종료, 실행 빌드에서는 Application.Quit 호출하도록 분기.
- Numpad 1 이전, 2 클리어, 3 다음 스테이지. Numpad 0 메뉴 토글 유지. 메뉴 닫힌 상태에서도 단축키 동작, 단계 경계/플레이 상태 제한은 버튼과 동일.
- 디버그 제목/상태/버튼/설명 및 시간 표시를 영어로 변경. 시간은 Stage 1 00:00 형식. 좌측 안내에 Numpad 0 추가.
- 현재 싱글 UI의 비활성 텍스트까지 한글 잔여 확인 후 저장. 게임 실행/종료 테스트는 수행하지 않음.
