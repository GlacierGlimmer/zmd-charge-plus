"""Small fixtures for the fail-closed export rule; no app build or export."""
import json
from pathlib import Path
import shutil
import unittest
import uuid

import completion


class CompletionChecks(unittest.TestCase):
    def setUp(self):
        self.base = Path(__file__).resolve().parents[2]/'artifacts/translation-gate-tests'
        self.base.mkdir(parents=True,exist_ok=True)
        self.repo = self.base/uuid.uuid4().hex
        self.repo.mkdir()
        self.packs = self.repo/'Localization'
        self.packs.mkdir()
        self.write('reviewed-language-catalog.json', {'languages':[{'code':'fr-FR'}]})
        self.required = ['Save settings', 'Status', 'Value {0}', 'Power limit']
        self.requirements = dict(requiredKeys=self.required, sourceSha256='fixture-source', localeCodes=['fr-FR'], englishFamily=['en-US'], technicalStrings=[], englishManualText=['Show your first HUD'], sourceTokens={'Power limit': {'power.limit':1}})
        self.write('reviewed-release-requirements.json', self.requirements)
        self.messages = {'Save settings':'Enregistrer les réglages','Status':'Status','Value {0}':'Valeur {0}','Power limit':'Limite selon power.limit'}
        self.guide = {'_labels':['Étapes','Détails','Exemple','Explication','Référence']}
        self.guide.update({chapter:['Titre','Résumé','Un\nDeux\nTrois','Description locale'] for chapter in completion.CHAPTERS})
        self.write('manual.fr-FR.json', self.guide)
        self.write('reviewed-shared-words.json', {'fr-FR':{'Status':'A deliberately reviewed fixture word with the same spelling.'}})
        self.save_messages_and_review()

    def tearDown(self):
        # Verify the resolved disposable directory before recursive cleanup.
        assert self.repo.resolve().is_relative_to(self.base.resolve()) and self.repo.resolve()!=self.base.resolve()
        shutil.rmtree(self.repo)
    def write(self, name, value):
        (self.packs/name).write_text(json.dumps(value,ensure_ascii=False),encoding='utf-8')
    def save_messages_and_review(self):
        self.write('fr-FR.json', self.messages)
        self.write('reviewed-language-completion.json', {'fr-FR':dict(sourceSha256='fixture-source', messagesSha256=completion.digest(self.packs/'fr-FR.json'),manualSha256=completion.digest(self.packs/'manual.fr-FR.json'),checkedBy='Fixture author',crossCheckedBy='Fixture reviewer')})
    def result(self): return completion.audit_repo(self.repo)['languages'][0]

    def test_complete_files_and_independent_review_pass(self):
        self.assertTrue(self.result()['complete'])

    def test_missing_key_blocks_export(self):
        del self.messages['Save settings']; self.save_messages_and_review()
        self.assertIn('Save settings',self.result()['missing'])
        self.assertFalse(self.result()['complete'])

    def test_plain_english_blocks_export(self):
        self.messages['Save settings']='Save settings';self.save_messages_and_review()
        self.assertIn('Save settings',self.result()['english'])

    def test_case_or_full_stop_does_not_translate_a_label(self):
        self.assertTrue(completion.english_fallback('Duration','duration',set(),{},False))
        self.assertTrue(completion.english_fallback('Current Time.','Current time',set(),{},False))
        self.assertFalse(completion.english_fallback('API Status','API-status',set(),{},False))

    def test_translated_prefix_does_not_hide_english_paragraph(self):
        key='A long English paragraph which must be fully translated.'
        self.assertTrue(completion.english_fallback(key,'Traduit : '+key,set(),{},False))

    def test_shared_word_requires_specific_review(self):
        self.write('reviewed-shared-words.json',{})
        self.assertIn('Status',self.result()['english'])
        self.assertEqual(completion.valid_shared({'A whole sentence must not be whitelisted.':'A long enough explanation.'}),{})

    def test_changed_numbered_argument_blocks_export(self):
        self.messages['Value {0}']='Valeur {1}';self.save_messages_and_review()
        self.assertIn('Value {0}',self.result()['syntax'])

    def test_source_identifier_in_rich_definition_is_allowed(self):
        self.assertNotIn('Power limit',self.result()['syntax'])

    def test_native_case_ending_is_not_part_of_cited_identifier(self):
        self.assertEqual(completion.tokens('vm.swapusage-ийн'),completion.tokens('vm.swapusage'))
        self.assertNotEqual(completion.tokens('{vm.swapusage-}'),completion.tokens('{vm.swapusage}'))

    def test_dutch_compound_preserves_source_api_stem(self):
        self.assertTrue(completion.syntax_valid('Node Version','Node.js-versie','nl-NL',{'sourceTokens':{'Node Version':{'Node.js':1}}}))
        self.assertFalse(completion.syntax_valid('{foo.bar}','{foo.bar-versie}','nl-NL',{}))

    def test_code_lookup_id_with_explanation_cannot_be_silently_unchanged(self):
        key='SystemClock.elapsedRealtime()/1000.'
        self.assertTrue(completion.english_fallback(key,key,set(),{},False))

    def test_short_native_template_can_be_reviewed_as_shared(self):
        self.assertIn('Type {0}',completion.valid_shared({'Type {0}':'Type is the standard Dutch word for this category.'}))

    def test_unknown_identifier_is_rejected(self):
        self.messages['Power limit']='Limite selon invented.path';self.save_messages_and_review()
        self.assertIn('Power limit',self.result()['syntax'])

    def test_edit_after_review_invalidates_review(self):
        self.messages['Save settings']='Enregistrer';self.write('fr-FR.json',self.messages)
        self.assertTrue(self.result()['review'])

    def test_author_cannot_be_own_cross_checker(self):
        review=completion.read(self.packs/'reviewed-language-completion.json')
        review['fr-FR']['crossCheckedBy']=review['fr-FR']['checkedBy']
        self.write('reviewed-language-completion.json',review)
        self.assertFalse(self.result()['complete'])

    def test_missing_native_manual_blocks_export(self):
        (self.packs/'manual.fr-FR.json').unlink()
        self.assertTrue(self.result()['manual'])

    def test_english_manual_body_blocks_export(self):
        self.guide['start'][3]='Show your first HUD';self.write('manual.fr-FR.json',self.guide)
        self.save_messages_and_review()
        self.assertTrue(self.result()['manual'])

    def test_replacement_glyph_blocks_export(self):
        self.messages['Save settings']='Traduction \ufffd';self.save_messages_and_review()
        self.assertTrue(self.result()['malformed'])

    def test_duplicate_json_key_is_rejected(self):
        (self.packs/'fr-FR.json').write_text('{"Status":"un","Status":"deux"}',encoding='utf-8')
        self.assertTrue(self.result()['malformed'])

    def platform_fixture(self):
        english={'_labels':['Steps','Details','Example','Code','Functions'],'linux':{chapter:['Native guide','Actual source','Read\nCheck\nSave','Store the key privately in secrets/master.key before moving accounts.'] for chapter in completion.NATIVE_CHAPTERS}}
        native={'_labels':['Étapes','Détails','Exemple','Code','Fonctions'],'linux':{chapter:['Guide natif','Source réelle','Lire\nVérifier\nEnregistrer','La clé privée se trouve dans secrets/master.key ; un autre compte peut nécessiter une nouvelle saisie.'] for chapter in completion.NATIVE_CHAPTERS}}
        self.write('platform-en.json',english);self.write('platform-fr.json',native)
        return native

    def test_native_platform_guide_requires_native_content(self):
        self.platform_fixture()
        self.assertEqual(completion.platform_manual_errors(self.packs/'platform-fr.json','linux',self.packs/'platform-en.json',False),[])
        self.assertTrue(completion.platform_manual_errors(self.packs/'platform-en.json','linux',self.packs/'platform-en.json',False))

    def test_native_platform_guide_cannot_drop_key_file_identifier(self):
        document=self.platform_fixture()
        document['linux']['maintenance'][3]='Une clé privée est utilisée.'
        self.write('platform-fr.json',document)
        self.assertTrue(completion.platform_manual_errors(self.packs/'platform-fr.json','linux',self.packs/'platform-en.json',False))

    def test_missing_platform_guide_is_not_replaced_by_windows_prose(self):
        self.platform_fixture()
        self.assertTrue(completion.platform_manual_errors(self.packs/'absent.json','linux',self.packs/'platform-en.json',False))

    def test_android_section_anchors_are_unique(self):
        guide=[dict(id=chapter,title='Titre',summary='Résumé',blocks=[dict(id='guide',title='Étapes',body='Un'),dict(id='guide',title='Détails',body='Deux')]) for chapter in completion.CHAPTERS]
        self.write('android.json',guide)
        self.assertTrue(any('anchor' in value for value in completion.manual_errors(self.packs/'android.json',True,set(),False)))


if __name__=='__main__':unittest.main()
