# 05. 校验模型

## 1. 为什么“文件数一样”不够

文件数、目录数、总字节数只能发现大规模缺失，不能发现：

- 同名文件内容被替换；
- 文件长度相同但字节不同；
- 部分写入/截断；
- 文件系统元数据错误；
- 大小写/路径差异；
- 稀疏文件/压缩文件的实际内容差异。

因此需要多层校验。

## 2. 校验层级

| 层级 | 检查 | 能发现 | 不能发现 |
|---|---|---|---|
| L1 | 文件数/目录数/字节数 | 大规模缺失 | 内容损坏 |
| L2 | 相对路径集合 | 缺失/多余路径 | 内容损坏 |
| L3 | 文件长度 | 截断/大小变化 | 同长度内容损坏 |
| L4 | 文件内容逐字节比较 | 内容损坏 | 元数据/ACL 问题 |
| L5 | `chkdsk /scan` | 文件系统结构/坏扇区 | 文件内容语义 |
| L6 | SMART/事件日志 | 硬件读取错误 | 逻辑损坏 |
| L7 | 整盘 SHA-256（镜像） | 镜像完整性 | 在线一致性 |

## 3. TreeCompare 算法

`TreeCompare.cs`：

1. 递归枚举源和目标的相对路径；
2. 跳过：
   - `System Volume Information`；
   - `$RECYCLE.BIN`；
   - reparse point（junction/symlink），避免循环；
3. 比较：
   - 是否存在；
   - 是文件还是目录；
   - 文件长度；
   - 文件内容（1 MiB 缓冲，逐字节）；
4. 记录 mismatch（最多 N 条，N 可配置）；
5. 每处理一定数量文件，写进度 CSV；
6. 返回：

```json
{
  "Success": true,
  "SourceFiles": 123456,
  "DestinationFiles": 123456,
  "SourceDirectories": 1234,
  "DestinationDirectories": 1234,
  "SourceBytes": 123456789,
  "DestinationBytes": 123456789,
  "MismatchCount": 0,
  "Mismatches": []
}
```

> 上面的数字是示例占位，不代表任何真实机器。

## 4. 进度 CSV

```text
CheckedAt,SourceBytesChecked,FilesChecked,Mismatches
```

用途：

- 实时显示速度和 ETA；
- 判断是否停滞；
- 失败后定位检查到哪个位置；
- 审计。

注意：进度是按“已检查文件数”累计，不一定按文件大小排序；ETA 同时按字节和文件数估算，取保守值。

## 5. 整盘镜像校验

如果保留整盘镜像：

- 镜像长度必须等于设备原始长度；
- 每 1 GiB 块计算 SHA-256；
- 块清单必须连续覆盖整个镜像；
- 最终整盘 SHA-256 必须匹配；
- 镜像清单记录源盘型号/序列号/固件/长度。

镜像校验只能证明“镜像文件与记录的哈希一致”，不能证明“在线源盘在复制瞬间是应用一致性快照”。

## 6. MBR/头尾元数据

对动态盘，建议额外保存：

- MBR 512 B；
- 头 1 MiB；
- 尾 4 MiB；
- 两个候选 NTFS 引导扇区（低地址和高地址）；
- 卷 GUID、NTFS 序列号、extent 列表。

这些证据用于：

- 证明当前卷首在高地址；
- 证明低地址是旧 VBR；
- 在恢复时判断分区表/LDM 元数据是否被改。

## 7. chkdsk 与 SMART

`chkdsk /scan`：

- 只读扫描；
- 检查文件记录、索引、安全描述符、USN；
- 报告坏扇区；
- 不修复。

SMART/可靠性计数器：

- `ReadErrorsTotal`；
- `ReadErrorsCorrected`；
- `ReadErrorsUncorrected`；
- 温度、通电时间；
- 事件日志中的 disk Event ID 7/51/153。

如果源盘出现未校正读取错误，停止重建，先做只读镜像/专业恢复。

## 8. 验收门槛

必须同时满足：

```text
L1 文件数/目录数/字节数匹配
L2 路径集合匹配（允许已记录的日志差异）
L3 文件长度匹配
L4 内容逐字节 0 mismatch
L5 chkdsk /scan 无问题、0 坏扇区
L6 SMART 无新增未校正错误
```

任一不满足，不得标记成功。

## 9. 证据保存

建议保存：

```text
preflight-*.json
copy-*.log
compare-progress-*.csv
compare-result-*.json
diskpart-*.log
chkdsk-before-*.log
chkdsk-after-*.log
SUCCESS.json / FAILED-*.json
```

所有证据都应包含时间、目标序列号（可脱敏）、命令和退出码。
