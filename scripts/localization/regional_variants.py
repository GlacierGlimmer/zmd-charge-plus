"""Authored regional spellings and playful English variants. Never alter code tokens."""
import copy
import json
import re
from pathlib import Path

PROTECTED=re.compile(r"\bv(?=\{[0-9])|\$\{[^{}]*\}|\{[^{}]*\}|(?:https?://|avares://)\S+|(?<![\w/])/(?:[A-Za-z0-9_.*-]+/)*[A-Za-z0-9_.*-]+|\b[A-Z]:\\[^\s,;]*|\b(?:yyyy-MM-dd(?: HH:mm:ss)?|HH:mm:ss|HH:MM:SS)\b|\b(?:docker ps -q|docker images -q|java -version|go version|node --version|rustc --version|python/py --version|wsl -l -q|nvidia-smi power\.limit)\b|\b[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_<>-]+)+|\b(?:[a-z]+[A-Z]|[A-Z][a-z]+[A-Z])[A-Za-z0-9]*\b|\b[A-Z][A-Z0-9_]{1,}\b|\b(?:Ctrl|Alt|Shift|F\d+|HUD|CPU|GPU|UI|API|JSON|HTTP|WMI|ID|Windows|Linux|macOS|Android|AIDA64|PawnIO|DeepSeek|Endfield Charge Plus|System|Memory|Success|Connected|Response|TimedOut)\b|\b(?:duration-long|duration|bytes|speed|upper|lower|auto:[0-9]+|(?:kb|mb|gb|tb):[0-9]+|time:[A-Za-z:]+|math:[a-z]+(?::[-0-9.]+)?|sub:[0-9:]+|replace:[^\s,;]+)\b")

def transform_prose(text, transform):
    output=[];position=0
    for match in PROTECTED.finditer(text):
        output.append(transform(text[position:match.start()]));output.append(match.group());position=match.end()
    output.append(transform(text[position:]));return ''.join(output)

def words(mapping):
    pattern=re.compile(r"\b("+'|'.join(re.escape(k) for k in sorted(mapping,key=len,reverse=True))+r")\b",re.I)
    def transform(value):
        def replace(match):
            original=match.group();next=mapping[original.lower()]
            return next.upper() if original.isupper() else next[0].upper()+next[1:] if original[0].isupper() else next
        return pattern.sub(replace,value)
    return transform

BRITISH=words({'color':'colour','colors':'colours','center':'centre','centered':'centred','license':'licence','customize':'customise','customized':'customised','initialize':'initialise','initialized':'initialised'})
PORTUGAL=words({'arquivo':'ficheiro','arquivos':'ficheiros','tela':'ecrã','telas':'ecrãs','usuário':'utilizador','usuários':'utilizadores','salvar':'guardar','salvo':'guardado','salvos':'guardados','gerenciar':'gerir','gerenciamento':'gestão','configurações':'definições','aplicativo':'aplicação','aplicativos':'aplicações','selecionado':'selecionado','monitoramento':'monitorização'})
PIRATE=words({'settings':'rigging','save':'stow','apply':'hoist','done':'all aboard','cancel':'belay','your':'yer','my':'me','you':'ye','yes':'aye','no':'nay','exit':'abandon ship','close':'batten down','search':'scout','help':'shipmate’s guide','about':'ship’s log','open':'unseal','delete':'send overboard','remove':'cast off','enabled':'aboard','disabled':'ashore','enable':'bring aboard','disable':'send ashore','error':'trouble ahead','failed':'ran aground','warning':'look alive','choose':'pick','language':'lingo','languages':'lingos','current':'aboard now','refresh':'take another sighting','preview':'spyglass','profile':'chart','profiles':'charts','backup':'spare chart','network':'sea lanes','window':'porthole','windows':'portholes','device':'vessel','devices':'vessels','unknown':'uncharted','unavailable':'lost at sea','completed':'shipshape','complete':'shipshape','copy':'copy aboard','restore':'haul back','next':'next aboard','previous':'astern'})
SHAKESPEARE=words({'settings':'ordinances','save':'preserve','apply':'enact','done':'it is done','cancel':'forbear','your':'thy','help':'a gentle guide','about':'of this work','choose':'select','error':'misfortune','failed':'came to naught','warning':'take heed','unknown':'yet unknown','unavailable':'not at hand','preview':'a foretaste','enabled':'awakened','disabled':'at rest','enable':'awaken','disable':'put to rest','language':'tongue','languages':'tongues','exit':'farewell','delete':'erase','remove':'dismiss','search':'seek','reset':'begin anew','display':'show','click':'press'})
ANGLISH=words({'settings':'set-up','configuration':'set-up','configure':'set up','experimental':'try-out','features':'crafts','data':'facts','management':'keeping','variable':'shiftword','variables':'shiftwords','language':'tongue','languages':'tongues','interface':'workface','global':'whole-app','scale':'size-step','information':'knowledge','instructions':'how-to','manual':'handbook','application':'worktool','applications':'worktools','select':'choose','selection':'choice','selected':'chosen','enable':'turn on','disable':'turn off','enabled':'on','disabled':'off','position':'stead','display':'showing','error':'fault','required':'needed','administrator':'overseer','default':'first-set','current':'now','previous':'former','next':'following','source':'well','sources':'wells','profile':'set','profiles':'sets','custom':'own-made','customize':'shape','delete':'wipe','remove':'take away','import':'bring in','export':'send out','advanced':'further','network':'webwork','memory':'mindstore','progress':'headway','duration':'lasting time','opacity':'covering','percent':'in a hundred','percentage':'share in a hundred','system':'workframe','automatic':'self-working','automatically':'by itself','immediate':'straightaway','immediately':'straightaway','animation':'moving show'})
LOLCAT=words({'settings':'setty thingz','save':'keepz','apply':'do eet','done':'iz done','cancel':'do not want','your':'ur','you':'u','is':'iz','are':'r','the':'da','this':'dis','that':'dat','with':'wif','please':'plz','language':'speek','languages':'speeks','search':'findz','help':'halp','about':'about dis','enable':'turnz on','disable':'turnz off','enabled':'iz on','disabled':'iz off','error':'oopsie','failed':'haz failed','warning':'watch outz','unavailable':'no haz','unknown':'dunno','choose':'pickz','copy':'copiez','remove':'takes away','delete':'deletz','preview':'peekz','profile':'setty','profiles':'settiez','window':'peek-hole','windows':'peek-holez','refresh':'lookz again','reset':'startz over','exit':'bai bai','open':'openz','font':'letterz','size':'bigness','global':'all da','data':'infoz','hardware':'computery bitz','variables':'changey thingz','variable':'changey thing'})
FLIP=dict(zip('abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,!?()[]','ɐqɔpǝɟᵷɥᴉɾʞꞁɯuodბɹsʇnʌʍxʎz∀𐐒ƆᗡƎℲ⅁HIſ⋊⅂WNOԀΌᴚS⊥∩ΛMX⅄Z0ƖᄅƐㄣϛ9ㄥ86˙‘¡¿)(]['))
def upside(value):
    # Rotate prose words individually: punctuation, formula order, numeric values
    # and the boundary next to an immutable token must remain executable.
    return re.sub(r"[A-Za-z]+",lambda m: ''.join(FLIP.get(c,c) for c in reversed(m.group())),value)


def build(packs:Path):
    def read(code):return json.loads((packs/(code+'.json')).read_text(encoding='utf-8'))
    def save(code,pack):
        (packs/(code+'.json')).write_text(json.dumps(pack,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    equivalents={'en-AU':'en-US','en-CA':'en-US','en-GB':'en-US','en-NZ':'en-US','de-AT':'de-DE','de-CH':'de-DE','fr-CA':'fr-FR','fr-CH':'fr-FR','es-AR':'es-ES','es-CL':'es-ES','es-EC':'es-ES','es-MX':'es-ES','es-UY':'es-ES','es-VE':'es-ES','pt-PT':'pt-BR','zh-HK':'zh-TW','fil-PH':'tl-PH','nl-BE':'nl-NL','val-ES':'ca-ES'}
    variants={'en-x-pirate':('en-US',PIRATE),'en-x-shakespeare':('en-US',SHAKESPEARE),'en-x-anglish':('en-US',ANGLISH),'en-x-lolcat':('en-US',LOLCAT),'en-x-upside-down':('en-US',upside)}
    for target,source in equivalents.items():
        if not (packs/(source+'.json')).exists():continue
        pack=read(source)
        if target in ['en-AU','en-GB','en-NZ']:pack={k:transform_prose(v,BRITISH) for k,v in pack.items()}
        if target=='de-CH':pack={k:v.replace('ß','ss') for k,v in pack.items()}
        if target=='pt-PT':pack={k:transform_prose(v,PORTUGAL) for k,v in pack.items()}
        save(target,pack)
        manual=packs/('manual.'+source+'.json')
        if manual.exists():
            doc=json.loads(manual.read_text(encoding='utf-8'))
            if target=='pt-PT':doc={k:[transform_prose(t,PORTUGAL) for t in value] for k,value in doc.items()}
            (packs/('manual.'+target+'.json')).write_text(json.dumps(doc,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for target,(source,transform) in variants.items():save(target,{k:transform_prose(v,transform) for k,v in read(source).items()})
    return equivalents,variants

if __name__=='__main__':build(Path(__file__).resolve().parents[2]/'Localization')
