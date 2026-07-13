using UnityEngine;
using ACLS.Logging;

namespace ACLS.Sim
{
    // 把 EraTrendService 装到 World 上的 MonoBehaviour 包装。
    // 订阅 World.OnDayTick,每日调用 AdvanceTo。
    //
    // 启动时机: GameBootstrap 构建 world 之后,状态机启动之前。
    public sealed class EraTrendInjector : MonoBehaviour
    {
        private World world;
        private EraTrendService service;

        public void Bind(World world, string presetId = null)
        {
            this.world = world;
            var anchors = EraTrendAnchors.Get(presetId);
            this.service = new EraTrendService(world, anchors);
            if (world != null)
            {
                if (world.EraTrend != null) world.EraTrend.ActiveAnchors = anchors;
                world.OnDayTick += HandleDayTick;
                // 启动时跑一次:根据剧本起始日期推断初始阶段（未初始化日期时不会触发任何锚点）
                if (world.Date.Year > 0) service.AdvanceTo(world.Date);
            }
            Log.Info(Log.Channels.System,
                "EraTrendInjector 已挂载: 剧本={0} 硬锚点={1} 初始阶段={2}",
                string.IsNullOrEmpty(presetId) ? "(空)" : presetId,
                anchors.Count,
                world?.EraTrend?.CurrentStageName ?? "(null)");
        }

        private void HandleDayTick()
        {
            if (world == null || service == null) return;
            service.AdvanceTo(world.Date);
        }

        private void OnDestroy()
        {
            if (world != null) world.OnDayTick -= HandleDayTick;
        }
    }
}
