# Contributing

Thanks for considering a contribution. This project is safety-critical.

## Before you start

- Read `README.md`, `docs/03-safety-model.md` and `docs/04-runbook.md`.
- Test changes in a VM or on disposable disks.
- Never test destructive scripts against a disk that contains unique data.
- Do not commit real images, logs or serial numbers.

## Pull request checklist

- [ ] I tested the change in a VM or on a disposable disk.
- [ ] I did not weaken any identity check, dry-run default or confirmation gate.
- [ ] I did not add a code path that clears/formats before source verification.
- [ ] I did not add binaries, images, logs or personal data.
- [ ] I updated the relevant documentation.
- [ ] I ran a PowerShell parser check on changed `.ps1` files.

## Code style

- PowerShell 5.1 compatible unless a file documents otherwise.
- Prefer explicit parameter names and `[CmdletBinding()]`.
- Destructive scripts must default to DryRun and require explicit flags.
- Use `diskpart /s` with a command file. Do not write commands to stdin
  immediately after starting DiskPart; that caused a real production failure.
- Log commands, exit codes, target serial and start/end times.
- Fail closed: if an invariant cannot be verified, stop.

## Documentation style

- Chinese is the primary language; short English summaries are welcome.
- Use placeholders such as `<TARGET_SERIAL>`, `<SOURCE>`, `<STAGING>`.
- Do not include personal file names or full directory trees.
- Separate verified facts from hypotheses.

## Reporting issues

Include:

- Windows version
- Disk model/firmware (serial redacted)
- Partition style and dynamic/extent summary
- Exact command (with paths redacted)
- Exit code and the relevant log lines (redacted)

Do not attach a full disk image or a complete user file inventory.
