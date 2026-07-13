using System;
using System.Collections.Generic;
using ACLS.Sim;
using ACLS.Logging;

namespace ACLS.Authoring.Stage
{
    // Decides whether each L1-L4 tier is stale enough to require rebuilding
    // before the next StagePlay turn.
    //
    // Design: queried (not pushed). Callers check Evaluate(World) right before
    // sending the user's action to the LLM and act on the report.
    //
    // Rules (tunable via the constants block):
    //   L1 must rebuild on: date change, location change, |Δnpc| >= 2, force flag.
    //   L1 light refresh on: shichen change within the same day/location.
    //   L2 should rebuild on: >= 3 days since last build, location change.
    //   L3 should rebuild on: >= 7 days since last build.
    //   L4 should rebuild on: >= 30 days since last build.
    //
    // The numbers (3 / 7 / 30) match game-design.md §5.1 layer cadences.
    public static class StageStaleness
    {
        public const int L2RebuildDays = 3;
        public const int L3RebuildDays = 7;
        public const int L4RebuildDays = 30;
        public const int L1NpcDeltaThreshold = 2;

        public struct Report
        {
            public bool L1NeedsFullRefresh;
            public bool L1NeedsLightRefresh;
            public bool L2NeedsRefresh;
            public bool L3NeedsRefresh;
            public bool L4NeedsRefresh;
            public string Reason;
        }

        public static Report Evaluate(World world, bool forceRefresh = false)
        {
            var r = new Report();
            var reasons = new List<string>();

            if (world == null || world.Stage == null)
            {
                r.Reason = "world/stage null";
                return r;
            }

            var meta = world.Stage.L1Meta;
            int currentLoc = world.Player?.Identity?.LocationId ?? 0;
            int currentNpcCount = CountNpcsAtLocation(world, currentLoc);

            if (forceRefresh)
            {
                r.L1NeedsFullRefresh = true;
                reasons.Add("L1 force");
                r.L2NeedsRefresh = true;
                reasons.Add("L2 force");
                r.L3NeedsRefresh = true;
                reasons.Add("L3 force");
                r.L4NeedsRefresh = true;
                reasons.Add("L4 force");
            }
            else
            {
                // L1 — full refresh triggers
                if (meta.BuiltAt == default || meta.BuiltAt.Year == 0)
                {
                    r.L1NeedsFullRefresh = true;
                    reasons.Add("L1 never built");
                }
                else if (world.Date > meta.BuiltAt)
                {
                    r.L1NeedsFullRefresh = true;
                    reasons.Add($"L1 day changed ({meta.BuiltAt}→{world.Date})");
                }
                else if (meta.LocationId != currentLoc)
                {
                    r.L1NeedsFullRefresh = true;
                    reasons.Add($"L1 location changed ({meta.LocationId}→{currentLoc})");
                }
                else if (Math.Abs(currentNpcCount - meta.ActiveNpcCount) >= L1NpcDeltaThreshold)
                {
                    r.L1NeedsFullRefresh = true;
                    reasons.Add($"L1 npc count Δ (was {meta.ActiveNpcCount}, now {currentNpcCount})");
                }
                else if (meta.ShichenIndex >= 0 && meta.ShichenIndex != DayOfYear(world.Date) % 16)
                {
                    // Light refresh: same scene, different shichen — NPC dispositions /
                    // scene lights shift, but not the whole composition.
                    r.L1NeedsLightRefresh = true;
                    reasons.Add($"L1 shichen changed ({meta.ShichenIndex}→{DayOfYear(world.Date) % 16})");
                }

                // L2 — 3-day cadence + location change
                if (world.Stage.L2Meta.BuiltAt.Year > 0)
                {
                    int daysSinceL2 = DaysBetween(world.Stage.L2Meta.BuiltAt, world.Date);
                    if (daysSinceL2 >= L2RebuildDays)
                    {
                        r.L2NeedsRefresh = true;
                        reasons.Add($"L2 stale ({daysSinceL2}d)");
                    }
                    else if (world.Stage.L2Meta.LocationId != currentLoc)
                    {
                        r.L2NeedsRefresh = true;
                        reasons.Add($"L2 location changed");
                    }
                }

                // L3 — 7-day cadence
                if (world.Stage.L3Meta.BuiltAt.Year > 0)
                {
                    int daysSinceL3 = DaysBetween(world.Stage.L3Meta.BuiltAt, world.Date);
                    if (daysSinceL3 >= L3RebuildDays)
                    {
                        r.L3NeedsRefresh = true;
                        reasons.Add($"L3 stale ({daysSinceL3}d)");
                    }
                }

                // L4 — 30-day cadence
                if (world.Stage.L4Meta.BuiltAt.Year > 0)
                {
                    int daysSinceL4 = DaysBetween(world.Stage.L4Meta.BuiltAt, world.Date);
                    if (daysSinceL4 >= L4RebuildDays)
                    {
                        r.L4NeedsRefresh = true;
                        reasons.Add($"L4 stale ({daysSinceL4}d)");
                    }
                }
            }

            r.Reason = reasons.Count == 0 ? "fresh" : string.Join("; ", reasons);
            Log.Debug(Log.Channels.Llm,
                "[Staleness] {0} | L1={1} L1light={2} L2={3} L3={4} L4={5} | current loc={6} npcs={7}",
                r.Reason, r.L1NeedsFullRefresh, r.L1NeedsLightRefresh,
                r.L2NeedsRefresh, r.L3NeedsRefresh, r.L4NeedsRefresh,
                currentLoc, currentNpcCount);
            return r;
        }

        private static int CountNpcsAtLocation(World world, int locationId)
        {
            if (world == null || locationId == 0) return 0;
            // Counts non-player alive characters at the location — same metric that
            // TouchL1 uses, so Δnpc in Evaluate matches what callers recorded.
            int playerId = world.PlayerCharacterId;
            int count = 0;
            foreach (var c in world.AliveCharacters())
            {
                if (c.Id == playerId) continue;
                if (c.Identity != null && c.Identity.LocationId == locationId) count++;
            }
            return count;
        }

        private static int DayOfYear(GameDate date)
        {
            return (date.Month - 1) * 30 + (date.Day - 1);
        }

        private static int DaysBetween(GameDate earlier, GameDate later)
        {
            if (earlier.Year == 0 || later.Year == 0) return 0;
            // Approximate — GameDate doesn't expose DaysSince. Use year*360 + month*30 + day.
            int e = earlier.Year * 360 + earlier.Month * 30 + earlier.Day;
            int l = later.Year * 360 + later.Month * 30 + later.Day;
            return l - e;
        }
    }
}