using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Pharmacy
{
    public sealed class GrechkaPharmacyMod : Mod
    {
        public GrechkaPharmacyMod(ModContentPack content) : base(content)
        { new Harmony("grechka.pharmacy").PatchAll(typeof(GrechkaPharmacyMod).Assembly); }
    }

    // Consumable treatment is separate from vanilla tending medicine. Every action is
    // deliberately narrower than "remove bad hediffs": implants, genes and quest effects survive.
    public sealed class IngestionOutcomeDoer_Pharmacy : IngestionOutcomeDoer
    {
        public string action = "effect";
        public HediffDef effect;
        public int durationTicks = 60000;
        public float amount = 5f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            PharmacyRules.Apply(pawn, action, effect, durationTicks, amount);
        }
    }

    public static class PharmacyRules
    {
        public const long TicksPerYear = 3600000;
        private static readonly HashSet<string> DetoxChemicals = new HashSet<string>(StringComparer.Ordinal)
            { "Alcohol", "Smokeleaf", "Psychite", "WakeUp", "GoJuice", "Ambrosia" };
        private static readonly HashSet<string> ChronicConditions = new HashSet<string>(StringComparer.Ordinal)
            { "BadBack", "Frail", "Cataract", "HearingLoss", "Dementia", "Alzheimers", "Asthma", "HeartArteryBlockage", "Cirrhosis" };
        private static readonly HashSet<string> LimbTags = new HashSet<string>(StringComparer.Ordinal)
            { "MovingLimbCore", "MovingLimbSegment", "MovingLimbDigit", "ManipulationLimbCore", "ManipulationLimbSegment", "ManipulationLimbDigit" };

        public static bool Eligible(Pawn pawn) => pawn != null && !pawn.Dead && pawn.health?.hediffSet != null
            && pawn.RaceProps != null && pawn.RaceProps.IsFlesh;

        public static bool CanApply(Pawn pawn, string action)
        {
            if (!Eligible(pawn)) return false;
            switch (action)
            {
                case "scar": return pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(h => h.IsPermanent() && !ProtectedPart(pawn, h.Part));
                case "limb": return LimbCandidate(pawn) != null;
                case "youth": return pawn.RaceProps.Humanlike && pawn.ageTracker != null && pawn.ageTracker.AgeBiologicalTicks > Math.Max(25 * TicksPerYear, pawn.ageTracker.AdultMinAgeTicks);
                case "detox": return pawn.health.hediffSet.hediffs.Any(IsOrdinaryAddiction);
                case "regeneration": return pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(h => !h.IsPermanent() && !ProtectedPart(pawn, h.Part));
                case "comprehensive": return CanApply(pawn, "regeneration") || CanApply(pawn, "scar")
                    || pawn.health.hediffSet.hediffs.Any(h => ChronicConditions.Contains(h.def.defName) && h.def.modContentPack?.IsOfficialMod != false && !ProtectedPart(pawn, h.Part));
                case "effect": return true;
                default: return false;
            }
        }

        public static bool Apply(Pawn pawn, string action, HediffDef effect, int durationTicks, float amount)
        {
            if (!Eligible(pawn)) return false;
            bool changed;
            switch (action)
            {
                case "scar": changed = HealScar(pawn); break;
                case "limb": changed = RestoreLimb(pawn); break;
                case "youth": changed = Rejuvenate(pawn, amount); break;
                case "effect":
                case "regeneration":
                case "comprehensive":
                case "detox": return AddOrRefresh(pawn, effect, durationTicks) != null;
                default: return false;
            }
            if (changed && effect != null) AddOrRefresh(pawn, effect, durationTicks);
            return changed;
        }

        public static Hediff AddOrRefresh(Pawn pawn, HediffDef def, int durationTicks)
        {
            if (!Eligible(pawn) || def == null) return null;
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing == null) existing = pawn.health.AddHediff(def);
            existing.TryGetComp<HediffComp_Treatment>()?.RestartCompletedCourse();
            HediffComp_Disappears expires = existing.TryGetComp<HediffComp_Disappears>();
            if (expires != null) expires.ticksToDisappear = Math.Max(expires.ticksToDisappear, Math.Max(1, durationTicks));
            return existing;
        }

        public static bool HealFreshInjury(Pawn pawn, float amount)
        {
            if (!Eligible(pawn) || amount <= 0) return false;
            Hediff_Injury injury = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                .Where(h => !h.IsPermanent() && !ProtectedPart(pawn, h.Part))
                .OrderByDescending(h => h.Severity).ThenBy(h => h.loadID).FirstOrDefault();
            if (injury == null) return false;
            injury.Heal(amount);
            return true;
        }

        public static bool HealScar(Pawn pawn)
        {
            if (!Eligible(pawn)) return false;
            Hediff_Injury scar = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                .Where(h => h.IsPermanent() && !ProtectedPart(pawn, h.Part))
                .OrderByDescending(h => h.Severity).ThenBy(h => h.loadID).FirstOrDefault();
            if (scar == null) return false;
            pawn.health.RemoveHediff(scar);
            return true;
        }

        public static bool HealChronic(Pawn pawn)
        {
            if (!Eligible(pawn)) return false;
            Hediff condition = pawn.health.hediffSet.hediffs.FirstOrDefault(h => ChronicConditions.Contains(h.def.defName)
                && h.def.modContentPack?.IsOfficialMod != false && !(h is Hediff_AddedPart) && !ProtectedPart(pawn, h.Part));
            if (condition == null) return false;
            pawn.health.RemoveHediff(condition);
            return true;
        }

        public static bool IsOrdinaryAddiction(Hediff hediff) => hediff is Hediff_Addiction addiction
            && addiction.Chemical != null && DetoxChemicals.Contains(addiction.Chemical.defName)
            && hediff.def.modContentPack?.IsOfficialMod != false;

        public static bool ReduceAddiction(Pawn pawn, string targetDefName, float amount)
        {
            if (!Eligible(pawn) || amount <= 0 || string.IsNullOrEmpty(targetDefName)) return false;
            Hediff addiction = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def.defName == targetDefName && IsOrdinaryAddiction(h));
            if (addiction == null) return false;
            if (addiction.Severity <= amount + 0.00001f) pawn.health.RemoveHediff(addiction);
            else addiction.Severity -= amount;
            return true;
        }

        public static bool ProtectedPart(Pawn pawn, BodyPartRecord part)
        {
            if (part == null) return false;
            return pawn.health.hediffSet.hediffs.Any(h => h.Part != null
                && (h is Hediff_AddedPart || h is Hediff_Implant || h.def.defName == "VPE_Sacrificed")
                && (AncestorOrSelf(h.Part, part) || AncestorOrSelf(part, h.Part)));
        }

        public static bool AncestorOrSelf(BodyPartRecord ancestor, BodyPartRecord part)
        {
            for (BodyPartRecord current = part; current != null; current = current.parent)
                if (current == ancestor) return true;
            return false;
        }

        public static BodyPartRecord LimbCandidate(Pawn pawn)
        {
            if (!Eligible(pawn)) return null;
            return pawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>()
                .Select(h => h.Part).Where(p => p != null && p.parent != null && p.depth == BodyPartDepth.Outside
                    && p.def.tags != null && p.def.tags.Any(t => LimbTags.Contains(t.defName))
                    && !pawn.health.hediffSet.PartIsMissing(p.parent) && !ProtectedPart(pawn, p))
                .OrderBy(p => Depth(p)).ThenBy(p => p.Index).FirstOrDefault();
        }

        private static int Depth(BodyPartRecord part)
        { int result = 0; for (; part.parent != null; part = part.parent) result++; return result; }

        public static bool RestoreLimb(Pawn pawn)
        {
            BodyPartRecord part = LimbCandidate(pawn);
            if (part == null) return false;
            pawn.health.RestorePart(part);
            return true;
        }

        public static long RejuvenatedAge(long ageTicks, float years)
        {
            long minimum = 25 * TicksPerYear;
            if (ageTicks <= minimum || years <= 0 || float.IsNaN(years) || float.IsInfinity(years)) return ageTicks;
            return Math.Max(minimum, ageTicks - (long)(Math.Min(years, 10000f) * TicksPerYear));
        }

        public static bool Rejuvenate(Pawn pawn, float years)
        {
            if (!Eligible(pawn) || pawn.ageTracker == null || !pawn.RaceProps.Humanlike) return false;
            long age = Math.Max(pawn.ageTracker.AdultMinAgeTicks, RejuvenatedAge(pawn.ageTracker.AgeBiologicalTicks, years));
            if (age > pawn.ageTracker.AgeBiologicalTicks) return false;
            if (age == pawn.ageTracker.AgeBiologicalTicks) return false;
            pawn.ageTracker.AgeBiologicalTicks = age;
            return true;
        }
    }

    [HarmonyPatch(typeof(Recipe_AdministerIngestible), nameof(Recipe_AdministerIngestible.AvailableOnNow))]
    public static class AdministrationAvailability
    {
        public static void Postfix(Recipe_AdministerIngestible __instance, Thing thing, ref bool __result)
        {
            if (!__result || !(thing is Pawn pawn) || __instance.recipe?.ingredients == null) return;
            foreach (var ingredient in __instance.recipe.ingredients)
                foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                    if (def.ingestible?.outcomeDoers != null)
                        foreach (var outcome in def.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_Pharmacy>())
                            if (!PharmacyRules.CanApply(pawn, outcome.action)) { __result = false; return; }
        }
    }

    public sealed class CompProperties_Treatment : HediffCompProperties
    {
        public string action = "regeneration";
        public int intervalTicks = 2500;
        public float amount = 2f;
        public int cycles = 24;
        public CompProperties_Treatment() { compClass = typeof(HediffComp_Treatment); }
    }

    public sealed class HediffComp_Treatment : HediffComp
    {
        private int elapsedTicks;
        private int completedCycles;
        private string targetAddiction;
        private bool selectedAddiction;
        private bool specialTreatmentDone;
        public int CompletedCycles => completedCycles;
        public int ElapsedTicks => elapsedTicks;
        public string TargetAddiction => targetAddiction;
        private CompProperties_Treatment Settings => (CompProperties_Treatment)props;

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref elapsedTicks, "pharmacyElapsedTicks", 0);
            Scribe_Values.Look(ref completedCycles, "pharmacyCompletedCycles", 0);
            Scribe_Values.Look(ref targetAddiction, "pharmacyTargetAddiction");
            Scribe_Values.Look(ref selectedAddiction, "pharmacySelectedAddiction", false);
            Scribe_Values.Look(ref specialTreatmentDone, "pharmacySpecialTreatmentDone", false);
        }

        public void RestartCompletedCourse()
        {
            if (completedCycles < Math.Max(1, Settings.cycles)) return;
            elapsedTicks = 0; completedCycles = 0; targetAddiction = null;
            selectedAddiction = false; specialTreatmentDone = false;
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        { Advance(delta); }

        public void Advance(int delta)
        {
            if (delta <= 0 || !PharmacyRules.Eligible(Pawn) || completedCycles >= Math.Max(1, Settings.cycles)) return;
            elapsedTicks = Math.Min(int.MaxValue - delta, elapsedTicks) + delta;
            int interval = Math.Max(1, Settings.intervalTicks);
            while (elapsedTicks >= interval && completedCycles < Math.Max(1, Settings.cycles))
            {
                elapsedTicks -= interval;
                completedCycles++;
                if (Settings.action == "detox")
                {
                    if (!selectedAddiction)
                    {
                        targetAddiction = Pawn.health.hediffSet.hediffs.Where(PharmacyRules.IsOrdinaryAddiction)
                            .OrderBy(h => h.def.defName, StringComparer.Ordinal).Select(h => h.def.defName).FirstOrDefault();
                        selectedAddiction = true;
                    }
                    PharmacyRules.ReduceAddiction(Pawn, targetAddiction, Settings.amount);
                }
                else if (Settings.action == "regeneration" || Settings.action == "comprehensive")
                {
                    PharmacyRules.HealFreshInjury(Pawn, Settings.amount);
                    if (Settings.action == "comprehensive" && !specialTreatmentDone)
                    {
                        // The powerful serum cures one old condition per dose, not one every tick.
                        specialTreatmentDone = PharmacyRules.HealScar(Pawn) || PharmacyRules.HealChronic(Pawn);
                    }
                }
            }
        }
    }

    public sealed class CompProperties_Aftereffect : HediffCompProperties
    {
        public HediffDef effect;
        public int durationTicks = 15000;
        public CompProperties_Aftereffect() { compClass = typeof(HediffComp_Aftereffect); }
    }

    public sealed class HediffComp_Aftereffect : HediffComp
    {
        public override void CompPostPostRemoved()
        {
            var expires = parent.TryGetComp<HediffComp_Disappears>();
            var settings = (CompProperties_Aftereffect)props;
            if (expires != null && expires.ticksToDisappear <= 0)
                PharmacyRules.AddOrRefresh(Pawn, settings.effect, settings.durationTicks);
        }
    }
}
