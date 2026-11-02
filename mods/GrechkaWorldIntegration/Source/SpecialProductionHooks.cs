using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    public sealed class SpecialProductionState : IExposable
    {
        public Dictionary<string,float> carry = new Dictionary<string,float>();
        public void ExposeData(){Scribe_Collections.Look(ref carry,"carry",LookMode.Value,LookMode.Value);carry??=new Dictionary<string,float>();}
    }
    public static class SpecialProduction
    {
        private static readonly ConditionalWeakTable<Thing,SpecialProductionState> states=new ConditionalWeakTable<Thing,SpecialProductionState>();
        public static Thing Machine(object value) => value is Thing t?t:value is ThingComp comp?comp.parent:value is Bill bill?bill.billStack?.billGiver as Thing:null;
        public static float ScaleFloat(float work,object owner)=>work*SpecialTierAdapter.Speed(Machine(owner));
        public static int ScaleInt(int work,object owner)=>(int)Math.Min(int.MaxValue,work*(double)SpecialTierAdapter.Speed(Machine(owner)));
        public static float Cost(float amount,object owner)=>amount*(1-SpecialTierAdapter.Discount(Machine(owner)));
        public static float WholeCost(float amount,object owner)=>amount<=0?amount:Math.Max(1,(float)Math.Ceiling(Cost(amount,owner)-.000001));
        public static int PeekAmount(Thing machine,string key,int original)
        {
            if(original<=0||SpecialTierAdapter.Output(machine)<=0)return original;
            var state=states.GetOrCreateValue(machine);
            double bonus=original*(double)SpecialTierAdapter.Output(machine)+(state.carry.TryGetValue(key,out float saved)?saved:0);
            return (int)Math.Min(int.MaxValue,original+Math.Floor(bonus+.000001));
        }
        public static int Amount(Thing machine,string key,int original)
        {
            if(original<=0||SpecialTierAdapter.Output(machine)<=0)return original;
            SpecialProductionState state=states.GetOrCreateValue(machine);
            double value=original*(double)SpecialTierAdapter.Output(machine)+(state.carry.TryGetValue(key,out float previous)?previous:0);
            int extra=(int)Math.Floor(value+.000001);state.carry[key]=(float)Math.Max(0,value-extra);
            return (int)Math.Min(int.MaxValue,(long)original+extra);
        }
        public static int InputCredit(Thing machine,string key,int physical,int room)
        {
            var state=states.GetOrCreateValue(machine);
            double credit=physical/(1d-SpecialTierAdapter.Discount(machine))+(state.carry.TryGetValue(key,out float saved)?saved:0);
            int whole=(int)Math.Floor(credit+.000001);
            state.carry[key]=whole>=room?0:(float)Math.Max(0,credit-whole);
            return Math.Min(room,whole);
        }
        public static Thing FreshStack(Thing product,object owner)
        {
            Thing machine=Machine(owner);
            if(product!=null&&machine!=null&&product.def.category==ThingCategory.Item&&product.def.stackLimit>1)
                product.stackCount=Amount(machine,product.def.defName,product.stackCount);
            return product;
        }
        public static void Expose(Thing thing)
        {
            if(!states.TryGetValue(thing,out var state)&&SpecialTierAdapter.Tier(thing)==0&&Scribe.mode!=LoadSaveMode.LoadingVars)return;
            Scribe_Deep.Look(ref state,"grechkaSpecialProduction");
            if(Scribe.mode==LoadSaveMode.LoadingVars){states.Remove(thing);if(state!=null)states.Add(thing,state);}
        }
        public static bool BillMachine(Thing machine)
        {
            string kind=machine?.GetType().FullName;
            return SpecialTierAdapter.Tier(machine)>0&&(kind=="Fortified.Building_WorkTableAutonomous"||kind=="RimWorld.Building_MechGestator"||kind=="VFEPirates.Building_WarcasketFoundry");
        }
        internal static IEnumerable<CodeInstruction> Counter(IEnumerable<CodeInstruction> source,FieldInfo field,bool floating,int expected=1)
        {
            var code=source.ToList();int changed=0;
            for(int i=1;i<code.Count;i++)if(code[i].StoresField(field)&&(code[i-1].opcode==OpCodes.Sub||code[i-1].opcode==OpCodes.Add))
            {
                code.Insert(i-1,new CodeInstruction(OpCodes.Ldarg_0));
                code.Insert(i,CodeInstruction.Call(typeof(SpecialProduction),floating?nameof(ScaleFloat):nameof(ScaleInt)));
                changed++;i+=2;
            }
            if(changed!=expected)throw new InvalidOperationException("Grechka work clock API mismatch: "+field+", sites="+changed);
            return code;
        }
        internal static IEnumerable<CodeInstruction> Nutrition(IEnumerable<CodeInstruction> source)
        {
            var field=AccessTools.Field(typeof(BuildingProperties),"nutritionCostPerDispense");
            foreach(var instruction in source)
            {
                yield return instruction;
                if(instruction.LoadsField(field)){yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialProduction),nameof(Cost));}
            }
        }
    }
    [HarmonyPatch(typeof(Thing),nameof(Thing.ExposeData))]
    public static class SpecialProductionSaveHook{public static void Postfix(Thing __instance)=>SpecialProduction.Expose(__instance);}
    [HarmonyPatch(typeof(Building_GeneAssembler),nameof(Building_GeneAssembler.DoWork))]
    public static class SpecialGeneWorkHook{public static void Prefix(Building_GeneAssembler __instance,ref float workAmount)=>workAmount=SpecialProduction.ScaleFloat(workAmount,__instance);}
    [HarmonyPatch(typeof(Building_GeneAssembler),nameof(Building_GeneAssembler.Start))]
    public static class SpecialGeneArchiteHook{public static void Prefix(Building_GeneAssembler __instance,ref int architesRequired)=>architesRequired=(int)SpecialProduction.WholeCost(architesRequired,__instance);}
    [HarmonyPatch(typeof(CompDeepDrill),nameof(CompDeepDrill.DrillWorkDone))]
    public static class SpecialDrillWorkHook{public static void Prefix(CompDeepDrill __instance,ref int __1)=>__1=SpecialProduction.ScaleInt(__1,__instance);}
    [HarmonyPatch(typeof(CompDeepDrill),"TryProducePortion")]
    public static class SpecialDrillYieldHook{public static void Prefix(CompDeepDrill __instance,ref float yieldPct)=>yieldPct*=1+SpecialTierAdapter.Output(__instance.parent);}
    [HarmonyPatch(typeof(Building_GeneExtractor),"Tick")]
    public static class SpecialGeneClockHook{public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Counter(instructions,AccessTools.Field(typeof(Building_GeneExtractor),"ticksRemaining"),false);}
    [HarmonyPatch(typeof(Building_SubcoreScanner),"Tick")]
    public static class SpecialSubcoreClockHook
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach(var instruction in SpecialProduction.Counter(instructions,AccessTools.Field(typeof(Building_SubcoreScanner),"fabricationTicksLeft"),false))
            {yield return instruction;if(instruction.Calls(AccessTools.Method(typeof(ThingMaker),nameof(ThingMaker.MakeThing)))){yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialProduction),nameof(SpecialProduction.FreshStack));}}
        }
    }
    [HarmonyPatch(typeof(Building_SubcoreScanner),nameof(Building_SubcoreScanner.GetRequiredCountOf))]
    public static class SpecialSubcoreInputHook
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {foreach(var instruction in instructions){yield return instruction;if(instruction.Calls(AccessTools.Method(typeof(IngredientCount),nameof(IngredientCount.GetBaseCount)))){yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialProduction),nameof(SpecialProduction.WholeCost));}}}
    }
    [HarmonyPatch(typeof(Bill_Autonomous),nameof(Bill_Autonomous.BillTick))]
    public static class SpecialMechClockHook{public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Counter(instructions,AccessTools.Field(typeof(Bill_Autonomous),"formingTicks"),true);}
    [HarmonyPatch(typeof(Bill_Mech),nameof(Bill_Mech.BillTick))]
    public static class SpecialMechActualClockHook{public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Counter(instructions,AccessTools.Field(typeof(Bill_Autonomous),"formingTicks"),true);}
    [HarmonyPatch(typeof(CompAtomizer),nameof(CompAtomizer.CompTick))]
    public static class SpecialAtomizerClockHook{public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Counter(instructions,AccessTools.Field(typeof(CompAtomizer),"ticksAtomized"),false);}
    [HarmonyPatch(typeof(CompScanner),nameof(CompScanner.Used))]
    public static class SpecialScannerWorkHook
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();var field=AccessTools.Field(typeof(CompScanner),"lastUserSpeed");int i=code.FindLastIndex(c=>c.StoresField(field));
            if(i<0)throw new InvalidOperationException("Scanner speed field not found");
            // Both native branches join immediately after the optional stat assignment.
            int at=i+1;var load=new CodeInstruction(OpCodes.Ldarg_0);load.labels.AddRange(code[at].labels);code[at].labels.Clear();
            code.InsertRange(at,new[]{load,new CodeInstruction(OpCodes.Dup),new CodeInstruction(OpCodes.Ldfld,field),new CodeInstruction(OpCodes.Ldarg_0),CodeInstruction.Call(typeof(SpecialProduction),nameof(SpecialProduction.ScaleFloat)),new CodeInstruction(OpCodes.Stfld,field)});
            return code;
        }
    }
    [HarmonyPatch(typeof(Building_FermentingBarrel),"get_ProgressPerTickAtCurrentTemp")]
    public static class SpecialBeerClockHook{public static void Postfix(Building_FermentingBarrel __instance,ref float __result)=>__result*=SpecialTierAdapter.Speed(__instance);}
    [HarmonyPatch(typeof(Building_FermentingBarrel),nameof(Building_FermentingBarrel.TakeOutBeer))]
    public static class SpecialBeerYieldHook{public static void Postfix(Building_FermentingBarrel __instance,ref Thing __result)=>__result=SpecialProduction.FreshStack(__result,__instance);}
    [HarmonyPatch(typeof(Building_NutrientPasteDispenser),nameof(Building_NutrientPasteDispenser.TryDispenseFood))]
    public static class SpecialPasteYieldHook
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Nutrition(instructions);
        public static void Postfix(Building_NutrientPasteDispenser __instance,ref Thing __result)=>__result=SpecialProduction.FreshStack(__result,__instance);
    }
    [HarmonyPatch(typeof(Building_NutrientPasteDispenser),nameof(Building_NutrientPasteDispenser.HasEnoughFeedstockInHoppers))]
    public static class SpecialPasteAvailabilityHook{public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>SpecialProduction.Nutrition(instructions);}
    [HarmonyPatch(typeof(Plant),"get_GrowthRate")]
    public static class SpecialHydroponicGrowthHook
    {
        public static void Postfix(Plant __instance,ref float __result)
        {
            if(__result<=0||!__instance.Spawned)return;
            var grower=__instance.Position.GetThingList(__instance.Map).OfType<Building_PlantGrower>().FirstOrDefault();
            if(grower!=null)__result*=SpecialTierAdapter.Speed(grower);
        }
    }
    [HarmonyPatch(typeof(RecipeWorker),nameof(RecipeWorker.GetIngredientCount))]
    public static class SpecialBillInputHook
    {public static void Postfix(Bill bill,ref float __result){Thing machine=bill?.billStack?.billGiver as Thing;if(SpecialProduction.BillMachine(machine)&&!ProfileVariants.IsUpgrade(bill.recipe))__result*=1-SpecialTierAdapter.Discount(machine);}}
    [HarmonyPatch(typeof(GenRecipe),nameof(GenRecipe.MakeRecipeProducts))]
    public static class SpecialBillOutputHook
    {
        public static void Postfix(RecipeDef recipeDef,IBillGiver billGiver,ref IEnumerable<Thing> __result)
        {
            Thing machine=billGiver as Thing;
            if(SpecialProduction.BillMachine(machine)&&!ProfileVariants.IsUpgrade(recipeDef))__result=Products(__result,recipeDef,machine);
        }
        private static IEnumerable<Thing> Products(IEnumerable<Thing> source,RecipeDef recipe,Thing machine)
        {
            foreach(Thing product in source)
            {
                Thing original=product is MinifiedThing min?min.InnerThing:product;
                if(original==null||original is Pawn||original is Corpse||original is IThingHolder||
                    recipe.products?.Any(p=>p.thingDef==original.def)!=true){yield return product;continue;}
                int total=SpecialProduction.Amount(machine,recipe.defName+"/"+original.def.defName,product.stackCount);
                int extra=total-product.stackCount;
                if(product.def.stackLimit>1){int add=Math.Min(extra,Math.Max(0,product.def.stackLimit-product.stackCount));product.stackCount+=add;extra-=add;}
                yield return product;
                while(extra>0)
                {
                    Thing created=ThingMaker.MakeThing(original.def,original.Stuff);
                    var quality=original.TryGetComp<CompQuality>();if(quality!=null)created.TryGetComp<CompQuality>()?.SetQuality(quality.Quality,ArtGenerationContext.Colony);
                    if(product is MinifiedThing)created=created.MakeMinified();
                    created.stackCount=Math.Min(extra,created.def.stackLimit);extra-=created.stackCount;yield return created;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Building_FermentingBarrel),"get_SpaceLeftForWort")]
    public static class SpecialBeerSpaceHook
    {public static void Postfix(Building_FermentingBarrel __instance,ref int __result)=>__result=(int)SpecialProduction.WholeCost(__result,__instance);}
    [HarmonyPatch(typeof(Building_FermentingBarrel),nameof(Building_FermentingBarrel.AddWort),new[]{typeof(int)})]
    public static class SpecialBeerInputHook
    {
        public static void Prefix(Building_FermentingBarrel __instance,ref int count)
        {
            float discount=SpecialTierAdapter.Discount(__instance);if(discount<=0||count<=0)return;
            int present=(int)AccessTools.Field(typeof(Building_FermentingBarrel),"wortCount").GetValue(__instance);
            // Physical hauling still consumes the original amount; stored wort represents its effective recipe yield.
            int capacity=(int)AccessTools.Field(typeof(Building_FermentingBarrel),"MaxCapacity").GetRawConstantValue();
            count=SpecialProduction.InputCredit(__instance,"wortInput",count,capacity-present);
        }
    }
    [HarmonyPatch(typeof(Building_FermentingBarrel),nameof(Building_FermentingBarrel.AddWort),new[]{typeof(Thing)})]
    public static class SpecialBeerPhysicalInputHook
    {
        public static bool Prefix(Building_FermentingBarrel __instance,Thing wort)
        {
            if(SpecialTierAdapter.Discount(__instance)<=0)return true;
            int physical=Math.Min(wort.stackCount,__instance.SpaceLeftForWort);
            if(physical>0){__instance.AddWort(physical);wort.SplitOff(physical).Destroy();}
            return false;
        }
    }
    [HarmonyPatch(typeof(Building_GrowthVat),"Tick")]
    public static class SpecialVatClockHook
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int changed=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.opcode==OpCodes.Ldc_R4&&(float)instruction.operand==20f)
                {changed++;yield return new CodeInstruction(OpCodes.Ldarg_0);yield return CodeInstruction.Call(typeof(SpecialProduction),nameof(SpecialProduction.ScaleFloat));}
            }
            if(changed!=1)throw new InvalidOperationException("Growth vat age clock API changed");
        }
    }
    [HarmonyPatch(typeof(Building_GrowthVat),"get_NutritionConsumedPerDay")]
    public static class SpecialVatInputHook{public static void Postfix(Building_GrowthVat __instance,ref float __result)=>__result=SpecialProduction.Cost(__result,__instance);}
    [HarmonyPatch(typeof(Building_GrowthVat),"TryGrowEmbryo")]
    public static class SpecialEmbryoClockHook
    {
        private static readonly FieldInfo start=AccessTools.Field(typeof(Building_Enterable),"startTick");
        public static void Prefix(Building_GrowthVat __instance,out int __state)=>__state=(int)start.GetValue(__instance);
        public static void Postfix(Building_GrowthVat __instance,int __state)
        {
            int next=(int)start.GetValue(__instance);float speed=SpecialTierAdapter.Speed(__instance);
            if(speed<=1||next==__state||!__instance.Working)return;
            int now=Find.TickManager.TicksGame;if(next>now)start.SetValue(__instance,now+Math.Max(1,(int)Math.Ceiling((next-now)/speed)));
        }
    }

    public static class HydroponicYield
    {
        private sealed class Quote{public Thing machine;public string key;public int original;}
        private static readonly ConditionalWeakTable<Plant,Quote> quotes=new ConditionalWeakTable<Plant,Quote>();
        public static void Preview(Plant plant,ref int amount)
        {
            if(amount<=0||!plant.Spawned)return;
            var machine=plant.Position.GetThingList(plant.Map).OfType<Building_PlantGrower>().FirstOrDefault();
            if(machine==null||SpecialTierAdapter.Output(machine)<=0)return;
            string key="hydro/"+plant.def.defName;
            quotes.Remove(plant);quotes.Add(plant,new Quote{machine=machine,key=key,original=amount});
            amount=SpecialProduction.PeekAmount(machine,key,amount);
        }
        public static void Collected(Plant plant)
        {
            if(!quotes.TryGetValue(plant,out var quote))return;
            SpecialProduction.Amount(quote.machine,quote.key,quote.original);quotes.Remove(plant);
        }
    }
    [HarmonyPatch(typeof(Plant),nameof(Plant.YieldNow))]
    public static class SpecialHydroponicYieldHook{public static void Postfix(Plant __instance,ref int __result)=>HydroponicYield.Preview(__instance,ref __result);}
    [HarmonyPatch(typeof(Plant),nameof(Plant.PlantCollected))]
    public static class SpecialHydroponicCollectHook{public static void Prefix(Plant __instance)=>HydroponicYield.Collected(__instance);}

}
