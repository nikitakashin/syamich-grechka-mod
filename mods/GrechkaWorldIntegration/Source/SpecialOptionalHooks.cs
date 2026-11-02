using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Grechka.WorldIntegration
{
    public static class SpecialOptional
    {
        public const string Fortified="Fortified.Building_WorkTableAutonomous";
        public const string Farmer="VanillaFurnitureExpandedFactory.Building_Autofarmer";
        public const string Grinder="VNPE.Building_NutrientGrinder";
        public static MethodBase Method(string type,string method)=>AccessTools.TypeByName(type) is Type t?AccessTools.Method(t,method):null;
        public static int FastInterval(int ticks,object owner)=>Math.Max(1,(int)Math.Ceiling(ticks/SpecialTierAdapter.Speed(SpecialProduction.Machine(owner))));
        public static float PasteOutput(float original,object owner)=>original*(1+SpecialTierAdapter.Output(SpecialProduction.Machine(owner)));
        public static int HarvestOutput(int original,object owner)=>SpecialProduction.Amount(SpecialProduction.Machine(owner),"autoHarvest",original);
    }
    [HarmonyPatch]
    public static class SpecialFortifiedClockHook
    {
        public static bool Prepare()=>SpecialOptional.Method(SpecialOptional.Fortified,"TickInterval")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method(SpecialOptional.Fortified,"TickInterval");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Counter(instructions,AccessTools.Field(AccessTools.TypeByName(SpecialOptional.Fortified),"curWorkAmount"),true);
    }
    [HarmonyPatch]
    public static class SpecialAutofarmerClockHook
    {
        public static bool Prepare()=>SpecialOptional.Method(SpecialOptional.Farmer,"Tick")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method(SpecialOptional.Farmer,"Tick");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Counter(instructions,AccessTools.Field(AccessTools.TypeByName(SpecialOptional.Farmer),"currentOffset"),true,2);
    }
    [HarmonyPatch]
    public static class SpecialAutofarmerYieldHook
    {
        public static bool Prepare()=>SpecialOptional.Method(SpecialOptional.Farmer,"ProcessRow")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method(SpecialOptional.Farmer,"ProcessRow");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.Calls(AccessTools.Method(typeof(RimWorld.Plant),nameof(RimWorld.Plant.YieldNow))))
                {yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialOptional),nameof(SpecialOptional.HarvestOutput));}
            }
        }
    }
    [HarmonyPatch]
    public static class SpecialGrinderClockHook
    {
        public static bool Prepare()=>SpecialOptional.Method(SpecialOptional.Grinder,"Tick")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method(SpecialOptional.Grinder,"Tick");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int changed=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.LoadsConstant(400)){changed++;yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialOptional),nameof(SpecialOptional.FastInterval));}
            }
            if(changed!=1)throw new InvalidOperationException("VNPE grinder interval API changed");
        }
    }
    [HarmonyPatch]
    public static class SpecialGrinderRecipeHook
    {
        public static bool Prepare()=>SpecialOptional.Method(SpecialOptional.Grinder,"TryProducePaste")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method(SpecialOptional.Grinder,"TryProducePaste");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach(var instruction in SpecialProduction.Nutrition(instructions))
            {
                yield return instruction;
                if(instruction.opcode==OpCodes.Ldc_R4&&(float)instruction.operand==1f)
                {yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialOptional),nameof(SpecialOptional.PasteOutput));}
            }
        }
    }
    [HarmonyPatch]
    public static class SpecialGrinderAvailabilityHook
    {
        public static bool Prepare()=>SpecialOptional.Method(SpecialOptional.Grinder,"HasEnoughFeed")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method(SpecialOptional.Grinder,"HasEnoughFeed");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Nutrition(instructions);
    }
    [HarmonyPatch]
    public static class SpecialRepairSpeedHook
    {
        public static bool Prepare()=>SpecialOptional.Method("MFSpacer.CompRepairStored","TickInterval")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method("MFSpacer.CompRepairStored","TickInterval");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var field=AccessTools.Field(AccessTools.TypeByName("MFSpacer.CompProperties_RepairStored"),"repairAmount");
            int changes=0;
            foreach(var instruction in instructions){yield return instruction;if(instruction.LoadsField(field)){changes++;yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialProduction),nameof(SpecialProduction.ScaleInt));}}
            if(changes!=1)throw new InvalidOperationException("Repair rack repairAmount API changed");
        }
    }
    [HarmonyPatch]
    public static class SpecialWarcasketWorkHook
    {
        public static bool Prepare()=>SpecialOptional.Method("VFEPirates.JobDriver_DoWelding","<MakeNewToils>b__3_5")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method("VFEPirates.JobDriver_DoWelding","<MakeNewToils>b__3_5");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getStat=AccessTools.Method(typeof(RimWorld.StatExtension),nameof(RimWorld.StatExtension.GetStatValue));
            var foundry=SpecialOptional.Method("VFEPirates.JobDriver_DoWelding","get_Foundry");int changes=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.Calls(getStat)){changes++;yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,foundry);yield return CodeInstruction.Call(typeof(SpecialProduction),nameof(SpecialProduction.ScaleFloat));}
            }
            if(changes!=1)throw new InvalidOperationException("Warcasket welding work API changed");
        }
    }
    [HarmonyPatch]
    public static class SpecialWarcasketInputHook
    {
        public static bool Prepare()=>SpecialOptional.Method("VFEPirates.Building_WarcasketFoundry","RequiredIngredients")!=null;
        public static MethodBase TargetMethod()=>SpecialOptional.Method("VFEPirates.Building_WarcasketFoundry","RequiredIngredients");
        public static void Postfix(Thing __instance,ref IEnumerable<IngredientCount> __result)
        {if(SpecialTierAdapter.Discount(__instance)>0)__result=Adjusted(__instance,__result);}
        private static IEnumerable<IngredientCount> Adjusted(Thing machine,IEnumerable<IngredientCount> ingredients)
        {
            foreach(var original in ingredients)
            {var clone=new IngredientCount{filter=original.filter};clone.SetBaseCount(SpecialProduction.Cost(original.GetBaseCount(),machine));yield return clone;}
        }
    }

}
