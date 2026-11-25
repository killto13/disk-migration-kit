# GitHub Publishing Notes / GitHub 发布说明

This file is a checklist for publishing the repository.  
本文件是发布到 GitHub 前的检查清单，不是软件运行说明。

## Suggested repository metadata

**Repository name**

```text
disk-migration-kit
```

**English description**

```text
Verifiable Windows dynamic-disk to basic-MBR migration: copy, byte-for-byte verify, rebuild, restore, verify. Portable WinForms GUI + PowerShell.
```

**中文简介**

```text
将 Windows 动态盘安全迁移为 MBR 基本盘的可验证工具包：复制、全量逐字节校验、重建、回拷、再校验。含便携 GUI 和 PowerShell 脚本。
```

**Topics**

```text
windows
disk
dynamic-disk
basic-disk
mbr
ntfs
powershell
winforms
data-migration
diskpart
backup
```

**License:** MIT  
**Default branch:** `main` (rename `master` if you prefer)

## Before publishing / 发布前审核

- [ ] Review `README.md`, `README.zh-CN.md`, `README.en.md`.
- [ ] Review `docs/03-safety-model.md` and `docs/04-runbook.md`.
- [ ] Confirm the repository contains no images, logs, CSV files, real serial numbers, machine names, user names or personal paths.
- [ ] Confirm `config.psd1`, logs and `dist/` are ignored.
- [ ] Test `.\app\build.ps1` and `.\app\package.ps1`.
- [ ] Test the portable zip on a VM or disposable disk.
- [ ] Choose the final license copyright holder in `LICENSE`.
- [ ] Add the real repository URL to the README if desired.

## Push with git

The local repository already has commits. If GitHub CLI is not installed, use a
normal remote:

```powershell
git remote add origin https://github.com/<GITHUB_USER>/disk-migration-kit.git
git branch -M main
git push -u origin main
```

If the repository does not exist yet, create it in the GitHub web UI first.
Do not commit `dist/`, `config.psd1`, logs, images or compare CSV files.

## Create a release

1. Build the portable zip:

   ```powershell
   .\app\package.ps1
   ```

2. Create a GitHub Release, tag `v1.0.0`.
3. Attach `dist\EDiskMigration-portable-YYYYMMDD.zip`.
4. Attach a SHA-256 checksum file.
5. Do not attach disk images or user data.

## Suggested release notes

```text
Disk Migration Kit v1.0.0

Portable Windows GUI for a verifiable dynamic-disk → basic-MBR migration.

- copy → full byte comparison → rebuild → restore → full comparison → chkdsk
- bilingual Chinese/English UI
- two-disk mode: staging and recovery share one root (DATA and logs separate)
- identity gates by model/serial/firmware/size
- explicit serial confirmation before the destructive step
- no disk images or personal data included

This is a reference implementation. Test on a VM or disposable disk first.
```

## Review boundary

This repository is prepared locally and has not been pushed to any remote.
Publishing is a separate, explicit step. The browser login session is not used
by the project.
