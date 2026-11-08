using Grechka.WorldIntegration;
using RimWorld;
using Verse;

internal static class BalanceApprovalChecks
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    internal static void Run(ModContentPack core)
    {
        var steel = DefDatabase<ThingDef>.GetNamed("Steel");
        var machine = new ThingDef { defName = "BalanceMigratedMachine", modContentPack = core, thingClass = typeof(Building),
            costList = new() { new(steel, 100) } };
        DefDatabase<ThingDef>.Add(machine);
        string previous = "ludeon.rimworld/BalanceMigratedMachine/2/True/Steel=100";
        var rule = new MachineProfileRuleDef { defName = "balance_migration_fixture", packageId = "ludeon.rimworld", targetDefName = machine.defName,
            minimumTier = 2, addHousing = true, replacementCost = new() { new NamedCost { thingDefName = "Steel", count = 20 } },
            previousSignatures = new() { previous } };
        DefDatabase<MachineProfileRuleDef>.Add(rule); WorldProfile.Capture();
        var state = new WorldProfileState(null) { applied = true, approvedMachines = new() { previous, previous + "/variantT4" } };
        WorldProfile.Apply(state, false);
        Check(machine.costList.Single(c => c.thingDef == steel).count == 20 && WorldProfile.IsApplied(machine),
            "Exact saved price approval migrates to the reduced balance without requiring reapplication");
        Check(state.approvedMachines.Contains(rule.Signature + "/variantT4") && state.approvedMachines.Contains(previous + "/variantT4")
            && !state.approvedMachines.Contains(rule.Signature + "/variantT5"),
            "Migration preserves history and only transfers already-approved variant tiers");
        var unknown = new WorldProfileState(null) { applied = true, approvedMachines = new() { previous + "0" } };
        WorldProfile.Apply(unknown, false);
        Check(machine.costList.Single(c => c.thingDef == steel).count == 100 && !WorldProfile.IsApplied(machine),
            "A similar but unknown signature cannot approve the new price");
        var fresh = new WorldProfileState(null) { approvedMachines = new() { previous } };
        WorldProfile.Apply(fresh, false);
        Check(!fresh.approvedMachines.Contains(rule.Signature) && !WorldProfile.IsApplied(machine),
            "An unapproved colony is not opted into the World profile by balance migration");
        WorldProfile.RestoreBaseline();
    }
}
