using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Equipment
{
    public sealed class EquipmentMod : Mod
    {
        public EquipmentMod(ModContentPack content) : base(content) { new Harmony("grechka.equipment").PatchAll(); }
    }

    public sealed class ImplantFamily : DefModExtension
    {
        public string family;
        public bool stableMind;
        public bool structural;
    }

    public static class ImplantRules
    {
        public static bool SameFamily(HediffDef a, HediffDef b)
        {
            string family = a.GetModExtension<ImplantFamily>()?.family;
            return !string.IsNullOrEmpty(family) && family == b.GetModExtension<ImplantFamily>()?.family;
        }
        public static bool Stable(Pawn pawn) => pawn?.health?.hediffSet.hediffs.Any(h => h.def.GetModExtension<ImplantFamily>()?.stableMind == true) == true;
    }

    // Only the ordinary mood lottery is blocked; scripted mental states remain available.
    [HarmonyPatch(typeof(MentalBreaker), "TryDoRandomMoodCausedMentalBreak")]
    public static class StableMindHook
    {
        public static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (!ImplantRules.Stable(___pawn)) return true;
            __result = false;
            return false;
        }
    }

    public sealed class Recipe_InstallFamily : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
                if (recipe.appliedOnFixedBodyParts.Contains(part.def) &&
                    !pawn.health.hediffSet.hediffs.Any(h => h.Part == part && h.def == recipe.addsHediff) &&
                    !pawn.health.hediffSet.AncestorHasDirectlyAddedParts(part)) yield return part;
        }
        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn surgeon, List<Thing> ingredients, Bill bill)
        {
            if (part == null || pawn.health.hediffSet.PartIsMissing(part)) return;
            if (pawn.health.hediffSet.hediffs.Any(h => h.Part == part && h.def == recipe.addsHediff)) return;
            if (surgeon != null && CheckSurgeryFail(surgeon, pawn, ingredients, part, bill)) return;
            List<Hediff> old = pawn.health.hediffSet.hediffs.Where(h => h.Part == part && ImplantRules.SameFamily(h.def, recipe.addsHediff)).ToList();
            // Add first: reconstructing a vital part must never transiently remove its support.
            pawn.health.AddHediff(recipe.addsHediff, part);
            foreach (Hediff hediff in old)
            {
                pawn.health.RemoveHediff(hediff);
                if (hediff.def.spawnThingOnRemoved != null && pawn.MapHeld != null)
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(hediff.def.spawnThingOnRemoved), pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near);
            }
            if (surgeon != null) TaleRecorder.RecordTale(TaleDefOf.DidSurgery, surgeon, pawn);
        }
    }

    public sealed class ThoughtWorker_GrechkaChip : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p) => p.health.hediffSet.hediffs.Any(h => h.def.defName == "Grechka_BrainChipT5_Hediff");
    }

    // Removing a replacement limb must leave the limb missing, not regrow a natural one.
    public sealed class Recipe_RemoveProsthesis : Recipe_RemoveBodyPart
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            return pawn.health.hediffSet.hediffs.Where(h => h.def == recipe.removesHediff && h.Part != null)
                .Select(h => h.Part).Distinct();
        }
    }

    public sealed class Recipe_RemoveFamily : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
            => pawn.health.hediffSet.hediffs.Where(h => h.def == recipe.removesHediff && h.Part != null)
                .Select(h => h.Part).Distinct();

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn surgeon, List<Thing> ingredients, Bill bill)
        {
            Hediff implant = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.Part == part && h.def == recipe.removesHediff);
            if (implant == null || (surgeon != null && CheckSurgeryFail(surgeon, pawn, ingredients, part, bill))) return;
            pawn.health.RemoveHediff(implant);
            if (implant.def.spawnThingOnRemoved != null && pawn.MapHeld != null)
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(implant.def.spawnThingOnRemoved), pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near);
            if (surgeon != null)
            {
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, surgeon, pawn);
                if (IsViolationOnPawn(pawn, part, Faction.OfPlayer)) ReportViolation(pawn, surgeon, pawn.HomeFaction, -70);
            }
        }
    }

    public sealed class CryoBullet : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            base.Impact(hitThing, blockedByShield);
            Pawn pawn = hitThing as Pawn;
            if (blockedByShield || pawn == null || pawn.Dead) return;
            HediffDef chill = DefDatabase<HediffDef>.GetNamed("Grechka_CryoSlow");
            Hediff effect = pawn.health.hediffSet.GetFirstHediffOfDef(chill) ?? pawn.health.AddHediff(chill);
            HediffComp_Disappears timer = effect.TryGetComp<HediffComp_Disappears>();
            if (timer != null) timer.ticksToDisappear = 180;
        }
    }

    public sealed class DroneProperties : DefModExtension
    {
        public ThingDef projectile;
        public float range = 24f;
        public int cooldown = 105;
    }

    public sealed class CombatDrone : Apparel
    {
        public int nextShot;
        public int burstRemaining;
        public Thing target;
        public bool enabled = true;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextShot, "nextShot");
            Scribe_Values.Look(ref burstRemaining, "burstRemaining");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_References.Look(ref target, "droneTarget");
        }
        public override IEnumerable<Gizmo> GetWornGizmos()
        {
            foreach (Gizmo gizmo in base.GetWornGizmos()) yield return gizmo;
            if (Wearer?.Faction == Faction.OfPlayer) yield return new Command_Toggle {
                defaultLabel = "Огонь дрона", defaultDesc = "Самостоятельно атаковать видимых врагов. Дрон не атакует мирных животных и союзников.",
                isActive = () => enabled, toggleAction = () => { enabled = !enabled; target = null; burstRemaining = 0; }
            };
        }
        private bool ValidTarget(Thing candidate, Pawn wearer, DroneProperties props)
        {
            return candidate is Pawn pawn && pawn.Spawned && pawn.Map == wearer.Map && !pawn.Dead && !pawn.Downed &&
                pawn.HostileTo(wearer) && !pawn.IsPsychologicallyInvisible() &&
                wearer.Position.DistanceToSquared(pawn.Position) <= props.range * props.range &&
                GenSight.LineOfSight(wearer.Position, pawn.Position, wearer.Map);
        }
        public void DroneTick(int tick)
        {
            Pawn wearer = Wearer;
            if (!enabled || wearer == null || !wearer.Spawned || wearer.Dead || wearer.Downed || wearer.InMentalState) { target = null; burstRemaining = 0; return; }
            if (tick < nextShot) return;
            DroneProperties props = def.GetModExtension<DroneProperties>();
            if (!ValidTarget(target, wearer, props))
            {
                target = wearer.Map.mapPawns.AllPawnsSpawned.Where(p => ValidTarget(p, wearer, props)).OrderBy(p => p.Position.DistanceToSquared(wearer.Position)).FirstOrDefault();
                burstRemaining = 0;
            }
            if (target == null) { nextShot = tick + 60; return; }
            if (burstRemaining == 0) burstRemaining = 3;
            Projectile projectile = (Projectile)GenSpawn.Spawn(props.projectile, wearer.Position, wearer.Map);
            projectile.Launch(wearer, wearer.DrawPos, target, target, ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetWorld, true, this);
            burstRemaining--;
            nextShot = tick + (burstRemaining > 0 ? 7 : props.cooldown);
        }
    }

    public sealed class DroneMapComponent : MapComponent
    {
        private readonly List<CombatDrone> drones = new List<CombatDrone>();
        public DroneMapComponent(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick % 60 == 0)
            {
                drones.Clear();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (pawn.apparel != null) foreach (Apparel apparel in pawn.apparel.WornApparel)
                        if (apparel is CombatDrone drone) drones.Add(drone);
            }
            for (int i = 0; i < drones.Count; i++) if (!drones[i].Destroyed) drones[i].DroneTick(tick);
        }
    }

    public sealed class IncidentWorker_ChipRaid : IncidentWorker_RaidEnemy
    {
        public override bool FactionCanBeGroupSource(Faction faction, IncidentParms parms, bool desperate = false)
            => faction.def.humanlikeFaction && base.FactionCanBeGroupSource(faction, parms, desperate);
        protected override void PostProcessSpawnedPawns(IncidentParms parms, List<Pawn> pawns)
        {
            base.PostProcessSpawnedPawns(parms, pawns);
            HediffDef chip = DefDatabase<HediffDef>.GetNamed("Grechka_CombatChipT4_Hediff");
            foreach (Pawn pawn in pawns)
            {
                if (!pawn.RaceProps.Humanlike) continue;
                pawn.apparel?.DestroyAll();
                pawn.equipment?.DestroyAllEquipment();
                BodyPartRecord brain = pawn.health.hediffSet.GetBrain();
                if (brain != null && !pawn.health.hediffSet.HasHediff(chip)) pawn.health.AddHediff(chip, brain);
            }
        }
        protected override string GetLetterLabel(IncidentParms parms) => "Рейд чипированных нудистов";
        protected override string GetLetterText(IncidentParms parms, List<Pawn> pawns) => "К колонии приближаются чипированные бойцы. На них нет брони, зато боевые импланты подавляют боль и резко ускоряют движение и удары.\n\n" + base.GetLetterText(parms, pawns);
    }
}
