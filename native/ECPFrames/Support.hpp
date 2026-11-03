// ECP console support adapter; ETW event decoding remains upstream PresentData.
#pragma once
#include <windows.h>
#include <chrono>
#include <thread>
#include <string>
#include <stdexcept>
#include <exception>
#include <functional>
namespace pmon::util {
inline int64_t GetCurrentTimestamp() { LARGE_INTEGER q{}; QueryPerformanceCounter(&q); return q.QuadPart; }
inline double GetTimestampPeriodSeconds() { LARGE_INTEGER q{}; QueryPerformanceFrequency(&q); return 1.0/q.QuadPart; }
inline double TimestampDeltaToSeconds(int64_t from,int64_t to,double period) { return (to-from)*period; }
class PrecisionWaiter { public: explicit PrecisionWaiter(double=0.001) noexcept {} double Wait(double seconds,bool=false) noexcept { auto before=GetCurrentTimestamp(); if(seconds>0) std::this_thread::sleep_for(std::chrono::duration<double>(seconds)); return (GetCurrentTimestamp()-before)*GetTimestampPeriodSeconds(); } };
template<class T> T Except(const char* message) { return T(message); }
inline std::string ReportException() { try { throw; } catch(const std::exception& e) { return e.what(); } catch(...) { return "ETW decode exception"; } }
namespace str { inline std::string ToNarrow(const std::wstring& s) { int n=WideCharToMultiByte(CP_UTF8,0,s.data(),(int)s.size(),nullptr,0,nullptr,nullptr); std::string r(n,0); WideCharToMultiByte(CP_UTF8,0,s.data(),(int)s.size(),r.data(),n,nullptr,nullptr); return r; } }
namespace log {
enum class Level { Error,Warning,Info,Debug };
struct GlobalPolicy { static GlobalPolicy Get() { return {}; } Level GetLogLevel() { return Level::Warning; } };
// stdout is exclusively the frame protocol. ECPFrames reports startup / lost events explicitly.
struct Entry { template<class T> Entry& pmwatch(const T&) { return *this; } template<class T> Entry& hr(const T&) { return *this; } template<class T> Entry& note(const T&) { return *this; } operator bool() const { return false; } };
}
namespace hash { inline size_t HashCombine(size_t a,size_t b) noexcept { return a^(b+0x9e3779b9+(a<<6)+(a>>2)); } }
}
#define PM_DEFINE_EX(name) class name : public std::runtime_error { public: using std::runtime_error::runtime_error; }
#define pmlog_warn(...) pmon::util::log::Entry{}
#define pmlog_dbg(...) pmon::util::log::Entry{}
#define pmlog_info(...) pmon::util::log::Entry{}
#define pmlog_error(...) pmon::util::log::Entry{}
#define pmlog_(...) pmon::util::log::Entry{}
