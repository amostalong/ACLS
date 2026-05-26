using System.Collections.Generic;

namespace ACLS.Sim
{
    public static class Effects
    {
        public static void ApplyAll(IList<EffectOp> ops, World world, Character actor)
        {
            if (ops == null) return;
            for (int i = 0; i < ops.Count; i++) ApplyOne(ops[i], world, actor);
        }

        public static void ApplyOne(EffectOp op, World world, Character actor)
        {
            switch (op.Kind)
            {
                case EffectKind.AdjustStat:
                    if (actor != null)
                    {
                        var axis = (StatAxis)op.IntArg;
                        actor.Stats = actor.Stats.Adjust(axis, op.IntArg2);
                    }
                    break;

                case EffectKind.AdjustGold:
                    world.Gold += op.IntArg2;
                    break;

                case EffectKind.AddTrait:
                    if (actor != null && !actor.Traits.Contains(op.IntArg))
                        actor.Traits.Add(op.IntArg);
                    break;

                case EffectKind.RemoveTrait:
                    actor?.Traits.Remove(op.IntArg);
                    break;

                case EffectKind.AdjustOpinion:
                {
                    if (actor == null) break;
                    int targetId = op.IntArg;
                    if (targetId == 0) break;
                    int delta = op.IntArg2;
                    int dir = op.IntArg3;  // 0 actor→target, 1 target→actor, 2 both
                    if (dir == 0 || dir == 2) world.Relations.Adjust(actor.Id, targetId, delta);
                    if (dir == 1 || dir == 2) world.Relations.Adjust(targetId, actor.Id, delta);
                    break;
                }

                case EffectKind.ChangeIdentity:
                    if (actor != null)
                    {
                        if (actor.Identity == null) actor.Identity = new Identity();
                        if (op.IntArg == -1) actor.Identity.TitleId = 0;
                        else if (op.IntArg != 0) actor.Identity.TitleId = op.IntArg;
                        if (op.IntArg2 == -1) actor.Identity.LocationId = 0;
                        else if (op.IntArg2 != 0) actor.Identity.LocationId = op.IntArg2;
                        if (op.IntArg3 == -1) actor.Identity.FactionId = 0;
                        else if (op.IntArg3 != 0) actor.Identity.FactionId = op.IntArg3;
                    }
                    break;

                case EffectKind.MoveLocation:
                    if (actor != null && actor.Identity != null)
                        actor.Identity.LocationId = op.IntArg;
                    break;

                case EffectKind.KillCharacter:
                {
                    int killId = op.IntArg == 0 ? (actor?.Id ?? 0) : op.IntArg;
                    if (killId != 0) world.KillCharacter(killId);
                    break;
                }

                case EffectKind.SpawnChild:
                    SpawnChild(world, actor, op.IntArg2);
                    break;

                case EffectKind.Marry:
                    Marry(world, actor, op.IntArg);
                    break;

                case EffectKind.Log:
                    // Picked up by a UI subscriber if any; sim itself does nothing.
                    break;

                case EffectKind.SetFlag:
                    if (!string.IsNullOrEmpty(op.StringArg)) world.SetFlag(op.StringArg);
                    break;

                case EffectKind.ClearFlag:
                    if (!string.IsNullOrEmpty(op.StringArg)) world.ClearFlag(op.StringArg);
                    break;
            }
        }

        private static void SpawnChild(World world, Character parent, int sexArg)
        {
            if (parent == null) return;
            Sex childSex = sexArg switch { 0 => Sex.Male, 1 => Sex.Female, _ => Rng.Chance(50) ? Sex.Male : Sex.Female };
            int spouseId = parent.SpouseId;
            Character mother = parent.Sex == Sex.Female ? parent : world.GetCharacter(spouseId);
            Character father = parent.Sex == Sex.Male ? parent : world.GetCharacter(spouseId);

            string surname = Names.ExtractSurname(father?.Name ?? parent.Name);
            string given = Names.RandomGiven(childSex);
            var child = new Character
            {
                Name = surname + given,
                Courtesy = "",
                Sex = childSex,
                Birth = world.Date,
                Stats = MixStats(father, mother),
                FatherId = father?.Id ?? 0,
                MotherId = mother?.Id ?? 0,
                IsHistorical = false,
                LifespanYears = Rng.Range(50, 80),
            };
            world.AddCharacter(child);
            if (father != null) father.ChildrenIds.Add(child.Id);
            if (mother != null && mother != father) mother.ChildrenIds.Add(child.Id);
        }

        private static Stats MixStats(Character father, Character mother)
        {
            Stats baseS = father != null && mother != null
                ? new Stats(
                    (father.Stats.Wu + mother.Stats.Wu) / 2,
                    (father.Stats.Tong + mother.Stats.Tong) / 2,
                    (father.Stats.Zhi + mother.Stats.Zhi) / 2,
                    (father.Stats.Zheng + mother.Stats.Zheng) / 2,
                    (father.Stats.Mei + mother.Stats.Mei) / 2)
                : father?.Stats ?? mother?.Stats ?? new Stats(8, 8, 8, 8, 8);
            // Add small noise per axis
            return new Stats(
                Clamp1to30(baseS.Wu + Rng.Range(-3, 3)),
                Clamp1to30(baseS.Tong + Rng.Range(-3, 3)),
                Clamp1to30(baseS.Zhi + Rng.Range(-3, 3)),
                Clamp1to30(baseS.Zheng + Rng.Range(-3, 3)),
                Clamp1to30(baseS.Mei + Rng.Range(-3, 3)));
        }

        private static int Clamp1to30(int v) => v < 1 ? 1 : v > 30 ? 30 : v;

        private static void Marry(World world, Character actor, int spouseIdArg)
        {
            if (actor == null || actor.SpouseId != 0) return;
            Character spouse;
            if (spouseIdArg != 0)
            {
                spouse = world.GetCharacter(spouseIdArg);
                if (spouse == null || spouse.SpouseId != 0) return;
            }
            else
            {
                Sex other = actor.Sex == Sex.Male ? Sex.Female : Sex.Male;
                int spouseAge = Rng.Range(16, 22);
                spouse = new Character
                {
                    Name = (other == Sex.Female ? "李" : Names.ExtractSurname(actor.Name) + "") + Names.RandomGiven(other),
                    Sex = other,
                    Birth = new GameDate(world.Date.Year - spouseAge, 1, 1),
                    Stats = new Stats(Rng.Range(5, 12), Rng.Range(5, 12), Rng.Range(5, 12), Rng.Range(5, 12), Rng.Range(8, 14)),
                    IsHistorical = false,
                    LifespanYears = Rng.Range(45, 75),
                };
                world.AddCharacter(spouse);
            }
            actor.SpouseId = spouse.Id;
            spouse.SpouseId = actor.Id;
        }
    }
}
