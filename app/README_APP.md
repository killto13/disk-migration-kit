# Disk Migration Kit - 便携软件

[中文](README_APP.md) | [English](README_APP.en.md)

> 动态盘 → MBR 基本盘的可验证迁移工具，带中英文图形界面。  
> Portable GUI for a copy → verify → rebuild → restore → verify workflow.

## 运行要求

- Windows 10 / 11（64 位）
- .NET Framework 4.8（Windows 10/11 自带）
- Windows PowerShell 5.1（系统自带）
- 管理员权限（启动时会请求 UAC）
- 一块独立、健康的暂存盘；一块恢复目录所在盘

## 使用步骤

1. 把压缩包**完整解压**到一个可写目录，不要只复制 exe。
2. 双击 `EDiskMigration.exe`，允许 UAC 提权。
3. 选择：
   - 目标磁盘（会显示型号、序列号、容量；带 `[BOOT]` 的磁盘不要选）；
   - 源卷（任意 NTFS 卷，不一定是 E:）；
   - 工作目录（暂存 `DATA` 和日志 `logs` 的根目录）；
   - 恢复目录（默认勾选“同一目录”，适合只有两块盘；也可分开）。
4. 需要英文界面时，使用菜单 **Language / 语言 → English**，程序会重启并记住选择。
5. 按顺序点击：
   1. **只读预检**
   2. **复制到暂存**
   3. **校验源→暂存**（必须 0 mismatch）
   4. **重建基本盘**（需要输入完整序列号并勾选确认）
   5. **回拷到新盘**
   6. **校验暂存→新盘**（必须 0 mismatch）
   7. **最终验收**
5. 成功后重启一次，并保留暂存副本直到确认稳定。

## 安全边界

- **会清空目标物理磁盘**；只用于非系统盘/非启动盘。
- 第 4 步是破坏性操作，按钮为红色；需要手动输入目标盘序列号。
- 如果第 3 步不是 0 mismatch，不要继续。
- 失败时不要盲目重试；先读 `docs/07-recovery-playbook.md`。
- 工具不会自动回滚；“停止当前步骤”可能留下中间状态。

## 文件说明

```text
EDiskMigration.exe     图形界面
README_APP.md          本文件
LICENSE                MIT
scripts/               实际执行的 PowerShell 脚本
docs/                  原理、运行手册、故障复盘、恢复手册
examples/              配置示例（GUI 会自动生成配置）
```

程序会把自动生成的配置写到：

```text
%LOCALAPPDATA%\EDiskMigrationKit\config.psd1
```

日志/报告写到你在界面上选择的“恢复目录”下。

## 常见问题

**Windows 提示“未知发布者”**  
本 exe 未做商业代码签名，Windows SmartScreen 可能提示。请从可信来源下载，核对 SHA-256，或自行从源码构建。

**找不到 scripts 目录**  
没有完整解压。请把 zip 解压到普通文件夹，保留 `scripts/`、`docs/` 与 exe 的相对位置。

**UAC 被拒绝**  
程序不会做任何操作。重新启动并允许管理员权限。

**第 3 步 mismatch**  
先停止会写源盘的服务/日志，分类 mismatch；不要直接进入第 4 步。

**重建后盘符消失**  
先看 `docs/07-recovery-playbook.md`。不要格式化；可能是卷 offline，可 `online volume` + `assign letter`。

**想只读检查，不做修改**  
只运行第 1 步；不要点击第 4 步。

## 构建（开发者）

```powershell
.\app\build.ps1
.\app\package.ps1
```

构建使用 .NET Framework 的 `csc.exe`，不需要 Visual Studio。
