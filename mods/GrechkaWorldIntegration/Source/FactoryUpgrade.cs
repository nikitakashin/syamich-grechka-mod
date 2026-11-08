using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Grechka.WorldIntegration
{
    public sealed class CompProperties_FactoryUpgrade : CompProperties
    { public CompProperties_FactoryUpgrade() { compClass = typeof(CompFactoryUpgrade); } }

    public sealed class CompFactoryUpgrade : ThingComp, IThingHolder
    {
        private ThingOwner<Thing> supplied;
        private bool requested;
        private bool applied;
        private float workLeft = 4000;
        private string fault;
        private ThingDef costsFor;
        private List<ThingDefCountClass> cachedCosts;
        private List<ThingDefCountClass> lockedCosts;
        private ThingDef lockedTarget;
        public bool Requested => requested;
        public ThingOwner<Thing> Supplies => supplied ??= new ThingOwner<Thing>(this, false);
        public ThingOwner GetDirectlyHeldThings() => Supplies;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, Supplies);
        public VariantEntry Entry => ProfileVariants.Entry(parent.def);
        public ThingDef Target => requested && lockedTarget != null ? lockedTarget : Entry == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(Entry.TargetName);
        public override void PostExposeData()
        {
            Scribe_Defs.Look(ref lockedTarget, "grechkaFactoryUpgradeTarget");
            Scribe_Collections.Look(ref lockedCosts, "grechkaFactoryUpgradeCosts", LookMode.Deep);
            Scribe_Deep.Look(ref supplied, "grechkaFactoryUpgradeSupplies", this);
            Scribe_Values.Look(ref requested, "grechkaFactoryUpgradeRequested");
            Scribe_Values.Look(ref applied, "grechkaFactoryUpgradeApplied");
            Scribe_Values.Look(ref workLeft, "grechkaFactoryUpgradeWork", 4000);
            Scribe_Values.Look(ref fault, "grechkaFactoryUpgradeFault");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) supplied ??= new ThingOwner<Thing>(this, false);
        }
        public List<ThingDefCountClass> Costs()
        {
            if (requested && lockedCosts != null) return lockedCosts;
            if (Entry == null || Entry.tier >= 5) return null;
            if (costsFor == parent.def && cachedCosts != null) return cachedCosts;
            string nextHousing = "Grechka_HousingT" + (Entry.tier + 1) + "_Item";
            string oldHousing = "Grechka_HousingT" + Entry.tier + "_Item";
            RecipeDef recipe = DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(r => r.products?.Any(p => p.thingDef.defName == nextHousing) == true);
            if (recipe == null) return null;
            var costs = new List<ThingDefCountClass>();
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                var allowed = ingredient.filter.AllowedThingDefs.ToList();
                // These owned Industry housing recipes deliberately use explicit materials.
                if (allowed.Count != 1) return null;
                ThingDef def = allowed[0]; if (def.defName == oldHousing) continue;
                int count = ingredient.CountRequiredOfFor(def, recipe);
                if (count > 0) costs.Add(new ThingDefCountClass(def, count));
            }
            costsFor = parent.def;
            return cachedCosts = costs.GroupBy(c => c.thingDef).Select(g => new ThingDefCountClass(g.Key, g.Sum(c => c.count))).ToList();
        }
        public int Need(ThingDef def) => Math.Max(0, (Costs()?.Where(c => c.thingDef == def).Sum(c => c.count) ?? 0) - Supplies.Where(t => t.def == def && !t.Destroyed).Sum(t => t.stackCount));
        public bool HasAll => Costs() is List<ThingDefCountClass> costs && costs.All(c => Need(c.thingDef) == 0);
        public bool Queue()
        {
            if (requested || Target == null || !ResearchTree.IsResearched(Target) || Costs() == null || !WorldProfile.IsApplied(parent.def) || !WorldProfile.IsApplied(Target)) return false;
            lockedTarget = Target; lockedCosts = Costs().Select(c => new ThingDefCountClass(c.thingDef, c.count)).ToList();
            requested = true; workLeft = 4000; fault = null; return true;
        }
        public bool Cancel()
        {
            if (applied) return FinishPayment();
            if (!parent.Spawned || Supplies.Any && !Supplies.TryDropAll(parent.Position, parent.Map, ThingPlaceMode.Near)) return false;
            requested = false; lockedCosts = null; lockedTarget = null; workLeft = 4000; fault = null; return true;
        }
        public int Accept(Thing item)
        {
            if (!requested || item == null || item.Destroyed) return 0;
            int count = Math.Min(item.stackCount, Need(item.def)); if (count <= 0) return 0;
            ThingOwner previous = item.holdingOwner; if (previous == null) return 0;
            return previous.TryTransferToContainer(item, Supplies, count, false);
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || !parent.Spawned) yield break;
            if (requested)
                yield return new Command_Action { defaultLabel = applied ? WorldText.Get("GrechkaWorldIntegration_42d6a97215d6", "Finish material payment") : WorldText.Get("GrechkaWorldIntegration_82d998ae2a80", "Cancel upgrade"), defaultDesc = applied ? WorldText.Get("GrechkaWorldIntegration_5596baf68f49", "Finish consuming retained materials for the already upgraded machine.") : WorldText.Get("GrechkaWorldIntegration_7146b1cba941", "Return delivered materials next to the machine."), icon = TexCommand.ClearPrioritizedWork, action = () => Cancel() };
            else if (Entry != null && Entry.tier < 5 && WorldProfile.IsApplied(parent.def) && ResearchTree.IsResearched(Target))
                yield return new Command_Action { defaultLabel = WorldText.Get("GrechkaWorldIntegration_392bededaba5", "Upgrade to T") + (Entry.tier + 1), defaultDesc = WorldText.Get("GrechkaWorldIntegration_b4939dc26eee", "A pawn will bring the additional materials and perform 4000 units of construction work. The previous-tier housing counts towards the cost. Finish the current batch and empty the internal containers.\n") + string.Join("; ", Costs()?.Select(c => c.Summary) ?? new[] { WorldText.Get("GrechkaWorldIntegration_4c9025be0c8d", "No recipe for the next housing") }), icon = TexCommand.Install, action = () => Queue() };
        }
        public override string CompInspectStringExtra()
        {
            if (!requested) return null;
            string missing = string.Join(", ", (Costs() ?? new()).Where(c => Need(c.thingDef) > 0).Select(c => c.thingDef.LabelCap + " ×" + Need(c.thingDef)));
            return WorldText.Get("GrechkaWorldIntegration_5b0413d994b4", "Upgrade: ") + (WorldText.Fault(fault) ?? (missing.Length > 0 ? WorldText.Get("GrechkaWorldIntegration_44451ecf7070", "requires ") + missing : FactoryUpgradeSafety.Problem(parent) ?? WorldText.Get("GrechkaWorldIntegration_b66a61548456", "work remaining ") + Math.Ceiling(workLeft)));
        }
        public bool Work(Pawn pawn)
        {
            if (applied) return FinishPayment();
            if (!requested || !HasAll || FactoryUpgradeSafety.Problem(parent) != null) return false;
            workLeft = Math.Max(0, workLeft - Math.Max(.01f, pawn.GetStatValue(StatDefOf.ConstructionSpeed)));
            pawn.skills?.Learn(SkillDefOf.Construction, .05f);
            if (workLeft <= 0) Complete();
            return true;
        }
        internal bool Complete()
        {
            if (applied) return FinishPayment();
            ThingDef next = Target;
            if (!requested || !HasAll || next == null || !parent.Spawned || FactoryUpgradeSafety.Problem(parent) != null) return false;
            if (!FactoryUpgradeSafety.Compatible(parent, next)) { fault = "GrechkaWorldIntegration_355c0d15c28d"; return false; }
            ThingDef old = parent.def; var oldProps = parent.AllComps.Select(c => c.props).ToList();
            var barrelField = parent.GetType().FullName == "VFED.Building_TurretGunBarrels" ? AccessTools.Field(parent.GetType(), "barrelIndex") : null;
            object barrelIndex = barrelField?.GetValue(parent);
            object turretTarget = parent is Building_TurretGun ? AccessTools.Field(typeof(Building_TurretGun), "currentTargetInt").GetValue(parent) : null;
            object turretWarmup = parent is Building_TurretGun ? AccessTools.Field(typeof(Building_TurretGun), "burstWarmupTicksLeft").GetValue(parent) : null;
            void RestoreTurret() { if (barrelField != null) barrelField.SetValue(parent, barrelIndex); if (turretTarget != null) { AccessTools.Field(typeof(Building_TurretGun), "currentTargetInt").SetValue(parent, turretTarget); AccessTools.Field(typeof(Building_TurretGun), "burstWarmupTicksLeft").SetValue(parent, turretWarmup); } }
            int hp = parent.HitPoints; float ratio = (float)hp / parent.MaxHitPoints;
            Map map = parent.Map; IntVec3 pos = parent.Position; Rot4 rotation = parent.Rotation;
            try
            {
                // WillReplace is important: real PipeSystem skips ResetProcess for this mode.
                parent.DeSpawn(DestroyMode.WillReplace);
                FactoryUpgradeSafety.SetDefinition(parent, next);
                GenSpawn.Spawn(parent, pos, map, rotation, WipeMode.Vanish, true, false);
                RestoreTurret();
                parent.HitPoints = Math.Max(1, (int)Math.Round(parent.MaxHitPoints * ratio));
                foreach (object process in FactoryUpgradeSafety.Processes(parent)) FactoryTierAdapter.SetInputs(process, false);
                applied = true;
            }
            catch (Exception error)
            {
                if (parent.Spawned) parent.DeSpawn(DestroyMode.WillReplace);
                parent.def = old;
                for (int i = 0; i < parent.AllComps.Count && i < oldProps.Count; i++) parent.AllComps[i].props = oldProps[i];
                AccessTools.Field(typeof(Thing), "graphicInt")?.SetValue(parent, null);
                WorldProfile.InvalidateThingStats(parent);
                if (!parent.Spawned) GenSpawn.Spawn(parent, pos, map, rotation, WipeMode.Vanish, true, false);
                RestoreTurret();
                parent.HitPoints = hp; fault = "GrechkaWorldIntegration_4c5487fe9857";
                Log.Error("[Grechka World] Factory upgrade rollback: " + error); return false;
            }
            return FinishPayment();
        }
        private bool FinishPayment()
        {
            try { Supplies.ClearAndDestroyContents(); requested = applied = false; lockedCosts = null; lockedTarget = null; workLeft = 4000; fault = null; return true; }
            catch (Exception error) { fault = "GrechkaWorldIntegration_d3cfbf414b70"; Log.Error("[Grechka World] Upgrade payment retained: " + error); return false; }
        }
        public override void CompTick() { if (applied && Find.TickManager.TicksGame % 250 == 0) FinishPayment(); }
        public override void CompTickRare() { if (applied) FinishPayment(); }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (applied) { Supplies.ClearAndDestroyContents(); applied = requested = false; }
            if (Supplies.Any && previousMap != null) Supplies.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near);
            base.PostDestroy(mode, previousMap);
        }
    }

    public static class FactoryUpgradeSafety
    {
        public static readonly List<Func<ThingWithComps, string>> AdditionalChecks = new List<Func<ThingWithComps, string>>();
        internal static object Get(object o, string name) => o == null ? null : AccessTools.Property(o.GetType(), name)?.GetValue(o) ?? AccessTools.Field(o.GetType(), name)?.GetValue(o);
        static IEnumerable<object> List(object o) => o is IEnumerable list ? list.Cast<object>() : Enumerable.Empty<object>();
        public static IEnumerable<object> Processes(ThingWithComps building) => building.AllComps.Where(c => c.GetType().FullName == "PipeSystem.CompAdvancedResourceProcessor").SelectMany(c => List(Get(Get(c, "ProcessStack"), "Processes")));
        public static bool BatchInProgress(object process) => Get(process, "PickUpReady") is true || Get(process, "currentResult") is int index && index > 0
            || Get(process, "Progress") is float progress && progress > 0
            || List(Get(process, "IngredientsOwners")).Any(o => Get(o, "Count") is int count && count > 0 || Get(o, "BeingFilled") is true);
        public static string Problem(ThingWithComps building)
        {
            if (building.GetType().FullName == "VFESecurity.Building_TurretSearchlight" && Get(building, "CurrentTarget") is LocalTargetInfo searchTarget && searchTarget.IsValid) return WorldText.Get("GrechkaWorldIntegration_a100b80d323d", "Switch off the active searchlight");
            if (building is Building_TurretGun turret && (Get(turret, "WarmingUp") is true || turret.AttackVerb?.state == VerbState.Bursting)) return WorldText.Get("GrechkaWorldIntegration_d70709676225", "Wait for the turret burst to finish");
            if (building.GetType().FullName == "VFEPirates.Building_WarcasketFoundry" && (Get(building, "occupant") != null || Get(building, "curWarcasketProject") != null)) return WorldText.Get("GrechkaWorldIntegration_5855b8353f80", "Finish the warcasket operation");
            if (building is Building_Storage && building.Spawned && building.OccupiedRect().Cells.Any(c => c.InBounds(building.Map) && c.GetThingList(building.Map).Any(t => t != building && t.def.category == ThingCategory.Item))) return WorldText.Get("GrechkaWorldIntegration_e8fac71eb272", "Empty the storage first");
            if (building is IThingHolder rootHolder && rootHolder.GetDirectlyHeldThings()?.Count > 0) return WorldText.Get("GrechkaWorldIntegration_cf0bf3a3379e", "Empty the machine contents");
            if (building.GetType().FullName == "VanillaFurnitureExpandedFactory.Building_Autofarmer" &&
                (Convert.ToInt32(Get(building, "state") ?? 0) != 0 || List(Get(building, "harvestStorageThings")).Any())) return WorldText.Get("GrechkaWorldIntegration_a393b6cc155e", "Finish the autofarmer operation and unload the harvest");
            if (building.GetType().Name == "Building_WorkTableAutonomous" && (Get(building, "prepared") is true || Get(building, "activeBill") != null)) return WorldText.Get("GrechkaWorldIntegration_bb94969daca1", "Finish the current automated batch");
            foreach (var check in AdditionalChecks) { string reason = check(building); if (reason != null) return reason; }
            foreach (object p in Processes(building))
                if (Get(p, "IsRunning") is true || BatchInProgress(p))
                    return WorldText.Get("GrechkaWorldIntegration_867c381e1e8a", "Finish the current factory batch");
            foreach (ThingComp comp in building.AllComps)
            {
                if (comp.GetType().FullName == "RimWorld.CompGenepackContainer" && List(Get(comp, "leftToLoad")).Any()) return WorldText.Get("GrechkaWorldIntegration_3d792257e41b", "Wait for genepack loading to finish");
                if (comp is IThingHolder holder && comp is not CompFactoryUpgrade && holder.GetDirectlyHeldThings()?.Count > 0)
                    return WorldText.Get("GrechkaWorldIntegration_7288d8fa58c5", "Empty the factory internal container");
            }
            return null;
        }
        public static bool Compatible(ThingWithComps building, ThingDef target) => target.thingClass == building.GetType() && target.size == building.def.size
            && building.AllComps.Select(c => c.GetType()).SequenceEqual((target.comps ?? new()).Select(c => c.compClass));
        public static void SetDefinition(ThingWithComps building, ThingDef target)
        {
            if (!Compatible(building, target)) throw new InvalidOperationException("Incompatible factory shape/components");
            building.def = target;
            for (int i = 0; i < building.AllComps.Count; i++) building.AllComps[i].props = target.comps[i];
            AccessTools.Field(typeof(Thing), "graphicInt")?.SetValue(building, null);
            WorldProfile.InvalidateThingStats(building);
        }
    }

    [HarmonyPatch]
    public static class FactoryUpgradeProcessPauseHook
    {
        static bool Prepare() => AccessTools.TypeByName("PipeSystem.Process") != null;
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("PipeSystem.Process"), "ShouldDoNow");
        public static bool Prefix(object __instance, ref bool __result)
        {
            var parent = FactoryUpgradeSafety.Get(__instance, "parent") as ThingWithComps;
            if (parent?.GetComp<CompFactoryUpgrade>()?.Requested != true || FactoryUpgradeSafety.BatchInProgress(__instance)) return true;
            __result = false; return false;
        }
    }

    public sealed class WorkGiver_FactoryUpgrade : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerBuildings.allBuildingsColonist.Where(b => b.GetComp<CompFactoryUpgrade>()?.Requested == true);
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) => JobOnThing(pawn, t, forced) != null;
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            var upgrade = (thing as ThingWithComps)?.GetComp<CompFactoryUpgrade>();
            if (upgrade?.Requested != true || thing.IsForbidden(pawn) || !pawn.CanReserveAndReach(thing, PathEndMode.Touch, Danger.Some)) return null;
            var costs = upgrade.Costs(); if (costs == null) return null;
            foreach (ThingDefCountClass cost in costs)
            {
                int need = upgrade.Need(cost.thingDef); if (need == 0) continue;
                Thing source = pawn.Map.listerThings.ThingsOfDef(cost.thingDef).Where(t => t.Spawned && !t.IsForbidden(pawn) && pawn.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Some))
                    .OrderBy(t => t.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
                if (source == null) continue;
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_DeliverFactoryUpgrade"), thing, source); job.count = Math.Min(need, source.stackCount); return job;
            }
            if (!upgrade.HasAll || FactoryUpgradeSafety.Problem((ThingWithComps)thing) != null) return null;
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_WorkFactoryUpgrade"), thing);
        }
    }
    public sealed class JobDriver_DeliverFactoryUpgrade : JobDriver
    {
        private CompFactoryUpgrade Upgrade => (job.targetA.Thing as ThingWithComps)?.GetComp<CompFactoryUpgrade>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed) && pawn.Reserve(job.targetB, job, 1, job.count, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A); this.FailOn(() => Upgrade?.Requested != true);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => Upgrade.Accept(pawn.carryTracker.CarriedThing));
        }
    }
    public sealed class JobDriver_WorkFactoryUpgrade : JobDriver
    {
        private CompFactoryUpgrade Upgrade => (job.targetA.Thing as ThingWithComps)?.GetComp<CompFactoryUpgrade>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A); this.FailOn(() => Upgrade?.Requested != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil work = ToilMaker.MakeToil(); work.defaultCompleteMode = ToilCompleteMode.Never;
            work.tickAction = () => { if (Upgrade == null || !Upgrade.Work(pawn)) EndJobWith(JobCondition.Incompletable); else if (!Upgrade.Requested) ReadyForNextToil(); };
            work.activeSkill = () => SkillDefOf.Construction;
            yield return work;
        }
    }
}
