# ECP Windows displayed-frame collector

The vendored ETW decoder is Intel PresentMon v2.6.0 (MIT), fetched from https://github.com/GameTechDev/PresentMon/tree/v2.6.0 . vendor files are unchanged; vendor-sha256.json records their hashes.

prepare.py creates a private build copy. It replaces the upstream general logging/exception/pacing support with Support.hpp, and adds one output-only CompletedWindow field before upstream clears Hwnd. Frame timestamp decoding and tracking remain upstream. ReportedPresentIds preserves actual output/layer attribution. No refresh-rate estimates or process-wide guesses are substituted for selected-window FPS.

ECPFrames.cpp emits actual displayed timestamps (QPC seconds), process ID, root HWND, swap chain, VidPn source and layer. Dropped/lost/repeated frames are omitted. Multiple windows of one process remain independent. An unavailable HWND/source remains unavailable.

One reader is shared by every HUD. --owner watches the app process; owner death stops the owned ETW session. Managed shutdown also stops the exact named session before terminating the reader.

Build with Visual Studio C++ tools, a current Windows SDK, and Python 3:

    build.cmd path-to-python.exe

Build the ARM64 helper with the MSVC ARM64 cross tools and libraries installed:

    build.cmd path-to-python.exe arm64

Both helpers use a static CRT. Copy build/ECPFrames-<architecture>.exe to Assets/PresentMon/ and update the corresponding SHA256 in FrameRateBackend.CollectorFor after rebuilding. ECP selects the helper by OS architecture: x64 Windows launches the x64 reader, and ARM64 Windows launches the ARM64 reader, including when the app itself is emulated. 32-bit Windows reports unsupported capture. Packaged ECP remains one EXE; both helpers are embedded and extracted with SHA256 verification.

Tests: official ETL replay, administrator live D3D11 two-window attribution, and automatic trace cleanup when the parent exits.
