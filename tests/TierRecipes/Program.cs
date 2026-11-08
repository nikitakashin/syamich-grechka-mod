using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
AccessTools.Method(Type.GetType("Waylith.Rendering.ShaderSourceLoader, Waylith", true), "SetOverrideShaderDirectories")
    .Invoke(null, new object[] { new[] { Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"), "Rendering", "Shaders") } });
AccessTools.Field(typeof(UnityData), "mainThreadId").SetValue(null, Environment.CurrentManagedThreadId);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
ModContentPack Pack(string name)
{
    var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
    AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, name); return pack;
}
var industry = Pack("grechka.industry"); var core = Pack("ludeon.rimworld"); var pharmacy = Pack("grechka.pharmacy");
ThingDef Bench(string name, ModContentPack pack)
{
    var def = new ThingDef { defName = name, label = name, thingClass = typeof(Building_WorkTable), category = ThingCategory.Building, modContentPack = pack, recipes = new() };
    DefDatabase<ThingDef>.Add(def); return def;
}
RecipeDef Recipe(string name)
{
    var recipe = new RecipeDef { defName = name, label = name, ingredients = new(), fixedIngredientFilter = new(), defaultIngredientFilter = new() };
    DefDatabase<RecipeDef>.Add(recipe); return recipe;
}
var harmony = new Harmony("grechka.tier-recipes.tests");
harmony.Patch(AccessTools.Method(typeof(Log), "Error", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Error)));
harmony.Patch(AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Error)));
harmony.CreateClassProcessor(typeof(TierRecipeInheritance)).Patch();
var benches = Enumerable.Range(1, 5).Select(t => Bench("Grechka_AssemblyBenchT" + t + "_Building", industry)).ToArray();
var low = Recipe("Fixture_LowRecipe"); var middle = Recipe("Fixture_MiddleRecipe"); var high = Recipe("Fixture_HighRecipe");
var upgrade = Recipe("Grechka_UpgradeAssemblyBenchT2");
benches[0].recipes.AddRange(new[] { low, upgrade }); benches[2].recipes.Add(middle); benches[4].recipes.Add(high);
var fromMod = Recipe("Fixture_OptionalModuleRecipe"); fromMod.recipeUsers = new() { benches[1] };
Check(benches[4].AllRecipes.ToHashSet().SetEquals(new[] { low, middle, high, fromMod }), "T5 inherits native and recipeUsers recipes from all lower tiers, without their upgrades");
Check(benches[1].AllRecipes.Contains(low) && !benches[1].AllRecipes.Contains(middle) && !benches[1].AllRecipes.Contains(high), "Low-tier benches never inherit high-tier recipes");
Check(benches[0].AllRecipes.Contains(upgrade), "The current bench retains its own upgrade recipe");
Check(ReferenceEquals(benches[4].AllRecipes, benches[4].AllRecipes), "Repeated native recipe queries reuse the merged list");
var late = Recipe("Fixture_LateRecipe"); benches[0].recipes.Add(late);
AccessTools.Field(typeof(ThingDef), "allRecipesCached").SetValue(benches[0], null);
Check(benches[4].AllRecipes.Contains(late), "Native cache invalidation propagates late recipes through all tiers");
var unrelated = Bench("Fixture_Unrelated", core); unrelated.recipes.Add(high);
Check(!unrelated.AllRecipes.Contains(low), "Unrelated workbench families stay separate");
Check(!TierMachineFamilies.TryDescribe(new ThingDef { defName = benches[0].defName, modContentPack = core }, out _, out _), "A matching ID from another package cannot establish a family");
var reactor3 = Bench("GrechkaPharmacy_ReactorT3_Building", pharmacy); var reactor5 = Bench("GrechkaPharmacy_ReactorT5_Building", pharmacy); reactor3.recipes.Add(middle);
Check(reactor5.AllRecipes.Contains(middle) && !reactor5.AllRecipes.Contains(low), "Pharmacy reactors inherit only within their own series");
var source = Bench("ElectricStove", core); source.recipes.Add(low);
var variant = Bench("GrechkaWorld_ElectricStove_T5_Building", core);
ProfileVariants.Entries[source.defName] = new VariantEntry { name = source.defName, sourceName = source.defName, tier = 1, minimum = 1, worktable = true };
ProfileVariants.Entries[variant.defName] = new VariantEntry { name = variant.defName, sourceName = source.defName, tier = 5, minimum = 1, worktable = true };
Check(variant.AllRecipes.Contains(low), "A registered generated machine retains original workbench recipes");
Check(SaveSettingsCompatibility.Key(benches[4].defName) == benches[0].defName && SaveSettingsCompatibility.Key(variant.defName) == "Stove", "Preset keys combine owned tiers and retain KV's native groups");

var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>()); game.uniqueIDsManager = new UniqueIDsManager(); Current.Game = game;
AccessTools.Field(typeof(Prefs), "data").SetValue(null, new PrefsData());
BillRepeatModeDefOf.RepeatCount = new BillRepeatModeDef { defName = "RepeatCount" }; DefDatabase<BillRepeatModeDef>.Add(BillRepeatModeDefOf.RepeatCount);
BillRepeatModeDefOf.Forever = new BillRepeatModeDef { defName = "Forever" };
BillRepeatModeDefOf.TargetCount = new BillRepeatModeDef { defName = "TargetCount" };
BillStoreModeDefOf.BestStockpile = new BillStoreModeDef { defName = "BestStockpile" }; DefDatabase<BillStoreModeDef>.Add(BillStoreModeDefOf.BestStockpile);
BillStack Stack(ThingDef def) => new Building_WorkTable { def = def }.BillStack;
var lowStack = Stack(benches[0]); var highStack = Stack(benches[4]);
var preset = new List<Bill> { new Bill_Production(low, null), new Bill_Production(high, null) };
SaveSettingsCompatibility.FilterForTarget(lowStack, preset);
Check(preset.Count == 1 && preset[0].recipe == low, "A low-tier target rejects unsupported recipes from a high-tier preset");
string temporary = Path.Combine(Path.GetTempPath(), "grechka-tier-presets-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
try
{
    AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, temporary);
    foreach (string folder in new[] { "Config", "Mods", "Data/Core/About" }) Directory.CreateDirectory(Path.Combine(temporary, folder));
    AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(temporary, "Mods"));
    AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(temporary, "Data"));
    File.WriteAllText(Path.Combine(temporary, "Config/ModsConfig.xml"), "<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
    File.WriteAllText(Path.Combine(temporary, "Data/Core/About/About.xml"), "<ModMetaData><name>Core</name><author>Ludeon Studios</author><packageId>ludeon.rimworld</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
    string legacy = Path.Combine(temporary, benches[1].defName); Directory.CreateDirectory(legacy);
    string legacyFile = Path.Combine(legacy, "old-template.txt"); File.WriteAllText(legacyFile, "legacy sentinel");
    var files = new List<FileInfo>();
    SaveSettingsCompatibility.AddLegacyFiles(benches[0].defName, files, temporary); SaveSettingsCompatibility.AddLegacyFiles(benches[0].defName, files, temporary);
    Check(files.Count == 1 && files[0].FullName == legacyFile && File.ReadAllText(legacyFile) == "legacy sentinel", "Legacy tier-specific files are discovered once without moving or rewriting them");
    string other = Path.Combine(temporary, unrelated.defName); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "other.txt"), "unrelated");
    SaveSettingsCompatibility.AddLegacyFiles(benches[0].defName, files, temporary);
    Check(files.Count == 1, "Another machine's presets never enter the shared list");
    string dll = Environment.GetEnvironmentVariable("SAVE_SETTINGS_ASSEMBLY");
    if (string.IsNullOrEmpty(dll) && Environment.GetEnvironmentVariable("MISC_ROBOTS_ASSEMBLY") is string misc)
        dll = Path.Combine(new FileInfo(misc).Directory.Parent.Parent.Parent.FullName, "3222246658/1.7/Assemblies/SaveStorageSettings.dll");
    if (dll != null && File.Exists(dll))
    {
        var assembly = Assembly.LoadFrom(dll);
        harmony.Patch(AccessTools.Method(typeof(Window), nameof(Window.Close)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.SkipClose)));
        foreach (Type hook in new[] { typeof(SaveSettingsFamilyHook), typeof(SaveSettingsLegacyFilesHook), typeof(SaveSettingsLoadScopeHook), typeof(SaveSettingsLoadedBillsHook) }) harmony.CreateClassProcessor(hook).Patch();
        var keyMethod = AccessTools.Method(assembly.GetType("SaveStorageSettings.Patch_Building_GetGizmos"), "GetTypeIBillGiver");
        Check((string)keyMethod.Invoke(null, new object[] { benches[4].defName }) == benches[0].defName, "The installed KV helper uses the same folder for T1 and T5");
        Check((string)keyMethod.Invoke(null, new object[] { variant.defName }) == "Stove", "The installed KV helper groups a generated stove with native stoves");
        var io = assembly.GetType("SaveStorageSettings.IOUtil");
        var material = new ThingDef { defName = "Fixture_AllowedMaterial", category = ThingCategory.Item, stackLimit = 75, alwaysHaulable = true,
            thingClass = typeof(Thing), thingCategories = new() { new ThingCategoryDef { defName = "Fixture_Materials" } } };
        DefDatabase<ThingDef>.Add(material); low.fixedIngredientFilter.SetAllow(material, true); low.defaultIngredientFilter.SetAllow(material, true);
        var savedBill = new Bill_Production(low, null) { repeatMode = BillRepeatModeDefOf.RepeatCount, repeatCount = 17, suspended = true, ingredientSearchRadius = 23 };
        savedBill.ingredientFilter.SetAllow(material, true);
        savedBill.RenamableLabel = "My saved order"; lowStack.AddBill(savedBill);
        var file = new FileInfo(Path.Combine(temporary, "native-kv-preset.txt"));
        Check((bool)AccessTools.Method(io, "SaveCraftingSettings").Invoke(null, new object[] { lowStack, file }), "The installed KV writer saves the original bill");
        SaveSettingsLoadScopeHook.CurrentStack = highStack;
        var loaded = (List<Bill>)AccessTools.Method(io, "LoadCraftingBills").Invoke(null, new object[] { file });
        Check(loaded.Count == 1 && loaded[0] is Bill_Production b && b.recipe == low && b.repeatCount == 17 && b.suspended && b.ingredientSearchRadius == 23 && b.RenamableLabel == "My saved order" && b.ingredientFilter.Allows(material), "KV's actual parser loads the old order for T5, preserving count, suspension, radius, name and ingredient filter");
        SaveSettingsLoadScopeHook.Prefix(lowStack, out BillStack previous); var error = new Exception("fixture failure");
        Check(ReferenceEquals(SaveSettingsLoadScopeHook.Finalizer(error, previous), error) && SaveSettingsLoadScopeHook.CurrentStack == highStack, "Load context is restored on exception without swallowing the error");
        SaveSettingsLoadScopeHook.CurrentStack = null;
        // Use KV's real file list and replace/append callbacks. Only closing the
        // unshown window is a UI boundary; the parser and BillStack stay native.
        var dialogType = assembly.GetType("SaveStorageSettings.Dialog.LoadCraftingDialog");
        var dialog = RuntimeHelpers.GetUninitializedObject(dialogType);
        AccessTools.Field(dialogType, "StorageTypeName").SetValue(dialog, benches[0].defName);
        AccessTools.Field(dialogType, "files").SetValue(dialog, new List<FileInfo>());
        AccessTools.Field(dialogType, "BillStack").SetValue(dialog, highStack);
        string oldFolder = Path.Combine(temporary, "SaveStorageSettings", benches[1].defName); Directory.CreateDirectory(oldFolder);
        string oldFile = Path.Combine(oldFolder, file.Name); File.Copy(file.FullName, oldFile);
        AccessTools.Method(dialogType, "ReloadFiles").Invoke(dialog, null);
        var actualFiles = (List<FileInfo>)AccessTools.Field(dialogType, "files").GetValue(dialog);
        Check(actualFiles.Count == 1 && actualFiles[0].FullName == oldFile, "KV's actual load dialog discovers an existing T2 preset from the shared family folder");
        highStack.AddBill(new Bill_Production(high, null));
        AccessTools.Method(dialogType, "DoFileInteraction").Invoke(dialog, new object[] { actualFiles[0] });
        Check(highStack.Bills.Count == 1 && highStack.Bills[0].recipe == low && SaveSettingsLoadScopeHook.CurrentStack == null, "The actual Replace action loads the lower-tier order into T5 and restores its context");
        var loadType = AccessTools.Field(dialogType, "Type"); loadType.SetValue(dialog, Enum.ToObject(loadType.FieldType, 1));
        AccessTools.Method(dialogType, "DoFileInteraction").Invoke(dialog, new object[] { actualFiles[0] });
        Check(highStack.Bills.Count == 2 && highStack.Bills.All(b => b.recipe == low), "The actual Append action retains existing orders");
        string powerDll = Path.Combine(new FileInfo(dll).Directory.Parent.Parent.Parent.FullName, "3528086894/1.7/Assemblies/PowerTab.dll");
        if (File.Exists(powerDll))
        {
            var power = Assembly.LoadFrom(powerDll);
            var tab = power.GetType("PowerTab.PowerTab", true);
            Check(power.GetTypes().Length > 0 && typeof(ITab).IsAssignableFrom(tab) && Activator.CreateInstance(tab) is ITab,
                "The downloaded Power Tab 1.7 assembly resolves its types and constructs the native inspector tab");
        }
    }
    else Console.WriteLine("SKIP installed KV checks: set SAVE_SETTINGS_ASSEMBLY to its 1.7 DLL");
}
finally { Directory.Delete(temporary, true); }
Console.WriteLine($"PASS {checks} tier recipe/preset checks. No game controlled; full UI acceptance remains open.");

static class Fixture
{
    public static bool SkipClose() => false;
    public static void Error(string text) => Console.WriteLine("Native error: " + text);
}
