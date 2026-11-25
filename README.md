# Disk Migration Kit

[简体中文](README.zh-CN.md) | [English](README.en.md)

> 将 Windows 动态盘（LDM）安全迁移为 MBR 基本盘的可验证工具包  
> A verifiable, fail-closed workflow for migrating a Windows LDM dynamic disk to a basic MBR disk.

![status](https://img.shields.io/badge/status-reference%20implementation-orange)
![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![license](https://img.shields.io/badge/license-MIT-green)

## 中文简介

Windows 的“动态盘转基本盘”不是无损操作。图形界面和 `diskpart` 都要求先删除所有卷；而动态卷的多个 extent 还可能物理顺序与逻辑顺序相反，直接改 MBR 分区类型会让 NTFS 从错误位置读取。

本项目采用唯一可验证的路线：

```text
完整复制 → 全量逐字节校验 → 重建基本盘 → 全量回拷 → 再次逐字节校验 → chkdsk
```

核心原则：**源盘没有通过全量校验之前，绝不清盘。**

包含：

- 便携 WinForms GUI（免安装）；
- 按 1→7 步引导，禁止跳步；
- 中英文界面；
- 两盘模式：暂存与恢复共用一个根目录，自动分离 `DATA` 和 `logs`；
- 目标盘按型号/序列号/固件/容量识别；
- 失败关闭、DryRun、显式序列号确认、日志/证据链；
- 可复用的 PowerShell 脚本和 C# 比较器。

详细中文说明：[README.zh-CN.md](README.zh-CN.md)

## English summary

Windows does not provide a safe in-place "dynamic disk to basic" conversion for a disk that contains data. A dynamic volume may consist of multiple extents whose physical order differs from the logical order; simply changing an MBR partition type from `0x42` to `0x07` can make NTFS read from the wrong volume start.

This project uses a copy → full byte-for-byte verify → rebuild MBR/basic → restore → verify → `chkdsk` workflow. **The source disk is never wiped until the staging copy passes a full comparison.**

It includes a portable WinForms GUI, a 1-to-7 step workflow, bilingual UI, a two-disk mode (staging and recovery share one root while `DATA` and `logs` stay separate), identity gates by model/serial/firmware/size, fail-closed behavior, and reusable PowerShell scripts plus a C# byte comparator.

Full English README: [README.en.md](README.en.md)

## Safety first

> This tool erases the target physical disk. Use only on a non-system/non-boot disk. Back up first and test on a VM or disposable disk.

- Read [docs/03-safety-model.md](docs/03-safety-model.md) before doing anything.
- Follow [docs/04-runbook.md](docs/04-runbook.md).
- After a failure, read [docs/07-recovery-playbook.md](docs/07-recovery-playbook.md); do not retry `clean` blindly.

## Repository layout

```text
docs/          Chinese deep-dive documentation; English index in docs/README.en.md
scripts/       PowerShell workflow (preflight, copy, compare, rebuild, restore, post-check)
app/           WinForms launcher source and build/package scripts
examples/      Sanitized config example
```

## Build the portable app

```powershell
.\app\build.ps1
.\app\package.ps1
```

Output: `dist\EDiskMigration-portable-YYYYMMDD.zip`.

## License

MIT. See [LICENSE](LICENSE).

## Privacy

This repository contains no disk images, user files, machine names, user names, real disk serial numbers, or personal paths. See [SECURITY.md](SECURITY.md).
