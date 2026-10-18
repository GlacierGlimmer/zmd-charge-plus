"""Reuse reviewed wording for equivalent UI spellings across the desktop/mobile views."""
import re
def canonical(text):
    text=text.casefold().strip().replace('colour','color').replace('scheme','profile')
    text=re.sub(r'[\s]+',' ',text)
    text=text.rstrip('.…:')
    return text

ALIASES={
    'An update is available.':'Update available',
    'Active scheme':'Active profile',
    'Current scheme':'Current profile',
    'CPU usage / cpu.usage':'Search: CPU usage / cpu.usage',
    'API request failed':'Request failed',
    'Restart the app as administrator':'Restart as administrator',
    'No shortcut configured':'No shortcut configured.',
}

def complete(source,pack):
    # Ambiguous normalized keys are left for an explicit translation decision.
    candidates={}
    for key,value in pack.items():candidates.setdefault(canonical(key),set()).add(value)
    changed={}
    for key in source:
        if key in pack:continue
        alias=ALIASES.get(key)
        if alias in pack:changed[key]=pack[alias];continue
        matches=candidates.get(canonical(key),set())
        if len(matches)==1:changed[key]=next(iter(matches))
    return changed
