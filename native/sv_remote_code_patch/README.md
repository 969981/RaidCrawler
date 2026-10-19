# 《紫》4.0.0：Windows sys-botbase ARM64 v2 Hook

本分支已包含 AArch64 v2 seqlock 跳板、离线构建器、Windows 安装/启用/热更新/停用/卸载入口及模拟测试。**该代码尚无真实 Switch 运行或奖励/存档验证；不得将 CI 成功等同于 HOOK_HIT。**

- 原 ELF SHA256: d17403eaa9d844db82f647fd2f1518806b10c0518d53602fc0ac1766298fdba0
- TitleID: 01008F6008C5E000；Violet 4.0.0；NSO Build ID 前缀 709bfd6611529864
- 原指令 main+0xE32984: AA1503E8
- 代码洞 main+0x343FC90..0x3440000，共 880B，与末尾 PLT 指令同属 4KiB 页；**是否能执行仍须实机测试**
- 入口跳转 B 指令最后安装；返回槽 cave+0x8C，返回 main+0xE32988
- cave+0x90: epoch，奇数=停用/更新中，偶数=启用
- cave+0x94: 固定协议签名 RHS2RaidPoin（12B）
- cave+0xA0: 45×16=720B 奖励行

## 执行原理

x23 指向 0xAA0 明文 Bootstrap，RaidPoint 在 x23+0x6B8，奖励行始于 x23+0x79C。ARM64 v2 回调首先核对 RaidPoin 标记及偶数 epoch，复制 720B，随后重新读取 epoch。如果前后不同，则从原始 x20+0x13C 区域恢复 720B。保留 x9-x14、恢复栈、执行原 mov x8,x21，再返回原 serializer 后继指令；此代码不在 RX 页记录计数。

Windows 写入按照「epoch 改奇数 → 更新 720B 并回读 → 恢复新偶数」进行；因此下一次符合条件的回调可尝试新奖励，游戏不必重启。此机制需要在真实 CPU 上验证竞态正确性，不得仅依赖模拟测试。

## 离线构建检查

```bash
python3 -m unittest discover -s native/sv_remote_code_patch -p 'test_*.py' -v
python3 native/sv_remote_code_patch/build_offline_plan.py /path/to/Violet4.0.0.elf --out ./offline-hook-plan
```

生成器不连接 Switch，不分发游戏 ELF。CI 交叉汇编并对照 Windows C# 镜像完整 144B 代码，避免旧版 BL memcpy 重定位槽错误。

## Windows 操作

请阅读 [实机测试指南](../../docs/research/violet400-windows-remote-hook-test-guide.md)。重点：离线备份存档后才手动安装；首次安装默认停用，启用后核对状态；修改模板无需重启；卸载先停用、恢复唯一的入口指令，但**不清空**仍可能被在途回调执行的 880B。真正 HOOK_HIT / 奖励结算 / Bag 入包 / 保存尚未实测。

不需要额外 Switch 插件，不需要把 BIN 文件拷到 SD，也不替换原版 sys-botbase。**拒绝叠加到已修改的入口或旧版 Skyline Hook。**
