using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LowPolyFarm.BlockTheWell
{
    /// <summary>
    /// 게임 전체 흐름 관리자.
    /// 상태: Title(시작 화면) → Playing → Clear(스테이지 클리어) → … → Won(4스테이지 모두 클리어).
    /// 스테이지는 씬에 미리 배치된 StageRoot 4개 중 현재 것만 켜는 방식으로 전환하고,
    /// 스테이지별 클리어 시간을 화면 오른쪽 UI에 기록한다.
    /// </summary>
    public sealed class WellGame : MonoBehaviour
    {
        public enum Mode { Title, Playing, Clear, Won, Failed }   // Failed: 낙하 시 안전장치 (격자 이동이라 실제로는 거의 발생하지 않음)
        public WellStage[] stages;
        public PlayerMovement player;
        public OrbitFollowCamera orbit;
        public Text heading, status, instruction, buttonText;
        public Text stageTimes;
        public GameObject debugPanel;
        public Text debugStatus;
        public Button debugPrevious, debugClear, debugNext;
        float[] elapsed;   // 스테이지별 경과 시간(초). 오른쪽 시간 UI에 mm:ss로 표시
        public Button actionButton, restartButton, quitButton;
        public Mode CurrentMode { get; private set; }
        public int StageIndex { get; private set; }
        public WellStage CurrentStage => stages[StageIndex];
        CharacterController controller;
        Animator animator;
        float initialYaw, initialPitch, initialDistance;   // 휠 클릭 시 복귀할 기본 시점
        void Awake()
        {
            // 씬에 설정된 카메라 각도/거리를 '기본 시점'으로 기억해 둔다.
            elapsed=new float[stages.Length];
            initialYaw=orbit.yaw;initialPitch=orbit.pitch;initialDistance=orbit.distance;
        }
        void Start()
        {
            Application.runInBackground=true;
            // 퍼즐 전용 격자 밀기 컴포넌트를 이 씬의 Player에만 붙인다(공용 PlayerMovement는 수정하지 않음).
            if(player.GetComponent<WellGridPusher>()==null)player.gameObject.AddComponent<WellGridPusher>();
            controller=player.GetComponent<CharacterController>();animator=player.GetComponent<Animator>();
            actionButton.onClick.AddListener(Advance);restartButton.onClick.AddListener(Restart);quitButton.onClick.AddListener(Quit);
            if(debugPrevious!=null)debugPrevious.onClick.AddListener(()=>DebugChangeStage(-1));
            if(debugClear!=null)debugClear.onClick.AddListener(DebugClearStage);
            if(debugNext!=null)debugNext.onClick.AddListener(()=>DebugChangeStage(1));
            if(debugPanel!=null)debugPanel.SetActive(false);
            ShowStage(0);SetMode(Mode.Title);
        }
        /// <summary>결과 화면 버튼(START / NEXT STAGE / PLAY AGAIN / RETRY) 처리.</summary>
        public void Advance()
        {
            if(CurrentMode==Mode.Title || CurrentMode==Mode.Won){System.Array.Clear(elapsed,0,elapsed.Length);ShowStage(0);SetMode(Mode.Playing);}
            else if(CurrentMode==Mode.Clear){ShowStage(StageIndex+1);SetMode(Mode.Playing);}
            else if(CurrentMode==Mode.Failed)Restart();
        }
        /// <summary>R 키 또는 RESTART 버튼: 현재 스테이지만 처음 상태로 되돌리고 시간도 0으로.</summary>
        public void Restart(){elapsed[StageIndex]=0;ShowStage(StageIndex);SetMode(Mode.Playing);}
        /// <summary>index번 스테이지만 활성화하고 바위·우물·플레이어 위치·카메라를 초기화한다.</summary>
        void ShowStage(int index)
        {
            // Restore inactive stages only after activation, so every rock has run Awake.
            for(int i=0;i<stages.Length;i++)stages[i].gameObject.SetActive(i==index);
            StageIndex=index;CurrentStage.ResetStage();
            // CharacterController가 켜져 있으면 위치를 직접 바꿔도 덮어써지므로 잠시 끄고 이동한다.
            controller.enabled=false;player.transform.SetPositionAndRotation(CurrentStage.spawn.position,CurrentStage.spawn.rotation);controller.enabled=true;
            orbit.target=player.transform;
            orbit.RestoreView(initialYaw,initialPitch,initialDistance);
            if(animator!=null)animator.Play(player.idleState,0,0);
        }
        void Update()
        {
            // 마우스 휠 클릭: 기본 시점으로 복귀
            if(orbit.enabled && Mouse.current!=null && Mouse.current.middleButton.wasPressedThisFrame)
                orbit.RestoreView(initialYaw,initialPitch,initialDistance);
            var key=Keyboard.current;
            // Numpad 0: 디버그 메뉴 토글 (Numpad 1 이전 / 2 클리어 / 3 다음 스테이지)
            if(key!=null && key.numpad0Key.wasPressedThisFrame && debugPanel!=null){
                debugPanel.SetActive(!debugPanel.activeSelf);
                RefreshDebugMenu();
            }
            if(key!=null && key.rKey.wasPressedThisFrame){Restart();return;}
            if(key!=null){
                if(key.numpad1Key.wasPressedThisFrame){DebugChangeStage(-1);return;}
                if(key.numpad2Key.wasPressedThisFrame){DebugClearStage();return;}
                if(key.numpad3Key.wasPressedThisFrame){DebugChangeStage(1);return;}
            }
            if(CurrentMode!=Mode.Playing)return;
            // 플레이 중에만 현재 스테이지 시간이 흐른다.
            elapsed[StageIndex]+=Time.deltaTime;
            RefreshTimes();
            status.text=$"STAGE {StageIndex+1}/{stages.Length}   |   WELLS BLOCKED {CurrentStage.BlockedCount}/{CurrentStage.wells.Length}";
            if(player.transform.position.y < -3){SetMode(Mode.Failed);return;}
            foreach(var rock in CurrentStage.rocks)if(rock.transform.position.y < -3){SetMode(Mode.Failed);return;}
            // 승리 조건: 현재 스테이지의 모든 우물이 바위로 막히면 클리어. 마지막 스테이지면 게임 클리어(Won).
            if(CurrentStage.BlockedCount==CurrentStage.wells.Length)SetMode(StageIndex==stages.Length-1?Mode.Won:Mode.Clear);
        }
        /// <summary>상태 전환. 플레이 중이 아닐 때는 이동·카메라 입력을 막고 결과 UI를 갱신한다.</summary>
        void SetMode(Mode mode)
        {
            RefreshTimes();
            CurrentMode=mode;player.enabled=mode==Mode.Playing;orbit.enabled=mode==Mode.Playing;
            if(animator!=null){animator.Play(player.idleState);animator.speed=1;}
            heading.text=mode switch {Mode.Title=>"BLOCK THE WELL",Mode.Clear=>"WELL BLOCKED!",Mode.Won=>"ALL WELLS BLOCKED!",Mode.Failed=>"OUT OF BOUNDS",_=>CurrentStage.title};
            instruction.text=mode==Mode.Failed?"A rock or the farmer fell. Press R to retry.":mode==Mode.Won?"All 4 stages complete. Thank you for playing!":"";
            actionButton.gameObject.SetActive(mode!=Mode.Playing);
            buttonText.text=mode switch{Mode.Title=>"START",Mode.Clear=>"NEXT STAGE",Mode.Won=>"PLAY AGAIN",_=>"RETRY"};
            status.text=$"STAGE {StageIndex+1}/{stages.Length}   |   WELLS BLOCKED {CurrentStage.BlockedCount}/{CurrentStage.wells.Length}";
            RefreshDebugMenu();
        }
        void DebugChangeStage(int direction)
        {
            int next=StageIndex+direction;
            if(next<0 || next>=stages.Length)return;
            elapsed[next]=0;
            ShowStage(next);
            SetMode(Mode.Playing);
        }
        void DebugClearStage()
        {
            if(CurrentMode!=Mode.Playing)return;
            // Debug completion uses the normal result screen without moving puzzle pieces.
            SetMode(StageIndex==stages.Length-1?Mode.Won:Mode.Clear);
        }
        void RefreshDebugMenu()
        {
            if(debugStatus!=null)debugStatus.text=$"Current Stage {StageIndex+1} / {stages.Length}";
            if(debugPrevious!=null)debugPrevious.interactable=StageIndex>0;
            if(debugNext!=null)debugNext.interactable=StageIndex<stages.Length-1;
            if(debugClear!=null)debugClear.interactable=CurrentMode==Mode.Playing;
        }
        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
        /// <summary>오른쪽 시간 UI 갱신: "Stage 1  00:42" 형식으로 4줄.</summary>
        void RefreshTimes()
        {
            if(stageTimes==null)return;
            var lines=new string[elapsed.Length];
            for(int i=0;i<elapsed.Length;i++){
                int seconds=Mathf.FloorToInt(elapsed[i]);
                lines[i]=$"Stage {i+1}  {seconds/60:00}:{seconds%60:00}";
            }
            stageTimes.text=string.Join("\n",lines);
        }
    }
}

