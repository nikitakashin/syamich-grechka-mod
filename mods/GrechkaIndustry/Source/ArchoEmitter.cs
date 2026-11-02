using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class LateSolarProperties : DefModExtension { public float nightFraction = .25f; }
    public sealed class CompPowerPlantLateSolar : CompPowerPlantSolar
    {
        private float RoofFactor()
        {
            int total = 0, clear = 0;
            foreach (var cell in parent.OccupiedRect()) { total++; if (!cell.Roofed(parent.Map)) clear++; }
            return total == 0 ? 0f : (float)clear / total;
        }
        protected override float DesiredPowerOutput
        {
            get
            {
                float fraction = parent.def.GetModExtension<LateSolarProperties>()?.nightFraction ?? .25f;
                return Math.Max(base.DesiredPowerOutput, -Props.PowerConsumption * fraction * RoofFactor());
            }
        }
    }
    public sealed class CompProperties_ArchoEmitter : CompProperties
    {
        public string role = "Acceleration";
        public int tier = 4;
        public float radius = 20;
        public CompProperties_ArchoEmitter() { compClass = typeof(CompArchoEmitter); }
    }
    public sealed class CompArchoEmitter : ThingComp
    {
        public CompProperties_ArchoEmitter Settings => (CompProperties_ArchoEmitter)props;
        public bool Active => parent.Spawned && parent.GetComp<CompPowerTrader>()?.PowerOn == true
            && parent.GetComp<CompFlickable>()?.SwitchIsOn != false
            && parent.GetComp<CompBreakdownable>()?.BrokenDown != true;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        { base.PostSpawnSetup(respawningAfterLoad); parent.Map.GetComponent<ArchoEmitterMap>().Register(this); }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        { map.GetComponent<ArchoEmitterMap>().Unregister(this); base.PostDeSpawn(map, mode); }
        public override string CompInspectStringExtra() => "Радиус: " + Settings.radius + "; " + (Active ? "работает" : "отключён");
    }
    public sealed class ArchoEmitterMap : MapComponent
    {
        private readonly HashSet<CompArchoEmitter> devices = new HashSet<CompArchoEmitter>();
        public ArchoEmitterMap(Map map) : base(map) { }
        public void Register(CompArchoEmitter device) => devices.Add(device);
        public void Unregister(CompArchoEmitter device) => devices.Remove(device);
        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 != 0 || devices.Count == 0) return;
            var active = devices.Where(d => d.Active).ToArray();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                int acceleration = 0, suppression = 0, wellbeing = 0;
                foreach (var device in active)
                {
                    var p = device.Settings;
                    if (!pawn.Position.InHorDistOf(device.parent.Position, p.radius)) continue;
                    bool own = pawn.Faction != null && pawn.Faction == device.parent.Faction;
                    if (p.role == "Suppression" && pawn.HostileTo(device.parent)) suppression = Math.Max(suppression, p.tier);
                    if (p.role == "Acceleration" && own) acceleration = Math.Max(acceleration, p.tier);
                    if (p.role == "Wellbeing" && own) wellbeing = Math.Max(wellbeing, p.tier);
                }
                Apply(pawn, "Grechka_ArchoAcceleration", acceleration);
                Apply(pawn, "Grechka_ArchoSuppression", suppression);
                if (wellbeing > 0)
                {
                    float restore = wellbeing >= 5 ? .008f : .002f;
                    if (pawn.needs?.food != null) pawn.needs.food.CurLevel += restore * pawn.needs.food.MaxLevel;
                    if (pawn.needs?.rest != null) pawn.needs.rest.CurLevel += restore * pawn.needs.rest.MaxLevel;
                }
            }
        }
        private static void Apply(Pawn pawn, string name, int tier)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamed(name);
            var existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (tier == 0) { if (existing != null) pawn.health.RemoveHediff(existing); return; }
            if (existing == null) existing = pawn.health.AddHediff(def);
            existing.Severity = tier == 5 ? 1f : .5f;
            var expires = existing.TryGetComp<HediffComp_Disappears>();
            if (expires != null) expires.ticksToDisappear = 120;
        }
    }
}
