from pathlib import Path
import shutil
root=Path(__file__).resolve().parent
stage=root/'build';stage.mkdir(exist_ok=True)
shutil.copytree(root/'vendor/PresentData',stage/'vendor/PresentData',dirs_exist_ok=True)
core=stage/'vendor/IntelPresentMon/CommonUtilities';core.mkdir(parents=True,exist_ok=True)
for n in ['Hash.h','Meta.h','SampleStatistics.h']:shutil.copy2(root/'vendor/IntelPresentMon/CommonUtilities'/n,core/n)
for n in ['Debug.hpp','TraceLogging.h','PresentMonTraceSession.hpp']:
 p=stage/'vendor/PresentData'/n;s=p.read_text(encoding='utf-8-sig')
 for inc in ['../IntelPresentMon/CommonUtilities/log/Log.h','../IntelPresentMon/CommonUtilities/Exception.h','../IntelPresentMon/CommonUtilities/PrecisionWaiter.h']:
  s=s.replace('#include "'+inc+'"','#include "../../Support.hpp"')
 p.write_text(s,encoding='utf-8')
# Upstream clears its transient map key during cleanup. Retain the observed HWND for output only.
p=stage/'vendor/PresentData/PresentMonTraceConsumer.hpp';s=p.read_text(encoding='utf-8-sig')
s=s.replace('    uint64_t Hwnd;', '    uint64_t CompletedWindow = 0; // ECP: observed HWND retained after tracking cleanup\n    uint64_t Hwnd;')
p.write_text(s,encoding='utf-8')
p=stage/'vendor/PresentData/PresentMonTraceConsumer.cpp';s=p.read_text(encoding='utf-8-sig')
s=s.replace('        p->Hwnd = 0;', '        p->CompletedWindow = p->Hwnd; // ECP output attribution; no change to tracking\n        p->Hwnd = 0;')
p.write_text(s,encoding='utf-8')
shutil.copy2(root/'Support.hpp',stage/'Support.hpp');shutil.copy2(root/'ECPFrames.cpp',stage/'ECPFrames.cpp')
