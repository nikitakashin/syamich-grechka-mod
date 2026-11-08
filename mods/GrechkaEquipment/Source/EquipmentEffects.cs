using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Equipment
{
    // Visuals follow confirmed native events; they never apply additional damage.
    public static class EquipmentEffects
    {
        public static void Flash(string name, Vector3 position, Map map, float scale, float rotation = 0f)
        {
            if (map == null || !position.ToIntVec3().InBounds(map)) return;
            FleckDef effect = DefDatabase<FleckDef>.GetNamed(name);
            FleckCreationData data = FleckMaker.GetDataStatic(position, map, effect, scale);
            data.rotation = rotation;
            map.flecks.CreateFleck(data);
        }
    }

    public sealed class Verb_CryoShoot : Verb_Shoot
    {
        protected override bool TryCastShot()
        {
            bool fired = base.TryCastShot();
            if (fired && caster.Spawned)
            {
                Vector3 direction = currentTarget.CenterVector3 - caster.DrawPos;
                direction.y = 0f;
                EquipmentEffects.Flash("Grechka_CryoMuzzleFlash", caster.DrawPos + direction.normalized * 0.7f,
                    caster.Map, 0.8f, direction.AngleFlat());
            }
            return fired;
        }
    }

    public sealed class DroneBulletT5 : Bullet
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            Vector3 position = ExactPosition;
            bool show = !blockedByShield && hitThing != null;
            base.Impact(hitThing, blockedByShield);
            if (show) EquipmentEffects.Flash("Grechka_DroneEMPFlash", position, map, 1.25f);
        }
    }

    public sealed class Hediff_CryoSlow : HediffWithComps
    {
        // The native health tab asks StateIcon only while this Hediff is present.
        public override TextureAndColor StateIcon => ContentFinder<Texture2D>.Get("UI/FreezeStatus");
    }
}
