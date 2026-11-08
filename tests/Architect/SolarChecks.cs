using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using Verse;

internal static class SolarChecks
{
    private static void Check(bool ok,string message)
    { if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message); }
    internal static void Run(string repo)
    {
        void Patch(XmlDocument xml,string name)
        {
            var patch=new XmlDocument();patch.Load(Path.Combine(repo,"mods/GrechkaIndustry/Patches",name+".xml"));
            foreach(XmlNode operation in patch.SelectNodes("Patch/Operation"))
                Check(DirectXmlToObject.ObjectFromXml<PatchOperation>(operation,false).Apply(xml),"Native solar patch applies: "+name);
        }
        foreach(bool existingFields in new[]{false,true})
        {
            var xml=new XmlDocument();
            xml.LoadXml("<Defs><ThingDef><defName>SolarGenerator</defName><designationCategory>Power</designationCategory><statBases/><comps/>"
                +(existingFields?"<uiOrder>2500</uiOrder><designatorDropdown>OldSolarGroup</designatorDropdown>":"")+"</ThingDef></Defs>");
            Patch(xml,"SolarGenerator");
            var vanilla=xml.SelectSingleNode("Defs/ThingDef");
            Check(vanilla.SelectSingleNode("designationCategory").InnerText=="Grechka_SolarCategory"
                &&vanilla.SelectSingleNode("uiOrder").InnerText=="0"&&vanilla.SelectSingleNode("designatorDropdown")==null,
                "Vanilla solar is first in its own tab, without an inherited old dropdown");
            Patch(xml,"AdvancedSolarGenerator");
        }
        // Exercise actual variant generation and native XML inheritance, including a category inherited from a parent.
        foreach(bool explicitCategory in new[]{false,true})
        {
            var xml=new XmlDocument();
            xml.LoadXml("<Defs><ThingDef Name='SolarFixtureBase' Abstract='True'><thingClass>Building</thingClass><designationCategory>Power</designationCategory></ThingDef>"
                +"<ThingDef ParentName='SolarFixtureBase'><defName>VFE_AdvancedSolarGenerator</defName><label>advanced solar</label>"
                +(explicitCategory?"<designationCategory>Power</designationCategory>":"")+"</ThingDef></Defs>");
            var catalog=new XmlDocument();catalog.Load(Path.Combine(repo,"mods/GrechkaWorldIntegration/Defs/MachineProfile.xml"));
            xml.DocumentElement.AppendChild(xml.ImportNode(catalog.SelectSingleNode("Defs/*[targetDefName='VFE_AdvancedSolarGenerator']"),true));
            ModContentPack Pack(string id)
            {
                var p=(ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
                AccessTools.Field(typeof(ModContentPack),"packageIdPlayerFacingInt").SetValue(p,id);return p;
            }
            var source=Pack("vanillaexpanded.vfepower");var industry=Pack("grechka.industry");
            var owners=new Dictionary<XmlNode,LoadableXmlAsset>();
            void Owner(XmlNode node,ModContentPack pack)
            {
                var a=(LoadableXmlAsset)RuntimeHelpers.GetUninitializedObject(typeof(LoadableXmlAsset));
                AccessTools.Field(typeof(LoadableXmlAsset),"mod").SetValue(a,pack);owners[node]=a;
            }
            foreach(XmlNode node in xml.DocumentElement.ChildNodes)Owner(node,source);
            for(int tier=1;tier<=5;tier++)
            {
                var housing=xml.CreateElement("ThingDef");housing.InnerXml="<defName>Grechka_HousingT"+tier+"_Item</defName>";
                xml.DocumentElement.AppendChild(housing);Owner(housing,industry);
            }
            Patch(xml,"AdvancedSolarGenerator");
            Patch(xml,"AdvancedSolarGenerator");
            var assembly=Assembly.LoadFrom(Path.Combine(repo,"mods/GrechkaWorldIntegration/Assemblies/GrechkaWorldIntegration.dll"));
            assembly.GetType("Grechka.WorldIntegration.ProfileVariantXmlHook",true).GetMethod("EntriesFromXml").Invoke(null,new object[]{xml,owners});
            Check(xml.SelectNodes("Defs/ThingDef[starts-with(defName,'GrechkaWorld_VFE_AdvancedSolarGenerator_')]").Count==2,
                "Advanced solar T4/T5 definitions remain loadable for existing saves");
            XmlInheritance.Clear();
            foreach(XmlNode node in xml.DocumentElement.ChildNodes)XmlInheritance.TryRegister(node,owners[node].mod);
            XmlInheritance.Resolve();
            foreach(XmlNode node in xml.SelectNodes("Defs/ThingDef[defName='VFE_AdvancedSolarGenerator' or starts-with(defName,'GrechkaWorld_VFE_AdvancedSolarGenerator_')]"))
            {
                XmlNode resolved=XmlInheritance.GetResolvedNodeFor(node);
                // Parse the real null reference field with the game's reader after its inheritance pass.
                var minimal=new XmlDocument();minimal.LoadXml("<ThingDef><defName>SolarHiddenFixture</defName>"+resolved.SelectSingleNode("designationCategory").OuterXml+"</ThingDef>");
                var def=DirectXmlToObject.ObjectFromXml<ThingDef>(minimal.DocumentElement,false);
                Check(def.designationCategory==null,"Advanced solar source/generated tiers have no construction category: "+node.SelectSingleNode("defName").InnerText);
            }
            XmlInheritance.Clear();
        }
    }
}
