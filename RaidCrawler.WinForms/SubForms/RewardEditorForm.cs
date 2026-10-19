using System.Text.Json;
using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>Local reward row editing/exports. Does NOT change live Switch memory.</summary>
public sealed class RewardEditorForm : Form
{
    private readonly IReadOnlyList<string> itemNames;
    private readonly IReadOnlyList<string> moveNames;
    private readonly IReadOnlyList<(int, int, int)> calculated;
    private readonly IReadOnlyList<RaidRewardEntry> defaults;
    private readonly PokeLdnRaidSelection? selectedRaid;
    private readonly DataGridView grid = new();
    private readonly Label counter = new();

    public IReadOnlyList<RaidRewardEntry>? AppliedPreview { get; private set; }

    public RewardEditorForm(IReadOnlyList<string> items, IReadOnlyList<string> moves,
        IReadOnlyList<(int, int, int)> calculated, int species, int teraType,
        IReadOnlyList<RaidRewardEntry>? existingPreview = null,
        PokeLdnRaidSelection? selectedRaid = null)
    {
        itemNames = items;
        moveNames = moves;
        this.calculated = calculated;
        this.selectedRaid = selectedRaid;
        defaults = RaidRewardEditor.FromCalculated(
            calculated.Select(r => (r.Item1, r.Item2, r.Item3)), species, teraType);

        Text = BenefitLanguage.T("太晶坑奖励编辑器 - benefit", "Rewards - benefit (offline editor)");
        Size = new Size(940, 625);
        MinimumSize = new Size(780, 450);
        StartPosition = FormStartPosition.CenterParent;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        Controls.Add(panel);

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 7, 5, 0),
            ForeColor = Color.DarkRed, AutoEllipsis = true,
            Text = BenefitLanguage.T("本地奖励编辑：应用预览不会写入 Switch；单场主持将单独启动 PokéLDN WSL（不是任天堂官方互联网联机）。",
                "REWARD EDITOR — Apply saves a local preview; Host selected raid starts a separate PokéLDN WSL LDN host (not Nintendo online)."),
        });
        counter.Dock = DockStyle.Bottom;
        counter.Height = 28;
        counter.Padding = new Padding(10, 1, 0, 0);
        header.Controls.Add(counter);
        panel.Controls.Add(header, 0, 0);

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemId", HeaderText = BenefitLanguage.T("道具 ID", "Item ID"), FillWeight = 14 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = BenefitLanguage.T("道具名称", "Item"), ReadOnly = true, FillWeight = 37 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = BenefitLanguage.T("数量", "Quantity"), FillWeight = 19 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Rare", HeaderText = BenefitLanguage.T("稀有", "Rare"), FillWeight = 10 });
        var subject = new DataGridViewComboBoxColumn
        {
            Name = "Subject",
            HeaderText = BenefitLanguage.T("发放条件", "Subject (0-5)"),
            FillWeight = 27,
            DataSource = BenefitLanguage.RewardSubjects.ToArray(),
            DisplayMember = nameof(BenefitLanguage.Choice.Label),
            ValueMember = nameof(BenefitLanguage.Choice.Value),
            ToolTipText = BenefitLanguage.T(
                "0 全体玩家；1 房主；2 参与者；3 一次性；4 团体战料理；5 晶耀护符（道具 2481）",
                "0 Everyone; 1 Host; 2 Guest; 3 Once; 4 Raid Power; 5 Glimmering Charm (item 2481)"),
        };
        grid.Columns.Add(subject);
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0)
                grid.Rows[e.RowIndex].Cells[1].Value = ItemName(grid.Rows[e.RowIndex].Cells[0].Value?.ToString());
        };
        grid.RowsAdded += (_, _) => UpdateCounter();
        grid.RowsRemoved += (_, _) => UpdateCounter();
        panel.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true,
            Padding = new Padding(5, 6, 3, 3),
        };
        panel.Controls.Add(buttons, 0, 2);
        Button(buttons, BenefitLanguage.T("新增奖励", "Add row"), (_, _) => AddRow(new RaidRewardEntry(1, 1, 0, 0)));
        Button(buttons, BenefitLanguage.T("删除选中", "Remove"), (_, _) =>
        {
            if (grid.CurrentRow is not null) grid.Rows.Remove(grid.CurrentRow);
        });
        Button(buttons, BenefitLanguage.T("恢复默认", "Reset defaults"), (_, _) => Populate(defaults));
        Button(buttons, BenefitLanguage.T("选中设护符(5)", "Set selected Charm (5)"), (_, _) => SetSelectedSubject(5));
        Button(buttons, BenefitLanguage.T("全部设护符(5)", "All Charm (5)"), (_, _) => SetAllSubject(5));
        Button(buttons, BenefitLanguage.T("全部全员(0)", "All players (0)"), (_, _) => SetAllSubject(0));
        Button(buttons, BenefitLanguage.T("应用预览", "Apply preview"), (_, _) => Export(rows =>
        {
            AppliedPreview = rows.ToArray();
            DialogResult = DialogResult.OK;
            Close();
        }));
        Button(buttons, BenefitLanguage.T("查看原奖励", "View calculated"), (_, _) =>
        {
            if (this.calculated.Count == 0) return;
            using var window = new RewardsView(itemNames, moveNames, this.calculated);
            window.ShowDialog(this);
        });
        Button(buttons, BenefitLanguage.T("导入 JSON", "Load JSON"), (_, _) => LoadJson());
        Button(buttons, BenefitLanguage.T("导出 JSON", "Save JSON"), (_, _) => SaveJson());
        Button(buttons, BenefitLanguage.T("导入 BIN", "Import .bin"), (_, _) => ImportBinary());
        Button(buttons, BenefitLanguage.T("导出 BIN", "Export .bin"), (_, _) => ExportBinary());
        Button(buttons, BenefitLanguage.T("复制 PokéLDN 参数", "Copy PokéLDN"), (_, _) => CopyPokeLdn());
        Button(buttons, BenefitLanguage.T("主持选中坑", "Host selected raid"), (_, _) => HostSelectedRaid());
        Button(buttons, BenefitLanguage.T("关闭", "Close"), (_, _) => Close());
        Populate(existingPreview ?? defaults);
    }

    private static void Button(FlowLayoutPanel host, string text, EventHandler action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 29, Margin = new Padding(3) };
        button.Click += action;
        host.Controls.Add(button);
    }

    private string ItemName(string? raw)
    {
        if (!int.TryParse(raw, out var id) || id < 0 || id >= itemNames.Count)
            return BenefitLanguage.T("（未知道具，请检查 ID）", "(unknown - check item ID)");
        try
        {
            return Rewards.IsTM(id)
                ? Rewards.GetNameTM(id, itemNames, moveNames, Rewards.TMIndexes)
                : itemNames[id];
        }
        catch (ArgumentOutOfRangeException)
        {
            return itemNames[id];
        }
        catch (IndexOutOfRangeException)
        {
            return itemNames[id];
        }
    }

    private void SetSelectedSubject(int value)
    {
        grid.EndEdit();
        if (grid.CurrentRow is { } row)
            row.Cells["Subject"].Value = value;
    }

    private void SetAllSubject(int value)
    {
        grid.EndEdit();
        foreach (DataGridViewRow row in grid.Rows)
            row.Cells["Subject"].Value = value;
    }

    private void UpdateCounter()
    {
        counter.Text = BenefitLanguage.T(
            $"奖励记录：{grid.Rows.Count} / {RaidRewardEditor.MaxRows} 行；发放条件：0全体 / 1房主 / 2参与者 / 3一次性 / 4料理 / 5晶耀护符(2481)",
            $"RaidPoint: {grid.Rows.Count} / {RaidRewardEditor.MaxRows} rows. Subject: 0=all, 1=host, 2=guest, 3=first-eligible, 4=Raid Power, 5=Glimmering Charm (ID 2481).");
    }

    private void AddRow(RaidRewardEntry entry)
    {
        if (grid.Rows.Count >= RaidRewardEditor.MaxRows)
        {
            MessageBox.Show(this, BenefitLanguage.T("每场最多可设置 45 行奖励。", "Maximum of 45 RaidPoint reward rows."), BenefitLanguage.T("奖励行数上限", "Reward capacity"));
            return;
        }
        grid.Rows.Add(entry.ItemId, ItemName(entry.ItemId.ToString()), entry.Quantity, entry.Rare == 1, entry.Subject);
        UpdateCounter();
    }

    private void Populate(IReadOnlyList<RaidRewardEntry> rows)
    {
        RaidRewardEditor.Validate(rows);
        grid.Rows.Clear();
        foreach (var entry in rows) AddRow(entry);
        UpdateCounter();
    }

    private IReadOnlyList<RaidRewardEntry> ReadRows()
    {
        grid.EndEdit();
        var rows = new List<RaidRewardEntry>();
        for (int i = 0; i < grid.Rows.Count; i++)
        {
            var cells = grid.Rows[i].Cells;
            if (!int.TryParse(cells[0].Value?.ToString(), out var item) ||
                !int.TryParse(cells[2].Value?.ToString(), out var amount) ||
                !int.TryParse(cells[4].Value?.ToString(), out var subject))
                throw new ArgumentException(BenefitLanguage.T($"第 {i + 1} 行：道具 ID、数量和发放条件必须为数字。", $"Row {i + 1}: item ID, quantity and subject must be numeric."));
            rows.Add(new RaidRewardEntry(item, amount, cells[3].Value is true ? 1 : 0, subject));
        }
        RaidRewardEditor.Validate(rows);
        if (rows.Count == 0)
            throw new ArgumentException(BenefitLanguage.T("至少保留一行奖励才能替换。", "Add at least one reward for a custom replacement."));
        return rows;
    }

    private void Export(Action<IReadOnlyList<RaidRewardEntry>> writer)
    {
        try { writer(ReadRows()); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("奖励编辑器", "Reward editor"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveJson() => Export(rows =>
    {
        using var dialog = new SaveFileDialog { Filter = BenefitLanguage.T("奖励 JSON (*.json)|*.json", "JSON (*.json)|*.json"), FileName = "raid-rewards.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    });

    private void LoadJson()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var rows = JsonSerializer.Deserialize<List<RaidRewardEntry>>(File.ReadAllText(dialog.FileName))
                ?? throw new InvalidDataException(BenefitLanguage.T("JSON 中没有找到奖励行。", "No entries found."));
            Populate(rows);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or IOException)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("导入失败", "Import failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ImportBinary()
    {
        using var dialog = new OpenFileDialog { Filter = BenefitLanguage.T("奖励数据 720 字节 (*.bin)|*.bin", "720-byte reward rows (*.bin)|*.bin") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { Populate(RaidRewardEditor.Decode(File.ReadAllBytes(dialog.FileName))); }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void HostSelectedRaid()
    {
        if (selectedRaid is null)
        {
            MessageBox.Show(this, BenefitLanguage.T("没有找到已选中的太晶坑。", "No selected raid snapshot is available."), BenefitLanguage.T("无法主持", "Host unavailable"));
            return;
        }
        Export(rows =>
        {
            using var window = new PokeLdnHostForm(selectedRaid, rows);
            window.ShowDialog(this);
        });
    }

    private void CopyPokeLdn() => Export(rows =>
    {
        string args = RaidRewardEditor.BuildPokeLdnRewardArguments(rows);
        Clipboard.SetText(args);
        MessageBox.Show(this,
            BenefitLanguage.T("已复制 PokéLDN 主持奖励参数。不会修改 Switch 中的坑位或任天堂官方互联网房间。",
                "Copied PokéLDN --raid-reward flags for local hosting. This does not modify the connected Switch or official internet raids."),
            BenefitLanguage.T("参数已复制", "Host arguments copied"));
    });

    private void ExportBinary() => Export(rows =>
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "720-byte reward rows (*.bin)|*.bin", FileName = "raid-reward-rows.bin",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllBytes(dialog.FileName, RaidRewardEditor.Encode(rows));
        MessageBox.Show(this, BenefitLanguage.T("已导出 720 字节奖励行。该文件不是完整 RaidPoint、压缩网络包或 Switch 实时补丁。", "Exported 720 raw reward bytes ONLY. This is not a complete RaidPoint, compressed packet, or live Switch patch."), BenefitLanguage.T("导出完成", "Exported"));
    });
}
