# RaidCrawler
![Version](https://img.shields.io/github/v/release/LegoFigure11/RaidCrawler?label=latest%20release)
![Download Count](https://img.shields.io/github/downloads/LegoFigure11/RaidCrawler/total?label=total%20downloads)
[![Build Status](https://img.shields.io/github/actions/workflow/status/LegoFigure11/RaidCrawler/dotnet-desktop.yml?branch=main)](https://nightly.link/LegoFigure11/RaidCrawler/workflows/dotnet-desktop/main/RaidCrawler.zip)

[sys-botbase](https://github.com/LegoFigure11/usb-botbase) client Raid Viewer for Pokémon Scarlet and Violet.

**Basic usage guide:** [Click here](https://billo-guides.github.io/cfw/sv/raidcrawler) (thanks [Billo](https://github.com/Billo-PS)!)

![Tool image](https://legofigure11.github.io/tools/desktop/res/RaidCrawler/README.png)

Leverages SysBot.Base.dll from [kwsch/SysBot.NET](https://github.com/kwsch/SysBot.NET)

## V4 N1 Read-Only Research (feature branch only / 独立实验分支)

> This feature branch adds a **read-only** Violet 4.0.0 native observer and PC hot-configuration prototype. It does **not** modify raid rewards, game saves, or outgoing packets. The Native/PC builds have CI coverage, but Nintendo Switch hardware verification is still pending. This is not the upstream RaidCrawler release.

- [中文完整使用手册 / USER_GUIDE.md](docs/live-reward-v4/USER_GUIDE.md)
- [详细技术架构 / TECHNICAL_ARCHITECTURE.md](docs/live-reward-v4/TECHNICAL_ARCHITECTURE.md)
- [开发任务与验收状态 / TASKS.md](docs/live-reward-v4/TASKS.md)
- [Mailbox V4 ABI 总览 / README.md](docs/live-reward-v4/README.md)

Download the Windows application and the strictly read-only subsdk9 separately from this branch's successful GitHub Actions artifacts; the matching Native build's subsdk9 and locator JSON are required for local pairing checks.
