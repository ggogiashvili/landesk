# LANDESK Desktop App – Build & Run

This project is a **desktop application** (Flutter UI + Rust backend). Use these steps to build and run it on your machine.

---

## Prerequisites (install before building)

You **must** have Rust (cargo) and Flutter installed and on your PATH. The build will fail with `'cargo' is not recognized` if Rust is missing.

### 1. Install Rust (required)

1. Go to **[rustup.rs](https://rustup.rs)** and download the Windows installer.
2. Run it and follow the prompts (default options are fine).
3. **Close and reopen** your terminal (or restart Cursor/VS Code) so `PATH` is updated.
4. Verify:
   ```powershell
   cargo --version
   rustc --version
   ```

### 2. Install Flutter (required)

- [flutter.dev](https://flutter.dev) – install Flutter and add it to `PATH`.
- Enable Windows desktop:
  ```powershell
  flutter doctor
  flutter config --enable-windows-desktop
  ```

### 3. Python 3 (required for build script)

```powershell
python --version   # or py -3 --version
```

### 4. Windows C++ build tools (required for Rust/Flutter on Windows)

- **Visual Studio 2022** with workload **“Desktop development with C++”**, or  
- **Build Tools for Visual Studio** with the C++ workload  

Without this, `cargo build` or Flutter may fail with link errors.

### 5. vcpkg (required for audio/video libraries)

The build needs **vcpkg** for C++ libraries (opus, libvpx, libyuv, aom, etc.). If you see **“Couldn't find VCPKG_ROOT”** or **magnum-opus** build errors, do this once:

1. **Install vcpkg** (pick a folder, e.g. `C:\vcpkg`):
   ```powershell
   git clone https://github.com/microsoft/vcpkg C:\vcpkg
   C:\vcpkg\bootstrap-vcpkg.bat
   ```

2. **Set the `VCPKG_ROOT` environment variable** to that folder:
   - **Temporary (current terminal):**
     ```powershell
     $env:VCPKG_ROOT = "C:\vcpkg"
     ```
   - **Permanent:** Windows key → “Environment variables” → “Edit system environment variables” → “Environment Variables” → under “User” or “System”, “New” → Name: `VCPKG_ROOT`, Value: `C:\vcpkg` → OK.

3. **Install dependencies** from the project root (uses `vcpkg.json` and `res\vcpkg`). Use the **full path** to `vcpkg.exe` (replace `C:\vcpkg` if you used a different folder):
   ```powershell
   cd G:\landesk\LANDESK-V2
   C:\vcpkg\vcpkg.exe install
   ```
   If you set `VCPKG_ROOT`, you can instead run: `& "$env:VCPKG_ROOT\vcpkg.exe" install`  
   This can take a long time the first time (downloads and builds libraries).

4. **Set `VCPKG_ROOT`** before building (or use a new terminal if you set it permanently). Then run the build again.

---

## Get the `hbb_common` dependency (required first time)

The project depends on **`libs/hbb_common`**, which is normally a git submodule. You need this folder with a valid `Cargo.toml` before building.

**Option A – You have git and this is a git repo:**  
From the project root:
```powershell
git submodule update --init --recursive
```

**Option B – No git repo (e.g. you downloaded a ZIP) or submodules fail:**  
Install [Git for Windows](https://git-scm.com/download/win) if needed, then from the project root:

```powershell
cd G:\landesk\LANDESK-V2
# If libs\hbb_common already exists but is empty or broken, delete it first:
# rmdir /s /q libs\hbb_common
git clone https://github.com/rustdesk/hbb_common libs\hbb_common
```

Then run the build (see below).

---

## Build desktop app (Windows)

From the project root (`LANDESK-V2`):

```powershell
# Build Flutter desktop (release build)
python build.py --flutter
```

Output folder:

```
flutter\build\windows\x64\runner\Release\
```

That folder contains:

- **rustdesk.exe** – main desktop app (run this)
- **flutter_windows.dll** – Flutter engine
- **librustdesk.dll** – Rust core
- **dylib_virtual_display.dll** – virtual display (if built)
- Other required DLLs

---

## Run the desktop app

**Option A – From build output**

```powershell
cd flutter\build\windows\x64\runner\Release
.\rustdesk.exe
```

**Option B – From project root**

```powershell
.\flutter\build\windows\x64\runner\Release\rustdesk.exe
```

---

## Development (debug build)

Faster builds, no packaging:

```powershell
# 1) Build Rust library (flutter feature)
cargo build --features flutter --lib

# 2) Build and run Flutter
cd flutter
flutter run -d windows
```

The build script always produces a release build. For a debug/development run, use `flutter run -d windows` as above.

---

## Other desktop platforms

- **Linux:**  
  `python3 build.py --flutter`  
  Output: `flutter/build/linux/x64/release/bundle/`

- **macOS:**  
  `python3 build.py --flutter`  
  Output: `flutter/build/macos/Build/Products/Release/`

---

## Optional build features

| Flag           | Description                |
|----------------|----------------------------|
| `--hwcodec`    | Hardware video encode/decode |
| `--vram`       | VRAM (Windows only)        |
| `--skip-portable-pack` | Skip creating portable installer (Windows) |

Example:

```powershell
python build.py --flutter --hwcodec
```

---

## Troubleshooting

- **“failed to load manifest for workspace member … hbb_common”** or **“failed to read libs/hbb_common/Cargo.toml”** – The `libs/hbb_common` folder is missing or empty. If you have a git repo: run `git submodule update --init --recursive`. If not (e.g. you downloaded a ZIP): run `git clone https://github.com/rustdesk/hbb_common libs\hbb_common` from the project root (delete `libs\hbb_common` first if it exists and is empty).
- **“Couldn't find VCPKG_ROOT”** or **magnum-opus build failed** – Install [vcpkg](https://github.com/microsoft/vcpkg), set the `VCPKG_ROOT` environment variable to the vcpkg folder (e.g. `$env:VCPKG_ROOT = "C:\vcpkg"`), then from the project root run: `C:\vcpkg\vcpkg.exe install` (use your vcpkg path). See “5. vcpkg” in Prerequisites.
- **“building aom:x64-windows failed”** – AOM (AV1 codec) often fails on Windows. See **“If vcpkg install fails on aom”** below.
- **“‘cargo’ is not recognized”** – Rust is not installed or not on PATH. Install from [rustup.rs](https://rustup.rs), then **close and reopen** your terminal (or restart Cursor) and run `cargo --version` to confirm.
- **“flutter not found”** – Install Flutter and add it to `PATH`.
- **“librustdesk.dll not found”** – Run the full `python build.py --flutter` from the repo root; do not run only `flutter build windows`.
- **MSVC/linker errors on Windows** – Install Visual Studio 2022 with the “Desktop development with C++” workload (or Build Tools with C++).

### If vcpkg install fails on aom

When `vcpkg install` fails with **“building aom:x64-windows failed”**:

1. **Fix conflicting VCPKG_ROOT** – If you see “ignoring mismatched VCPKG_ROOT”, another path (e.g. Visual Studio’s vcpkg) is set. Use only the vcpkg folder you use for this project:
   ```powershell
   $env:VCPKG_ROOT = "C:\vcpkg"
   # Optional: remove the other VCPKG_ROOT from System/User environment variables so it doesn’t override.
   ```

2. **Ninja “ninja.exe -v” failed (Error code: 1)** – The project’s **aom** port (`res/vcpkg/aom/portfile.cmake`) is patched to use the **Visual Studio generator** on Windows x64 instead of Ninja, so this step is skipped for aom. Clear the aom build and retry (do **not** set `VCPKG_FORCE_SYSTEM_BINARIES` unless you have both CMake and Ninja installed and on `PATH`):
   ```powershell
   $env:VCPKG_ROOT = "C:\vcpkg"
   Remove-Item -Recurse -Force "C:\vcpkg\downloads\tools\ninja-*" -ErrorAction SilentlyContinue
   Remove-Item -Recurse -Force "C:\vcpkg\buildtrees\aom" -ErrorAction SilentlyContinue
   C:\vcpkg\vcpkg.exe install
   ```
   Only if that still fails and you have **both** [CMake](https://cmake.org/download/) and [Ninja](https://github.com/ninja-build/ninja/releases) on your `PATH`, then run:
   ```powershell
   $env:VCPKG_FORCE_SYSTEM_BINARIES = "1"
   C:\vcpkg\vcpkg.exe install
   ```
   If you see **“Could not fetch cmake”** after setting `VCPKG_FORCE_SYSTEM_BINARIES`, unset it: `Remove-Item Env:VCPKG_FORCE_SYSTEM_BINARIES` and use vcpkg’s downloaded tools instead.

3. **Try older AOM** – In the same PowerShell session:
   ```powershell
   $env:USE_AOM_391 = "1"
   Remove-Item -Recurse -Force "C:\vcpkg\buildtrees\aom" -ErrorAction SilentlyContinue
   C:\vcpkg\vcpkg.exe install
   ```

4. **See the real error** – Open the log vcpkg prints, e.g. `C:\vcpkg\buildtrees\aom\config-x64-windows-out.log`, and check the last lines for the exact cause.

5. **Update vcpkg** – In the vcpkg folder run `git pull`, then from the project root run `C:\vcpkg\vcpkg.exe update` and try `vcpkg.exe install` again.
