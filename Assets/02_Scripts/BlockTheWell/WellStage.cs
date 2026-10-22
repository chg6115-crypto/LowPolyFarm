using UnityEngine;
namespace LowPolyFarm.BlockTheWell
{
    /// <summary>
    /// 스테이지 1개를 대표하는 루트 오브젝트.
    /// 하위에 Tiles(바닥) / Walls(검정 고정 바위) / Rocks(밀 수 있는 바위) / Wells(우물) / Spawn을 미리 배치해 두고,
    /// WellGame은 이 컴포넌트를 통해 "몇 개의 우물이 막혔는지"와 "스테이지 초기화"만 요청한다.
    /// </summary>
    public sealed class WellStage : MonoBehaviour
    {
        public string title;                 // 스테이지 제목 (플레이 중 상단 표시)
        [TextArea] public string hint;       // 스테이지 힌트 (현재 화면에는 표시하지 않음)
        public Transform spawn;              // 플레이어 시작 위치
        public WellRock[] rocks;             // 이 스테이지에서 밀 수 있는 바위들
        public WellBlockZone[] wells;        // 이 스테이지의 목표 우물들

        /// <summary>현재 바위로 막혀 있는 우물 수. 우물 수와 같아지면 스테이지 클리어.</summary>
        public int BlockedCount
        {
            get
            {
                int n = 0;
                foreach (var well in wells)
                    if (well.Blocked) n++;
                return n;
            }
        }

        /// <summary>R 재시작·스테이지 전환 시 호출. 우물 판정과 바위 위치를 처음 상태로 되돌린다.</summary>
        public void ResetStage()
        {
            foreach (var well in wells) well.ResetZone();
            foreach (var rock in rocks) rock.ResetRock();
            // 위치를 직접 바꾼 뒤 같은 프레임의 물리 판정이 옛 위치를 쓰지 않도록 즉시 동기화한다.
            Physics.SyncTransforms();
        }
    }
}
