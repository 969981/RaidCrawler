using System.Diagnostics;
using System.Text.Json;
using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Launches ONE PokéLDN SV local LDN host through WSL, with explicit user confirmation.
/// No SysBot write or Nintendo online action is performed by RaidCrawler.
/// </summary>
public sealed class PokeLdnHostForm : Form
{
    private sealed class HostSettings
    {
        public string Distro { get; set; } = "Ubuntu-24.04";
        public string RepoPath { get; set; } = "/home/develop/github/pokeldn";
        public string PokemonPath { get; set; } = "";
        public string Progress { get; set; } = "4star";
        public int Guests { get; set; } = 1;
        public int Seconds { get; set; } = 240;
        public bool IpHost { get; set; }
    }

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidCrawler", "pokeldn-host.json");

    private readonly PokeLdnRaidSelection selection;
    private readonly IReadOnlyList<RaidRewardEntry> rewardRows;
    private readonly TextBox distro = new() { Width = 160 };
    private readonly TextBox repo = new() { Width = 500 };
    private readonly TextBox pokemon = new() { Width = 500 };
    private readonly ComboBox progress = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown guests = new() { Minimum = 1, Maximum = 3, Value = 1 };
    private readonly NumericUpDown seconds = new() { Minimum = 30, Maximum = 7200, Value = 240 };
    private readonly CheckBox ipHost = new() { Text = BenefitLanguage.T("使用 --ip-host（仅 ldn_mitm／模拟器局域网）", "Use --ip-host (ldn_mitm/emulator LAN only)"), AutoSize = true };
    private readonly Button launch = new() { Text = BenefitLanguage.T("启动当前场次", "Start selected raid"), AutoSize = true };
    private readonly Button stop = new() { Text = BenefitLanguage.T("停止主持", "Stop host"), AutoSize = true, Enabled = false };
    private readonly Button copy = new() { Text = BenefitLanguage.T("复制 WSL 命令", "Copy WSL command"), AutoSize = true };
    private readonly TextBox log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, Font = new Font("Consolas", 9),
    };
    private Process? host;

    public PokeLdnHostForm(PokeLdnRaidSelection selected, IReadOnlyList<RaidRewardEntry> rows)
    {
        selection = selected;
        rewardRows = rows.ToArray();
        Text = BenefitLanguage.T($"PokéLDN－单场太晶坑 {selection.Seed:X8}（{BenefitLanguage.Map(selection.Map)}）", $"PokéLDN — one raid {selection.Seed:X8} ({selection.Map})");
        Size = new Size(940, 665);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;

        var screen = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        screen.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        screen.RowStyles.Add(new RowStyle(SizeType.Absolute, 225));
        screen.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(screen);

        var heading = new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = true,
            Text = BenefitLanguage.T(
                $"仅主持一场：{BenefitLanguage.Version(selection.Version)} / {BenefitLanguage.Map(selection.Map)} / {BenefitLanguage.Crystal(selection.Content)}；种子 {selection.Seed:X8}，区域 {selection.Area}，坑位 {selection.Den}，物种 ID {selection.Species}，{selection.Stars} 星；奖励 {rows.Count}/45 行。\r\n" +
                "PokéLDN 会根据种子和游戏进度重新生成首领，请在玩家进入前核对物种与星级。无线 LDN 需要相应网络硬件与权限；--ip-host 仅供 ldn_mitm／模拟器局域网使用，并非任天堂官方互联网模式。\r\n" +
                "此操作单独启动 WSL 主持进程，不会改写 Switch 内存或地图上其他太晶坑。",
                $"ONE RAID ONLY — {selection.Version}/{selection.Map}/{selection.Content}; " +
                $"Seed {selection.Seed:X8}, area {selection.Area}, den {selection.Den}, " +
                $"species {selection.Species}, {selection.Stars}★; {rows.Count}/45 reward rows.\r\n" +
                "PokéLDN derives the encounter from Seed + story progress (verify it matches before joining). " +
                "Radio hosting requires suitable LDN hardware/root; --ip-host is only for ldn_mitm/emulator, NOT Nintendo online.\r\n" +
                "This launcher starts a separate WSL host process. It never writes to your Switch memory or other dens."),
            ForeColor = Color.DarkRed,
        };
        screen.Controls.Add(heading);

        var inputs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8 };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        screen.Controls.Add(inputs);
        AddInput(inputs, 0, BenefitLanguage.T("WSL 发行版", "WSL distribution"), distro);
        AddInput(inputs, 1, BenefitLanguage.T("PokéLDN 路径（WSL）", "PokéLDN repo (WSL)"), repo);
        AddInput(inputs, 2, BenefitLanguage.T("主持宝可梦 .pk9", "Host .pk9 (WSL path)"), pokemon);
        progress.DataSource = BenefitLanguage.Progress.ToArray();
        progress.DisplayMember = nameof(BenefitLanguage.ProgressChoice.Label);
        progress.ValueMember = nameof(BenefitLanguage.ProgressChoice.Value);
        progress.SelectedValue = "4star";
        AddInput(inputs, 3, BenefitLanguage.T("游戏进度", "Story progress"), progress);
        AddInput(inputs, 4, BenefitLanguage.T("参与主机数（1–3）", "Guest consoles (1–3)"), guests);
        AddInput(inputs, 5, BenefitLanguage.T("主持时长（秒）", "Session seconds"), seconds);
        AddInput(inputs, 6, BenefitLanguage.T("网络模式", "Network transport"), ipHost);

        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        row.Controls.Add(launch);
        row.Controls.Add(stop);
        row.Controls.Add(copy);
        var close = new Button { Text = BenefitLanguage.T("关闭", "Close"), AutoSize = true };
        close.Click += (_, _) => Close();
        row.Controls.Add(close);
        inputs.Controls.Add(row, 1, 7);
        var logs = new GroupBox { Text = BenefitLanguage.T("PokéLDN 日志（不抓取网络数据包）", "PokéLDN output (no packet capture)"), Dock = DockStyle.Fill };
        logs.Controls.Add(log);
        screen.Controls.Add(logs);

        launch.Click += (_, _) => StartHost();
        stop.Click += (_, _) => StopHost();
        copy.Click += (_, _) => CopyCommand();
        FormClosing += OnFormClosing;

        try
        {
            if (File.Exists(SettingsFile))
            {
                var saved = JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(SettingsFile));
                if (saved is not null) ApplySettings(saved);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Print(BenefitLanguage.T("配置恢复失败：", "Settings not restored: ") + ex.Message);
        }
        if (progress.SelectedIndex < 0) progress.SelectedValue = "4star";
        if (selection.IsEvent)
        {
            launch.Enabled = false;
            copy.Enabled = false;
            Print(BenefitLanguage.T("这是活动坑。当前版本尚未支持活动配信目录，不能安全重建主持。", "This is an event den; current host integration requires a delivery folder and cannot safely reproduce it."));
        }
    }

    private static void AddInput(TableLayoutPanel table, int line, string caption, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        table.Controls.Add(new Label { Text = caption, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, line);
        table.Controls.Add(control, 1, line);
    }

    private void ApplySettings(HostSettings s)
    {
        distro.Text = s.Distro;
        repo.Text = s.RepoPath;
        pokemon.Text = s.PokemonPath;
        progress.SelectedValue = s.Progress;
        guests.Value = Math.Clamp(s.Guests, 1, 3);
        seconds.Value = Math.Clamp(s.Seconds, 30, 7200);
        ipHost.Checked = s.IpHost;
    }

    private HostSettings ReadSettings() => new()
    {
        Distro = distro.Text.Trim(), RepoPath = repo.Text.Trim(), PokemonPath = pokemon.Text.Trim(),
        Progress = progress.SelectedValue?.ToString() ?? "4star",
        Guests = (int)guests.Value, Seconds = (int)seconds.Value, IpHost = ipHost.Checked,
    };

    private static void ValidateWsl(HostSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Distro) || s.Distro.Any(c => !char.IsLetterOrDigit(c) && c is not ('-' or '_' or '.')))
            throw new ArgumentException("WSL distribution must contain only letters, digits, dash, underscore or dot.");
        if (string.IsNullOrWhiteSpace(s.RepoPath) || !s.RepoPath.StartsWith('/') ||
            s.RepoPath.IndexOfAny(['\n', '\r', '\0']) >= 0)
            throw new ArgumentException("PokéLDN repo must be an absolute WSL/Linux path.");
    }

    private static ProcessStartInfo CreateStartInfo(HostSettings options, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string part in new[] { "--distribution", options.Distro, "--cd", options.RepoPath,
                    "--exec", "python3", "-u", "bin/sv_host.py" }.Concat(arguments))
            info.ArgumentList.Add(part);
        return info;
    }

    private (HostSettings, IReadOnlyList<string>) BuildPlan()
    {
        HostSettings settings = ReadSettings();
        ValidateWsl(settings);
        IReadOnlyList<string> args = PokeLdnHostPlan.BuildArguments(
            selection, rewardRows, settings.PokemonPath, settings.Progress,
            settings.Guests, settings.Seconds, settings.IpHost);
        return (settings, args);
    }

    private void SaveSettings(HostSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void StartHost()
    {
        if (host is { HasExited: false }) return;
        try
        {
            var (settings, args) = BuildPlan();
            var details = BenefitLanguage.T(
                $"确定启动单场本地 LDN 主持？\n游戏：{BenefitLanguage.Version(selection.Version)}；地图：{BenefitLanguage.Map(selection.Map)}；结晶：{BenefitLanguage.Crystal(selection.Content)}\n种子：{selection.Seed:X8}；自定义奖励：{rewardRows.Count} 行；进度：{settings.Progress}。\n\nPokéLDN 将根据种子和进度重新生成首领，请核对星级、物种与属性。此功能不是任天堂官方互联网联机，也不会修改现有坑位存档。",
                $"Start a LOCAL LDN host for {selection.Version}/{selection.Map}/{selection.Content}, " +
                $"Seed {selection.Seed:X8}, {rewardRows.Count} custom rows, progress {settings.Progress}?\n\n" +
                "PokéLDN regenerates the species from seed/progress. Check the console encounter before proceeding. " +
                "This is NOT a Nintendo online raid or an in-game den patch.");
            if (MessageBox.Show(this, details, BenefitLanguage.T("确认单场主持", "Confirm selected single-raid hosting"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            SaveSettings(settings);
            var p = new Process { StartInfo = CreateStartInfo(settings, args), EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) Print(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Print("[stderr] " + e.Data); };
            p.Exited += (_, _) =>
            {
                int exit;
                try { exit = p.ExitCode; }
                catch { return; }
                Print(BenefitLanguage.T($"PokéLDN 主持已退出（退出码 {exit}）。", $"PokéLDN host exited (code {exit})."));
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(() => { launch.Enabled = !selection.IsEvent; stop.Enabled = false; });
            };
            if (!p.Start())
                throw new IOException(BenefitLanguage.T("无法启动 wsl.exe。", "wsl.exe failed to start."));
            host = p;
            launch.Enabled = false;
            stop.Enabled = true;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            Print(BenefitLanguage.T($"PokéLDN 单场主持已启动；种子 {selection.Seed:X8}，奖励 {rewardRows.Count} 行；未修改 Switch 内存。", $"Started one-raid PokéLDN in WSL. Seed={selection.Seed:X8}, {rewardRows.Count} reward rows. No memory patch was applied."));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("PokéLDN 启动失败", "PokéLDN host could not start"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StopHost()
    {
        try
        {
            if (host is { HasExited: false } current)
            {
                current.Kill(entireProcessTree: true);
                Print(BenefitLanguage.T("已请求停止。再次主持前请确认 WSL 中对应 Python 进程已退出。", "Stop requested. For WSL, verify the Python process is gone before hosting again."));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Print(BenefitLanguage.T("停止失败：", "Stop error: ") + ex.Message);
        }
    }

    private void CopyCommand()
    {
        try
        {
            var (settings, args) = BuildPlan();
            string quoted = string.Join(" ", new[] { "wsl.exe", "--distribution", settings.Distro,
                "--cd", settings.RepoPath, "--exec", "python3", "-u", "bin/sv_host.py" }
                .Concat(args).Select(s => s.Contains(' ') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s));
            Clipboard.SetText(quoted);
            Print(BenefitLanguage.T("已复制主持命令；运行前请核对 WSL 路径和环境。", "Copied host command (review paths and setup before running)."));
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("无法生成命令", "Cannot build command"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Print(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            if (IsHandleCreated) BeginInvoke(() => Print(line));
            return;
        }
        log.AppendText(line + Environment.NewLine);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (host is { HasExited: false })
        {
            if (MessageBox.Show(this, BenefitLanguage.T("PokéLDN 正在主持。确定停止并关闭窗口吗？", "The PokéLDN host is running. Stop it and close the window?"),
                    BenefitLanguage.T("结束单场主持", "Stop selected raid host"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            StopHost();
        }
        host?.Dispose();
        host = null;
    }
}
