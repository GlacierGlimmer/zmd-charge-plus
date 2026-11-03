"""Derived languages must preserve executable examples and cited APIs."""
import unittest
from regional_variants import transform_prose,PIRATE,SHAKESPEARE,ANGLISH,LOLCAT,upside
from orthographic_variants import old_indonesian,old_russian,serbian,belarusian


class VariantChecks(unittest.TestCase):
    def test_code_paths_dates_commands_and_api_names_stay_exact(self):
        examples=['{cpu.usage|0.0}','${env:API_KEY}','/proc/stat','/sys/class/power_supply','/sys/dev/block/*/stat','C:\\Windows\\System32','yyyy-MM-dd HH:mm:ss','HH:MM:SS','docker ps -q','docker images -q','java -version','nvidia-smi power.limit','PowerOnHours','BatteryManager','EXTRA_HEALTH','Integer.MIN_VALUE','https://example.com/api','auto:1','duration-long']
        for transform in [PIRATE,SHAKESPEARE,ANGLISH,LOLCAT,upside,old_indonesian,old_russian,serbian,belarusian]:
            for example in examples:
                self.assertIn(example,transform_prose('Use '+example+' to display a value.',transform),(transform.__name__,example))

    def test_style_still_transforms_prose_around_immutable_syntax(self):
        source='Save your settings with {cpu.usage|0.0}.'
        result=transform_prose(source,PIRATE)
        self.assertNotEqual(source,result)
        self.assertIn('{cpu.usage|0.0}',result)


if __name__=='__main__':unittest.main()
