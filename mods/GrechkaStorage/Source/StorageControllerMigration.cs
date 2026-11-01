using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    public sealed partial class SignalNetworkMap
    {
        private readonly HashSet<Building> migrations = new HashSet<Building>();
        private void ScheduleMigration(Building building) { migrations.Add(building); }
        public override void MapComponentUpdate() { ProcessMigrations(); }
        public override void MapComponentTick()
        { ProcessMigrations(); }
        private void ProcessMigrations()
        {
            if (migrations.Count == 0) return;
            foreach (Building old in migrations.ToArray())
            {
                migrations.Remove(old);
                if (!old.Spawned || old.Map != map || old.GetType() != typeof(Building)) continue;
                IntVec3 position = old.Position; Rot4 rotation = old.Rotation;
                var retainedComps = old.AllComps.ToList();
                var replacement = (Building_StorageController)ThingMaker.MakeThing(old.def, old.Stuff);
                replacement.SetFaction(old.Faction);
                replacement.HitPoints = old.HitPoints;
                try
                {
                    old.DeSpawn(DestroyMode.Vanish);
                    old.AllComps.Clear(); replacement.AllComps.Clear();
                    foreach (ThingComp comp in retainedComps) { comp.parent = replacement; replacement.AllComps.Add(comp); }
                    GenSpawn.Spawn(replacement, position, map, rotation);
                    old.Destroy(DestroyMode.Vanish);
                }
                catch (Exception error)
                {
                    if (replacement.Spawned) replacement.DeSpawn(DestroyMode.Vanish);
                    replacement.AllComps.Clear(); old.AllComps.Clear();
                    foreach (ThingComp comp in retainedComps) { comp.parent = old; old.AllComps.Add(comp); }
                    if (!old.Spawned && !old.Destroyed) GenSpawn.Spawn(old, position, map, rotation);
                    replacement.Destroy(DestroyMode.Vanish);
                    // A restored old building must not requeue an endless retry.
                    migrations.Remove(old);
                    Log.Error("[Grechka Storage] Old diagnostic controller retained after migration failure: " + error);
                }
            }
        }
    }
}
