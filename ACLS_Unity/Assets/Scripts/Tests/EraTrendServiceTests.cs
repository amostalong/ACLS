using NUnit.Framework;
using ACLS.Sim;

namespace ACLS.Tests
{
    public class EraTrendServiceTests
    {
        [Test]
        public void InitialDate_184_01_01_NoAnchorsTriggered_ButBackfillsForeshadows()
        {
            var world = new World();
            world.Date = new GameDate(184, 1, 1);
            var svc = new EraTrendService(world, EraTrendAnchors.Hard);
            svc.AdvanceTo(world.Date);

            Assert.AreEqual(0, world.EraTrend.TriggeredAnchorIds.Count);
            // 184/1/1 还没到黄巾 184/2/1,不应触发锚点。
            // 但首次调用会 Backfill"错过"的前兆:今天还没到黄巾,会 Backfill 吗?不会。
            // 实际今天 < trigger,逻辑上应等 AdvanceTo 走到 trigger - DaysBefore 时才注入。
            // 黄巾 184/2/1 - 90天 = 183/11/3 (不可能在游戏期内), - 45天 = 183/12/17, -14天 = 184/1/18。
            // 所以 184/1/1 → 1/1 那天没前兆可入, Backfill 不入。
            Assert.AreEqual(0, world.EraTrend.ForeshadowingInjected.Count);
            StringAssert.Contains("未起", world.EraTrend.CurrentStageName);
        }

        [Test]
        public void Date_184_01_18_BackfillsHuangjinL1Foreshadowing()
        {
            // 184/1/18 = 黄巾 184/2/1 - 14天 → 首次调用 + Backfill:今天 < trigger,
            // Backfill 只处理 today >= trigger 的锚点,所以这里靠正常注入路径。
            var world = new World();
            world.Date = new GameDate(184, 1, 18);
            var svc = new EraTrendService(world, EraTrendAnchors.Hard);
            svc.AdvanceTo(world.Date);

            bool hasL1 = false;
            for (int i = 0; i < world.EraTrend.ForeshadowingInjected.Count; i++)
            {
                var f = world.EraTrend.ForeshadowingInjected[i];
                if (f.AnchorId == "anchor_huangjin_onset" && f.TargetLayer == "L1" && f.DaysBeforeAnchor == 14)
                    hasL1 = true;
            }
            Assert.IsTrue(hasL1, "应注入 黄巾/L1/14天 前兆");
        }

        [Test]
        public void Date_184_03_01_BackfillsHuangjin90And45_AndTriggersAnchor()
        {
            // 184/3/1 > 黄巾 184/2/1,首次调用应 Backfill 90/45/14 天前兆 + 触发锚点。
            var world = new World();
            world.Date = new GameDate(184, 3, 1);
            var svc = new EraTrendService(world, EraTrendAnchors.Hard);
            svc.AdvanceTo(world.Date);

            Assert.IsTrue(world.EraTrend.TriggeredAnchorIds.Contains("anchor_huangjin_onset"));
            int l3 = 0, l2 = 0, l1 = 0;
            for (int i = 0; i < world.EraTrend.ForeshadowingInjected.Count; i++)
            {
                var f = world.EraTrend.ForeshadowingInjected[i];
                if (f.AnchorId != "anchor_huangjin_onset") continue;
                if (f.TargetLayer == "L3" && f.DaysBeforeAnchor == 90) l3++;
                if (f.TargetLayer == "L2" && f.DaysBeforeAnchor == 45) l2++;
                if (f.TargetLayer == "L1" && f.DaysBeforeAnchor == 14) l1++;
            }
            Assert.AreEqual(1, l3, "应追补 黄巾/L3/90天 前兆");
            Assert.AreEqual(1, l2, "应追补 黄巾/L2/45天 前兆");
            Assert.AreEqual(1, l1, "应追补 黄巾/L1/14天 前兆");
        }

        [Test]
        public void Date_184_03_17_TriggersHuangjinL2Foreshadowing()
        {
            // 184/3/17 > 锚点,但首次调用 Backfill 后再调用今日应不会重复注入。
            // 验证 Backfill 标记的去重���效。
            var world = new World();
            world.Date = new GameDate(184, 3, 17);
            var svc = new EraTrendService(world, EraTrendAnchors.Hard);
            svc.AdvanceTo(world.Date);

            // 第一次 Backfill 已注入 L2 45 天前兆
            int l2 = 0;
            for (int i = 0; i < world.EraTrend.ForeshadowingInjected.Count; i++)
            {
                var f = world.EraTrend.ForeshadowingInjected[i];
                if (f.AnchorId == "anchor_huangjin_onset" && f.TargetLayer == "L2" && f.DaysBeforeAnchor == 45) l2++;
            }
            Assert.AreEqual(1, l2, "Backfill + 正常路径不重复注入");
        }

        [Test]
        public void Date_184_11_01_TriggersZhangjiaoDead()
        {
            var world = new World();
            world.Date = new GameDate(184, 11, 1);
            var svc = new EraTrendService(world, EraTrendAnchors.Hard);
            svc.AdvanceTo(world.Date);

            Assert.IsTrue(world.EraTrend.TriggeredAnchorIds.Contains("anchor_zhangjiao_dead"));
        }

        [Test]
        public void AdvanceTwice_SameDate_DoesNotDoubleInject()
        {
            var world = new World();
            world.Date = new GameDate(184, 3, 17);
            var svc = new EraTrendService(world, EraTrendAnchors.Hard);
            svc.AdvanceTo(world.Date);
            int after1 = world.EraTrend.ForeshadowingInjected.Count;
            svc.AdvanceTo(world.Date);
            int after2 = world.EraTrend.ForeshadowingInjected.Count;
            Assert.AreEqual(after1, after2, "同一天重复 AdvanceTo 不应重复注入前兆");
        }

        [Test]
        public void ReadEraTrendTool_All_ReturnsFormattedText()
        {
            var world = new World();
            world.Date = new GameDate(184, 3, 17);
            var svc = new EraTrendService(world, EraTrendAnchors.Hard);
            svc.AdvanceTo(world.Date);

            var tool = new ACLS.Llm.Tools.ReadEraTrendTool(world);
            var text = tool.ExecuteAsync("{\"scope\":\"all\"}", default).GetAwaiter().GetResult();
            StringAssert.Contains("时代大势", text);
        }
    }
}
