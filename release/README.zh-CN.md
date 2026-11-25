# 便携软件

[简体中文](README.zh-CN.md) | [English](README.en.md)

这个目录直接包含打包好的便携软件，方便从仓库下载。

## 文件

- `EDiskMigration-portable-20261125.zip`  
  完整便携包，解压后运行 `EDiskMigration.exe`。
- `EDiskMigration.exe`  
  单独的图形界面程序；需要和仓库根目录的 `scripts/`、`docs/` 保持相对位置。
- `EDiskMigration-SHA256SUMS.txt`  
  SHA-256 校验值。

## 使用

1. 下载 `EDiskMigration-portable-20261125.zip`。
2. 完整解压到一个可写目录，不要只把 exe 拿出来。
3. 双击 `EDiskMigration.exe`，允许 UAC。
4. 选择目标磁盘、源卷、工作目录和恢复目录。
5. 按界面 1→7 步执行，第 3 步必须 0 mismatch 才能进入第 4 步。

## 警告

- 第 4 步会清空目标物理磁盘。
- 只用于非系统盘/非启动盘。
- 先备份，并在虚拟机或可丢弃磁盘上验证。
- 软件未签名，Windows 可能提示“未知发布者”；请核对 SHA-256。
- 参考实现，不提供数据恢复保证。

## 校验值

```text
SHA256  23ED5B8B52B2B49F5C84B3444D467E10280766D13D80C15EBB9A78012EEF3580  EDiskMigration-portable-20261125.zip
SHA256  D3ABB54560551F657EF48A6C64CABDE06982982F78EE6196A27D2E24F0B6EB5C  EDiskMigration.exe
```

完整源码：[仓库根目录](../README.zh-CN.md)  
许可证：[MIT](../LICENSE)
