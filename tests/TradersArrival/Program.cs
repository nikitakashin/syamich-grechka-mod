using System.Xml.Linq;
using Grechka.Traders;
using HarmonyLib;
using RimWorld;
using Verse;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
var orbital = new IncidentDef { defName = "OrbitalTraderArrival", baseChance = 1f };
var caravan = new IncidentDef { defName = "TraderCaravanArrival", baseChance = 1f };
var raid = new IncidentDef { defName = "RaidEnemy", baseChance = 1f };
Check(ArrivalControl.MultiplierFor(orbital, .25f, 4f) == .25f && ArrivalControl.MultiplierFor(caravan, .25f, 4f) == 4f, "Separate ship/caravan controls");
Check(ArrivalControl.MultiplierFor(raid, 0f, 0f) == 1f, "Other incidents ignore trading controls");
Check(ArrivalControl.ClampFrequency(float.NaN) == 1f && ArrivalControl.ClampFrequency(float.PositiveInfinity) == 1f && ArrivalControl.ClampFrequency(-1) == 0f && ArrivalControl.ClampFrequency(8) == 5f, "Corrupt/excessive frequency values are bounded");

var cycleProps = new StorytellerCompProperties_OnOffCycle { incident = orbital, onDays = 7f, offDays = 8f, minSpacingDays = 3f, onDaysNoTreeConnectors = 5f, offDaysNoTreeConnectors = 10f, numIncidentsRange = new FloatRange(1f, 2f), minDaysPassed = 5f };
var cycle = new StorytellerComp_OnOffCycle { props = cycleProps };
ArrivalControl.ConfigureComp(cycle, 1f, 3f);
Check(ReferenceEquals(cycle.props, cycleProps), "Default x1 retains native orbital properties");
ArrivalControl.ConfigureComp(cycle, 5f, 1f);
var scaledCycle = (StorytellerCompProperties_OnOffCycle)cycle.props;
Check(!ReferenceEquals(scaledCycle, cycleProps) && Near(scaledCycle.onDays + scaledCycle.offDays, 3f) && Near(scaledCycle.minSpacingDays, .6f), "Fivefold orbital control shortens cycles, no duplicate incidents");
Check(Near(scaledCycle.onDaysNoTreeConnectors, 1f) && Near(scaledCycle.offDaysNoTreeConnectors, 2f), "Alternative ideology cycle scales consistently");
Check(cycleProps.onDays == 7f && cycleProps.offDays == 8f && scaledCycle.numIncidentsRange.max == 2f && scaledCycle.minDaysPassed == 5f, "Shared Def, incident count range and initial grace period remain intact");
ArrivalControl.ConfigureComp(cycle, 5f, 1f);
Check(Near(((StorytellerCompProperties_OnOffCycle)cycle.props).onDays, 1.4f), "Repeated settings application does not compound");
Check(ReferenceEquals(cycle.props, scaledCycle), "Unchanged UI repaint reuses configured properties");
ArrivalControl.ConfigureComp(cycle, .5f, 1f);
Check(Near(((StorytellerCompProperties_OnOffCycle)cycle.props).offDays, 16f), "Half frequency doubles cycle length from baseline");
ArrivalControl.ConfigureComp(cycle, 1f, 1f);
Check(ReferenceEquals(cycle.props, cycleProps), "Reset restores native property object");

var caravanProps = new StorytellerCompProperties_FactionInteraction { incident = caravan, baseIncidentsPerYear = 5f, minSpacingDays = 6f, minDaysPassed = 5f };
var caravanComp = new StorytellerComp_FactionInteraction { props = caravanProps };
ArrivalControl.ConfigureComp(caravanComp, 0f, 2f);
var scaledCaravan = (StorytellerCompProperties_FactionInteraction)caravanComp.props;
Check(scaledCaravan.baseIncidentsPerYear == 10f && scaledCaravan.minSpacingDays == 3f && scaledCaravan.minDaysPassed == 5f, "Caravans scale yearly rate and minimum spacing, retain grace period");
Check(caravanProps.baseIncidentsPerYear == 5f && ArrivalControl.AllowDedicated(caravanComp, 0f, 2f), "Shared faction properties retained and ship zero does not block caravans");
Check(!ArrivalControl.AllowDedicated(cycle, 0f, 2f) && !ArrivalControl.AllowDedicated(caravanComp, 2f, 0f), "Zero independently disables each dedicated natural schedule");
Check(ArrivalControl.AdjustWeight(caravanComp, caravan, 7f, 2f, 2f) == 7f, "Dedicated rate is never multiplied twice by random weights");
Check(ArrivalControl.AdjustWeight(caravanComp, caravan, 7f, 2f, 0f) == 0f, "Zero dedicated rate has zero incidental weight");

var raidProps = new StorytellerCompProperties_OnOffCycle { incident = raid, onDays = 7f, offDays = 8f };
var raidComp = new StorytellerComp_OnOffCycle { props = raidProps };
ArrivalControl.ConfigureComp(raidComp, 5f, 0f);
Check(ReferenceEquals(raidComp.props, raidProps) && ArrivalControl.AllowDedicated(raidComp, 0f, 0f), "Unrelated dedicated comp remains untouched");
var mtbProps = new StorytellerCompProperties_SingleMTB { incident = orbital, mtbDays = 15f };
var mtb = new StorytellerComp_SingleMTB { props = mtbProps };
ArrivalControl.ConfigureComp(mtb, 3f, 1f);
Check(((StorytellerCompProperties_SingleMTB)mtb.props).mtbDays == 5f && mtbProps.mtbDays == 15f, "Single-MTB storytellers also scale without shared mutations");

var orbitCategory = new IncidentCategoryDef { defName = "Fixture_OrbitalVisitor" };
var factionCategory = new IncidentCategoryDef { defName = "Fixture_FactionArrival" };
var threatCategory = new IncidentCategoryDef { defName = "Fixture_ThreatBig" };
orbital.category = orbitCategory; caravan.category = factionCategory; raid.category = threatCategory;
var visitor = new IncidentDef { defName = "Fixture_VisitorGroup", category = factionCategory, baseChance = 1f };
foreach (var incident in new[] { orbital, caravan, raid, visitor }) DefDatabase<IncidentDef>.Add(incident);
var randomProps = new StorytellerCompProperties_RandomMain { mtbDays = 10f, maxThreatBigIntervalDays = 13f,
    categoryWeights = new List<IncidentCategoryEntry> {
        new IncidentCategoryEntry { category = orbitCategory, weight = 2f },
        new IncidentCategoryEntry { category = factionCategory, weight = 4f },
        new IncidentCategoryEntry { category = threatCategory, weight = 4f } } };
var random = new StorytellerComp_RandomMain { props = randomProps };
ArrivalControl.ConfigureComp(random, 2f, .5f);
var adjusted = (StorytellerCompProperties_RandomMain)random.props;
Check(adjusted.categoryWeights[0].weight == 4f && adjusted.categoryWeights[1].weight == 3f && adjusted.categoryWeights[2].weight == 4f, "Randy scales both category and within-category probabilities");
Check(Near(adjusted.mtbDays, 100f / 11f) && adjusted.maxThreatBigIntervalDays == 13f, "Randy adjusts event budget to preserve nominal unrelated rate and forced-raid threshold");
Check(ArrivalControl.AdjustWeight(random, caravan, 6f, 2f, .5f) == 3f, "Randy caravan weight scales inside mixed arrival category");
Check(randomProps.mtbDays == 10f && randomProps.categoryWeights[0].weight == 2f && !ReferenceEquals(adjusted.categoryWeights, randomProps.categoryWeights), "Randy shared category list is never edited");
ArrivalControl.ConfigureComp(random, 0f, 0f);
adjusted = (StorytellerCompProperties_RandomMain)random.props;
Check(adjusted.categoryWeights[0].weight == 0f && adjusted.categoryWeights[1].weight == 2f && adjusted.categoryWeights[2].weight == 4f, "Randy zero removes trade slots and retains visitor/raid baseline weights");
ArrivalControl.ConfigureComp(random, 1f, 1f);
Check(ReferenceEquals(random.props, randomProps), "Randy reset restores original properties and list");

var otherModReplacement = new StorytellerCompProperties_OnOffCycle { incident = orbital, onDays = 12f, offDays = 18f };
cycle.props = otherModReplacement;
ArrivalControl.ConfigureComp(cycle, 2f, 1f);
Check(((StorytellerCompProperties_OnOffCycle)cycle.props).onDays == 6f, "Replacement properties from another mod become the new baseline");

var harmony = new Harmony("grechka.traders.arrival-tests");
foreach (Type patch in new[] { typeof(TraderStorytellerInitialization), typeof(TraderDedicatedSchedule), typeof(TraderRandomIncidentWeight) }) harmony.CreateClassProcessor(patch).Patch();
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Storyteller), "InitializeStorytellerComps")).Owners.Contains(harmony.Id), "Initialization patch binds to installed RimWorld 1.7");
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(StorytellerComp), "IncidentChanceFinal")).Owners.Contains(harmony.Id), "Random weight patch binds to installed RimWorld 1.7");
foreach (var method in TraderDedicatedSchedule.TargetMethods()) Check(Harmony.GetPatchInfo(method).Owners.Contains(harmony.Id), "Natural schedule patch binds: " + method.DeclaringType.Name);
GrechkaTradersMod.Settings.orbitalFrequency = 0f;
GrechkaTradersMod.Settings.caravanFrequency = 0f;
Check(!cycle.MakeIntervalIncidents(null).Any() && !caravanComp.MakeIntervalIncidents(null).Any() && !mtb.MakeIntervalIncidents(null).Any(), "Bound native schedule calls produce no incidents at zero before touching game state");
GrechkaTradersMod.Settings.orbitalFrequency = 1f;
GrechkaTradersMod.Settings.caravanFrequency = 1f;
Check(cycle.MakeIntervalIncidents(null).GetType().Name.Contains("MakeIntervalIncidents"), "Native schedule iterator is retained at x1");
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(IncidentWorker_TraderCaravanArrival), "TryExecuteWorker"))?.Owners.Contains(harmony.Id) != true &&
    Harmony.GetPatchInfo(AccessTools.Method(typeof(IncidentWorker_OrbitalTraderArrival), "TryExecuteWorker"))?.Owners.Contains(harmony.Id) != true,
    "Arrival control leaves direct requested/quest/debug incident execution unpatched");

string managed = Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR");
var actual = XDocument.Load(Path.Combine(managed, "../../../Data/Core/Defs/Storyteller/Storytellers.xml"));
var installedOrbitSchedules = actual.Descendants("li").Where(e => (string)e.Element("incident") == "OrbitalTraderArrival").ToList();
var installedCaravanSchedules = actual.Descendants("li").Where(e => (string)e.Element("incident") == "TraderCaravanArrival").ToList();
Check(installedOrbitSchedules.Count >= 2 && installedOrbitSchedules.All(e => (string)e.Attribute("Class") == "StorytellerCompProperties_OnOffCycle"), "Installed Cassandra/Phoebe orbital schedules use the patched native owner");
Check(installedCaravanSchedules.Count >= 2 && installedCaravanSchedules.All(e => (string)e.Attribute("Class") == "StorytellerCompProperties_FactionInteraction"), "Installed Cassandra/Phoebe caravan schedules use the patched native owner");
Console.WriteLine($"PASS {checks} GrechkaTraders arrival checks against installed libraries; no game launched or controlled.");
