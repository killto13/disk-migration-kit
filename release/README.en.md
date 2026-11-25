# Portable Software

[简体中文](README.zh-CN.md) | [English](README.en.md)

This folder contains the packaged portable software for direct download from the repository.

## Files

- `EDiskMigration-portable-20261125.zip`  
  Full portable package; extract and run `EDiskMigration.exe`.
- `EDiskMigration.exe`  
  Standalone GUI executable; it needs the repository's `scripts/` and `docs/` folders in their relative layout.
- `EDiskMigration-SHA256SUMS.txt`  
  SHA-256 checksums.

## Usage

1. Download `EDiskMigration-portable-20261125.zip`.
2. Extract the whole zip into a writable folder. Do not copy only the EXE.
3. Run `EDiskMigration.exe` and approve UAC.
4. Select the target disk, source volume, working folder and recovery folder.
5. Follow steps 1-7. Step 3 must be 0 mismatch before step 4 becomes available.

## Warning

- Step 4 erases the target physical disk.
- Use only on a non-system/non-boot disk.
- Back up first and test on a VM or disposable disk.
- The EXE is unsigned; Windows may show “Unknown publisher”. Verify the SHA-256.
- Reference implementation; no data-recovery guarantee.

## Checksums

```text
SHA256  23ED5B8B52B2B49F5C84B3444D467E10280766D13D80C15EBB9A78012EEF3580  EDiskMigration-portable-20261125.zip
SHA256  D3ABB54560551F657EF48A6C64CABDE06982982F78EE6196A27D2E24F0B6EB5C  EDiskMigration.exe
```

Source: [repository root](../README.en.md)  
License: [MIT](../LICENSE)
