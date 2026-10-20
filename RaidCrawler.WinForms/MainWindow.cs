using PKHeX.Core;
using PKHeX.Drawing;
using PKHeX.Drawing.PokeSprite;
using RaidCrawler.Core.Connection;
using RaidCrawler.Core.Discord;
using RaidCrawler.Core.Structures;
using RaidCrawler.WinForms.SubForms;
using SysBot.Base;
using System.Diagnostics;
using System.Text.Json;
using static RaidCrawler.Core.Structures.Offsets;

namespace RaidCrawler.WinForms;

public partial class MainWindow : Form
{
    private static CancellationTokenSource Source = new();
    private static CancellationTokenSource DateAdvanceSource = new();

    private static readonly Lock _connectLock = new();
    private static readonly Lock _readLock = new();

    private readonly ClientConfig Config;
    private ConnectionWrapperAsync ConnectionWrapper = default!;

    private SwitchConnectionConfig ConnectionConfig;

    private readonly RaidContainer RaidContainer;
    private readonly NotificationHandler Webhook;

    private List<RaidFilter> RaidFilters = [];
    private static readonly Image MapBase = Image.FromStream(
        new MemoryStream(Utils.GetBinaryResource("paldea.png"))
    );
    private static readonly Image MapKitakami = Image.FromStream(
        new MemoryStream(Utils.GetBinaryResource("kitakami.png"))
    );
    private static readonly Image MapBlueberry = Image.FromStream(
        new MemoryStream(Utils.GetBinaryResource("blueberry.png"))
    );
    private static Dictionary<string, float[]>? DenLocationsBase;
    private static Dictionary<string, float[]>? DenLocationsKitakami;
    private static Dictionary<string, float[]>? DenLocationsBlueberry;

    // statistics
    public int StatDaySkipTries;
    public int StatDaySkipSuccess;
    public readonly string formTitle;

    private ulong RaidBlockOffsetBase;
    private ulong RaidBlockOffsetKitakami;
    private ulong RaidBlockOffsetBlueberry;
    private bool IsReading;
    private bool HideSeed;
    private bool ShowExtraMoves;

    private Color DefaultColor;
    private FormWindowState _WindowState;
    private readonly Stopwatch stopwatch = new();
    private TeraRaidView? teraRaidView;

    private bool StopAdvances =>
        !Config.EnableFilters || RaidFilters.Count == 0 || RaidFilters.All(x => !x.Enabled);

    private readonly Version CurrentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;

    public MainWindow()
    {
        Config = new ClientConfig();
#if DEBUG
        var date = File.GetLastWriteTime(AppContext.BaseDirectory);
        var build = $" (dev-{date:yyyyMMdd})";
#else
        var build = "";
#endif
        var v = CurrentVersion;
        var filterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "filters.json");
        if (File.Exists(filterPath))
            RaidFilters = JsonSerializer.Deserialize<List<RaidFilter>>(File.ReadAllText(filterPath)) ?? [];
        DenLocationsBase = JsonSerializer.Deserialize<Dictionary<string, float[]>>(Utils.GetStringResource("den_locations_base.json") ?? "{}");
        DenLocationsKitakami = JsonSerializer.Deserialize<Dictionary<string, float[]>>(Utils.GetStringResource("den_locations_kitakami.json") ?? "{}");
        DenLocationsBlueberry = JsonSerializer.Deserialize<Dictionary<string, float[]>>(Utils.GetStringResource("den_locations_blueberry.json") ?? "{}");

        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        if (File.Exists(configPath))
        {
            var text = File.ReadAllText(configPath);
            Config = JsonSerializer.Deserialize<ClientConfig>(text)!;
        }
        else
        {
            Config = new();
        }

        ConnectionConfig = new SwitchConnectionConfig
        {
            Protocol = SwitchProtocol.WiFi,
            IP = "192.168.0.0",
            Port = 6000,
        };
        formTitle = $"RaidCrawler v{v.Major}.{v.Minor}.{v.Build}{build} {Config.InstanceName}";
        Text = formTitle;

        // load raids
        RaidContainer = new(Config.Game);

        SpriteBuilder.ShowTeraThicknessStripe = 0x4;
        SpriteBuilder.ShowTeraOpacityStripe = 0xAF;
        SpriteBuilder.ShowTeraOpacityBackground = 0xFF;
        SpriteUtil.ChangeMode(SpriteBuilderMode.SpritesArtwork5668);

        var protocol = Config.Protocol;
        ConnectionConfig = new()
        {
            IP = Config.IP,
            Port = protocol is SwitchProtocol.WiFi ? 6000 : Config.UsbPort,
            Protocol = Config.Protocol,
        };

        Webhook = new(Config);
        InitializeComponent();

        // N1 read-only host diagnostics: reuse the active sys-botbase connection.
        // Located beside Settings / Date Tools, so the WinForms designer is untouched.
        var hostProbeButton = new Button
        {
            Text = "发包副本Hook",
            Name = "HostProbeButton",
            Location = new Point(222, 302),
            Size = new Size(103, 23),
        };
        hostProbeButton.Click += (_, _) =>
        {
            if (ConnectionWrapper is not { Connected: true })
            {
                MessageBox.Show(this, "请先通过 sys-botbase 连接 Switch。",
                    "房主探针", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var form = new HostProbeWindow(ConnectionWrapper.Connection);
            form.ShowDialog(this);
        };
        hostProbeButton.Text = "发送副本Hook";
        hostProbeButton.Width = 136;
        Controls.Add(hostProbeButton);

        // True sys-botbase-only path (no NSO Hook, subsdk9, mailbox or SD file).
        var sysbotOnlyButton = new Button
        {
            Text = "旧堆扫描（诊断）",
            Name = "SysbotOnlyHostButton",
            Location = new Point(368, 302),
            Size = new Size(116, 23),
        };
        sysbotOnlyButton.Click += (_, _) =>
        {
            if (ConnectionWrapper is not { Connected: true })
            {
                MessageBox.Show(this, "请先连接 Switch 的 sys-botbase。", "方案 A",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var form = new SysbotOnlyHostWindow(ConnectionWrapper.Connection);
            form.ShowDialog(this);
        };
        Controls.Add(sysbotOnlyButton);

        // Reparent existing designer buttons and fields into a responsive
        // layout. This changes presentation only; all Click handlers and
        // runtime enable/disable logic remain attached to the same controls.
        InitializeResponsiveMainLayout(hostProbeButton, sysbotOnlyButton);

        btnOpenMap.Enabled = false;
        Rewards.Enabled = false;
        SendScreenshot.Enabled = false;
        CheckEnableFilters.Checked = Config.EnableFilters;

        if (Config.Protocol is SwitchProtocol.USB)
        {
            InputSwitchIP.Visible = false;
            LabelSwitchIP.Visible = false;
            USB_Port_TB.Visible = true;
            USB_Port_label.Visible = true;
        }
        else
        {
            InputSwitchIP.Visible = true;
            LabelSwitchIP.Visible = true;
            USB_Port_TB.Visible = false;
            USB_Port_label.Visible = false;
        }
    }

    private void UpdateStatus(string status)
    {
        // Translate messages emitted by the unmodified SysBot.Base connection
        // wrapper. Keep protocol/status codes in English.
        ToolStripStatusLabel.Text = status switch
        {
            "Connecting..." => "正在连接……",
            "Connected!" => "连接成功！",
            "Disconnecting controller..." => "正在解除控制器连接……",
            "Disconnecting..." => "正在断开连接……",
            "Disconnected!" => "已断开连接！",
            _ => status,
        };
    }

    private void ButtonEnable(bool enable, params object[] obj)
    {
        lock (_readLock)
        {
            foreach (object o in obj)
            {
                if (o is not Button btn)
                    continue;

                if (InvokeRequired)
                    Invoke(() => btn.Enabled = enable);
                else
                    btn.Enabled = enable;
            }

            IsReading = !enable;
        }
    }

    private void ShowDialog(object obj)
    {
        var window = (Form?)obj;
        if (window is null)
            return;

        window.StartPosition = FormStartPosition.CenterParent;
        if (InvokeRequired)
            Invoke(() => window.ShowDialog());
        else
            window.ShowDialog();
    }

    private int GetRaidBoost()
    {
        if (InvokeRequired)
            return Invoke(() => RaidBoost.SelectedIndex);
        return RaidBoost.SelectedIndex;
    }

    public int GetStatDaySkipTries() => StatDaySkipTries;

    public int GetStatDaySkipSuccess() => StatDaySkipSuccess;

    private void MainWindow_Load(object sender, EventArgs e)
    {
        CenterToScreen();
        InputSwitchIP.Text = Config.IP;
        USB_Port_TB.Text = Config.UsbPort.ToString();
        DefaultColor = IVs.BackColor;
        RaidBoost.SelectedIndex = 0;
        ToggleStreamerView();
        CheckForUpdates();
    }

    private void InputSwitchIP_Changed(object sender, EventArgs e)
    {
        TextBox textBox = (TextBox)sender;
        Config.IP = textBox.Text;
    }

    private void USB_Port_Changed(object sender, EventArgs e)
    {
        if (Config.Protocol is SwitchProtocol.WiFi)
            return;

        TextBox textBox = (TextBox)sender;
        if (int.TryParse(textBox.Text, out int port) && port >= 0)
        {
            Config.UsbPort = port;
            return;
        }

        Task.Run(async () => await this.DisplayMessageBox(Webhook, "请输入有效的 USB 端口号。", Source.Token).ConfigureAwait(false), Source.Token);
    }

    private void ButtonConnect_Click(object sender, EventArgs e)
    {
        lock (_connectLock)
        {
            if (ConnectionWrapper is { Connected: true })
                return;

            ConnectionConfig = new()
            {
                IP = Config.IP,
                Protocol = Config.Protocol,
                Port = Config.Protocol is SwitchProtocol.WiFi ? 6000 : Config.UsbPort,
            };
            ConnectionWrapper = new(ConnectionConfig, UpdateStatus);
            Connect(Source.Token);
        }
    }

    private void Connect(CancellationToken token)
    {
        Task.Run(
            async () =>
            {
                ButtonEnable(false, ButtonConnect, SendScreenshot, btnOpenMap, Rewards);
                try
                {
                    (bool success, string err) = await ConnectionWrapper
                        .Connect(token)
                        .ConfigureAwait(false);
                    if (!success)
                    {
                        ButtonEnable(true, ButtonConnect);
                        await this
                            .DisplayMessageBox(Webhook, err, token)
                            .ConfigureAwait(false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    ButtonEnable(true, ButtonConnect);
                    await this
                        .DisplayMessageBox(Webhook, ex.Message, token)
                        .ConfigureAwait(false);
                    return;
                }

                UpdateStatus("正在检测游戏版本……");
                string id = await ConnectionWrapper.Connection
                    .GetTitleID(token)
                    .ConfigureAwait(false);
                var game = id switch
                {
                    ScarletID => "Scarlet",
                    VioletID => "Violet",
                    _ => "",
                };

                if (game is "")
                {
                    try
                    {
                        (bool success, string err) = await ConnectionWrapper
                            .DisconnectAsync(token)
                            .ConfigureAwait(false);
                        if (!success)
                        {
                            ButtonEnable(true, ButtonConnect);
                            await this
                                .DisplayMessageBox(Webhook, err, token)
                                .ConfigureAwait(false);
                            return;
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                    finally
                    {
                        ButtonEnable(true, ButtonConnect);
                        await this
                            .DisplayMessageBox(Webhook,
                                "无法检测到 Switch 上运行的《宝可梦 朱》或《宝可梦 紫》！",
                                token
                            )
                            .ConfigureAwait(false);
                    }
                    return;
                }

                Config.Game = game;
                RaidContainer.SetGame(Config.Game);

                UpdateStatus("正在读取剧情进度……");
                Config.Progress = await ConnectionWrapper
                    .GetStoryProgress(token)
                    .ConfigureAwait(false);
                Config.EventProgress = Math.Min(Config.Progress, 3);

                UpdateStatus("正在读取活动太晶坑状态……");
                try
                {
                    await ReadEventRaids(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ButtonEnable(true, ButtonConnect);
                    await this.DisplayMessageBox(Webhook, $"读取活动太晶坑时发生错误： {ex.Message}", token)
                        .ConfigureAwait(false);
                    return;
                }

                UpdateStatus("正在读取太晶坑……");
                try
                {
                    await ReadRaids(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ButtonEnable(true, ButtonConnect);
                    await this.DisplayMessageBox(Webhook, $"读取太晶坑时发生错误： {ex.Message}", token)
                        .ConfigureAwait(false);
                    return;
                }

                ButtonEnable(true, ButtonAdvanceDate, ButtonReadRaids, ButtonDisconnect, ButtonViewRAM, ButtonDownloadEvents, SendScreenshot, btnOpenMap, Rewards, B_DateTools);
                if (InvokeRequired)
                {
                    Invoke(() =>
                    {
                        ComboIndex.Enabled = true;
                        ComboIndex.SelectedIndex = 0;
                    });
                }
                else
                {
                    ComboIndex.SelectedIndex = 0;
                }

                UpdateStatus("已完成！");
            },
            token
        );
    }

    private void Disconnect_Click(object sender, EventArgs e)
    {
        lock (_connectLock)
        {
            if (ConnectionWrapper is not { Connected: true })
                return;

            Disconnect(Source.Token);
        }
    }

    private void Disconnect(CancellationToken token)
    {
        Task.Run(
            async () =>
            {
                ButtonEnable(false, ButtonAdvanceDate, ButtonReadRaids, ButtonDisconnect, ButtonViewRAM, ButtonDownloadEvents, SendScreenshot, B_DateTools);
                try
                {
                    (bool success, string err) = await ConnectionWrapper
                        .DisconnectAsync(token)
                        .ConfigureAwait(false);
                    if (!success)
                        await this.DisplayMessageBox(Webhook, err, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await this
                        .DisplayMessageBox(Webhook, ex.Message, token)
                        .ConfigureAwait(false);
                }

                await Source.CancelAsync();
                await DateAdvanceSource.CancelAsync();
                Source = new();
                DateAdvanceSource = new();
                RaidBlockOffsetBase = 0;
                ButtonEnable(true, ButtonConnect);
            },
            token
        );
    }

    private void ButtonPrevious_Click(object sender, EventArgs e)
    {
        var count = RaidContainer.GetRaidCount();
        if (count > 0)
        {
            var index = (ComboIndex.SelectedIndex + count - 1) % count; // Wrap around
            if (ModifierKeys == Keys.Shift)
            {
                for (int i = 0; i < count; i++)
                {
                    var chk = (index + count - i) % count;
                    if (
                        StopAdvances
                        || RaidFilters.Any(
                            z =>
                                z.FilterSatisfied(
                                    RaidContainer,
                                    RaidContainer.Encounters[chk],
                                    RaidContainer.Raids[chk],
                                    RaidBoost.SelectedIndex
                                )
                        )
                    )
                    {
                        index = chk;
                        break;
                    }
                }
            }
            ComboIndex.SelectedIndex = index;
        }
    }

    private void ButtonNext_Click(object sender, EventArgs e)
    {
        var count = RaidContainer.GetRaidCount();
        if (count > 0)
        {
            var index = (ComboIndex.SelectedIndex + count + 1) % count; // Wrap around
            if (ModifierKeys == Keys.Shift)
            {
                for (int i = 0; i < count; i++)
                {
                    var chk = (index + count + i) % count;
                    if (StopAdvances || RaidFilters.Any(z => z.FilterSatisfied(RaidContainer, RaidContainer.Encounters[chk], RaidContainer.Raids[chk], RaidBoost.SelectedIndex)))
                    {
                        index = chk;
                        break;
                    }
                }
            }
            ComboIndex.SelectedIndex = index;
        }
    }

    private void ButtonAdvanceDate_Click(object sender, EventArgs e)
    {
        if (ConnectionWrapper is not { Connected: true })
            return;

        ButtonAdvanceDate.Visible = false;
        StopAdvance_Button.Visible = true;
        Task.Run(async () => await AdvanceDateClick(DateAdvanceSource.Token).ConfigureAwait(false), Source.Token);
    }

    private async Task AdvanceDateClick(CancellationToken token)
    {
        try
        {
            ButtonEnable(false, ButtonViewRAM, ButtonAdvanceDate, ButtonDisconnect, ButtonDownloadEvents, SendScreenshot, ButtonReadRaids);
            Invoke(() => Label_DayAdvance.Visible = true);
            SearchTimer.Start();
            stopwatch.Start();
            _WindowState = WindowState;

            var advanceTextInit =
                $"日期推进成功 {GetStatDaySkipSuccess()} / {GetStatDaySkipTries()}";
            Invoke(() => Label_DayAdvance.Text = advanceTextInit);
            if (teraRaidView is not null)
                Invoke(() => teraRaidView.DaySkips.Text = advanceTextInit);

            var stop = false;
            var raids = RaidContainer.Raids;
            int skips = 0;

            while (!stop)
            {
                if (skips >= Config.SystemReset)
                {
                    // When raids are generated, the game determines raids for both the current and next day.
                    // In order to avoid rescanning the same raids on a reset, save the game before reset.
                    await ConnectionWrapper.SaveGame(Config, token).ConfigureAwait(false);
                    await ConnectionWrapper.CloseGame(token).ConfigureAwait(false);
                    await ConnectionWrapper.StartGame(Config, token).ConfigureAwait(false);

                    RaidBlockOffsetBase = 0;
                    RaidBlockOffsetKitakami = 0;
                    RaidBlockOffsetBlueberry = 0;
                    skips = 0;

                    // Read the initial raids upon reopening the game to correctly detect if the next advance fails
                    await ReadRaids(token).ConfigureAwait(false);
                    raids = RaidContainer.Raids;
                }

                var previousSeeds = raids.Select(z => z.Seed).ToList();
                UpdateStatus("正在修改日期……");
                bool streamer = Config.StreamerView && teraRaidView is not null;
                Action<int>? action = streamer ? teraRaidView!.UpdateProgressBar : null;
                await ConnectionWrapper
                    .AdvanceDate(Config, skips, token, action)
                    .ConfigureAwait(false);
                await ReadRaids(token).ConfigureAwait(false);
                raids = RaidContainer.Raids;

                Invoke(DisplayRaid);
                if (streamer)
                    Invoke(DisplayPrettyRaid);

                stop = StopAdvanceDate(previousSeeds);
                skips++;
                var advanceText =
                    $"日期推进成功 {GetStatDaySkipSuccess()} / {GetStatDaySkipTries()}";
                Invoke(() => Label_DayAdvance.Text = advanceText);
                if (teraRaidView is not null)
                    Invoke(() => teraRaidView.DaySkips.Text = advanceText);
            }

            stopwatch.Stop();
            SearchTimer.Stop();
            var timeSpan = stopwatch.Elapsed;
            var time = $"{timeSpan.Days:00}:{timeSpan.Hours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";

            if (Config.PlaySound)
                System.Media.SystemSounds.Asterisk.Play();

            if (Config.FocusWindow)
            {
                Invoke(() =>
                {
                    WindowState = _WindowState;
                    Activate();
                });
            }

            if (Config.EnableFilters)
            {
                var encounters = RaidContainer.Encounters;
                var rewards = RaidContainer.Rewards;
                var boost = Invoke(() => RaidBoost.SelectedIndex);
                var satisfiedFilters = new List<(RaidFilter, ITeraRaid, Raid, IReadOnlyList<(int, int, int)>)>();
                for (int i = 0; i < raids.Count; i++)
                {
                    foreach (var filter in RaidFilters)
                    {
                        if (filter is null)
                            continue;

                        if (filter.FilterSatisfied(RaidContainer, encounters[i], raids[i], boost))
                        {
                            satisfiedFilters.Add((filter, encounters[i], raids[i], rewards[i]));
                            if (InvokeRequired)
                                Invoke(() => ComboIndex.SelectedIndex = i);
                            else
                                ComboIndex.SelectedIndex = i;
                        }
                    }
                }

                if (Config.EnableNotification)
                {
                    foreach (var satisfied in satisfiedFilters)
                    {
                        var teraType = satisfied.Item3.GetTeraType(satisfied.Item2);
                        var color = TypeColor.GetTypeSpriteColor((byte)teraType);
                        var hexColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                        var blank = new PK9
                        {
                            Species = satisfied.Item2.Species,
                            Form = satisfied.Item2.Form,
                        };

                        var spriteName = GetSpriteNameForUrl(
                            blank,
                            satisfied.Item3.CheckIsShiny(satisfied.Item2)
                        );
                        await Webhook
                            .SendNotification(satisfied.Item2, satisfied.Item3, satisfied.Item1, time, satisfied.Item4, hexColor, spriteName, Source.Token)
                            .ConfigureAwait(false);
                    }
                }

                // Save game on match.
                if (Config.SaveOnMatch && satisfiedFilters.Count > 0)
                    await ConnectionWrapper.SaveGame(Config, token).ConfigureAwait(false);

                if (Config.EnableAlertWindow)
                    await this.DisplayMessageBox(Webhook, $"{Config.AlertWindowMessage}\n\n耗时：{time}", token, "已找到目标！").ConfigureAwait(false);
                Invoke(() => Text = $"{formTitle} [已在 {time}]");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("已停止日期推进。");
            SearchTimer.Stop();
            if (ex is not TaskCanceledException)
                await this.DisplayMessageBox(Webhook, ex.Message, token, "日期推进错误").ConfigureAwait(false);
        }

        if (InvokeRequired)
        {
            Invoke(() =>
            {
                ButtonAdvanceDate.Visible = true;
                StopAdvance_Button.Visible = false;
            });
        }
        else
        {
            ButtonAdvanceDate.Visible = true;
            StopAdvance_Button.Visible = false;
        }

        var buttons = new[]
        {
            ButtonViewRAM,
            ButtonAdvanceDate,
            ButtonDisconnect,
            ButtonDownloadEvents,
            SendScreenshot,
            ButtonReadRaids,
        };
        ButtonEnable(true, buttons);
        DateAdvanceSource = new();
    }

    private void StopAdvanceButton_Click(object sender, EventArgs e)
    {
        StopAdvance_Button.Visible = false;
        ButtonAdvanceDate.Visible = true;
        DateAdvanceSource.Cancel();
        DateAdvanceSource = new();
        teraRaidView?.ResetProgressBar();

        stopwatch.Stop();
        SearchTimer.Stop();
    }

    private void ButtonReadRaids_Click(object sender, EventArgs e)
    {
        Task.Run(async () => await ReadRaidsAsync(Source.Token).ConfigureAwait(false), Source.Token);
    }

    private async Task ReadRaidsAsync(CancellationToken token)
    {
        if (IsReading)
        {
            await this
                .DisplayMessageBox(Webhook,
                    "请等待当前内存读取操作完成。",
                    token
                )
                .ConfigureAwait(false);
            return;
        }

        ButtonEnable(false, ButtonViewRAM, ButtonAdvanceDate, ButtonDisconnect, ButtonDownloadEvents, SendScreenshot, ButtonReadRaids);
        try
        {
            await ReadRaids(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this
                .DisplayMessageBox(Webhook,
                    $"读取太晶坑时发生错误： {ex.Message}",
                    token
                )
                .ConfigureAwait(false);
        }

        ButtonEnable(true, ButtonViewRAM, ButtonAdvanceDate, ButtonDisconnect, ButtonDownloadEvents, SendScreenshot, ButtonReadRaids);
    }

    private void ViewRAM_Click(object sender, EventArgs e)
    {
        if (IsReading)
        {
            Task.Run(async () => await this.DisplayMessageBox(Webhook, "请等待当前内存读取操作完成。", Source.Token).ConfigureAwait(false), Source.Token);
            return;
        }

        ButtonEnable(false, ButtonViewRAM);
        RaidBlockViewer window = default!;

        if (ConnectionWrapper is { Connected: true } && ModifierKeys == Keys.Shift)
        {
            try
            {
                var data = ConnectionWrapper.Connection
                    .ReadBytesAbsoluteAsync(
                        RaidBlockOffsetBase,
                        (int)RaidBlock.SIZE_BASE,
                        Source.Token
                    )
                    .Result;
                window = new(data, RaidBlockOffsetBase);
            }
            catch (Exception ex)
            {
                ButtonEnable(true, ButtonViewRAM);
                Task.Run(
                    async () =>
                        await this
                            .DisplayMessageBox(Webhook, ex.Message, Source.Token)
                            .ConfigureAwait(false),
                    Source.Token
                );
                return;
            }
        }
        else if (RaidContainer.Raids.Count > ComboIndex.SelectedIndex)
        {
            var data = RaidContainer.Raids[ComboIndex.SelectedIndex].GetData();
            window = new(data, RaidBlockOffsetBase);
        }

        ShowDialog(window);
        ButtonEnable(true, ButtonViewRAM);
    }

    private void StopFilter_Click(object sender, EventArgs e)
    {
        var form = new FilterSettings(ref RaidFilters);
        ShowDialog(form);
    }

    private void DownloadEvents_Click(object sender, EventArgs e)
    {
        if (ConnectionWrapper is not { Connected: true })
            return;

        if (IsReading)
        {
            Task.Run(async () => await this.DisplayMessageBox(Webhook, "请等待当前内存读取操作完成。", Source.Token).ConfigureAwait(false), Source.Token);
            return;
        }

        Task.Run(async () => await DownloadEventsAsync(Source.Token).ConfigureAwait(false), Source.Token);
    }

    private async Task DownloadEventsAsync(CancellationToken token)
    {
        ButtonEnable(false, ButtonViewRAM, ButtonAdvanceDate, ButtonDisconnect, ButtonDownloadEvents, SendScreenshot, ButtonReadRaids);
        UpdateStatus("正在读取活动太晶坑状态……");

        try
        {
            await ReadEventRaids(token, true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this
                .DisplayMessageBox(Webhook,
                    $"读取活动太晶坑时发生错误： {ex.Message}",
                    token
                )
                .ConfigureAwait(false);
        }

        ButtonEnable(true, ButtonViewRAM, ButtonAdvanceDate, ButtonDisconnect, ButtonDownloadEvents, SendScreenshot, ButtonReadRaids);
        UpdateStatus("已完成！");
    }

    private void Seed_Click(object sender, EventArgs e)
    {
        if (ModifierKeys == Keys.Shift && RaidContainer.Raids.Count > ComboIndex.SelectedIndex)
        {
            var raid = RaidContainer.Raids[ComboIndex.SelectedIndex];
            Seed.Text = HideSeed ? $"{raid.Seed:X8}" : "已隐藏";
            EC.Text = HideSeed ? $"{raid.EC:X8}" : "已隐藏";
            PID.Text =
                (HideSeed ? $"{raid.PID:X8}" : "已隐藏")
                + $"{(raid.IsShiny ? " (☆)" : string.Empty)}";
            HideSeed = !HideSeed;
            ActiveControl = null;
        }
    }

    private void ConfigSettings_Click(object sender, EventArgs e)
    {
        var form = new ConfigWindow(Config);
        ShowDialog(form);
    }

    private void EnableFilters_Click(object sender, EventArgs e)
    {
        Config.EnableFilters = CheckEnableFilters.Checked;
    }

    private readonly JsonSerializerOptions options = new() { WriteIndented = true };
    private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
    {
        var configpath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        string output = JsonSerializer.Serialize(Config, options);
        using StreamWriter sw = new(configpath);
        sw.Write(output);

        if (ConnectionWrapper is { Connected: true })
        {
            try
            {
                _ = ConnectionWrapper.DisconnectAsync(Source.Token).Result;
            }
            catch
            {
                // ignored
            }
        }

        Source.Cancel();
        DateAdvanceSource.Cancel();
        Source = new();
        DateAdvanceSource = new();
    }

    private async Task ReadEventRaids(CancellationToken token, bool force = false)
    {
        var priorityFile = Path.Combine(
            Directory.GetCurrentDirectory(),
            "cache",
            "raid_priority_array"
        );
        if (!force && File.Exists(priorityFile))
        {
            var (_, version) = FlatbufferDumper.DumpDeliveryPriorities(
                await File.ReadAllBytesAsync(priorityFile, token)
            );
            var block = await ConnectionWrapper
                .ReadBlockDefault(
                    BCATRaidPriorityLocation,
                    "raid_priority_array.tmp",
                    true,
                    token
                )
                .ConfigureAwait(false);
            var (_, v2) = FlatbufferDumper.DumpDeliveryPriorities(block);
            if (version != v2)
                force = true;

            var tempFile = Path.Combine(
                Directory.GetCurrentDirectory(),
                "cache",
                "raid_priority_array.tmp"
            );
            if (File.Exists(tempFile))
                File.Delete(tempFile);

            if (v2 == 0) // raid reset
                return;
        }

        var deliveryRaidPriorityFlatbuffer = await ConnectionWrapper
            .ReadBlockDefault(BCATRaidPriorityLocation, "raid_priority_array", force, token)
            .ConfigureAwait(false);
        var (groupID, priority) = FlatbufferDumper.DumpDeliveryPriorities(deliveryRaidPriorityFlatbuffer);
        if (priority == 0)
            return;

        var deliveryRaidFlatbuffer = await ConnectionWrapper
            .ReadBlockDefault(BCATRaidBinaryLocation, "raid_enemy_array", force, token)
            .ConfigureAwait(false);
        var deliveryFixedRewardFlatbuffer = await ConnectionWrapper
            .ReadBlockDefault(
                BCATRaidFixedRewardLocation,
                "fixed_reward_item_array",
                force,
                token
            )
            .ConfigureAwait(false);
        var deliveryLotteryRewardFlatbuffer = await ConnectionWrapper
            .ReadBlockDefault(
                BCATRaidLotteryRewardLocation,
                "lottery_reward_item_array",
                force,
                token
            )
            .ConfigureAwait(false);

        RaidContainer.DistTeraRaids = TeraDistribution.GetAllEncounters(deliveryRaidFlatbuffer);
        RaidContainer.MightTeraRaids = TeraMight.GetAllEncounters(deliveryRaidFlatbuffer);
        RaidContainer.DeliveryRaidPriority = groupID;
        RaidContainer.DeliveryRaidFixedRewards = FlatbufferDumper.DumpFixedRewards(
            deliveryFixedRewardFlatbuffer
        );
        RaidContainer.DeliveryRaidLotteryRewards = FlatbufferDumper.DumpLotteryRewards(
            deliveryLotteryRewardFlatbuffer
        );
    }

    private void DisplayRaid()
    {
        int index = ComboIndex.SelectedIndex;
        var raids = RaidContainer.Raids;
        if (raids.Count > index)
        {
            Raid raid = raids[index];
            var encounter = RaidContainer.Encounters[index];

            Seed.Text = !HideSeed ? $"{raid.Seed:X8}" : "已隐藏";
            EC.Text = !HideSeed ? $"{raid.EC:X8}" : "已隐藏";
            PID.Text = GetPIDString(raid, encounter);
            Area.Text =
                $"{Areas.GetArea((int)(raid.Area - 1), raid.MapParent)} - 坑位 {raid.Den}";
            labelEvent.Visible = raid.IsEvent;

            var teraType = raid.GetTeraType(encounter);
            TeraType.Text = RaidContainer.Strings.types[teraType];

            int starCount = encounter switch
            {
                TeraDistribution => encounter.Stars,
                TeraMight => encounter.Stars,
                _ => raid.GetStarCount(raid.Difficulty, Config.Progress, raid.IsBlack),
            };
            Difficulty.Text = string.Concat(Enumerable.Repeat("☆", starCount));

            var param = encounter.GetParam();
            var blank = new PK9 { Species = encounter.Species, Form = encounter.Form };

            raid.GenerateDataPK9(blank, param, encounter.Shiny, raid.Seed);

            var img = blank.Sprite();
            ApplyTeraColor((byte)teraType, img, SpriteBackgroundType.BottomStripe);

            var form = ShowdownParsing.GetStringFromForm(
                encounter.Form,
                RaidContainer.Strings,
                encounter.Species,
                EntityContext.Gen9
            );
            if (form.Length > 0 && form[0] != '-')
                form = form.Insert(0, "-");

            Species.Text = $"{RaidContainer.Strings.Species[encounter.Species]}{form}";
            Sprite.Image = img;
            GemIcon.Image = GetDisplayGemImage(teraType, raid);
            Gender.Text = blank.Gender switch { 0 => "雄性", 1 => "雌性", _ => "无性别" };

            var nature = blank.Nature;
            Nature.Text = $"{RaidContainer.Strings.Natures[(int)nature]}";
            Ability.Text = $"{RaidContainer.Strings.Ability[blank.Ability]}";

            var extraMoves = new ushort[] { 0, 0, 0, 0 };
            for (int i = 0; i < encounter.ExtraMoves.Length; i++)
            {
                if (i < extraMoves.Length)
                    extraMoves[i] = encounter.ExtraMoves[i];
            }

            Move1.Text = ShowExtraMoves
                ? RaidContainer.Strings.Move[extraMoves[0]]
                : RaidContainer.Strings.Move[encounter.Move1];
            Move2.Text = ShowExtraMoves
                ? RaidContainer.Strings.Move[extraMoves[1]]
                : RaidContainer.Strings.Move[encounter.Move2];
            Move3.Text = ShowExtraMoves
                ? RaidContainer.Strings.Move[extraMoves[2]]
                : RaidContainer.Strings.Move[encounter.Move3];
            Move4.Text = ShowExtraMoves
                ? RaidContainer.Strings.Move[extraMoves[3]]
                : RaidContainer.Strings.Move[encounter.Move4];

            Span<int> _ivs = stackalloc int[6];
            blank.GetIVs(_ivs);
            IVs.Text = IVsString(Utils.ToSpeedLast(_ivs));
            toolTip.SetToolTip(IVs, IVsString(Utils.ToSpeedLast(_ivs), true));

            // Ported from develop: PK9 size attributes generated for this
            // precise raid seed. All three are raw 0–255 scalar bytes.
            // Do NOT label HeightScalar as metres or WeightScalar as kg.
            _heightScalarDisplay.Text = blank.HeightScalar.ToString();
            _weightScalarDisplay.Text = blank.WeightScalar.ToString();
            _scaleDisplay.Text = blank.Scale.ToString();

            PID.BackColor = raid.CheckIsShiny(encounter) ? Color.Gold : DefaultColor;
            IVs.BackColor = IVs.Text is "31/31/31/31/31/31" ? Color.YellowGreen : DefaultColor;
            return;
        }

        var msg = $"无法显示序号 {index} 的太晶坑。请关闭可能改变内存布局的作弊码或叠加插件（如 Edizon），重启 Switch 后重试。";
        Task.Run(async () => await this.DisplayMessageBox(Webhook, msg, Source.Token).ConfigureAwait(false), Source.Token);
    }

    private static Bitmap? GetDisplayGemImage(int teratype, Raid raid)
    {
        var shouldDisplayBlack = raid.IsBlack || raid.Flags == 3;
        var baseImg = shouldDisplayBlack
            ? (Bitmap?)Properties.Resources.ResourceManager.GetObject($"black_{teratype:D2}")
            : (Bitmap?)Properties.Resources.ResourceManager.GetObject($"gem_{teratype:D2}");
        if (baseImg is null)
            return null;

        var backlayer = new Bitmap(
            baseImg.Width + 10,
            baseImg.Height + 10,
            baseImg.PixelFormat
        );
        baseImg = ImageUtil.LayerImage(backlayer, baseImg, 5, 5);
        var pixels = baseImg.GetBitmapData();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 0;
            }
        }

        baseImg = ImageUtil.GetBitmap(
            pixels,
            baseImg.Width,
            baseImg.Height,
            baseImg.PixelFormat
        );
        if (shouldDisplayBlack)
        {
            var color = Color.Indigo;
            SpriteUtil.GetSpriteGlow(baseImg, color.B, color.G, color.R, out var glow);
            baseImg = ImageUtil.LayerImage(
                ImageUtil.GetBitmap(glow, baseImg.Width, baseImg.Height, baseImg.PixelFormat),
                baseImg,
                0,
                0
            );
        }
        else if (raid.IsEvent)
        {
            var color = Color.DarkTurquoise;
            SpriteUtil.GetSpriteGlow(baseImg, color.B, color.G, color.R, out var glow);
            baseImg = ImageUtil.LayerImage(
                ImageUtil.GetBitmap(glow, baseImg.Width, baseImg.Height, baseImg.PixelFormat),
                baseImg,
                0,
                0
            );
        }
        return baseImg;
    }

    private void DisplayPrettyRaid()
    {
        if (teraRaidView is null)
        {
            Task.Run(
                async () =>
                    await this
                        .DisplayMessageBox(Webhook,
                            "太晶坑详情窗口尚未初始化。",
                            Source.Token
                        )
                        .ConfigureAwait(false),
                Source.Token
            );
            return;
        }

        int index = ComboIndex.SelectedIndex;
        var raids = RaidContainer.Raids;
        if (raids.Count > index)
        {
            Raid raid = raids[index];
            var encounter = RaidContainer.Encounters[index];

            teraRaidView.Area.Text =
                $"{Areas.GetArea((int)(raid.Area - 1), raid.MapParent)} - 坑位 {raid.Den}";

            var teraType = raid.GetTeraType(encounter);
            teraRaidView.TeraType.Image = (Bitmap)
                Properties.Resources.ResourceManager.GetObject($"gem_text_{teraType}")!;

            int StarCount = encounter switch
            {
                TeraDistribution => encounter.Stars,
                TeraMight => encounter.Stars,
                _ => raid.GetStarCount(raid.Difficulty, Config.Progress, raid.IsBlack),
            };
            teraRaidView.Difficulty.Text = string.Concat(Enumerable.Repeat("⭐", StarCount));

            var param = encounter.GetParam();
            var blank = new PK9 { Species = encounter.Species, Form = encounter.Form };

            raid.GenerateDataPK9(blank, param, encounter.Shiny, raid.Seed);

            var img = blank.Sprite();

            teraRaidView.picBoxPokemon.Image = img;
            var form = Utils.GetFormString(blank.Species, blank.Form, RaidContainer.Strings);

            teraRaidView.Species.Text =
                $"{RaidContainer.Strings.Species[encounter.Species]}{form}";
            teraRaidView.Gender.Text = blank.Gender switch { 0 => "雄性", 1 => "雌性", _ => "无性别" };

            var nature = blank.Nature;
            teraRaidView.Nature.Text = $"{RaidContainer.Strings.Natures[(int)nature]}";
            teraRaidView.Ability.Text = $"{RaidContainer.Strings.Ability[blank.Ability]}";

            teraRaidView.Move1.Text =
                encounter.Move1 > 0 ? RaidContainer.Strings.Move[encounter.Move1] : "---";
            teraRaidView.Move2.Text =
                encounter.Move2 > 0 ? RaidContainer.Strings.Move[encounter.Move2] : "---";
            teraRaidView.Move3.Text =
                encounter.Move3 > 0 ? RaidContainer.Strings.Move[encounter.Move3] : "---";
            teraRaidView.Move4.Text =
                encounter.Move4 > 0 ? RaidContainer.Strings.Move[encounter.Move4] : "---";

            var length = encounter.ExtraMoves.Length < 4 ? 4 : encounter.ExtraMoves.Length;
            var extraMoves = new ushort[length];
            for (int i = 0; i < encounter.ExtraMoves.Length; i++)
                extraMoves[i] = encounter.ExtraMoves[i];

            teraRaidView.Move5.Text =
                extraMoves[0] > 0 ? RaidContainer.Strings.Move[extraMoves[0]] : "---";
            teraRaidView.Move6.Text =
                extraMoves[1] > 0 ? RaidContainer.Strings.Move[extraMoves[1]] : "---";
            teraRaidView.Move7.Text =
                extraMoves[2] > 0 ? RaidContainer.Strings.Move[extraMoves[2]] : "---";
            teraRaidView.Move8.Text =
                extraMoves[3] > 0 ? RaidContainer.Strings.Move[extraMoves[3]] : "---";

            Span<int> _ivs = stackalloc int[6];
            blank.GetIVs(_ivs);
            var ivs = Utils.ToSpeedLast(_ivs);

            // HP
            teraRaidView.HP.Text = $"{ivs[0]:D2}";
            teraRaidView.HP.BackColor = Color.FromArgb(0, 5, 25);
            if (teraRaidView.HP.Text is "31")
                teraRaidView.HP.BackColor = Color.ForestGreen;
            else if (teraRaidView.HP.Text is "00")
                teraRaidView.HP.BackColor = Color.DarkRed;

            // ATK
            teraRaidView.ATK.Text = $"{ivs[1]:D2}";
            teraRaidView.ATK.BackColor = Color.FromArgb(0, 5, 25);
            if (teraRaidView.ATK.Text is "31")
                teraRaidView.ATK.BackColor = Color.ForestGreen;
            else if (teraRaidView.ATK.Text is "00")
                teraRaidView.ATK.BackColor = Color.DarkRed;

            // DEF
            teraRaidView.DEF.Text = $"{ivs[2]:D2}";
            teraRaidView.DEF.BackColor = Color.FromArgb(0, 5, 25);
            if (teraRaidView.DEF.Text is "31")
                teraRaidView.DEF.BackColor = Color.ForestGreen;
            else if (teraRaidView.DEF.Text is "00")
                teraRaidView.DEF.BackColor = Color.DarkRed;

            // SPA
            teraRaidView.SPA.Text = $"{ivs[3]:D2}";
            teraRaidView.SPA.BackColor = Color.FromArgb(0, 5, 25);
            if (teraRaidView.SPA.Text is "31")
                teraRaidView.SPA.BackColor = Color.ForestGreen;
            else if (teraRaidView.SPA.Text is "00")
                teraRaidView.SPA.BackColor = Color.DarkRed;

            // SPD
            teraRaidView.SPD.Text = $"{ivs[4]:D2}";
            teraRaidView.SPD.BackColor = Color.FromArgb(0, 5, 25);
            if (teraRaidView.SPD.Text is "31")
                teraRaidView.SPD.BackColor = Color.ForestGreen;
            else if (teraRaidView.SPD.Text is "00")
                teraRaidView.SPD.BackColor = Color.DarkRed;

            // SPEED
            teraRaidView.SPEED.Text = $"{ivs[5]:D2}";
            teraRaidView.SPEED.BackColor = Color.FromArgb(0, 5, 25);
            if (teraRaidView.SPEED.Text is "31")
                teraRaidView.SPEED.BackColor = Color.ForestGreen;
            else if (teraRaidView.SPEED.Text is "00")
                teraRaidView.SPEED.BackColor = Color.DarkRed;

            var map = GenerateMap(raid, teraType);
            if (map is null)
                Task.Run(async () => await this.DisplayMessageBox(Webhook, "生成地图时发生错误。", Source.Token).ConfigureAwait(false), Source.Token);
            teraRaidView.Map.Image = map;

            // Rewards
            var rewards = RaidContainer.Rewards[index];

            teraRaidView.textAbilityPatch.Text = "0";
            teraRaidView.textAbilityPatch.ForeColor = Color.DimGray;
            teraRaidView.labelAbilityPatch.ForeColor = Color.DimGray;

            teraRaidView.textAbilityCapsule.Text = "0";
            teraRaidView.textAbilityCapsule.ForeColor = Color.DimGray;
            teraRaidView.labelAbilityCapsule.ForeColor = Color.DimGray;

            teraRaidView.textBottleCap.Text = "0";
            teraRaidView.textBottleCap.ForeColor = Color.DimGray;
            teraRaidView.labelBottleCap.ForeColor = Color.DimGray;

            teraRaidView.textSweetHerba.Text = "0";
            teraRaidView.textSweetHerba.ForeColor = Color.DimGray;
            teraRaidView.labelSweetHerba.ForeColor = Color.DimGray;

            teraRaidView.textSaltyHerba.Text = "0";
            teraRaidView.textSaltyHerba.ForeColor = Color.DimGray;
            teraRaidView.labelSaltyHerba.ForeColor = Color.DimGray;

            teraRaidView.textBitterHerba.Text = "0";
            teraRaidView.textBitterHerba.ForeColor = Color.DimGray;
            teraRaidView.labelBitterHerba.ForeColor = Color.DimGray;

            teraRaidView.textSourHerba.Text = "0";
            teraRaidView.textSourHerba.ForeColor = Color.DimGray;
            teraRaidView.labelSourHerba.ForeColor = Color.DimGray;

            teraRaidView.textSpicyHerba.Text = "0";
            teraRaidView.textSpicyHerba.ForeColor = Color.DimGray;
            teraRaidView.labelSpicyHerba.ForeColor = Color.DimGray;

            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i].Item1 == 645)
                {
                    teraRaidView.textAbilityCapsule.Text = (
                        int.Parse(teraRaidView.textAbilityCapsule.Text) + 1
                    ).ToString();
                    teraRaidView.textAbilityCapsule.ForeColor = Color.White;
                    teraRaidView.labelAbilityCapsule.ForeColor = Color.WhiteSmoke;
                }
                if (rewards[i].Item1 == 795)
                {
                    teraRaidView.textBottleCap.Text = (
                        int.Parse(teraRaidView.textBottleCap.Text) + 1
                    ).ToString();
                    teraRaidView.textBottleCap.ForeColor = Color.White;
                    teraRaidView.labelBottleCap.ForeColor = Color.WhiteSmoke;
                }
                if (rewards[i].Item1 == 1606)
                {
                    teraRaidView.textAbilityPatch.Text = (
                        int.Parse(teraRaidView.textAbilityPatch.Text) + 1
                    ).ToString();
                    teraRaidView.textAbilityPatch.ForeColor = Color.White;
                    teraRaidView.labelAbilityPatch.ForeColor = Color.WhiteSmoke;
                }
                if (rewards[i].Item1 == 1904)
                {
                    teraRaidView.textSweetHerba.Text = (
                        int.Parse(teraRaidView.textSweetHerba.Text) + 1
                    ).ToString();
                    teraRaidView.textSweetHerba.ForeColor = Color.White;
                    teraRaidView.labelSweetHerba.ForeColor = Color.WhiteSmoke;
                }
                if (rewards[i].Item1 == 1905)
                {
                    teraRaidView.textSaltyHerba.Text = (
                        int.Parse(teraRaidView.textSaltyHerba.Text) + 1
                    ).ToString();
                    teraRaidView.textSaltyHerba.ForeColor = Color.White;
                    teraRaidView.labelSaltyHerba.ForeColor = Color.WhiteSmoke;
                }
                if (rewards[i].Item1 == 1906)
                {
                    teraRaidView.textSourHerba.Text = (
                        int.Parse(teraRaidView.textSourHerba.Text) + 1
                    ).ToString();
                    teraRaidView.textSourHerba.ForeColor = Color.White;
                    teraRaidView.labelSourHerba.ForeColor = Color.WhiteSmoke;
                }
                if (rewards[i].Item1 == 1907)
                {
                    teraRaidView.textBitterHerba.Text = (
                        int.Parse(teraRaidView.textBitterHerba.Text) + 1
                    ).ToString();
                    teraRaidView.textBitterHerba.ForeColor = Color.White;
                    teraRaidView.labelBitterHerba.ForeColor = Color.WhiteSmoke;
                }
                if (rewards[i].Item1 == 1908)
                {
                    teraRaidView.textSpicyHerba.Text = (
                        int.Parse(teraRaidView.textSpicyHerba.Text) + 1
                    ).ToString();
                    teraRaidView.textSpicyHerba.ForeColor = Color.White;
                    teraRaidView.labelSpicyHerba.ForeColor = Color.WhiteSmoke;
                }
            }

            var shiny = raid.CheckIsShiny(encounter);
            teraRaidView.Shiny.Visible = shiny;
            teraRaidView.picShinyAlert.Enabled = shiny;
            return;
        }

        var msg = $"无法显示序号 {index} 的太晶坑。请关闭可能改变内存布局的作弊码或叠加插件（如 Edizon），重启 Switch 后重试。";
        Task.Run(
            async () =>
                await this
                    .DisplayMessageBox(Webhook, msg, Source.Token)
                    .ConfigureAwait(false),
            Source.Token
        );
    }

    private string GetPIDString(Raid raid, ITeraRaid? enc)
    {
        if (HideSeed)
            return "已隐藏";

        const string shiny_mark = " (☆)";
        var pid = $"{raid.PID:X8}";
        return raid.CheckIsShiny(enc) ? pid + shiny_mark : pid;
    }

    private static string IVsString(ReadOnlySpan<int> ivs, bool verbose = false)
    {
        string s = string.Empty;
        var stats = new[] { "HP", "Atk", "Def", "SpA", "SpD", "Spe" };
        for (int i = 0; i < ivs.Length; i++)
        {
            s += $"{ivs[i]:D2}{(verbose ? " " + stats[i] : string.Empty)}";
            if (i < 5)
                s += "/";
        }
        return s;
    }

    private static void ApplyTeraColor(byte elementalType, Bitmap img, SpriteBackgroundType type)
    {
        var color = TypeColor.GetTypeSpriteColor(elementalType);
        var thk = SpriteBuilder.ShowTeraThicknessStripe;
        var op = SpriteBuilder.ShowTeraOpacityStripe;
        var bg = SpriteBuilder.ShowTeraOpacityBackground;
        ApplyColor(img, type, color, thk, op, bg);
    }

    private static void ApplyColor(Bitmap img, SpriteBackgroundType type, Color color, int thick, byte opacStripe, byte opacBack)
    {
        if (type == SpriteBackgroundType.BottomStripe)
        {
            int stripeHeight = thick; // from bottom
            if ((uint)stripeHeight > img.Height) // clamp negative & too-high values back to height.
                stripeHeight = img.Height;

            img.BlendTransparentTo(color, opacStripe, img.Width * 4 * (img.Height - stripeHeight));
        }
        if (type == SpriteBackgroundType.TopStripe)
        {
            int stripeHeight = thick; // from top
            if ((uint)stripeHeight > img.Height) // clamp negative & too-high values back to height.
                stripeHeight = img.Height;

            img.BlendTransparentTo(color, opacStripe, 0, img.Width * 4 * stripeHeight);
        }
        if (type == SpriteBackgroundType.FullBackground) // full background
            img.ChangeTransparentTo(color, opacBack);
    }

    private static Bitmap? GenerateMap(Raid raid, int teratype)
    {
        var original = PKHeX.Drawing.Misc.TypeSpriteUtil.GetTypeSpriteGem((byte)teratype);
        if (original is null)
            return null;

        var gem = new Bitmap(original, new Size(30, 30));
        SpriteUtil.GetSpriteGlow(gem, 0xFF, 0xFF, 0xFF, out var glow, true);
        gem = ImageUtil.LayerImage(gem, ImageUtil.GetBitmap(glow, gem.Width, gem.Height, gem.PixelFormat), 0, 0);
        if (DenLocationsBase is null || DenLocationsBase.Count == 0 ||
            DenLocationsKitakami is null || DenLocationsKitakami.Count == 0 ||
            DenLocationsBlueberry is null || DenLocationsBlueberry.Count == 0)
        {
            return null;
        }

        var locData = raid.MapParent switch
        {
            TeraRaidMapParent.Paldea => DenLocationsBase,
            TeraRaidMapParent.Kitakami => DenLocationsKitakami,
            _ => DenLocationsBlueberry,
        };
        var map = raid.MapParent switch
        {
            TeraRaidMapParent.Paldea => MapBase,
            TeraRaidMapParent.Kitakami => MapKitakami,
            _ => MapBlueberry,
        };
        try
        {
            (double x, double z) = GetCoordinate(raid, locData, gem);
            return ImageUtil.LayerImage((Bitmap)map, gem, (int)x, (int)z);
        }
        catch
        {
            return null;
        }
    }

    private static (double x, double y) GetCoordinate(Raid raid, IReadOnlyDictionary<string, float[]> locData, Bitmap gem)
    {
        var m = MapMagic.GetMapMagic(raid.MapParent);
        double x = m.ConvertX(locData[$"{raid.Area}-{raid.LotteryGroup}-{raid.Den}"][0]) - (gem.Size.Width / 2);
        double z = m.ConvertZ(locData[$"{raid.Area}-{raid.LotteryGroup}-{raid.Den}"][2]) - (gem.Size.Height / 2);

        return (x, z);
    }

    private bool StopAdvanceDate(IEnumerable<uint> previousSeeds)
    {
        var raids = RaidContainer.Raids;
        var curSeeds = raids.Select(x => x.Seed).ToArray();
        var sameRaids = curSeeds.Except(previousSeeds).ToArray().Length == 0;

        StatDaySkipTries++;
        if (sameRaids)
            return false;

        StatDaySkipSuccess++;
        if (!Config.EnableFilters)
            return true;

        foreach (RaidFilter rf in RaidFilters)
        {
            var index = Invoke(() => RaidBoost.SelectedIndex);
            var encounters = RaidContainer.Encounters;
            if (rf.FilterSatisfied(RaidContainer, encounters, raids, index))
                return true;
        }

        return StopAdvances;
    }

    private async Task ReadRaids(CancellationToken token)
    {
        if (Config is { PaldeaScan: false, KitakamiScan: false, BlueberryScan: false })
        {
            await this.DisplayMessageBox(Webhook, "请先在“常规设置”中选择要扫描的区域。", token, "尚未选择扫描区域").ConfigureAwait(false);
            return;
        }

        if (RaidBlockOffsetBase == 0)
        {
            UpdateStatus("正在缓存太晶坑数据指针……");
            RaidBlockOffsetBase = await ConnectionWrapper.Connection
                .PointerAll(RaidBlockPointerBase.ToArray(), token)
                .ConfigureAwait(false);
            RaidBlockOffsetKitakami = await ConnectionWrapper.Connection
                .PointerAll(RaidBlockPointerKitakami.ToArray(), token)
                .ConfigureAwait(false);
            RaidBlockOffsetBlueberry = await ConnectionWrapper.Connection
                .PointerAll(RaidBlockPointerBlueberry.ToArray(), token)
                .ConfigureAwait(false);
        }

        RaidContainer.ClearRaids();
        RaidContainer.ClearEncounters();
        RaidContainer.ClearRewards();

        // Base
        var msg = string.Empty;
        int delivery,
            enc;

        if (Config.PaldeaScan)
        {
            UpdateStatus("正在读取帕底亚太晶坑数据……");
            var data = await ConnectionWrapper.Connection
                .ReadBytesAbsoluteAsync(RaidBlockOffsetBase + RaidBlock.HEADER_SIZE, (int)RaidBlock.SIZE_BASE, token)
                .ConfigureAwait(false);

            (delivery, enc) = RaidContainer.ReadAllRaids(data, Config.Progress, Config.EventProgress, GetRaidBoost(), TeraRaidMapParent.Paldea);
            if (enc > 0)
                msg += $"未找到对应的太晶坑遭遇，共 {enc} 个。\n";

            if (delivery > 0)
                msg += $"活动分发组 ID 无效，共 {delivery} 个。可尝试删除“cache”文件夹。\n";

            if (msg != string.Empty)
            {
                msg += $"\n详细信息请查看文件“raid_dbg_{TeraRaidMapParent.Paldea}.txt”。";
                await this.DisplayMessageBox(Webhook, msg, token, "读取太晶坑失败").ConfigureAwait(false);
            }
        }

        var raids = RaidContainer.Raids;
        var encounters = RaidContainer.Encounters;
        var rewards = RaidContainer.Rewards;
        RaidContainer.ClearRaids();
        RaidContainer.ClearEncounters();
        RaidContainer.ClearRewards();

        // Kitakami
        if (Config.KitakamiScan)
        {
            UpdateStatus("正在读取北上乡太晶坑数据……");
            var data = await ConnectionWrapper.Connection
                .ReadBytesAbsoluteAsync(RaidBlockOffsetKitakami, (int)RaidBlock.SIZE_KITAKAMI, token)
                .ConfigureAwait(false);

            msg = string.Empty;
            (delivery, enc) = RaidContainer.ReadAllRaids(data, Config.Progress, Config.EventProgress, GetRaidBoost(), TeraRaidMapParent.Kitakami);
            if (enc > 0)
                msg += $"未找到对应的太晶坑遭遇，共 {enc} 个。\n";

            if (delivery > 0)
                msg += $"活动分发组 ID 无效，共 {delivery} 个。可尝试删除“cache”文件夹。\n";

            if (msg != string.Empty)
            {
                msg += $"\n详细信息请查看文件“raid_dbg_{TeraRaidMapParent.Kitakami}.txt”。";
                await this.DisplayMessageBox(Webhook, msg, token, "读取太晶坑失败")
                    .ConfigureAwait(false);
            }
        }

        var allRaids = raids.Concat(RaidContainer.Raids).ToList().AsReadOnly();
        var allEncounters = encounters.Concat(RaidContainer.Encounters).ToList().AsReadOnly();
        var allRewards = rewards.Concat(RaidContainer.Rewards).ToList().AsReadOnly();
        RaidContainer.ClearRaids();
        RaidContainer.ClearEncounters();
        RaidContainer.ClearRewards();

        // Blueberry
        if (Config.BlueberryScan)
        {
            UpdateStatus("正在读取蓝莓学园太晶坑数据……");
            var data = await ConnectionWrapper.Connection
                .ReadBytesAbsoluteAsync(RaidBlockOffsetBlueberry, (int)RaidBlock.SIZE_BLUEBERRY, token)
                .ConfigureAwait(false);

            msg = string.Empty;
            (delivery, enc) = RaidContainer.ReadAllRaids(data, Config.Progress, Config.EventProgress, GetRaidBoost(), TeraRaidMapParent.Blueberry);
            if (enc > 0)
                msg += $"未找到对应的太晶坑遭遇，共 {enc} 个。\n";

            if (delivery > 0)
                msg += $"活动分发组 ID 无效，共 {delivery} 个。可尝试删除“cache”文件夹。\n";

            if (msg != string.Empty)
            {
                msg += $"\n详细信息请查看文件“raid_dbg_{TeraRaidMapParent.Blueberry}.txt”。";
                await this.DisplayMessageBox(Webhook, msg, token, "读取太晶坑失败")
                    .ConfigureAwait(false);
            }
        }

        allRaids = allRaids.Concat(RaidContainer.Raids).ToList().AsReadOnly();
        allEncounters = allEncounters.Concat(RaidContainer.Encounters).ToList().AsReadOnly();
        allRewards = allRewards.Concat(RaidContainer.Rewards).ToList().AsReadOnly();

        RaidContainer.SetRaids(allRaids);
        RaidContainer.SetEncounters(allEncounters);
        RaidContainer.SetRewards(allRewards);

        UpdateStatus("已完成！");

        var filterMatchCount = Enumerable.Range(0, allRaids.Count)
            .Count(c => RaidFilters.Any(z => z.FilterSatisfied(RaidContainer, allEncounters[c], allRaids[c], GetRaidBoost())));
        if (InvokeRequired)
            Invoke(() => LabelLoadedRaids.Text = $"匹配数量：{filterMatchCount}");
        else
            LabelLoadedRaids.Text = $"匹配数量：{filterMatchCount}";

        if (allRaids.Count > 0)
        {
            ButtonEnable(true, ButtonPrevious, ButtonNext);
            var dataSource = Enumerable.Range(0, allRaids.Count)
                .Select(z => $"{z + 1:D} / {allRaids.Count:D}")
                .ToArray();
            if (InvokeRequired)
                Invoke(() => ComboIndex.DataSource = dataSource);
            else
                ComboIndex.DataSource = dataSource;

            if (InvokeRequired)
                Invoke(() => ComboIndex.SelectedIndex = ComboIndex.SelectedIndex < allRaids.Count ? ComboIndex.SelectedIndex : 0);
            else
                ComboIndex.SelectedIndex = ComboIndex.SelectedIndex < allRaids.Count ? ComboIndex.SelectedIndex : 0;
        }
        else
        {
            ButtonEnable(false, ButtonPrevious, ButtonNext);
            if (allRaids.Count > RaidBlock.MAX_COUNT_BASE + RaidBlock.MAX_COUNT_KITAKAMI || allRaids.Count == 0)
            {
                msg = "内存读取异常。请关闭作弊码或可能改变内存布局的叠加插件，重启 Switch 后重试。";
                await this.DisplayMessageBox(Webhook, msg, token, "读取太晶坑失败").ConfigureAwait(false);
            }
        }
    }

    public void Game_SelectedIndexChanged(string name)
    {
        Config.Game = name;
        RaidContainer.SetGame(name);
        if (RaidContainer.Raids.Count > 0)
            DisplayRaid();
    }

    public void Protocol_SelectedIndexChanged(SwitchProtocol protocol)
    {
        Config.Protocol = protocol;
        ConnectionConfig.Protocol = protocol;
        if (protocol is SwitchProtocol.USB)
        {
            InputSwitchIP.Visible = false;
            LabelSwitchIP.Visible = false;
            USB_Port_label.Visible = true;
            USB_Port_TB.Visible = true;
        }
        else
        {
            InputSwitchIP.Visible = true;
            LabelSwitchIP.Visible = true;
            USB_Port_label.Visible = false;
            USB_Port_TB.Visible = false;
        }
    }

    private void DisplayMap(object sender, EventArgs e)
    {
        var raids = RaidContainer.Raids;
        if (raids.Count == 0)
        {
            Task.Run(async () => await this.DisplayMessageBox(Webhook, "尚未载入太晶坑数据。", Source.Token).ConfigureAwait(false), Source.Token);
            return;
        }

        var raid = raids[ComboIndex.SelectedIndex];
        var encounter = RaidContainer.Encounters[ComboIndex.SelectedIndex];
        var teraType = raid.GetTeraType(encounter);
        var map = GenerateMap(raid, teraType);
        if (map is null)
        {
            Task.Run(async () => await this.DisplayMessageBox(Webhook, "生成地图时发生错误。", Source.Token).ConfigureAwait(false), Source.Token);
            return;
        }

        var form = new MapView(map);
        ShowDialog(form);
    }

    private void Rewards_Click(object sender, EventArgs e)
    {
        if (RaidContainer.Raids.Count == 0)
        {
            Task.Run(async () => await this.DisplayMessageBox(Webhook, "尚未载入太晶坑数据。", Source.Token).ConfigureAwait(false), Source.Token);
            return;
        }

        var rewards = RaidContainer.Rewards[ComboIndex.SelectedIndex];
        if (rewards is null)
        {
            Task.Run(async () => await this.DisplayMessageBox(Webhook, "显示奖励时发生错误。", Source.Token).ConfigureAwait(false), Source.Token);
            return;
        }

        var form = new RewardsView(RaidContainer.Strings.Item, RaidContainer.Strings.Move, rewards);
        ShowDialog(form);
    }

    private void RaidBoost_SelectedIndexChanged(object sender, EventArgs e)
    {
        RaidContainer.ClearRewards();
        var raids = RaidContainer.Raids;
        var encounters = RaidContainer.Encounters;

        List<List<(int, int, int)>> newRewards = [];
        for (int i = 0; i < raids.Count; i++)
        {
            var raid = raids[i];
            var encounter = encounters[i];
            newRewards.Add(encounter.GetRewards(RaidContainer, raid, RaidBoost.SelectedIndex));
        }
        RaidContainer.SetRewards(newRewards);
    }

    private void Move_Clicked(object sender, EventArgs e)
    {
        if (RaidContainer.Raids.Count == 0)
        {
            Task.Run(async () => await this.DisplayMessageBox(Webhook, "尚未载入太晶坑数据。", Source.Token).ConfigureAwait(false), Source.Token);
            return;
        }

        var encounter = RaidContainer.Encounters[ComboIndex.SelectedIndex];
        if (encounter is null)
            return;

        ShowExtraMoves ^= true;
        // A responsive table owns the position. Moving Location manually
        // used to collide with neighboring fields at Windows 125%/150% DPI.
        LabelMoves.Text = ShowExtraMoves ? "追加招式：" : "招式 1：";

        var length = encounter.ExtraMoves.Length < 4 ? 4 : encounter.ExtraMoves.Length;
        var extraMoves = new ushort[length];
        for (int i = 0; i < encounter.ExtraMoves.Length; i++)
            extraMoves[i] = encounter.ExtraMoves[i];

        Move1.Text = ShowExtraMoves
            ? RaidContainer.Strings.Move[extraMoves[0]]
            : RaidContainer.Strings.Move[encounter.Move1];
        Move2.Text = ShowExtraMoves
            ? RaidContainer.Strings.Move[extraMoves[1]]
            : RaidContainer.Strings.Move[encounter.Move2];
        Move3.Text = ShowExtraMoves
            ? RaidContainer.Strings.Move[extraMoves[2]]
            : RaidContainer.Strings.Move[encounter.Move3];
        Move4.Text = ShowExtraMoves
            ? RaidContainer.Strings.Move[extraMoves[3]]
            : RaidContainer.Strings.Move[encounter.Move4];
    }

    private void ComboIndex_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (RaidContainer.Raids.Count == 0)
            return;

        DisplayRaid();
        if (Config.StreamerView)
            DisplayPrettyRaid();
    }

    private void SendScreenshot_Click(object sender, EventArgs e)
    {
        Task.Run(async () =>
            {
                try
                {
                    await Webhook.SendScreenshot(ConnectionWrapper.Connection, Source.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await this.DisplayMessageBox(Webhook, $"发送截图失败：{ex.Message}", Source.Token).ConfigureAwait(false);
                }
            },
            Source.Token
        );
    }

    private void SearchTimer_Elapsed(object sender, EventArgs e)
    {
        if (!stopwatch.IsRunning)
            return;

        var timeSpan = stopwatch.Elapsed;
        var time = $"{timeSpan.Days:00}:{timeSpan.Hours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";

        Invoke(() => Text = $"{formTitle} [已搜索 {time}]");
        if (Config.StreamerView && teraRaidView is not null)
            Invoke(() => teraRaidView.textSearchTime.Text = time);
    }

    public void TestWebhook() => Task.Run(async () => await TestWebhookAsync(Source.Token).ConfigureAwait(false), Source.Token);

    private async Task TestWebhookAsync(CancellationToken token)
    {
        var filter = new RaidFilter { Name = "测试 Webhook" };

        int i = ComboIndex.SelectedIndex;

        var raids = RaidContainer.Raids;
        var encounters = RaidContainer.Encounters;
        var rewards = RaidContainer.Rewards;
        if (i <= -1 || encounters[i] is null || raids[i] is null)
        {
            await this.DisplayMessageBox(Webhook, "请先连接 Switch 并找到一个符合条件的太晶坑。", token).ConfigureAwait(false);
            return;
        }

        var timeSpan = stopwatch.Elapsed;
        string time = $"{timeSpan.Hours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
        var teraType = raids[i].GetTeraType(encounters[i]);
        var color = TypeColor.GetTypeSpriteColor((byte)teraType);
        var hexColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";

        var blank = new PK9
        {
            Species = encounters[i].Species,
            Form = encounters[i].Form,
            Gender = encounters[i].Gender,
        };
        blank.SetSuggestedFormArgument(blank.Species, blank.Form, blank.Context, new LegalityAnalysis(blank).Info.EvoChainsAllGens);

        var spriteName = GetSpriteNameForUrl(blank, raids[i].CheckIsShiny(encounters[i]));
        await Webhook.SendNotification(encounters[i], raids[i], filter, time, rewards[i], hexColor, spriteName, token).ConfigureAwait(false);
    }

    public void ToggleStreamerView()
    {
        if (Config.StreamerView)
        {
            teraRaidView = new();
            teraRaidView.Map.Image = MapBase;
            teraRaidView.Show();
        }
        else if (!Config.StreamerView && teraRaidView is not null)
        {
            teraRaidView.Close();
        }
    }

    private static string GetSpriteNameForUrl(PK9 pk, bool shiny)
    {
        // Since we're later using this for URL assembly later, we need dashes instead of underscores for forms.
        var spriteName = SpriteName.GetResourceStringSprite(pk.Species, pk.Form, pk.Gender, pk.FormArgument, EntityContext.Gen9, shiny)[1..];
        return spriteName.Replace('_', '-').Insert(0, "_");
    }

    private void B_DateTools_Click(object sender, EventArgs e)
    {
        Task.Run(async () =>
        {
            try
            {
                var tick = await ConnectionWrapper.GetCurrentTime(Source.Token).ConfigureAwait(false);
                var form = new TickModifier(tick, ConnectionWrapper, Webhook, Source.Token);
                ShowDialog(form);
            }
            catch (Exception ex)
            {
                await this.DisplayMessageBox(Webhook, $"读取日期失败：{ex.Message}", Source.Token).ConfigureAwait(false);
            }
        });
    }


    private void CheckForUpdates()
    {
        Task.Run(async () =>
        {
            Version? latestVersion;
            try { latestVersion = Utils.GetLatestVersion(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception while checking for latest version: {ex}");
                return;
            }

            if (latestVersion is null || latestVersion <= CurrentVersion)
                return;

            while (!IsHandleCreated) // Wait for form to be ready
                await Task.Delay(2_000).ConfigureAwait(false);
            await InvokeAsync(() => NotifyNewVersionAvailable(latestVersion));
        });
    }

    private void NotifyNewVersionAvailable(Version version)
    {
        Text += $" - 发现新版本 v{version.Major}.{version.Minor}.{version.Build} 可更新！";
        UpdateStatus($"发现新版本 v{version.Major}.{version.Minor}.{version.Build} 可更新！");
#if !DEBUG
        using UpdateNotifPopup nup = new(CurrentVersion, version);
        if (nup.ShowDialog() == DialogResult.OK)
        {
            Process.Start(new ProcessStartInfo("https://github.com/LegoFigure11/RaidCrawler/releases/")
            {
                UseShellExecute = true
            });
        }
#endif
    }
}
