using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Storage
{
    public sealed class CompProperties_SignalNode : CompProperties
    {
        public bool controller;
        public bool cable;
        public CompProperties_SignalNode() { compClass = typeof(CompSignalNode); }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (controller == cable) yield return "Signal node must be either a controller or cable.";
        }
    }

    public sealed class CompSignalNode : ThingComp
    {
        private SignalNetworkMap manager;
        public bool IsController { get { return ((CompProperties_SignalNode)props).controller; } }
        public bool IsCable { get { return ((CompProperties_SignalNode)props).cable; } }
        public SignalNetworkMap Manager { get { return manager; } }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (parent.def.IsBlueprint || parent.def.IsFrame) return;
            manager = parent.Map.GetComponent<SignalNetworkMap>();
            manager.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            manager?.Unregister(this); manager = null;
            base.PostDeSpawn(map, mode);
        }

        public override string CompInspectStringExtra()
        {
            if (!IsController || manager == null) return null;
            List<CompSignalNode> controllers = manager.ControllersIn(manager.NetworkOf(parent));
            int active = controllers.Count(c => DeviceStatus.Ready(c.parent));
            return StorageText.Get("GrechkaStorage_Text037") + controllers.Count + StorageText.Get("GrechkaStorage_Text038") + active
                + (active == 0 ? StorageText.Get("GrechkaStorage_Text039") : StorageText.Get("GrechkaStorage_Text040"));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!IsController || manager == null || !parent.Spawned || parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = StorageText.Get("GrechkaStorage_Text041"),
                defaultDesc = StorageText.Get("GrechkaStorage_Text042"),
                icon = parent.def.uiIcon,
                action = () => Find.WindowStack.Add(new Window_SignalNetwork(this))
            };
        }
    }

    internal static class DeviceStatus
    {
        internal static string Reason(ThingWithComps thing)
        {
            if (!thing.Spawned) return StorageText.Get("GrechkaStorage_Text043");
            if (thing.Faction != Faction.OfPlayer) return StorageText.Get("GrechkaStorage_Text044");
            if (thing.GetComp<CompBreakdownable>()?.BrokenDown == true) return StorageText.Get("GrechkaStorage_Text045");
            if (thing.GetComp<CompFlickable>()?.SwitchIsOn == false) return StorageText.Get("GrechkaStorage_Text046");
            if (thing.GetComp<CompStunnable>()?.StunHandler?.Stunned == true) return StorageText.Get("GrechkaStorage_Text047");
            CompPowerTrader power = thing.GetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return StorageText.Get("GrechkaStorage_Text048");
            return null;
        }
        internal static bool Ready(ThingWithComps thing) { return Reason(thing) == null; }
    }

    public sealed class Window_SignalNetwork : Window
    {
        private readonly CompSignalNode controller;
        private Vector2 scroll;
        private int tab;
        private readonly StorageTerminalUI terminal = new StorageTerminalUI();
        private readonly AutocraftUI crafting = new AutocraftUI();
        public override Vector2 InitialSize { get { return new Vector2(850, 640); } }
        public Window_SignalNetwork(CompSignalNode controller)
        {
            this.controller = controller;
            doCloseX = true; draggable = true; resizeable = true;
            preventCameraMotion = false; absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, rect.width - 35, 35), "Grechka Storage");
            Text.Font = GameFont.Small;
            if (!controller.parent.Spawned || controller.parent.Faction != Faction.OfPlayer || controller.Manager == null)
            {
                Widgets.Label(new Rect(0, 45, rect.width, 55), StorageText.Get("GrechkaStorage_Text049"));
                return;
            }
            SignalNetworkMap manager = controller.Manager;
            NetworkView view = manager.ViewFor(controller);
            List<CompSignalNode> controllers = manager.ControllersIn(view.NetworkId);
            bool online = controllers.Any(c => DeviceStatus.Ready(c.parent));
            StorageArt.Icon(new Rect(0, 44, 24, 24), online ? "StorageNetworkOnline" : "StorageNoPower");
            Widgets.Label(new Rect(30, 42, rect.width - 30, 30), (online ? StorageText.Get("GrechkaStorage_Text050") : StorageText.Get("GrechkaStorage_Text051"))
                + StorageText.Get("GrechkaStorage_Text052") + controllers.Count + StorageText.Get("GrechkaStorage_Text053") + view.CableCount
                + StorageText.Get("GrechkaStorage_Text054") + view.Devices.Count);
            string[] tabs = { StorageText.Get("GrechkaStorage_Text055"), StorageText.Get("GrechkaStorage_Text056"), StorageText.Get("GrechkaStorage_Text057"), StorageText.Get("GrechkaStorage_Text058"), StorageText.Get("GrechkaStorage_Text059") };
            for (int i = 0; i < tabs.Length; i++)
                if (Widgets.ButtonText(new Rect(i * rect.width / tabs.Length, 81, rect.width / tabs.Length - 5, 32), (tab == i ? "• " : "") + tabs[i])) tab = i;
            if (tab == 4 && controller.parent is Building_StorageController craftController)
            { crafting.Draw(new Rect(0, 128, rect.width, rect.height - 133), craftController); return; }
            if (tab != 2)
            {
                if (controller.parent is Building_StorageController building)
                    terminal.Draw(new Rect(0, 128, rect.width, rect.height - 133), building, tab);
                else Widgets.Label(new Rect(0, 130, rect.width, 60), StorageText.Get("GrechkaStorage_Text060"));
                return;
            }
            Widgets.DrawLineHorizontal(0, 132, rect.width);
            var outer = new Rect(0, 145, rect.width, rect.height - 150);
            var inner = new Rect(0, 0, outer.width - 20,
                Mathf.Max(outer.height, (view.Devices.Count + view.OtherBranchDevices.Count) * 46));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                float y = 0;
                foreach (Building machine in view.Devices.Concat(view.OtherBranchDevices))
                {
                    if (!machine.Spawned || machine.Faction != Faction.OfPlayer) continue;
                    Widgets.ThingIcon(new Rect(2, y + 3, 34, 34), machine);
                    Widgets.Label(new Rect(45, y + 8, inner.width * 0.50f - 45, 30), machine.LabelCap);
                    string reason = DeviceStatus.Reason(machine);
                    bool otherBranch = view.OtherBranchDevices.Contains(machine);
                    StorageArt.Status(new Rect(inner.width * 0.50f, y + 8, inner.width * 0.50f - 110, 30),
                        otherBranch ? StorageText.Get("GrechkaStorage_Text061") : reason ?? (online ? StorageText.Get("GrechkaStorage_Text062") : StorageText.Get("GrechkaStorage_Text063")),
                        otherBranch ? "StorageNetworkBroken" : reason != null ? "StorageMachineUnavailable" : online ? "StorageNetworkOnline" : "StorageNoPower");
                    if (otherBranch) TooltipHandler.TipRegion(new Rect(0, y, inner.width, 44),
                        StorageText.Get("GrechkaStorage_Text064"));
                    if (Widgets.ButtonText(new Rect(inner.width - 102, y + 5, 100, 32), StorageText.Get("GrechkaStorage_Text065")))
                        CameraJumper.TryJumpAndSelect(machine);
                    y += 46;
                }
            }
            finally { Widgets.EndScrollView(); }
        }
    }
}
