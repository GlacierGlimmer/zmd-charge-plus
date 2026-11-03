#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shellapi.h>
#include <cstdint>
#include <string>
#include <vector>
#include "host-generated.h"

// Use the same apphost entry contract as the self-contained .NET apphost.
// The CLR and application stay in this process, with this EXE as ProcessPath.
using HostMain = int(__cdecl*)(int, const wchar_t**, const wchar_t*, const wchar_t*, const wchar_t*);
using ErrorWriter = void(__cdecl*)(const wchar_t*);
using SetErrorWriter = ErrorWriter(__cdecl*)(ErrorWriter);
static std::wstring hostErrors;

static void __cdecl CaptureError(const wchar_t* message)
{
    if (message && hostErrors.size() < 4096)
        hostErrors.append(message, 0, 4096 - hostErrors.size()).append(L"\n");
}

static std::wstring FullPath(const std::wstring& path)
{
    const DWORD size = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
    if (!size) return path;
    std::vector<wchar_t> buffer(size);
    const DWORD used = GetFullPathNameW(path.c_str(), size, buffer.data(), nullptr);
    return used && used < size ? std::wstring(buffer.data(), used) : path;
}

static bool Same(const std::wstring& first, const wchar_t* second)
{
    return CompareStringOrdinal(first.c_str(), -1, second, -1, TRUE) == CSTR_EQUAL;
}

static const NativeMessages& Messages()
{
    ULONG count = 0, size = 0;
    GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &count, nullptr, &size);
    std::vector<wchar_t> names(size ? size : 1);
    if (size && GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &count, names.data(), &size))
    {
        for (const wchar_t* name = names.data(); *name; name += wcslen(name) + 1)
        {
            const std::wstring locale(name);
            const std::wstring primary = locale.substr(0, locale.find(L'-'));
            const wchar_t* selected = nullptr;
            if (Same(primary, L"zh"))
                selected = (Same(locale, L"zh-TW") || Same(locale, L"zh-HK") || Same(locale, L"zh-MO") ||
                    locale.find(L"Hant") != std::wstring::npos) ? L"zh-TW" : L"zh-CN";
            for (const auto& entry : nativeMessages)
                if ((selected && Same(entry.code, selected)) || (!selected && Same(primary, entry.code)))
                    return entry;
        }
    }
    return nativeMessages[0]; // English when the system language is unavailable.
}

static int Explain(const std::wstring& exe, const std::wstring& runtime,
    const std::wstring& details, DWORD error)
{
    const auto& text = Messages();
    std::wstring body = text.instructions;
    body.append(L"\n\n").append(text.location).append(L"\n").append(exe);
    body.append(L"\n\n").append(text.runtime).append(L"\n").append(runtime);
    if (!details.empty())
        body.append(L"\n\n").append(text.details).append(L"\n").append(details);
    if (error)
        body.append(L"\n\n0x").append([](DWORD value) {
            wchar_t code[9]{}; swprintf_s(code, L"%08lX", value); return std::wstring(code);
        }(error));
    MessageBoxW(nullptr, body.c_str(), text.title, MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    return static_cast<int>(error ? error : ERROR_FILE_NOT_FOUND);
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    std::vector<wchar_t> module(32768);
    const DWORD length = GetModuleFileNameW(nullptr, module.data(), static_cast<DWORD>(module.size()));
    if (!length || length >= module.size())
        return Explain(L"Endfield Charge Plus", L"Runtime", L"GetModuleFileNameW", GetLastError());
    const std::wstring exe(module.data(), length);
    const std::wstring directory = exe.substr(0, exe.find_last_of(L"\\/"));
    const std::wstring assembly = FullPath(directory + L"\\" + managedAssemblyPath);
    const std::wstring runtime = assembly.substr(0, assembly.find_last_of(L"\\/"));

    size_t invalid = 0;
    std::wstring missing;
    for (const auto& file : expectedFiles)
    {
        WIN32_FILE_ATTRIBUTE_DATA attributes{};
        const auto path = runtime + L"\\" + file.path;
        const bool found = GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &attributes) != FALSE;
        const auto size = (static_cast<uint64_t>(attributes.nFileSizeHigh) << 32) | attributes.nFileSizeLow;
        if (!found || (attributes.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || size != file.size)
        {
            if (invalid++ < 6) missing.append(file.path).append(L"\n");
        }
    }
    if (invalid)
    {
        if (invalid > 6) missing.append(L"… +").append(std::to_wstring(invalid - 6));
        return Explain(exe, runtime, missing, ERROR_FILE_NOT_FOUND);
    }

    DWORD previousMode = 0;
    SetThreadErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX, &previousMode);
    const auto library = LoadLibraryExW((runtime + L"\\hostfxr.dll").c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    const DWORD loadError = GetLastError();
    SetThreadErrorMode(previousMode, nullptr);
    if (!library) return Explain(exe, runtime, L"hostfxr.dll", loadError);
    const auto main = reinterpret_cast<HostMain>(GetProcAddress(library, "hostfxr_main_startupinfo"));
    const auto setWriter = reinterpret_cast<SetErrorWriter>(GetProcAddress(library, "hostfxr_set_error_writer"));
    if (!main || !setWriter) return Explain(exe, runtime, L"hostfxr.dll", ERROR_PROC_NOT_FOUND);
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return Explain(exe, runtime, L"CommandLineToArgvW", GetLastError());
    const auto previousWriter = setWriter(CaptureError);
    const int result = main(argc, const_cast<const wchar_t**>(argv), exe.c_str(), runtime.c_str(), assembly.c_str());
    setWriter(previousWriter);
    LocalFree(argv);
    if (result && !hostErrors.empty()) return Explain(exe, runtime, hostErrors, static_cast<DWORD>(result));
    return result;
}
