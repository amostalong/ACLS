using System.Collections.Generic;
using ACLS.Llm;
using ACLS.Sim;

namespace ACLS.Authoring
{
    // Maps the symbolic, LLM-friendly effect specs from LlmReply into the
    // runtime EffectOp struct that Effects.ApplyOne understands.
    //
    // Step-2 whitelist: AdjustStat / AdjustGold / AdjustOpinion / AddTrait /
    // RemoveTrait. Anything else is silently dropped (with a console warning)
    // — we don't want LLM to be able to call KillCharacter or Marry yet.
    public static class EffectParser
    {
        public static List<EffectOp> ParseAll(IList<LlmReply.EffectSpec> specs, World world)
        {
            var result = new List<EffectOp>();
            if (specs == null) return result;
            for (int i = 0; i < specs.Count; i++)
            {
                if (TryParse(specs[i], world, out var op))
                {
                    result.Add(op);
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[EffectParser] dropped: {specs[i]?.Kind} (unsupported or malformed)");
                }
            }
            return result;
        }

        public static bool TryParse(LlmReply.EffectSpec spec, World world, out EffectOp op)
        {
            op = default;
            if (spec == null || string.IsNullOrEmpty(spec.Kind)) return false;

            switch (spec.Kind)
            {
                case "AdjustStat":
                {
                    if (!TryParseStatAxis(spec.Stat, out var axis)) return false;
                    op = new EffectOp { Kind = EffectKind.AdjustStat, IntArg = (int)axis, IntArg2 = spec.Delta };
                    return true;
                }

                case "AdjustGold":
                {
                    op = new EffectOp { Kind = EffectKind.AdjustGold, IntArg2 = spec.Delta };
                    return true;
                }

                case "AdjustOpinion":
                {
                    int targetId = ResolveCharacterId(world, spec.Target);
                    if (targetId == 0) return false;
                    // Step-2 default: bidirectional (both feel the same way).
                    op = new EffectOp
                    {
                        Kind = EffectKind.AdjustOpinion,
                        IntArg = targetId,
                        IntArg2 = spec.Delta,
                        IntArg3 = 2,
                    };
                    return true;
                }

                case "AddTrait":
                {
                    int traitId = ResolveTraitId(spec.Trait);
                    if (traitId == 0) return false;
                    op = new EffectOp { Kind = EffectKind.AddTrait, IntArg = traitId };
                    return true;
                }

                case "RemoveTrait":
                {
                    int traitId = ResolveTraitId(spec.Trait);
                    if (traitId == 0) return false;
                    op = new EffectOp { Kind = EffectKind.RemoveTrait, IntArg = traitId };
                    return true;
                }

                case "SetFlag":
                {
                    if (string.IsNullOrWhiteSpace(spec.Flag)) return false;
                    op = new EffectOp { Kind = EffectKind.SetFlag, StringArg = spec.Flag.Trim() };
                    return true;
                }

                case "ClearFlag":
                {
                    if (string.IsNullOrWhiteSpace(spec.Flag)) return false;
                    op = new EffectOp { Kind = EffectKind.ClearFlag, StringArg = spec.Flag.Trim() };
                    return true;
                }

                default:
                    return false;   // gated: LLM cannot Kill/Marry/Spawn/Move/etc. in Step 2
            }
        }

        private static bool TryParseStatAxis(string s, out StatAxis axis)
        {
            axis = StatAxis.Wu;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim())
            {
                case "Wu":    case "wu":    case "武": axis = StatAxis.Wu;    return true;
                case "Tong":  case "tong":  case "统": axis = StatAxis.Tong;  return true;
                case "Zhi":   case "zhi":   case "智": axis = StatAxis.Zhi;   return true;
                case "Zheng": case "zheng": case "政": axis = StatAxis.Zheng; return true;
                case "Mei":   case "mei":   case "魅": axis = StatAxis.Mei;   return true;
                default: return false;
            }
        }

        // Step-2 traits: just the 3 placeholders. Add dictionary lookup once
        // we have a TraitDef.Key field.
        private static int ResolveTraitId(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            string normalized = s.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "cautious": case "谨慎":
                    return WorldFactory.TRAIT_CAUTIOUS;
                case "decisive": case "果决":
                    return WorldFactory.TRAIT_DECISIVE;
                case "studious": case "好学":
                    return WorldFactory.TRAIT_STUDIOUS;
                default: return 0;
            }
        }

        private static int ResolveCharacterId(World world, string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string trimmed = name.Trim();
            // Try exact match first, then "ends with" (so 配角 字号 也能解析)
            for (int i = 0; i < world.CharacterList.Count; i++)
            {
                var c = world.CharacterList[i];
                if (c.Name == trimmed) return c.Id;
            }
            for (int i = 0; i < world.CharacterList.Count; i++)
            {
                var c = world.CharacterList[i];
                if (!string.IsNullOrEmpty(c.Name) && (trimmed.Contains(c.Name) || c.Name.Contains(trimmed)))
                    return c.Id;
            }
            return 0;
        }
    }
}
