# Documentation / 文档索引

[中文主页](../README.zh-CN.md) | [English README](../README.en.md)

The full deep-dive documents are currently written in Chinese. This page gives an
English index and a concise summary of each document. Contributions of full
English translations are welcome.

| # | Document | Purpose |
|---|---|---|
| 01 | [Scope and principles](01-scope-and-principles.md) | What is supported, what is not, and why the workflow is copy-first |
| 02 | [Architecture](02-architecture.md) | Components, data flow, state machine, why compare twice |
| 03 | [Safety model](03-safety-model.md) | Gates, fail-closed rules, threat model, data retention |
| 04 | [Runbook](04-runbook.md) | Step-by-step commands and stop conditions |
| 05 | [Verification](05-verification.md) | File counts, path sets, byte comparison, chkdsk, SMART |
| 06 | [DiskPart postmortem](06-diskpart-postmortem.md) | The stdin race that left the volume offline |
| 07 | [Recovery playbook](07-recovery-playbook.md) | What to do at each failure point |
| 08 | [Limitations](08-limitations.md) | Supported scope and known risks |
| 09 | [Operations timeline](09-operations-timeline.md) | Sanitized phase timeline and lessons |
| 10 | [FAQ](10-faq.md) | Why not in-place, why two comparisons, and more |

## 01. Scope and principles

The project supports a single non-boot LDM dynamic disk with an NTFS simple
volume, migrated to an MBR basic disk. It does not support system disks,
cross-disk extents, RAID, Storage Spaces, BitLocker, or in-place conversion.
Design principles: verify before destroy, identify the disk by serial/model/
firmware/size, fail closed, keep evidence, minimize writes, and make recovery
possible.

## 02. Architecture

Components: preflight, copy, compare, rebuild, restore, post-check, plus progress
and periodic guards. The state machine tracks preflight → copy → verify →
rebuild → restore → verify → complete. A full comparison is required both before
the wipe and after the restore. DiskPart is invoked with `/s` command files, not
interactive stdin.

## 03. Safety model

Gates include administrator rights, target identity, non-boot status, source
volume health, staging space, 0-mismatch verification, no cancel flag, dry-run by
default, and explicit serial confirmation. A status flag is never proof of the
disk state: inspect the disk, partition table and volume. Keep independent
backups until the new disk has been rebooted and verified.

## 04. Runbook

Prepare the config, run preflight, copy, compare, rebuild, restore, compare again,
then post-check. Stop on any identity mismatch, unhealthy volume, insufficient
space, or mismatch. After a failure, do not blindly rerun `clean`; inspect the
disk first.

## 05. Verification

Levels: file counts, relative path sets, file lengths, byte-for-byte content,
`chkdsk /scan`, SMART and event logs. `TreeCompare` skips `System Volume
Information`, `$RECYCLE.BIN` and reparse points. Progress CSV supports live speed
and ETA. A successful migration requires 0 mismatches in both directions.

## 06. DiskPart postmortem

A real failure: the automation wrote commands to DiskPart's stdin immediately
after starting it. `select disk <N>` did not take effect, DiskPart printed help,
and `clean` never ran. `SourceDestroyed=true` had been set before the call. The
volume was only dismounted/offline; `online volume` + `assign letter` restored
the mount. The fix is to use `diskpart /s` and to set destructive state only
after a successful clean.

## 07. Recovery playbook

Scenarios: failure during copy/compare; volume offline but LDM intact; clean
executed but no partition; restore incomplete; wrong target disk; source read
errors. The first rule is to stop writing. The second is to inspect the disk and
volume before formatting. The third is to use the staging copy or image for
recovery.

## 08. Limitations

Single-disk simple volumes only; MBR target ≤ 2 TiB; NTFS only; no system disk,
RAID, Storage Spaces or BitLocker; online copy is not application-consistent;
live logs and antivirus may cause mismatches. Reference implementation, no
production guarantee.

## 09. Operations timeline

Sanitized phases: preflight, staging copy, full comparison, a failed rebuild due
to DiskPart input timing, offline volume recovery, a retry with `/s`, successful
rebuild, restore, full comparison, chkdsk and final acceptance. No machine-
specific counts, dates, serial numbers or paths are recorded.

## 10. FAQ

Highlights: why `diskpart convert basic` is destructive; why changing `0x42` to
`0x07` can corrupt a multi-extent volume; why two comparisons are needed; why
live logs produce mismatches; why `SourceDestroyed=true` can be misleading; why
a missing drive letter is not necessarily data loss; why `/MT` must not be
combined blindly with `/IPG` or `/EFSRAW`.
