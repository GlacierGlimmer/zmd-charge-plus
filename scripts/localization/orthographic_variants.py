"""Script and historical-spelling variants; inputs are ECP-authored translations."""
import json
import re
from pathlib import Path
from regional_variants import transform_prose

SERBIAN=dict(zip('АБВГДЂЕЖЗИЈКЛЉМНЊОПРСТЋУФХЦЧЏШ','A B V G D Đ E Ž Z I J K L Lj M N Nj O P R S T Ć U F H C Č Dž Š'.split()))
SERBIAN.update({k.lower():v.lower() for k,v in list(SERBIAN.items())})

def serbian(value):
    return ''.join(SERBIAN.get(char,char) for char in value)

BELARUSIAN=dict(zip('АБВГДЕЁЖЗІЙКЛМНОПРСТУЎФХЦЧШЫЬЭЮЯ','A B V H D Je Jo Ž Z I J K Ł M N O P R S T U Ŭ F Ch C Č Š Y _ E Ju Ja'.split()))
BELARUSIAN.update({k.lower():v.lower() for k,v in list(BELARUSIAN.items())})
def belarusian(value):
    soft={'ль':'l','сь':'ś','зь':'ź','ць':'ć','нь':'ń','Ль':'L','Сь':'Ś','Зь':'Ź','Ць':'Ć','Нь':'Ń'}
    for original,changed in soft.items():value=value.replace(original,changed)
    return ''.join(BELARUSIAN.get(char,char).replace('_','') for char in value)

def old_indonesian(value):
    spelling={'sy':'sj','ny':'nj','kh':'ch','u':'oe','j':'dj','c':'tj','y':'j'}
    def word(match):
        text=match.group();result=re.sub('sy|ny|kh|[ujcy]',lambda m:spelling[m.group()],text.lower())
        return result.upper() if text.isupper() else result[:1].upper()+result[1:] if text[0].isupper() else result
    return re.sub(r'[^\W\d_]+',word,value)

def old_russian(value):
    roots={'размер':'размѣр','отмен':'отмѣн','примен':'примѣн','перемен':'перемѣн','мест':'мѣст','сведен':'свѣдѣн','сеть':'сѣть','сети':'сѣти','нет':'нѣт','все':'всѣ','всех':'всѣх','здесь':'здѣсь'}
    def word(match):
        original=match.group();text=original.lower()
        for before,after in roots.items():
            if text.startswith(before):text=after+text[len(before):];break
        text=re.sub('и(?=[аеёиоуыэюяйѣ])','і',text)
        if text[-1:] in 'бвгджзклмнпрстфхцчшщ':text+='ъ'
        return text.upper() if original.isupper() else text[:1].upper()+text[1:] if original[0].isupper() else text
    return re.sub('[А-Яа-яЁё]+',word,value)

def build(packs:Path):
    variants={'sr-Latn':('sr-Cyrl',serbian),'be-Latn':('be-BY',belarusian),'id-x-old':('id-ID',old_indonesian),'ru-x-old':('ru-RU',old_russian)}
    for target,(source,transform) in variants.items():
        original=packs/(source+'.json')
        if not original.exists():continue
        value=json.loads(original.read_text(encoding='utf-8'))
        (packs/(target+'.json')).write_text(json.dumps({k:transform_prose(v,transform) for k,v in value.items()},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        guide=packs/('manual.'+source+'.json')
        if guide.exists():
            value=json.loads(guide.read_text(encoding='utf-8'))
            (packs/('manual.'+target+'.json')).write_text(json.dumps({k:[transform_prose(v,transform) for v in values] for k,values in value.items()},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return variants

if __name__=='__main__':build(Path(__file__).resolve().parents[2]/'Localization')
