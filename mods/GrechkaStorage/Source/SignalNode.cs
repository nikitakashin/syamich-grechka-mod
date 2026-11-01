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
            return "Контроллеров в сети: " + controllers.Count + "; включено: " + active
                + (active == 0 ? "\nСеть недоступна: нет работающего контроллера" : "\nСигнальная сеть доступна");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!IsController || manager == null || !parent.Spawned || parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Открыть сеть",
                defaultDesc = "Подключённые устройства и состояние сигнальной сети.",
                icon = parent.def.uiIcon,
                action = () => Find.WindowStack.Add(new Window_SignalNetwork(this))
            };
        }
    }

    internal static class DeviceStatus
    {
        internal static string Reason(ThingWithComps thing)
        {
            if (!thing.Spawned) return "Не установлено";
            if (thing.Faction != Faction.OfPlayer) return "Не принадлежит колонии";
            if (thing.GetComp<CompBreakdownable>()?.BrokenDown == true) return "Поломка";
            if (thing.GetComp<CompFlickable>()?.SwitchIsOn == false) return "Выключено";
            if (thing.GetComp<CompStunnable>()?.StunHandler?.Stunned == true) return "Оглушено";
            CompPowerTrader power = thing.GetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return "Нет питания";
            return null;
        }
        internal static bool Ready(ThingWithComps thing) { return Reason(thing) == null; }
    }

    public sealed class Window_SignalNetwork : Window
    {
        private readonly CompSignalNode controller;
        private Vector2 scroll;
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
            Widgets.Label(new Rect(0, 0, rect.width - 35, 35), "Grechka Storage — сигнальная сеть");
            Text.Font = GameFont.Small;
            if (!controller.parent.Spawned || controller.parent.Faction != Faction.OfPlayer || controller.Manager == null)
            {
                Widgets.Label(new Rect(0, 45, rect.width, 55), "Контроллер больше недоступен колонии.");
                return;
            }
            SignalNetworkMap manager = controller.Manager;
            NetworkView view = manager.ViewFor(controller);
            List<CompSignalNode> controllers = manager.ControllersIn(view.NetworkId);
            bool online = controllers.Any(c => DeviceStatus.Ready(c.parent));
            Widgets.Label(new Rect(0, 42, rect.width, 30), (online ? "Сеть доступна" : "Нет работающего контроллера")
                + "   |   Контроллеров: " + controllers.Count + "   |   Кабелей: " + view.CableCount
                + "   |   Устройств: " + view.Devices.Count);
            Widgets.Label(new Rect(0, 77, rect.width, 50), "Сигнальный кабель соединяет устройства. Электричество поступает по обычной электросети.");
            Widgets.DrawLineHorizontal(0, 132, rect.width);
            var outer = new Rect(0, 145, rect.width, rect.height - 150);
            var inner = new Rect(0, 0, outer.width - 20,
                Mathf.Max(outer.height, (view.Devices.Count + view.OtherBranchDevices.Count) * 46));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            float y = 0;
            foreach (Building machine in view.Devices.Concat(view.OtherBranchDevices))
            {
                if (!machine.Spawned || machine.Faction != Faction.OfPlayer) continue;
                Widgets.ThingIcon(new Rect(2, y + 3, 34, 34), machine);
                Widgets.Label(new Rect(45, y + 8, inner.width * 0.50f - 45, 30), machine.LabelCap);
                string reason = DeviceStatus.Reason(machine);
                bool otherBranch = view.OtherBranchDevices.Contains(machine);
                Widgets.Label(new Rect(inner.width * 0.50f, y + 8, inner.width * 0.34f, 30),
                    otherBranch ? "Другая сигнальная ветка" : reason ?? (online ? "Доступно" : "Сеть выключена"));
                if (otherBranch) TooltipHandler.TipRegion(new Rect(0, y, inner.width, 44),
                    "Устройство касается нескольких отдельных веток и обслуживается другой. Соедините ветки кабелем, чтобы получить общую сеть.");
                if (Widgets.ButtonText(new Rect(inner.width - 102, y + 5, 100, 32), "Показать"))
                    CameraJumper.TryJumpAndSelect(machine);
                y += 46;
            }
            Widgets.EndScrollView();
        }
    }
}
