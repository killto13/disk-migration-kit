# Disk Migration Kit - Portable App

[中文说明](README_APP.md) | English

> A portable GUI for the copy → verify → rebuild → restore → verify workflow.

## Requirements

- Windows 10/11 (64-bit)
- .NET Framework 4.8 (included with Windows 10/11)
- Windows PowerShell 5.1
- Administrator rights (UAC prompt)

## Usage

1. Extract the whole zip into a writable folder. Do not copy only the EXE.
2. Run `EDiskMigration.exe` and approve UAC.
3. Select:
   - target disk (model, serial and size are shown; do not select `[BOOT]`);
   - source volume (any NTFS volume, not necessarily E:);
   - working folder (root for staging `DATA` and `logs`);
   - recovery folder (the default “same folder” option is suitable for two-disk machines; it can be separated).
4. To use English, choose **Language / 语言 → English**. The app restarts and remembers the choice.
5. Run the steps in order:
   1. **Read-only preflight**
   2. **Copy to staging**
   3. **Verify source → staging** (must be 0 mismatch)
   4. **Rebuild as basic** (requires typing the full serial and checking the confirmation)
   5. **Restore to new disk**
   6. **Verify staging → new disk** (must be 0 mismatch)
   7. **Final post-check**
6. Reboot once after success and keep the staging copy until key applications have been verified.

## Safety boundaries

- **The target physical disk is erased.**
- Use only on a non-system/non-boot disk.
- Step 4 is destructive; the button is red and requires the exact serial number.
- If step 3 is not 0 mismatch, do not continue.
- On failure, do not retry blindly; read `docs/07-recovery-playbook.md`.
- The tool does not roll back automatically. Stopping a step can leave an intermediate state.

## Files

```text
EDiskMigration.exe     GUI
README_APP.md          Chinese app readme
README_APP.en.md       English app readme
LICENSE                MIT
scripts/               PowerShell workflow
docs/                  Design, runbook, postmortem, recovery
examples/              Config example
```

Generated config:

```text
%LOCALAPPDATA%\EDiskMigrationKit\config.psd1
```

Logs and reports are written under the recovery folder chosen in the GUI.

## Common issues

**“scripts directory not found”**  
The zip was not fully extracted. Keep `scripts/`, `docs/` and the EXE in their original relative layout.

**UAC denied**  
The app performs no operation. Start again and approve administrator rights.

**Step 3 has mismatches**  
Stop writers on the source, classify the mismatches, and do not proceed to step 4.

**Drive letter disappeared after rebuild**  
Read `docs/07-recovery-playbook.md`. Do not format; the volume may only be offline and can be restored with `online volume` + `assign letter`.

**I only want a read-only check**  
Run step 1 only. Do not click step 4.

## Build

```powershell
.\app\build.ps1
.\app\package.ps1
```

The build uses the .NET Framework `csc.exe`; Visual Studio is not required.
