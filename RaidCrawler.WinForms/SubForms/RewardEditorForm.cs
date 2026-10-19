using System.Text.Json;
using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>Local reward row editing/exports. Does NOT change live Switch memory.</summary>
public sealed class RewardEditorForm : Form
{
    private readonly IReadOnlyList<string> itemNames;
    private readonly IReadOnlyList<string> moveNames;
    private readonly IReadOnlyList<(int, int, int)> calculated;
    private readonly IReadOnlyList<RaidRewardEntry?> defaults;
    private readonly PokeLdnRaidSelection? selectedRaid;
    private readonly DataGridView grid = new();
    private readonly Label counter = new();

    public IReadOnlyList<RaidRewardEntry?>? AppliedSlots { get; private set; }

    public RewardEditorForm(IReadOnlyList<string> items, IReadOnlyList<string> moves,
        IReadOnlyList<(int, int, int)> calculated, int species, int teraType,
        IReadOnlyList<RaidRewardEntry?>? existingPreview = null,
        PokeLdnRaidSelection? selectedRaid = null)
    {
        itemNames = items;
        moveNames = moves;
        this.calculated = calculated;
        this.selectedRaid = selectedRaid;
        // This editor defaults to the requested 20-item Charm-999 template; seed drops are view-only.
        defaults = RaidRewardPresets.ScreenshotCharm45Slots();

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
            Text = BenefitLanguage.T("默认前20行晶耀护符999，剩余25行空白。本界面准备奖励，尚未实现游戏原生联网写入；不必启动 PokéLDN 主持。",
                "Default: 20 charm-only ×999 rows, 25 empty slots. Game-native online injection is NOT implemented."),
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
            {
                var cells = grid.Rows[e.RowIndex].Cells;
                cells[1].Value = ItemName(cells[0].Value?.ToString());
                if (!string.IsNullOrWhiteSpace(cells[0].Value?.ToString()))
                {
                    cells[2].Value ??= 999;
                    cells[4].Value ??= 5;
                }
            }
            UpdateCounter();
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
        Button(buttons, BenefitLanguage.T("新增奖励", "Add row"), (_, _) => AddRow(new RaidRewardEntry(1, 999, 0, 5)));
        Button(buttons, BenefitLanguage.T("清空选中槽", "Clear selected"), (_, _) =>
        {
            if (grid.CurrentRow is { } row)
            {
                row.Cells["ItemId"].Value = null;
                row.Cells["ItemName"].Value = null;
                row.Cells["Quantity"].Value = null;
                row.Cells["Rare"].Value = false;
                row.Cells["Subject"].Value = null;
                UpdateCounter();
            }
        });
        Button(buttons, BenefitLanguage.T("恢复20行默认", "Reset 20 defaults"), (_, _) => PopulateSlots(defaults));
        Button(buttons, BenefitLanguage.T("选中设护符(5)", "Set selected Charm (5)"), (_, _) => SetSelectedSubject(5));
        Button(buttons, BenefitLanguage.T("全部设护符(5)", "All Charm (5)"), (_, _) => SetAllSubject(5));
        Button(buttons, BenefitLanguage.T("全部全员(0)", "All players (0)"), (_, _) => SetAllSubject(0));
        Button(buttons, BenefitLanguage.T("应用预览", "Apply preview"), (_, _) => Export(rows =>
        {
            AppliedSlots = ReadSlots();
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
        Button(buttons, BenefitLanguage.T("原生联网写入检查", "Online write preflight"), (_, _) => OnlinePreflight());
        Button(buttons, BenefitLanguage.T("PokéLDN本地主持(可选)", "PokéLDN LAN host (optional)"), (_, _) => HostSelectedRaid());
        Button(buttons, BenefitLanguage.T("关闭", "Close"), (_, _) => Close());
        PopulateSlots(existingPreview ?? defaults);
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
            if (!IsEmpty(row)) row.Cells["Subject"].Value = value;
    }

    private static bool IsEmpty(DataGridViewRow row) =>
        string.IsNullOrWhiteSpace(row.Cells["ItemId"].Value?.ToString());

    private void UpdateCounter()
    {
        int used = grid.Rows.Cast<DataGridViewRow>().Count(row => !IsEmpty(row));
        counter.Text = BenefitLanguage.T(
            $"已配置 {used}/45 行，空位 {45-used}；Subject 0全员 / 1房主 / 2参与者 / 3一次性 / 4料理 / 5晶耀护符(2481)",
            $"Configured {used}/45, empty {45-used}; Subject 0 all, 1 host, 2 guest, 3 once, 4 raid power, 5 charm.");
    }

    private void SetSlot(int index, RaidRewardEntry? entry)
    {
        if (index < 0 || index >= RaidRewardEditor.MaxRows)
            throw new ArgumentOutOfRangeException(nameof(index));
        var cells = grid.Rows[index].Cells;
        cells["ItemId"].Value = entry?.ItemId;
        cells["ItemName"].Value = entry is null ? null : ItemName(entry.ItemId.ToString());
        cells["Quantity"].Value = entry?.Quantity;
        cells["Rare"].Value = entry?.Rare == 1;
        cells["Subject"].Value = entry?.Subject;
    }

    private void AddRow(RaidRewardEntry entry)
    {
        for (int i = 0; i < grid.Rows.Count; i++)
        {
            if (!IsEmpty(grid.Rows[i])) continue;
            SetSlot(i, entry);
            grid.CurrentCell = grid.Rows[i].Cells["ItemId"];
            UpdateCounter();
            return;
        }
        MessageBox.Show(this, BenefitLanguage.T("最多 45 行奖励。", "Maximum of 45 reward rows."),
            BenefitLanguage.T("奖励行数上限", "Reward capacity"));
    }

    private void PopulateSlots(IReadOnlyList<RaidRewardEntry?> rows)
    {
        if (rows.Count > RaidRewardEditor.MaxRows)
            throw new ArgumentException("Only 45 reward slots are supported.");
        var full = new RaidRewardEntry?[RaidRewardEditor.MaxRows];
        for (int i = 0; i < rows.Count; i++) full[i] = rows[i];
        RaidRewardEditor.EncodeSlots(full); // enforce all six subjects/row bounds
        grid.Rows.Clear();
        for (int i = 0; i < RaidRewardEditor.MaxRows; i++) grid.Rows.Add();
        for (int i = 0; i < RaidRewardEditor.MaxRows; i++) SetSlot(i, full[i]);
        UpdateCounter();
    }

    private IReadOnlyList<RaidRewardEntry?> ReadSlots()
    {
        grid.EndEdit();
        var slots = new RaidRewardEntry?[RaidRewardEditor.MaxRows];
        for (int i = 0; i < grid.Rows.Count; i++)
        {
            var cells = grid.Rows[i].Cells;
            string? itemText = cells["ItemId"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(itemText))
            {
                if (!string.IsNullOrWhiteSpace(cells["Quantity"].Value?.ToString()) ||
                    cells["Subject"].Value is not null)
                    throw new ArgumentException($"第 {i+1} 个空槽含有其他字段，请填入道具 ID 或清空整行。");
                continue;
            }
            if (!int.TryParse(itemText, out int item) ||
                !int.TryParse(cells["Quantity"].Value?.ToString(), out int quantity) ||
                !int.TryParse(cells["Subject"].Value?.ToString(), out int subject))
                throw new ArgumentException($"第 {i+1} 行：道具 ID、数量及发放条件必须有效。");
            slots[i] = new RaidRewardEntry(item, quantity, cells["Rare"].Value is true ? 1 : 0, subject);
        }
        RaidRewardEditor.EncodeSlots(slots);
        if (slots.All(row => row is null))
            throw new ArgumentException("至少需要 1 行奖励。");
        return slots;
    }

    private IReadOnlyList<RaidRewardEntry> ReadRows() =>
        ReadSlots().OfType<RaidRewardEntry>().ToArray();

    private void OnlinePreflight()
    {
        string info = BenefitLanguage.T(
            "当前未发现已经过官方互联网联机场景验证的原生奖励 Hook。\\n" +
            "已确认：20+25 槽编辑、Subject 0–5、720 字节离线数据，以及《紫》4.0.0 的序列化和接收 RVA。\\n" +
            "尚缺：运行时主机角色确认、发送前稳定拦截、内存对象生命周期验证、4.0.0 以外版本适配和实机对照。\\n" +
            "因此当前不会对 Switch 写入，也不会显示虚假的『写入成功』。你可以导出 BIN 并阅读研究文档。",
            "Native online raid reward injection is unverified. This version only prepares and exports 45 reward slots; it does not write to your console.");
        MessageBox.Show(this, info, BenefitLanguage.T("官方联网写入预检（未就绪）", "Native online write preflight"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        using var dialog = new SaveFileDialog { Filter = BenefitLanguage.T("奖励 JSON (*.json)|*.json", "JSON (*.json)|*.json"), FileName = "raid-rewards-45.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(ReadSlots(), new JsonSerializerOptions { WriteIndented = true }));
    });

    private void LoadJson()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var rows = JsonSerializer.Deserialize<List<RaidRewardEntry?>>(File.ReadAllText(dialog.FileName))
                ?? throw new InvalidDataException(BenefitLanguage.T("JSON 中没有找到奖励行。", "No entries found."));
            PopulateSlots(rows);
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
        try { PopulateSlots(RaidRewardEditor.DecodeSlots(File.ReadAllBytes(dialog.FileName))); }
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
        File.WriteAllBytes(dialog.FileName, RaidRewardEditor.EncodeSlots(ReadSlots()));
        MessageBox.Show(this, BenefitLanguage.T("已导出 720 字节奖励行。该文件不是完整 RaidPoint、压缩网络包或 Switch 实时补丁。", "Exported 720 raw reward bytes ONLY. This is not a complete RaidPoint, compressed packet, or live Switch patch."), BenefitLanguage.T("导出完成", "Exported"));
    });
}
