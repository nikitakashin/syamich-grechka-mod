import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('tier_intake', REPO / 'scripts/sync-tier-artwork.py')
intake = importlib.util.module_from_spec(spec); spec.loader.exec_module(intake)

class TierIntakeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        p = self.root/'mods/GrechkaWorldIntegration/Defs/MachineProfile.xml'; p.parent.mkdir(parents=True)
        p.write_text('<Defs><Rule><packageId>fixture.mod</packageId><targetDefName>Bench</targetDefName><minimumTier>3</minimumTier><addHousing>true</addHousing></Rule></Defs>')
        output=io.BytesIO(); Image.new('RGBA',(2,2),(150,150,150,255)).save(output,format='PNG'); self.png=output.getvalue()
        self.registry=self.root/'review.json'
        self.record={'packageId':'fixture.mod','sourceDefName':'Bench','tier':3,'kind':'Multi','reviewed':True,'sha256':{}}
        for view in intake.DIRECTIONS:
            p=self.root/f'assets/GrechkaWorldIntegration/Textures/TierSprites/fixture.mod/Bench/Bench_T3_{view}.png'
            p.parent.mkdir(parents=True,exist_ok=True); p.write_bytes(self.png)
            self.record['sha256'][view]=hashlib.sha256(self.png).hexdigest()
    def tearDown(self): self.temp.cleanup()
    def prepare(self,records=None):
        self.registry.write_text(json.dumps({'version':1,'records': records if records is not None else [self.record]}))
        return intake.prepare(self.root,self.registry)
    def test_complete_set_keeps_reviewed_bytes_and_does_not_mutate_before_apply(self):
        copies,xml=self.prepare(); self.assertEqual(len(copies),4)
        self.assertIn(b'<tier>3</tier>',xml)
        self.assertFalse(any(p.exists() for p,_ in copies))
        # Another art session edits the source after validation: the staged bytes remain the reviewed ones.
        next((self.root/'assets').rglob('*.png')).write_bytes(b'new unreviewed draft')
        for p,b in copies: intake.write_atomic(p,b); self.assertEqual(p.read_bytes(),self.png)
    def test_changed_hash_rejects_entire_batch(self):
        self.record['sha256']['west']='0'*64
        with self.assertRaisesRegex(ValueError,'Changed/unreviewed'): self.prepare()
        self.assertFalse((self.root/'mods/GrechkaWorldIntegration/Textures').exists())
    def test_missing_view_never_generates_binding(self):
        del self.record['sha256']['east']
        with self.assertRaisesRegex(ValueError,'Incomplete'): self.prepare()
    def test_below_minimum_and_unreviewed_rejected(self):
        self.record['tier']=2
        with self.assertRaisesRegex(ValueError,'profile tier'): self.prepare()
        self.record['tier']=3; self.record['reviewed']=False
        with self.assertRaisesRegex(ValueError,'not reviewed'): self.prepare()
    def test_duplicate_path_traversal_and_custom_renderer_rejected(self):
        with self.assertRaisesRegex(ValueError,'Duplicate'): self.prepare([self.record,self.record])
        self.record['sourceDefName']='../Bench'
        with self.assertRaisesRegex(ValueError,'Unsafe'): self.prepare()
        self.record['sourceDefName']='Bench';self.record['kind']='DarklightMulti'
        with self.assertRaisesRegex(ValueError,'adapter'): self.prepare()

    def test_native_states_are_verified_before_publish_and_keep_runtime_facing_suffix(self):
        package, name = 'vanillaexpanded.vcooke', 'VCE_CheesePress'
        rule = self.root/'mods/GrechkaWorldIntegration/Defs/MachineProfile.xml'
        rule.write_text(rule.read_text().replace('fixture.mod', package).replace('Bench', name))
        self.record.update(packageId=package, sourceDefName=name, stateHashes={'full': dict(self.record['sha256'])})
        root = self.root/'assets/GrechkaWorldIntegration/Textures/TierSprites'/package/name
        root.mkdir(parents=True)
        for side in intake.DIRECTIONS:
            for state in ('empty', 'full'):
                (root/f'{name}_T3_{side}__{state}.png').write_bytes(self.png)
        copies, xml = self.prepare()
        self.assertEqual({p.name for p,_ in copies},
            {f'{name}_T3{suffix}_{side}.png' for suffix in ('','__full') for side in intake.DIRECTIONS})
        self.assertIn(b'<li>full</li>', xml)
        del self.record['stateHashes']['full']['west']
        with self.assertRaisesRegex(ValueError, 'Incomplete native state'):
            self.prepare()

    def test_compact_delivery_keeps_original_and_verifies_original_hash(self):
        source = next((self.root/'assets').rglob('*.png'))
        digest = hashlib.sha256(self.png).hexdigest()
        result = intake.verify_frame(source, digest, 0.5)
        self.assertEqual(source.read_bytes(), self.png)
        with Image.open(io.BytesIO(result)) as reduced:
            self.assertEqual((reduced.mode,reduced.size), ('RGBA',(1,1)))

if __name__=='__main__': unittest.main()
