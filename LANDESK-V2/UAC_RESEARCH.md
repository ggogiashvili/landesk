# Research Report: UAC Handling and Secure Desktop Interaction in RustDesk

This document outlines the technical mechanism RustDesk uses to handle Windows User Account Control (UAC) prompts and interact with the Secure Desktop.

## Executive Summary
RustDesk handles UAC by elevating its process to **SYSTEM** privileges (usually via a Windows Service). This allows it to duplicate security tokens from system processes (like `winlogon.exe`) and manually switch its execution threads to the **Secure Desktop** (`Winlogon`) where UAC prompts are displayed.

---

## 1. Key Files and Responsibilities

### [windows.rs](src/platform/windows.rs)
- **Role**: High-level orchestration and management.
- **Key Logic**:
    - `is_elevated`: Checks if the current process has administrator tokens.
    - `run_uac`: Triggers the initial elevation prompt using the `runas` verb.
    - `elevate_or_run_as_system`: Decision logic to either restart as Admin or attempt to run as SYSTEM.
    - `install_me`: Logic for installing the application as a service to gain persistent SYSTEM access.

### [windows.cc](src/platform/windows.cc)
- **Role**: Low-level Win32 API interactions (C++).
- **Key Logic**:
    - `GetLogonPid`: Scans processes to find the PID of `winlogon.exe` (which owns the Secure Desktop).
    - `GetSessionUserTokenWin`: Duplicates the token of the logon process to gain systemic access to that session.
    - `selectInputDesktop`: The core mechanism that switches the thread to the active input desktop (allowing capture/control of UAC).
    - `LaunchProcessWin`: Uses `CreateProcessAsUserW` to start privileged components in the targeted session.

---

## 2. Technical Mechanism

### Step A: Initial Elevation
When a user launches RustDesk, it checks if it is elevated. If not, it uses `ShellExecuteW` with the `runas` parameter to request administrator permissions.

### Step B: The Service (SYSTEM User)
To interact with the Secure Desktop, RustDesk ideally runs as a **Windows Service**. Services run under the `SYSTEM` account, which has the `SeTcbPrivilege` (Act as part of the operating system), allowing it to cross session boundaries.

### Step C: Token Impersonation
When a remote user connects, the service needs to launch a "server" process in the active user's session. It does this by:
1. Finding `winlogon.exe` in the target Session ID.
2. Opening its process token.
3. Using `CreateProcessAsUserW` to launch the capture server with that token.

### Step D: Handling the "Secure Desktop" (UAC Switch)
When a UAC prompt appears, Windows switches from the `Default` desktop to the `Winlogon` (Secure) desktop. RustDesk handles this switch by:
1. Detecting the desktop change.
2. Calling `OpenInputDesktop`.
3. Calling `SetThreadDesktop` to move its capture and input injection threads to the new active desktop.
4. Once the prompt is cleared, it repeat the process to switch back to the `Default` desktop.

---

## 3. Implementation Table

| Feature | Target File | Implementation Detail |
| :--- | :--- | :--- |
| **Elevation Detection** | `windows.rs` | Uses `OpenProcessToken` and `GetTokenInformation`. |
| **Elevation Trigger** | `windows.rs` | Calls `ShellExecuteW` with `"runas"`. |
| **Session Capture** | `windows.cc` | `GetLogonPid` finds `winlogon.exe` to bridge sessions. |
| **UAC Control** | `windows.cc` | `selectInputDesktop` switches the thread to the `Winlogon` desk. |
| **Input Injection** | `windows.cc` | Low-level hooks and `PostMessage` used in the active desktop context. |

---

## 4. Conclusion for Implementation
If you are looking to fix or implement similar behavior in your project, the most critical takeaway is that your process **must** have SYSTEM-level privileges (via a service) to reliably use `SetThreadDesktop` and `OpenInputDesktop` for UAC interaction. Without SYSTEM privileges, your application will lose visibility/input as soon as the OS switches to the Secure Desktop.
