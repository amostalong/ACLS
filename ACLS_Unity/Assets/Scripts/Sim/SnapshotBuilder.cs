using System.Text;

namespace ACLS.Sim
{
    // Renders the World into the four layered text blocks.
    // Tier-specific cadence is enforced here: caller can call Refresh() every
    // turn and only the tiers that have actually rolled over will be rebuilt.
    //
    // Sim layer — no UnityEngine, no Authoring/Loc dependencies. Trait names
    // are looked up via a small inline map; if richer localization is needed,
    // a higher layer can wrap this builder with L10n substitution.
    public static class SnapshotBuilder
    {
        public static void Refresh(World world, WorldSnapshot snap)
        {
            if (world == null || snap == null) return;

            // T1 — every day
            if (!IsSameDay(snap.Tier1BuiltAt, world.Date))
            {
                snap.Tier1 = BuildT1(world);
                snap.Tier1BuiltAt = world.Date;
            }
            // T2 — every day
            if (!IsSameDay(snap.Tier2BuiltAt, world.Date))
            {
                snap.Tier2 = BuildT2(world);
                snap.Tier2BuiltAt = world.Date;
            }
            // T3 — every month
            if (!snap.Tier3BuiltAt.IsSameMonthAs(world.Date))
            {
                snap.Tier3 = BuildT3(world);
                snap.Tier3BuiltAt = world.Date;
            }
            // T4 — every year
            if (!snap.Tier4BuiltAt.IsSameYearAs(world.Date))
            {
                snap.Tier4 = BuildT4(world);
                snap.Tier4BuiltAt = world.Date;
            }
        }

        public static void RefreshTier(World world, WorldSnapshot snap, SnapshotTiers tier)
        {
            switch (tier)
            {
                case SnapshotTiers.T1: snap.Tier1 = BuildT1(world); snap.Tier1BuiltAt = world.Date; break;
                case SnapshotTiers.T2: snap.Tier2 = BuildT2(world); snap.Tier2BuiltAt = world.Date; break;
                case SnapshotTiers.T3: snap.Tier3 = BuildT3(world); snap.Tier3BuiltAt = world.Date; break;
                case SnapshotTiers.T4: snap.Tier4 = BuildT4(world); snap.Tier4BuiltAt = world.Date; break;
            }
        }

        private static bool IsSameDay(GameDate a, GameDate b) =>
            a.Year == b.Year && a.Month == b.Month && a.Day == b.Day;

        // -------- T1 --------

        // 当下场景：日期 + 主角自身 + 此刻同地角色。
        private static string BuildT1(World world)
        {
            var sb = new StringBuilder();
            sb.Append("[T1·当下] ").Append(world.Date.ToString());
            var p = world.Player;
            if (p == null)
            {
                sb.Append("（主角尚未确定）");
                return sb.ToString();
            }

            var loc = world.GetLocation(p.Identity?.LocationId ?? 0);
            sb.Append(" · ").Append(loc?.Name ?? "?");
            sb.Append('\n');

            sb.Append("[主角] ").Append(p.Name);
            if (!string.IsNullOrEmpty(p.Courtesy)) sb.Append('（').Append(p.Courtesy).Append('）');
            sb.Append("，").Append(p.AgeAt(world.Date)).Append(" 岁，")
              .Append(p.Sex == Sex.Male ? "男" : "女").Append('\n');

            sb.Append("[属性] 武").Append(p.Stats.Wu)
              .Append(" 统").Append(p.Stats.Tong)
              .Append(" 智").Append(p.Stats.Zhi)
              .Append(" 政").Append(p.Stats.Zheng)
              .Append(" 魅").Append(p.Stats.Mei)
              .Append(" · 钱 ").Append(world.Gold).Append('\n');

            if (p.Traits != null && p.Traits.Count > 0)
            {
                sb.Append("[特质] ");
                for (int i = 0; i < p.Traits.Count; i++)
                {
                    if (i > 0) sb.Append('、');
                    sb.Append(TraitName(p.Traits[i]));
                }
                sb.Append('\n');
            }

            if (world.Flags != null && world.Flags.Count > 0)
            {
                sb.Append("[标记] ");
                for (int i = 0; i < world.Flags.Count; i++)
                {
                    if (i > 0) sb.Append('、');
                    sb.Append(world.Flags[i]);
                }
                sb.Append('\n');
            }

            // 同地（含主角）
            if (loc != null && loc.CharactersPresent != null && loc.CharactersPresent.Count > 1)
            {
                sb.Append("[在场] ");
                bool first = true;
                for (int i = 0; i < loc.CharactersPresent.Count; i++)
                {
                    int cid = loc.CharactersPresent[i];
                    if (cid == p.Id) continue;
                    var c = world.GetCharacter(cid);
                    if (c == null || c.IsDead) continue;
                    if (!first) sb.Append('、');
                    sb.Append(c.Name).Append(' ').Append(c.AgeAt(world.Date)).Append(" 岁");
                    first = false;
                }
                if (!first) sb.Append('\n');
            }

            return sb.ToString().TrimEnd();
        }

        // -------- T2 --------

        // 个人圈：家眷 + 出身地详细 + 周边可达地。
        private static string BuildT2(World world)
        {
            var sb = new StringBuilder();
            sb.Append("[T2·个人圈]\n");

            var p = world.Player;
            if (p != null)
            {
                var spouse = world.GetCharacter(p.SpouseId);
                if (spouse != null)
                {
                    sb.Append("[家眷] 妻 ").Append(spouse.Name).Append(' ')
                      .Append(spouse.AgeAt(world.Date)).Append(" 岁");
                    if (p.ChildrenIds.Count > 0)
                    {
                        sb.Append("；子女 ");
                        for (int i = 0; i < p.ChildrenIds.Count; i++)
                        {
                            if (i > 0) sb.Append('、');
                            var c = world.GetCharacter(p.ChildrenIds[i]);
                            if (c == null) continue;
                            sb.Append(c.Name).Append(' ').Append(c.AgeAt(world.Date)).Append(" 岁");
                        }
                    }
                    sb.Append('\n');
                }
                else
                {
                    sb.Append("[家眷] 单身\n");
                }

                var loc = world.GetLocation(p.Identity?.LocationId ?? 0);
                if (loc != null)
                {
                    var fac = world.GetFaction(loc.OwnerFactionId);
                    sb.Append("[出身地] ").Append(loc.Name);
                    if (!string.IsNullOrEmpty(loc.Region)) sb.Append('（').Append(loc.Region).Append('）');
                    if (fac != null) sb.Append(" · ").Append(fac.Name);
                    sb.Append(" · 繁荣度 ").Append(loc.Prosperity).Append('\n');
                }
            }

            // 邻地：列举所有非主角所在地的城邑。
            int hereId = p?.Identity?.LocationId ?? 0;
            if (world.LocationList.Count > 1)
            {
                sb.Append("[邻地] ");
                bool first = true;
                for (int i = 0; i < world.LocationList.Count; i++)
                {
                    var l = world.LocationList[i];
                    if (l.Id == hereId) continue;
                    if (!first) sb.Append('、');
                    sb.Append(l.Name);
                    first = false;
                }
                sb.Append('\n');
            }

            return sb.ToString().TrimEnd();
        }

        // -------- T3 --------

        // 关注圈：势力近况 + 名士军阀（Phase 3 才会有具体人物，此处给骨架）。
        private static string BuildT3(World world)
        {
            var sb = new StringBuilder();
            sb.Append("[T3·关注圈]\n");

            // 派系状态
            sb.Append("[势力]\n");
            for (int i = 0; i < world.FactionList.Count; i++)
            {
                var f = world.FactionList[i];
                sb.Append("- ").Append(f.Name);
                if (f.OwnedLocationIds != null && f.OwnedLocationIds.Count > 0)
                    sb.Append("（治 ").Append(f.OwnedLocationIds.Count).Append(" 地）");
                sb.Append('\n');
            }
            sb.Append("- 太平道：钜鹿张角兄弟，门徒数十万，遍布青徐幽冀八州（朝廷视为隐患，未公开镇压）\n");

            // 名士与年轻军阀（184 年的关键人物快照，按年龄给当下定位）
            int year = world.Date.Year;
            sb.Append("[人物·184 年前后]\n");
            sb.Append("- 曹操（孟德，").Append(year - 155).Append(" 岁）：议郎/濮阳令，刚遭陷罢归乡\n");
            sb.Append("- 袁绍（本初，").Append(year - 154).Append(" 岁）：洛阳，与何进交结\n");
            sb.Append("- 刘备（玄德，").Append(year - 161).Append(" 岁）：涿郡，织席贩履\n");
            sb.Append("- 孙坚（文台，").Append(year - 155).Append(" 岁）：盐渎丞\n");
            sb.Append("- 董卓（仲颖，").Append(year - 138).Append(" 岁）：并州刺史\n");

            return sb.ToString().TrimEnd();
        }

        // -------- T4 --------

        // 时代大势：基于年份给阶段定义 + 即将发生的历史枢轴。
        private static string BuildT4(World world)
        {
            var sb = new StringBuilder();
            sb.Append("[T4·时代大势]\n");

            int y = world.Date.Year;
            sb.Append("[纪年] ").Append(y).Append(" 年 · 东汉末年。\n");

            if (y < 184)
                sb.Append("[政局] 灵帝在位，宦官擅权，党锢未除，士林郁郁。北方天灾频仍，民心思变。\n");
            else if (y < 189)
                sb.Append("[政局] 黄巾大起，朝廷调皇甫嵩、卢植、朱儁三路镇压；豪强乘机崛起。\n");
            else if (y < 196)
                sb.Append("[政局] 灵帝崩，何进死，董卓入京，废立天子。关东诸侯起兵讨董，天下大乱。\n");
            else if (y < 220)
                sb.Append("[政局] 曹操挟天子以令诸侯，袁绍、刘表、孙策各据一方，群雄逐鹿。\n");
            else
                sb.Append("[政局] 曹丕代汉，蜀吴并立，三国鼎立成形。\n");

            sb.Append("[即将] ");
            if (y < 184) sb.Append("184 春 黄巾起义；184 末 张角病死。");
            else if (y < 189) sb.Append("189 灵帝崩，何进死，董卓入京。");
            else if (y < 200) sb.Append("190 关东联军讨董；195 天子东归；196 曹操迎驾。");
            else if (y < 208) sb.Append("200 官渡之战；208 赤壁之战。");
            else if (y < 220) sb.Append("219 关羽北伐失利；220 曹丕称帝。");
            else sb.Append("（鼎立既成，留待长卷）");
            sb.Append('\n');

            return sb.ToString().TrimEnd();
        }

        // 简单的 trait id → 中文名映射；Sim 层不引 L10n，所以内联。
        private static string TraitName(int id) => id switch
        {
            1 => "谨慎",
            2 => "果决",
            3 => "好学",
            _ => "#" + id,
        };
    }
}
