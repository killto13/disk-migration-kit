# 04. 运行手册（Runbook）

> 本文中的 `<SOURCE>`、`<STAGING>`、`<RECOVERY>`、`<TARGET_SERIAL>` 都是占位符。  
> 任何清盘命令都必须由操作者逐项核对；不要把本文直接复制到生产环境盲跑。

## 0. 前置条件

- [ ] Windows 10/11，管理员权限（或 SYSTEM）；
- [ ] 目标盘不是系统盘/启动盘；
- [ ] 目标盘是单盘 LDM 简单卷；
- [ ] 暂存盘健康、容量足够、与目标盘独立；
- [ ] 恢复盘/恢复目录可用；
- [ ] 已关闭源盘上运行的程序；
- [ ] 已停止会持续写源盘的日志服务；
- [ ] 已记录目标盘型号、序列号、固件、容量；
- [ ] 已获得明确授权。

## 1. 准备配置

```powershell
Copy-Item .\examples\config.example.psd1 .\config.psd1
notepad .\config.psd1
```

至少填写：

```powershell
TargetDiskNumber = 2
TargetModel      = '<TARGET_MODEL>'
TargetSerial     = '<TARGET_SERIAL>'
TargetFirmware   = '<TARGET_FIRMWARE>'
SourceVolume     = '<SOURCE>'
StagingRoot      = '<STAGING>'
RecoveryRoot     = '<RECOVERY>'
```

> `StagingRoot` 与 `RecoveryRoot` 可以是同一个根目录（适合只有两块盘），脚本会分别使用 `<ROOT>\DATA` 和 `<ROOT>\logs`。  
> 但不要把 `RecoveryRoot` 指向 `<StagingRoot>\DATA`，否则日志会混入被校验的数据树。

## 2. 只读预检

```powershell
.\scripts\00-Preflight.ps1 -Config .\config.psd1
```

通过条件：

- 型号、序列号、固件、容量匹配；
- `IsBoot=False`、`IsSystem=False`；
- 源卷 NTFS/Healthy；
- 能看到卷和 extent；
- 暂存盘存在且空间足够；
- 没有取消标志。

把输出的 JSON 保存到恢复目录，作为身份基线。

## 3. 复制源数据到暂存盘

```powershell
.\scripts\01-CopyData.ps1 -Config .\config.psd1
```

要求：

- 源盘只读打开；
- 暂存目录是独立磁盘；
- 不删除暂存盘上已有文件；
- 记录 Robocopy 退出码和日志。

Robocopy 退出码 `0–7` 通常表示成功/有跳过；`>=8` 表示失败。以脚本封装为准。

## 4. 全量比较（源 → 暂存）

```powershell
.\scripts\02-CompareTrees.ps1 -Config .\config.psd1 -Direction SourceToStaging
```

通过条件：

```text
Success = true
MismatchCount = 0
SourceFiles = DestinationFiles
SourceBytes = DestinationBytes
```

如果有 mismatch：

1. **停止**，不要清盘；
2. 分类：内容/长度/缺失/读取错误；
3. 如果是活日志/临时文件，停止写入服务后重试；
4. 如果是杀毒软件拦截，明确决定是否排除；
5. 重新运行比较，直到 0 mismatch。

可选：在另一个窗口观察进度/ETA：

```powershell
.\scripts\Show-Progress.ps1 `
  -ProgressCsv '<RECOVERY_ROOT>\logs\SourceToStaging-progress-<RUNSTAMP>.csv' `
  -TotalBytes <TOTAL_BYTES> -TotalFiles <TOTAL_FILES>
```

可选：每 5 分钟做一次只读巡检：

```powershell
.\scripts\Periodic-Guard.ps1 -Config .\config.psd1 -RunStamp <RUNSTAMP> `
  -WrapperProcessId <WRAPPER_PID> -IntervalSeconds 300
```

`<RUNSTAMP>` 使用进度 CSV 文件名里的时间戳；`<WRAPPER_PID>` 是启动迁移的父进程 PID（可省略）。

## 5. 重建基本盘（破坏性）

先 DryRun：

```powershell
.\scripts\03-RebuildBasicVolume.ps1 -Config .\config.psd1
```

确认计划无误后，显式执行：

```powershell
.\scripts\03-RebuildBasicVolume.ps1 -Config .\config.psd1 `
  -Execute `
  -ConfirmSerial '<TARGET_SERIAL>' `
  -IUnderstandThisErasesTheTargetDisk
```

脚本会：

1. 再次确认序列号；
2. 卸载源卷；
3. `diskpart /s` 执行 clean；
4. 初始化 MBR；
5. 创建主分区；
6. 分配盘符；
7. 格式化 NTFS。

### 停止条件

- 序列号不匹配；
- 卷无法卸载；
- DiskPart 输出“没有选择磁盘”或 VDS 错误；
- clean 后磁盘未重新枚举；
- 分区/格式化失败。

任何停止都不要“再 clean 一次试试”；先保留日志，检查磁盘状态。

## 6. 回拷到新基本盘

```powershell
.\scripts\04-RestoreData.ps1 -Config .\config.psd1
```

## 7. 全量比较（暂存 → 新盘）

```powershell
.\scripts\02-CompareTrees.ps1 -Config .\config.psd1 -Direction StagingToTarget
```

通过条件同第 4 步。只有 0 mismatch 才能进入验收。

## 8. 验收

```powershell
.\scripts\05-PostCheck.ps1 -Config .\config.psd1
```

验收项：

- `Get-Disk`：`PartitionStyle=MBR`；
- `Get-Partition`：目标盘存在目标盘符；
- `Get-Volume`：NTFS/Healthy；
- 文件数/目录数/字节数；
- `chkdsk /scan`：无问题、0 坏扇区；
- SMART：读取错误无增长；
- 事件日志：无 disk Event ID 7；
- 写入 `SUCCESS.json`。

## 9. 成功后

1. 重启一次；
2. 启动关键应用，确认可用；
3. 抽查关键文件哈希；
4. 保留暂存副本/镜像至少一段时间；
5. 确认无问题后，再讨论删除备份。

## 10. 时间预期

实际耗时取决于数据量、文件数量、磁盘类型、杀毒软件和是否已有暂存副本：

- 复制：分钟级到小时级；
- 全量比较：通常是最慢的阶段；
- 重建 + 回拷：数十分钟到数小时；
- 最终比较：与第一次比较接近；
- 总计：可能数小时到十几小时。

不要用“剩余时间”做唯一决策依据；以比较结果为准。

## 11. 中断后的恢复

| 中断点 | 处理 |
|---|---|
| 复制中 | 重新运行复制，校验后继续 |
| 比较中 | 重新运行比较 |
| 卷已卸载、未 clean | `online volume` + `assign letter`，恢复挂载 |
| clean 已执行、未建分区 | 重新初始化/建分区，或从镜像恢复 |
| 回拷中 | 重新运行回拷，再比较 |
| 最终比较失败 | 保留暂存副本，修复差异，不要删除 |

详见 `07-recovery-playbook.md`。
