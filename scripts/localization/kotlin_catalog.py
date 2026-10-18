"""Collect Kotlin UI pairs without executing app code or contacting a translator."""
import argparse
import json
import re
from pathlib import Path

HAN=re.compile(r'[\u3400-\u9fff]')

def quoted(source,start):
    raw=source.startswith('"""',start)
    quote='"""' if raw else '"';i=start+len(quote);out=[];slots=[]
    while i<len(source):
        if source.startswith(quote,i):return ''.join(out),i+len(quote)
        char=source[i]
        if char=='\\' and not raw:
            i+=1
            if i>=len(source):break
            esc=source[i]
            if esc=='u' and re.fullmatch('[0-9a-fA-F]{4}',source[i+1:i+5]):out.append(chr(int(source[i+1:i+5],16)));i+=5;continue
            out.append({'n':'\n','r':'\r','t':'\t','b':'\b','"':'"',"'":"'",'\\':'\\','$':'$'}.get(esc,esc));i+=1;continue
        if char=='$' and i+1<len(source):
            if source[i+1]=='{':
                begin=i+2;i=begin;depth=1
                while i<len(source) and depth:
                    if source[i]=='"':_,i=quoted(source,i);continue
                    if source[i]=='{':depth+=1
                    elif source[i]=='}':depth-=1
                    i+=1
                slots.append(source[begin:i-1]);out.append('{'+str(len(slots)-1)+'}');continue
            variable=re.match(r'[A-Za-z_][A-Za-z0-9_]*',source[i+1:])
            if variable:slots.append(variable.group());out.append('{'+str(len(slots)-1)+'}');i+=len(variable.group())+1;continue
        out.append(char);i+=1
    raise ValueError('Unterminated Kotlin string')

def tokens(source):
    result=[];i=0
    while i<len(source):
        if source[i].isspace():i+=1;continue
        if source.startswith('//',i):i=source.find('\n',i);i=len(source) if i<0 else i;continue
        if source.startswith('/*',i):
            i+=2;depth=1
            while i<len(source) and depth:
                if source.startswith('/*',i):depth+=1;i+=2
                elif source.startswith('*/',i):depth-=1;i+=2
                else:i+=1
            continue
        if source[i]=='"':value,i=quoted(source,i);result.append(('string',value));continue
        if source[i]=="'":
            i+=1
            while i<len(source):
                if source[i]=='\\':i+=2;continue
                if source[i]=="'":i+=1;break
                i+=1
            continue
        word=re.match(r'[A-Za-z_][A-Za-z0-9_]*',source[i:])
        if word:result.append(('id',word.group()));i+=len(word.group());continue
        result.append(('symbol',source[i]));i+=1
    return result

def collect(root):
    catalog={};origins={}
    def add(zh,en,path):
        if not HAN.search(zh) or HAN.search(en) or not en.strip():return
        catalog.setdefault(en,zh);origins.setdefault(en,set()).add(path)
    for folder in ['app/src/main','core/src/main']:
        for path in (root/folder).rglob('*.kt'):
            source=path.read_text(encoding='utf-8-sig');t=tokens(source);name=str(path.relative_to(root)).replace('\\','/')
            if path.name=='VariableRegistry.kt':
                raw=re.search(r'private val RAW_TABLE: String = """(.*?)"""',source,re.S)
                if raw:
                    for line in raw.group(1).splitlines():
                        if not line.strip():continue
                        row=line.strip().split('|');assert len(row)==12,(name,len(row))
                        for zh,en in [(row[4],row[5]),(row[6],row[7]),(row[9],row[10])]:add(zh,en,name)
            for i in range(len(t)-4):
                # t(zh,en), s(zh,en), and equivalent local two-argument helpers.
                if t[i][0]=='id' and t[i+1][1]=='(' and t[i+2][0]=='string' and t[i+3][1]==',' and t[i+4][0]=='string':add(t[i+2][1],t[i+4][1],name)
                # mapOf(zh to en), including key-addressed EcpMessages pairs.
                if t[i][0]=='string' and t[i+1]==('id','to') and t[i+2][0]=='string':add(t[i][1],t[i+2][1],name)
                # Registry constructors use named label/description/note fields.
                if t[i][0]=='id' and (t[i][1].endswith('Zh') or t[i][1]=='androidNote') and t[i+1][1]=='=' and t[i+2][0]=='string':
                    wanted=t[i][1][:-2]+'En' if t[i][1].endswith('Zh') else 'androidNoteEn'
                    for j in range(i+3,min(i+200,len(t)-2)):
                        if t[j][0]=='id' and t[j][1]==t[i][1]:break
                        if t[j]==('id',wanted) and t[j+1][1]=='=' and t[j+2][0]=='string':add(t[i+2][1],t[j+2][1],name);break
    return catalog,{k:sorted(v) for k,v in origins.items()}

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('repository',type=Path);parser.add_argument('output',type=Path);args=parser.parse_args()
    catalog,origins=collect(args.repository);args.output.mkdir(parents=True,exist_ok=True)
    for name,value in [('catalog.android.zh-CN.json',catalog),('catalog.android.origins.json',origins)]:
        (args.output/name).write_text(json.dumps(value,ensure_ascii=False,indent=2,sort_keys=True)+'\n',encoding='utf-8')
    print('Android source catalog:',len(catalog),'messages')
