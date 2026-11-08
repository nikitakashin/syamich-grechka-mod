#!/usr/bin/env python3
"""Negative fixtures for the data-only drone validator; no game/DLL execution."""
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("check_drones", ROOT / "scripts/check-drones.py")
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class DroneConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="grechka-drones-config-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for relative in ["mods/GrechkaDrones", "mods/GrechkaIndustry/Defs", "profiles/GrechkaWorld"]:
            shutil.copytree(ROOT / relative, self.root / relative,
                            ignore=shutil.ignore_patterns("bin", "obj", "Assemblies", "__pycache__"))

    def edit_xml(self, relative, mutate):
        path = self.root / "mods/GrechkaDrones" / relative
        tree = ET.parse(path)
        mutate(tree.getroot())
        tree.write(path, encoding="utf-8", xml_declaration=True)

    def rejected(self, message):
        with self.assertRaisesRegex(ValueError, message):
            checker.validate(self.root)

    def test_fixture_baseline(self):
        self.assertIn("PASS GrechkaDrones", checker.validate(self.root))

    def test_actual_repository(self):
        self.assertIn("PASS GrechkaDrones", checker.validate(ROOT))

    def test_unknown_source_class(self):
        self.edit_xml("Defs/Bases.xml", lambda r: setattr(r.find("ThingDef/thingClass"), "text", "Grechka.Drones.MissingDrone"))
        self.rejected('missing C# class')

    def test_missing_extension_field_in_source(self):
        path = self.root / "mods/GrechkaDrones/Source/Drones.cs"
        path.write_text(path.read_text().replace("public float foamRadius", "public float removedRadius"))
        self.rejected('XML fields do not match C# source')

    def test_invalid_extension_field_in_xml(self):
        self.edit_xml("Defs/Drones.xml", lambda r: ET.SubElement(r.find("ThingDef/modExtensions/li"), "unknownField"))
        self.rejected('unsupported drone extension fields')

    def test_duplicate_unit(self):
        self.edit_xml("Defs/Drones.xml", lambda r: r.append(ET.fromstring(ET.tostring(r[0]))))
        self.rejected('Duplicate definition')

    def test_unstable_numeric_suffix(self):
        self.edit_xml("Defs/Drones.xml", lambda r: setattr(r.find("ThingDef/defName"), "text", "Grechka_RescueDroneT1"))
        self.rejected('must not end with a number')

    def test_flammable_unit(self):
        self.edit_xml("Defs/Drones.xml", lambda r: setattr(r.find("ThingDef/statBases/Flammability"), "text", "0.1"))
        self.rejected('must be fireproof')

    def test_flammable_station(self):
        self.edit_xml("Defs/Stations.xml", lambda r: setattr(r.find("ThingDef/statBases/Flammability"), "text", "0.1"))
        self.rejected('must be fireproof')

    def test_wrong_station_spawn_reference(self):
        self.edit_xml("Defs/Stations.xml", lambda r: setattr(r.find("ThingDef/spawnThingDef"), "text", "Grechka_WarriorDroneT1_Unit"))
        self.rejected('spawn references differ')

    def test_wrong_role(self):
        self.edit_xml("Defs/Drones.xml", lambda r: setattr(r.find("ThingDef/modExtensions/li/role"), "text", "Hauler"))
        self.rejected('role/tier mismatch')

    def test_decreasing_health(self):
        self.edit_xml("Defs/Drones.xml", lambda r: setattr(r.find("ThingDef[defName='Grechka_WarriorDroneT5_Unit']/race/baseHealthScale"), "text", "0.1"))
        self.rejected('tier progression decreases health')

    def test_ranged_verb(self):
        self.edit_xml("Defs/Drones.xml", lambda r: ET.SubElement(ET.SubElement(r[0], "verbs"), "li"))
        self.rejected('ranged verbs')

    def test_optional_recipe_bad_target(self):
        self.edit_xml("Patches/Industry.xml", lambda r: setattr(r.find("Operation/match/operations/li/xpath"), "text", "Defs/ThingDef[defName='Absent']/costList"))
        self.rejected('targets external definitions|match one target')

    def test_optional_recipe_unknown_material(self):
        def mutate(r):
            cost = r.find("Operation/match/operations/li/value/costList")
            cost[0].tag = "Grechka_NonexistentHousing_Item"
        self.edit_xml("Patches/Industry.xml", mutate)
        self.rejected('lacks tier housing/component')

    def test_optional_content_made_required(self):
        def mutate(r):
            dep = ET.SubElement(r.find("modDependencies"), "li")
            ET.SubElement(dep, "packageId").text = "grechka.industry"
        self.edit_xml("About/About.xml", mutate)
        self.rejected('Industry must remain optional')

    def test_profile_missing_addon(self):
        profile = self.root / "profiles/GrechkaWorld/mods.json"
        data = json.loads(profile.read_text())
        profile.write_text(json.dumps([entry for entry in data if entry["package"] != "grechka.drones"]))
        self.rejected('Manifest and active profile differ')


if __name__ == "__main__":
    unittest.main()
