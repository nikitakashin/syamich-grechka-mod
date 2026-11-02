using Grechka.WorldIntegration;
using RimWorld;
using Verse;

internal static class SpecialRecipeChecks
{
    private static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
    internal static void Run(ModContentPack pack,ThingDef steel)
    {
        foreach(Type type in new[]{typeof(Building_MechGestator),typeof(FixtureAutonomousTable)})
        {
            string id="FixtureSpecial_"+type.Name;
            var original=new ThingDef{defName=id+"_Base",modContentPack=pack,thingClass=type,category=ThingCategory.Building,costList=new(){new ThingDefCountClass(steel,20)},statBases=new(){new StatModifier{stat=StatDefOf.WorkTableWorkSpeedFactor,value=1.25f}}};
            var variant=new ThingDef{defName=id+"_Higher",modContentPack=pack,thingClass=type,category=ThingCategory.Building,recipes=new(),statBases=new(){new StatModifier{stat=StatDefOf.WorkTableWorkSpeedFactor,value=1.25f}}};
            DefDatabase<ThingDef>.Add(original);DefDatabase<ThingDef>.Add(variant);
            var rule=new MachineProfileRuleDef{defName=id+"_Rule",packageId=pack.PackageIdPlayerFacing,targetDefName=original.defName,minimumTier=3,addHousing=true};DefDatabase<MachineProfileRuleDef>.Add(rule);
            var recipe=new RecipeDef{defName=id+"_Recipe",recipeUsers=new(){original},products=new(){new ThingDefCountClass(steel,1)}};DefDatabase<RecipeDef>.Add(recipe);
            ProfileVariants.Entries[original.defName]=new VariantEntry{sourceName=original.defName,name=original.defName,ruleName=rule.defName,tier=3,minimum=3,worktable=false};
            ProfileVariants.Entries[variant.defName]=new VariantEntry{sourceName=original.defName,name=variant.defName,ruleName=rule.defName,tier=4,minimum=3,worktable=false};
            Check(!variant.AllRecipes.Contains(recipe),type.Name+": native recipeUsers(source) initially does not cover a different Def");
            WorldProfile.Capture();var state=new WorldProfileState(null);WorldProfile.Apply(state,true);
            Check(variant.AllRecipes.Count(r=>ReferenceEquals(r,recipe))==1,type.Name+": special IBillGiver variant inherits the exact native recipe provider");
            Check(recipe.recipeUsers.Count==1&&recipe.recipeUsers[0]==original,type.Name+": original provider list remains unchanged");
            Check(variant.statBases.Single(s=>s.stat==StatDefOf.WorkTableWorkSpeedFactor).value==1.25f&&!ProfileVariants.Entry(variant).worktable,type.Name+": recipe inheritance does not enable duplicate ordinary-worktable speed bonuses");
            WorldProfile.Apply(state,false);
            Check(variant.AllRecipes.Count(r=>ReferenceEquals(r,recipe))==1,type.Name+": reload keeps one recipe identity without duplicates");
            WorldProfile.RestoreBaseline();
        }
    }
}
// Represents the IBillGiver inheritance contract used by optional autonomous worktable subclasses.
internal sealed class FixtureAutonomousTable : Building_WorkTable { }
