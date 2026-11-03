// ECP protocol for Intel PresentMon's actual displayed ETW frames.
#include "Support.hpp"
#include "vendor/PresentData/PresentMonTraceConsumer.hpp"
#include "vendor/PresentData/PresentMonTraceSession.hpp"
#include <atomic>
#include <cstdio>
#include <thread>
#include <vector>
#include <unordered_set>
#include <cwchar>
int wmain(int argc,wchar_t** argv) {
    std::wstring name; const wchar_t* etl=nullptr; DWORD owner=0;
    for(int i=1;i<argc;i++) {
        if(!wcscmp(argv[i],L"--session_name") && i+1<argc) name=argv[++i];
        else if(!wcscmp(argv[i],L"--owner") && i+1<argc) owner=wcstoul(argv[++i],nullptr,10);
        else if(!wcscmp(argv[i],L"--etl_file") && i+1<argc) etl=argv[++i];
        else { fprintf(stderr,"error: invalid collector argument\n"); return 2; }
    }
    if(name.empty() || name.size()>200) { fprintf(stderr,"error: missing session name\n"); return 2; }
    HANDLE parent=owner ? OpenProcess(SYNCHRONIZE,FALSE,owner) : nullptr;
    if(owner && !parent) { fprintf(stderr,"error: owner process unavailable\n"); return 3; }
    PMTraceConsumer consumer(65536); consumer.mTrackDisplay=true; consumer.mTrackFrameType=true;
    PMTraceSession session; session.mPMConsumer=&consumer;
    ULONG code=session.Start(etl,name.c_str());
    if(code) { fprintf(stderr,"error: ETW start %lu%s\n",code,code==ERROR_ACCESS_DENIED ? " (access denied)":""); if(parent) CloseHandle(parent); return (int)code; }
    std::atomic<bool> done=false; ULONG processCode=0;
    std::thread processing([&] { processCode=ProcessTrace(&session.mTraceHandle,1,nullptr,nullptr); done=true; });
    printf("ProcessID,WindowHandle,SwapChainAddress,VidPnSourceId,LayerIndex,DisplayedTimestamp\n"); fflush(stdout);
    auto drain=[&] {
        std::vector<std::shared_ptr<PresentEvent>> events; consumer.DequeuePresentEvents(events);
        for(auto& p:events) {
            if(p->IsLost || p->PresentFailed || p->FinalState!=PresentResult::Presented) continue;
            HWND window=(HWND)(uintptr_t)p->CompletedWindow; if(window && !etl) { auto root=GetAncestor(window,GA_ROOT); if(root) window=root; }
            for(auto& [type,qpc]:p->Displayed) {
                if(!qpc || type==FrameType::Repeated) continue;
                double seconds=(double)qpc/session.mTimestampFrequency.QuadPart;
                if(p->ReportedPresentIds.empty()) printf("%u,0x%llx,0x%llx,,,%.9f\n",p->ProcessId,(unsigned long long)(uintptr_t)window,(unsigned long long)p->SwapChainAddress,seconds);
                else { std::unordered_set<uint64_t> emitted; for(auto& [layer,id]:p->ReportedPresentIds) if(emitted.insert(layer).second)
                    printf("%u,0x%llx,0x%llx,%u,%u,%.9f\n",p->ProcessId,(unsigned long long)(uintptr_t)window,(unsigned long long)p->SwapChainAddress,(uint32_t)(layer>>32),(uint32_t)layer,seconds);
                }
            }
        }
        fflush(stdout);
    };
    while(!done) {
        drain();
        if(parent && WaitForSingleObject(parent,0)==WAIT_OBJECT_0) { session.Stop(); break; }
        std::this_thread::sleep_for(std::chrono::milliseconds(40));
    }
    processing.join(); drain();
    if(session.mNumEventsLost || session.mNumBuffersLost) fprintf(stderr,"error: ETW lost events=%lu buffers=%lu\n",session.mNumEventsLost,session.mNumBuffersLost);
    session.Stop(); if(parent) CloseHandle(parent);
    if(processCode!=ERROR_SUCCESS && processCode!=ERROR_CANCELLED) fprintf(stderr,"error: ProcessTrace %lu\n",processCode);
    return processCode==ERROR_CANCELLED ? 0 : (int)processCode;
}
