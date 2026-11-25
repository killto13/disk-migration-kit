# Security and sensitive data

This project operates on raw disks and can destroy data. Treat every script and
log as sensitive.

## Never commit

- Disk images (`*.img`, `*.img.partial`, `*.vhd`, `*.vhdx`, `*.bin`)
- File inventories, chunk manifests, compare progress CSV files
- Raw logs, PowerShell transcripts, `diskpart` output, `chkdsk` output
- Real disk serial numbers, machine names, user names, e-mail addresses
- Personal file listings, directory trees, screenshots of Disk Management
- `config.psd1` with real paths or serial numbers

The `.gitignore` covers the common cases, but it is not a substitute for
reviewing `git status` before publishing.

## Redaction rules

When sharing an issue or a postmortem:

- Replace the disk serial with `<TARGET_SERIAL>`.
- Replace the machine name with `<MACHINE>`.
- Replace user names with `<USER>`.
- Replace drive letters that identify personal volumes with `<SOURCE>`,
  `<STAGING>` and `<RECOVERY>`.
- Remove personal file names. Keep only counts and sizes.
- Never paste an entire recovery manifest or chat transcript.

## Antivirus and PUA

The workflow compares files byte-for-byte. If a file is blocked or quarantined
by antivirus, the comparison will report a mismatch or a read error. Do **not**
disable antivirus or add broad exclusions just to make a comparison pass.
Decide explicitly whether the file is required; otherwise exclude it or accept
the documented difference.

## Reporting a vulnerability

Please open a private security advisory or contact the maintainers before
public disclosure. Do not attach disk images, personal data or logs containing
serials.

## Destructive code review

Changes to `scripts/03-RebuildBasicVolume.ps1` and any script that calls
`diskpart clean`, `Initialize-Disk`, `Format-Volume` or `Remove-Partition`
require an explicit safety review. A pull request that weakens identity checks,
confirmation parameters or dry-run behavior will be rejected.
