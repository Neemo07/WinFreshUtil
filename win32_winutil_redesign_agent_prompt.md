# Agent Task: Modernize C++ Win32 GUI & Reconstruct Local WinUtil Section

## Role & Objective
You are a Senior C++ Win32 Desktop & UI/UX Engineer. You are working inside an **existing C++ Win32 codebase**. Your mission is to:
1. Redesign the existing Win32 application with a clean, modern native Windows 11-style GUI.
2. Thoroughly inspect and learn from the reference folder at `C:\Users\ImPro\Desktop\winutil`, then reconstruct the **WinUtil** section from scratch inside this C++ Win32 project.
3. Enforce a **strict 100% offline/local architecture**—the application must operate completely locally with **zero** internet requests at runtime.

---

## Core Constraints (Non-Negotiable)
- **Zero Internet Usage:** Never call `WinINet`, `WinHTTP`, `URLDownloadToFile`, `libcurl`, external Winsock connections, or PowerShell web cmdlets (`Invoke-WebRequest`, `Invoke-RestMethod`, `iwr`, `irm`). Never link `wininet.lib` or `winhttp.lib`.
- **100% Local Assets & Configs:** All JSON definitions, scripts, icons, and assets must be bundled locally (embedded via `.rc` `RCDATA` resources or stored in a local folder resolved relative to `GetModuleFileNameW`).
- **Preserve Existing Features:** Do not break, remove, or degrade any existing non-WinUtil functionality in the application.
- **No UI Thread Blocking:** All tweaks, registry edits, service operations, and local script executions must run on background worker threads.
- **Strict Resource Safety:** Use RAII wrappers or guaranteed cleanup for every Win32 handle (`CloseHandle`, `RegCloseKey`, `CloseServiceHandle`) and GDI object (`DeleteObject`, `DeleteDC`).

---

## Phase 1: Codebase & Reference Audit (Plan First — Do Not Edit Code Yet)
Before modifying any files, perform a complete audit:
1. **Existing C++ Win32 Project:**
   - Identify the build system (`.sln`/`.vcxproj` or `CMakeLists.txt`), C++ standard, target platform (`x64`/`x86`), character set (`UNICODE`), manifest setup, and existing rendering approach (standard Win32 controls, GDI/GDI+, or Direct2D).
   - Map all existing windows, `WndProc` handlers, and non-WinUtil features that must remain intact.
2. **Reference Folder (`C:\Users\ImPro\Desktop\winutil`):**
   - Recursively inspect all relevant files (JSON configs, tweak definitions, service/registry operations, PowerShell scripts, and UI layout).
   - Map out all categories, tweaks, and actions to be reconstructed.
3. **Offline Dependency Audit:**
   - Identify every online URL, remote JSON fetch, CDN asset, or online package manager command (e.g., `winget`/`choco` downloads) in the reference folder.
4. **Submit Architectural Plan:**
   - Present a clear plan detailing:
     - How the existing UI will be modernized without breaking legacy features.
     - How the new folder structure will integrate into the existing build system.
     - How each reference asset/script will be localized.
     - A list of any inherently online items (e.g., external app installers) so I can confirm whether to point them to a local `installers\` folder or omit them.

**Wait for my approval on the plan before modifying source files.**

---

## Target C++ Win32 Folder Structure
Organize the modernized UI and reconstructed WinUtil section cleanly (adapting to the existing repository's root `src/` directory without breaking existing modules):

```text
src/
├── core/                              # Shared Win32 infrastructure & RAII helpers
│   ├── DpiHelper.hpp/.cpp             # Per-Monitor DPI v2 scaling & metric helpers
│   ├── ProcessRunner.hpp/.cpp         # Hidden CreateProcessW + stdout/stderr pipe capture
│   ├── RegistryHelper.hpp/.cpp        # RAII wrappers for RegOpenKeyExW/SetValueExW/DeleteValueW
│   ├── ServiceHelper.hpp/.cpp         # RAII wrappers for OpenSCManagerW/ChangeServiceConfigW
│   └── ThreadPool.hpp/.cpp            # Worker thread dispatch + PostMessageW UI callbacks
│
├── ui/                                # Modern Win32 UI layer
│   ├── Theme.hpp/.cpp                 # Dark color palette, DWM dark titlebar, typography
│   ├── DoubleBuffer.hpp/.cpp          # Flicker-free painting (memory DC / BeginBufferedPaint)
│   ├── SidebarNav.hpp/.cpp            # Modern left sidebar navigation control
│   ├── Controls/                      # Custom/owner-drawn modern controls
│   │   ├── ModernButton.hpp/.cpp
│   │   ├── ModernToggle.hpp/.cpp
│   │   ├── SearchBox.hpp/.cpp
│   │   └── LogConsole.hpp/.cpp        # Bounded, auto-scrolling live execution log console
│   └── MainWindow.hpp/.cpp            # Root window hosting existing views + WinUtil view
│
├── winutil/                           # Reconstructed WinUtil section (100% local)
│   ├── WinUtilModels.hpp              # Data models for categories, tweaks, reg/service/script ops
│   ├── WinUtilConfigLoader.hpp/.cpp   # Loads local JSON/RCDATA definitions (zero network)
│   ├── WinUtilEngine.hpp/.cpp         # Executes tweaks natively or via bundled local scripts
│   └── WinUtilView.hpp/.cpp           # Scrollable, searchable WinUtil panel & tweak cards
│
└── resources/                         # Local bundled assets (or embedded via .rc RCDATA)
    ├── app.manifest                   # Per-Monitor DPI Awareness v2 + Common Controls v6
    ├── winutil_data/
    │   ├── tweaks.json                # Localized tweak definitions reconstructed from reference
    │   ├── services.json              # Localized service configurations
    │   └── scripts/                   # Sanitized local PowerShell/CMD scripts (zero web calls)
    └── icons/                         # Local icons/bitmaps
```

Update `.vcxproj` / `CMakeLists.txt` and post-build steps (or `.rc` resource compilation) so all `winutil_data/` assets are automatically bundled or copied next to the built executable.

---

## Phase 2: Modern Win32 GUI Redesign
Redesign the application UI so it looks and behaves like a polished, modern native Windows 11 desktop utility:
- **Window Chrome & Dark Theme:**
  - Enable immersive dark mode on the title bar using `DwmSetWindowAttribute` (`DWMWA_USE_IMMERSIVE_DARK_MODE`) and rounded corners where supported on Windows 11.
  - Use a cohesive dark palette (deep neutral background, elevated card surfaces, subtle 1px borders, clean accent color, distinct hover/pressed/disabled states).
  - Render modern controls cleanly using owner-draw/custom-draw (`WM_DRAWITEM`, `NM_CUSTOMDRAW`, `WM_CTLCOLOR*`) or GDI+/Direct2D.
  - Prevent all flicker using double-buffered painting (`WM_ERASEBKGND` returning `1` + memory DC / `BeginBufferedPaint`).
- **Typography & Layout:**
  - Use `Segoe UI Variable` (with fallback to `Segoe UI`) across all controls.
  - Implement a clean sidebar navigation, structured category/tweak cards, consistent padding/margins, smooth mouse-wheel scrolling, and a real-time search/filter input for WinUtil items.
- **High-DPI Awareness:**
  - Configure **Per-Monitor DPI Awareness v2** in `app.manifest` (and/or `SetProcessDpiAwarenessContext`).
  - Handle `WM_DPICHANGED` properly and scale all fonts, margins, and control dimensions dynamically via `GetDpiForWindow` / `MulDiv`. Avoid hardcoded pixel coordinates.
- **Responsive Execution & Feedback:**
  - Dispatch tweak execution to worker threads and communicate progress, completion, and log lines back to the UI thread using `PostMessageW` (`WM_APP + N`).
  - Provide a collapsible or docked live log console with a bounded line/buffer cap and clear success/warning/error status indicators.

---

## Phase 3: WinUtil Section Reconstruction (100% Local)
Rebuild the WinUtil section from the ground up using `C:\Users\ImPro\Desktop\winutil` as the blueprint:
- **Prefer Native C++ Win32 APIs:**
  - Execute registry tweaks directly via `RegistryHelper` (`RegOpenKeyExW`, `RegCreateKeyExW`, `RegSetValueExW`, `RegDeleteValueW`), handling 64-bit registry views (`KEY_WOW64_64KEY`) where appropriate.
  - Execute service tweaks directly via `ServiceHelper` (`OpenSCManagerW`, `OpenServiceW`, `ChangeServiceConfigW`, `ControlService`).
  - For complex operations requiring PowerShell/CMD, invoke bundled local scripts via `ProcessRunner` (`CreateProcessW` with `CREATE_NO_WINDOW`, anonymous pipes via `CreatePipe` for live stdout/stderr streaming, and explicit execution timeouts).
- **Local Configuration Loading:**
  - Parse all WinUtil definitions from local `winutil_data/` JSON files (or embedded `.rc` `RCDATA` resources) using a lightweight local/header-only C++ JSON parser (e.g., `nlohmann/json` vendored locally).
  - Resolve all file paths relative to the executable directory via `GetModuleFileNameW` (never rely on the current working directory).
- **Inherently Online Reference Features:**
  - If any item in `C:\Users\ImPro\Desktop\winutil` requires downloading external installers or remote payloads, **never** silently execute a web request. Either wire it to check a local `installers\` folder or omit it according to my Phase 1 approval.

---

## Phase 4: Build, Resource Safety & Offline Verification
Before concluding, run and report the results of every check below:
1. **Strict Network Audit:**
   - Search the entire repository (C++ source, headers, build files, `.rc`, and bundled scripts) for: `http://`, `https://`, `WinHttp`, `InternetOpen`, `URLDownload`, `wininet.lib`, `winhttp.lib`, `curl`, `Invoke-WebRequest`, `Invoke-RestMethod`, `irm`, and `iwr`.
   - Confirm zero runtime network calls or remote asset references remain.
2. **Win32 Resource & Thread Safety Audit:**
   - Verify no GDI object leaks (`CreateFont`, `CreateSolidBrush`, `CreatePen`, `CreateCompatibleBitmap` all properly deleted).
   - Verify all kernel, registry, service, and pipe handles are cleanly closed, and worker threads shut down cleanly on `WM_CLOSE`/`WM_DESTROY`.
3. **Build & Offline Runtime Verification:**
   - Confirm the project compiles cleanly with zero errors and no new warnings.
   - Verify all existing non-WinUtil features, the modernized GUI, DPI scaling, search/filtering, local WinUtil configs, and tweak execution work properly with the machine completely disconnected from the internet.