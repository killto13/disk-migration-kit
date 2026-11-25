# 06. DiskPart 失败复盘：一次“源盘看起来消失了”的事故

> 这是一次真实迁移中的失败与修复记录。它说明：**状态标志不等于事实，DiskPart 交互时序必须被严肃对待。**

## 1. 现象

迁移进行到重建阶段：

1. 源卷已通过全量比较，0 mismatch；
2. 脚本成功执行 `fsutil volume dismount <SOURCE>`；
3. 脚本调用 DiskPart，准备：

   ```text
   select disk <N>
   attributes disk clear readonly
   clean
   exit
   ```

4. DiskPart 输出帮助文本，并两次提示：

   ```text
   没有选择磁盘。
   请选择一个磁盘，然后重试。
   ```

5. 脚本判定失败，写入 `FAILED-*.json`；
6. 状态文件显示 `SourceDestroyed=true`；
7. 源盘符消失，看起来像“盘被清了”。

## 2. 实际发生了什么

只读诊断发现：

```text
DISKPART> list disk
磁盘 <N>    联机    <SIZE>    2048 KB    *   (Dyn)

DISKPART> select disk <N>
磁盘 <N> 现在是所选磁盘。

DISKPART> detail disk
类型   : SATA
状态   : 联机
当前只读状态: 否
...
卷 0    简单    <SIZE>    正常    脱机
分区 1  动态数据  <SIZE>    31 KB
```

结论：

- 磁盘 <N> 仍然在线；
- LDM 分区仍然存在；
- `clean` **没有执行**；
- 源卷只是被 dismount，卷状态变成“脱机”，所以盘符消失；
- `SourceDestroyed=true` 是脚本在调用 DiskPart **之前**写入的，属于误报。

## 3. 根因

脚本的 `Invoke-DiskPart` 是这样实现的：

```powershell
$process = Start-Process diskpart -PassThru ...
foreach ($command in $Commands) {
    $process.StandardInput.WriteLine($command)
}
$process.StandardInput.Close()
$stdout = $process.StandardOutput.ReadToEnd()
```

问题：

- DiskPart 是交互式程序，启动后需要初始化、扫描配置、打印 `DISKPART>`；
- 脚本在进程刚启动时立刻写入所有命令；
- 第一条 `select disk <N>` 没有被正确解析/执行；
- 后续 `attributes`、`clean` 自然报“没有选择磁盘”；
- DiskPart 输出帮助文本，进一步说明输入没有被按预期解析。

这不是磁盘不存在、不是权限不足、不是序列号错误。用 `diskpart /s` 执行同样的 `select disk <N>` 立即成功。

## 4. 修复

改为命令文件：

```powershell
$scriptFile = Join-Path $env:TEMP ('diskpart-' + [guid]::NewGuid() + '.txt')
$Commands | Set-Content -LiteralPath $scriptFile -Encoding ASCII
$psi.Arguments = '/s "' + $scriptFile + '"'
# Start, read stdout/stderr, WaitForExit, remove file
```

优点：

- DiskPart 自己按顺序读命令；
- 不需要和交互式提示符赛跑；
- 退出码和完整输出可捕获；
- 命令文件可作为审计证据。

## 5. 修复后的额外改进

1. **不要提前设置 `SourceDestroyed`**  
   只有在 `clean` 成功、并且磁盘重新枚举后，才写 `SourceDestroyed=true`。

2. **clean 后检查磁盘状态**  
   - `Get-Disk` 是否重新出现；
   - `PartitionStyle` 是否为 RAW/MBR；
   - 分区是否可创建。

3. **失败后先恢复挂载，不要格式化**  
   如果只是卷 offline：

   ```text
   select disk <N>
   select volume <V>
   online volume
   assign letter=<SOURCE>
   exit
   ```

   然后检查文件系统、文件数、关键文件哈希。

4. **按 RunStamp 过滤旧失败标记**  
   旧运行的 `FAILED-*.json` 不能阻塞新运行，也不能被误读为当前失败。

5. **验证失败标志的真实性**  
   失败时检查：
   - `Win32_DiskPartition.Type`；
   - `Get-Disk` / `Get-Partition`；
   - `Get-Volume`；
   - 原始 MBR（如可读）。

## 6. 恢复过程

本次事故的恢复只做了三件事：

```text
select disk <N>
select volume <V>
online volume
assign letter=<SOURCE>
```

没有 clean、没有 format、没有初始化、没有整盘恢复。

结果：

- 源卷重新挂载；
- NTFS/Healthy；
- 文件数、关键文件哈希与事故前一致；
- 之后修复 DiskPart 调用并重试，迁移成功。

## 7. 教训

1. **交互式工具不要用“启动后立即灌 stdin”的方式驱动。**  
   能用在 `/s` 文件就用 `/s`。

2. **状态标志必须在事实之后写。**  
   “即将销毁”不是“已经销毁”。

3. **盘符消失不等于数据丢失。**  
   先检查卷/分区/LDM 状态，再决定是否恢复。

4. **失败路径必须保留证据。**  
   DiskPart 的完整 stdout 是定位问题的关键。

5. **不要在失败后立刻重试清盘。**  
   先确认磁盘到底处于哪个阶段。

## 8. 可复用的检查命令

只读诊断：

```text
list disk
select disk <N>
detail disk
list partition
list volume
exit
```

恢复挂载（不格式化）：

```text
select disk <N>
select volume <V>
online volume
assign letter=<SOURCE>
exit
```

> `online volume` / `assign letter` 只改变挂载状态，不写数据区。执行前仍应确认磁盘/卷编号。
