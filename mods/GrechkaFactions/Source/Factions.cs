using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Factions
{
    public sealed class MemberProfile : DefModExtension
    {
        public string group;
        public int implantCount = 1;
        public List<SkillDef> specialties = new List<SkillDef>();
    }

    public sealed class FactionsMod : Mod
    {
        public FactionsMod(ModContentPack content) : base(content)
        {
            new Harmony("grechka.factions").PatchAll();
        }
    }

    public static class MemberGeneration
    {
        // This runs once at generation. Save loading and faction changes never refill implants or skills.
        public static void Apply(Pawn pawn)
        {
            if (pawn?.RaceProps?.Humanlike != true || pawn.Dead) return;
            string faction = pawn.Faction?.def?.defName;
            if (faction == null || !faction.StartsWith("GrechkaFaction_", StringComparison.Ordinal)) return;
            string group = faction.Substring("GrechkaFaction_".Length);
            MemberProfile profile = pawn.kindDef?.GetModExtension<MemberProfile>();
            // Faction-wide fallback also covers vanilla pawn kinds selected by outside incidents.
            if (group == "Artel")
            {
                Train(pawn, SkillDefOf.Intellectual, 16);
                foreach (SkillRecord skill in pawn.skills.skills)
                    if (!skill.TotallyDisabled) skill.Level = Math.Max(skill.Level, 8);
                IEnumerable<SkillDef> specialties = profile?.specialties?.Count > 0
                    ? profile.specialties : new[] { SkillDefOf.Crafting, SkillDefOf.Construction };
                foreach (SkillDef skill in specialties) Train(pawn, skill, 16);
                int count = Math.Clamp(profile?.implantCount ?? 1, 1, 3);
                Install(pawn, "Grechka_GrechkaArmT5_Hediff", "Arm");
                if (count >= 2) Install(pawn, "Grechka_GrechkaLegT5_Hediff", "Leg");
                if (count >= 3) Install(pawn, "Grechka_GrechkaEyeT5_Hediff", "Eye");
            }
            else if (group == "Bare")
            {
                Train(pawn, SkillDefOf.Melee, 16);
                Install(pawn, "Grechka_CombatChipT5_Hediff", "Brain");
                Install(pawn, "Grechka_MovementChipT5_Hediff", "Brain");
                // Outside incidents may supply a vanilla kind. Preserve its generated clothes as loot.
                if (pawn.apparel != null && pawn.inventory != null)
                    foreach (Apparel apparel in pawn.apparel.WornApparel.ToList())
                    {
                        pawn.apparel.Remove(apparel);
                        pawn.inventory.innerContainer.TryAdd(apparel);
                    }
            }
            else if (group == "Rescue") Train(pawn, SkillDefOf.Medicine, 14);
            else if (group == "Mills")
            {
                Train(pawn, SkillDefOf.Plants, 12);
                Train(pawn, SkillDefOf.Cooking, 10);
            }
            else if (group == "Post" && pawn.inventory != null)
            {
                int tier = Rand.RangeInclusive(1, 3);
                ThingDef block = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_LuckyBlockT" + tier + "_Item");
                if (block != null && !pawn.inventory.innerContainer.Any(t => t.def.defName.StartsWith("Grechka_LuckyBlock", StringComparison.Ordinal)))
                    pawn.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(block));
            }
        }

        private static void Train(Pawn pawn, SkillDef def, int minimum)
        {
            SkillRecord skill = pawn.skills?.GetSkill(def);
            if (skill == null || skill.TotallyDisabled) return;
            skill.Level = Math.Max(skill.Level, Rand.RangeInclusive(minimum, 20));
            skill.passion = Passion.Major;
        }

        private static bool HasNaturalAncestors(Pawn pawn, BodyPartRecord part)
        {
            for (BodyPartRecord ancestor = part.parent; ancestor != null; ancestor = ancestor.parent)
                if (pawn.health.hediffSet.HasDirectlyAddedPartFor(ancestor)) return false;
            return true;
        }

        private static void Install(Pawn pawn, string hediffName, string partName)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(hediffName);
            if (def == null)
            {
                Log.ErrorOnce("[Grechka Factions] Missing required implant " + hediffName, GenText.StableStringHash(hediffName));
                return;
            }
            if (pawn.health.hediffSet.HasHediff(def)) return;
            BodyPartRecord part = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(p => p.def.defName == partName && !pawn.health.hediffSet.HasDirectlyAddedPartFor(p)
                    && HasNaturalAncestors(pawn, p));
            if (part != null) pawn.health.AddHediff(def, part);
        }
    }

    [HarmonyPatch]
    public static class MemberGenerationHook
    {
        public static MethodBase TargetMethod() => AccessTools.Method(typeof(PawnGenerator), "GenerateNewPawnInternal");

        public static void Postfix(Pawn __result) => MemberGeneration.Apply(__result);
    }
}
