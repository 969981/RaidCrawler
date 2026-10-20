> **方案 A 实验写入（2026-10-20）**：Windows `host` 分支已集成纯 C# + 标准 sys-botbase 的**受控单行奖励 RAM 写入**。主界面 **纯sys-botbase → 受控奖励写入（实验）**。只允许已确认的《紫》4.0.0 测试机、明确地址的候选对象、先机球×1–10 `Subject=5`；写前/写后验证，支持符合条件时尝试恢复。**当前没证明候选对象实际参与发包，也没有参与者到账的实机证据**。参见 [Hexbyt3 4.0.0 与 Plan A 实验报告](docs/host/PLAN_A_4_0_0_RAM_WRITE_EXPERIMENT.md)。默认构建仍**不包含 Switch Native 插件**。

> **纯 sys-botbase 方案 A（无需 Switch 额外插件）**：默认 Windows 构建现只发布 `RaidCrawler-host-sysbot-only`，不会携带 `subsdk9` 或 `main.npdm`。C# 主窗口的“纯sys-botbase”用于只读调查真实房主 RaidPoint；尚未证实稳定当前对象指针，**没有开放实机奖励修改**。使用方法与 GO/NO-GO 见 [方案 A 技术说明](docs/host/PLAN_A_SYSBOT_ONLY.md)。Native 探针是另一个可选研究分支，用户无需安装。

# RaidCrawler

> **简体中文版（host 分支）**：已汉化主界面、设置、筛选、奖励、太晶坑资料和 Host Probe。说明与下载方式见 [简体中文使用说明](docs/zh-CN/README.md)；[查看 host 分支 Windows 构建](https://github.com/969981/RaidCrawler/actions/workflows/host.yml)。该版本为实验分支，非 upstream/main 正式发布。

![Version](https://img.shields.io/github/v/release/LegoFigure11/RaidCrawler?label=latest%20release)
![Download Count](https://img.shields.io/github/downloads/LegoFigure11/RaidCrawler/total?label=total%20downloads)
[![Build Status](https://img.shields.io/github/actions/workflow/status/LegoFigure11/RaidCrawler/dotnet-desktop.yml?branch=main)](https://nightly.link/LegoFigure11/RaidCrawler/workflows/dotnet-desktop/main/RaidCrawler.zip)

[sys-botbase](https://github.com/LegoFigure11/usb-botbase) client Raid Viewer for Pokémon Scarlet and Violet.

**Basic usage guide:** [Click here](https://billo-guides.github.io/cfw/sv/raidcrawler) (thanks [Billo](https://github.com/Billo-PS)!)

![Tool image](https://legofigure11.github.io/tools/desktop/res/RaidCrawler/README.png)

Leverages SysBot.Base.dll from [kwsch/SysBot.NET](https://github.com/kwsch/SysBot.NET)
