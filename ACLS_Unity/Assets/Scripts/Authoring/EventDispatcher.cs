using System.Collections.Generic;
using UnityEngine;
using ACLS.Data;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // Subscribes to World ticks and queues PendingEvents when triggers/conditions
    // match. One event queued per evaluation pass — UI consumes the queue
    // sequentially.
    public sealed class EventDispatcher : MonoBehaviour
    {
        private World world;
        private List<GameEventDef> all;

        public void Bind(World world)
        {
            if (this.world != null)
            {
                this.world.OnDayTick -= OnDay;
                this.world.OnMonthTick -= OnMonth;
            }
            this.world = world;
            this.all = new List<GameEventDef>(Registry.AllEvents);
            world.OnDayTick += OnDay;
            world.OnMonthTick += OnMonth;
        }

        private void OnDestroy()
        {
            if (world != null)
            {
                world.OnDayTick -= OnDay;
                world.OnMonthTick -= OnMonth;
            }
        }

        private void OnDay()
        {
            // Dated events fire on the exact day.
            for (int i = 0; i < all.Count; i++)
            {
                var ev = all[i];
                if (ev.Trigger.Kind != EventTriggerKind.Dated) continue;
                if (world.Date.Year != ev.Trigger.DatedYear) continue;
                if (world.Date.Month != ev.Trigger.DatedMonth) continue;
                if (world.Date.Day != ev.Trigger.DatedDay) continue;
                TryQueue(ev);
            }
        }

        private void OnMonth()
        {
            // Periodic / Conditional. One queued per pass to keep the UI simple.
            for (int i = 0; i < all.Count; i++)
            {
                var ev = all[i];
                switch (ev.Trigger.Kind)
                {
                    case EventTriggerKind.Periodic:
                        if ((world.Date.Year * 12 + world.Date.Month) % System.Math.Max(1, ev.Trigger.PeriodMonths) != 0) continue;
                        if (ev.Trigger.ChancePercent > 0 && !Rng.Chance(ev.Trigger.ChancePercent)) continue;
                        if (TryQueue(ev)) return;
                        break;

                    case EventTriggerKind.Conditional:
                        if (ev.Trigger.ChancePercent > 0 && !Rng.Chance(ev.Trigger.ChancePercent)) continue;
                        if (TryQueue(ev)) return;
                        break;
                }
            }
        }

        private bool TryQueue(GameEventDef ev)
        {
            int actorId = SelectActor(ev);
            if (actorId == 0) return false;
            var actor = world.GetCharacter(actorId);
            if (actor == null) return false;
            if (world.IsOnCooldown(ev.Id, actorId)) return false;
            if (!Conditions.EvalAll(ev.Conditions, world, actor)) return false;

            world.EnqueueEvent(new PendingEvent { EventDefId = ev.Id, ActorCharacterId = actorId });
            if (ev.CooldownMonths > 0)
            {
                var avail = world.Date;
                for (int m = 0; m < ev.CooldownMonths; m++) avail = avail.AddDays(30);
                world.SetCooldown(ev.Id, actorId, avail);
            }
            return true;
        }

        private int SelectActor(GameEventDef ev)
        {
            switch (ev.Scope)
            {
                case EventScope.PlayerOnly:
                    return world.PlayerCharacterId;

                case EventScope.AnyHistoricalChar:
                {
                    // Pick a random historical alive character. For Phase 1 there are none.
                    int picked = 0, count = 0;
                    foreach (var c in world.AliveCharacters())
                    {
                        if (!c.IsHistorical) continue;
                        count++;
                        if (Rng.Range(1, count) == 1) picked = c.Id;
                    }
                    return picked;
                }

                case EventScope.Global:
                    return world.PlayerCharacterId;  // anchored on the player; def can re-target via Effects

                default:
                    return 0;
            }
        }
    }
}
