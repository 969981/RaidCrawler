using System.Text;
using System.Text.Json;
using PKHeX.Core;
using RaidCrawler.Core.Structures;
using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Local-only charm APPEND profile editor; the underlying legacy grid still displays 45 slots.
/// Only 20 subject-5 rows may be saved, with all normal game drops preserved.
/// The official Nintendo online-host injection point has NOT been verified.
/// </summary>
public sealed class RewardEditorForm : Form
{
    private readonly IReadOnlyList<string> names;
    private readonly IReadOnlyList<string> moves;
    private readonly IReadOnlyList<(int, int, int)> calculated;
    private readonly RaidRewardItemCatalog catalog;
    private readonly ISwitchConnectionAsync? consoleConnection;
    private readonly DataGridView grid = new();
    private readonly Label counter = new();
    private readonly RaidRewardEntry?[] defaults = RaidRewardPresets.ScreenshotCharm45Slots();
    private readonly ContextMenuStrip actions = new();
    private readonly CheckBox charmEnabled = new();
    public CharmBonusProfile? SavedCharmBonusProfile { get; private set; }
    private RewardItemPickerPopup? picker;
    private bool populating;

    public IReadOnlyList<RaidRewardEntry?>? AppliedSlots { get; private set; }

    public RewardEditorForm(IReadOnlyList<string> items, IReadOnlyList<string> moves,
        IReadOnlyList<(int, int, int)> calculated, int species, int teraType,
        IReadOnlyList<RaidRewardEntry?>? existingPreview = null,
        ISwitchConnectionAsync? consoleConnection = null)
    {
        names = items;
        this.moves = moves;
        this.calculated = calculated;
        this.consoleConnection = consoleConnection;
        catalog = RaidRewardItemCatalog.FromGameStrings(items, GameInfo.GetStrings("en").Item);

        Text = BenefitLanguage.T("太晶坑奖励编辑器 - benefit", "SV Raid Reward Editor - benefit");
        MinimumSize = new Size(760, 520);
        Size = new Size(990, 690);
        StartPosition = FormStartPosition.CenterParent;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        Controls.Add(outer);

        var top = new Panel { Dock = DockStyle.Fill };
        var notice = new Label
        {
            Dock = DockStyle.Top, Height = 32, AutoEllipsis = true,
            ForeColor = Color.DarkRed, Padding = new Padding(6, 3, 0, 0),
            Text = BenefitLanguage.T(
                "晶耀护符追加模式：原版固定／随机奖励全部保留，仅配置最多20条额外奖励。尚未实现游戏内追加。",
                "Charm append mode: all original drops are preserved, up to 20 EXTRA rows. Live injection not implemented."),
        };
        counter.Dock = DockStyle.Bottom;
        counter.Height = 24;
        counter.Padding = new Padding(6, 3, 0, 0);
        top.Controls.AddRange([notice, counter]);
        outer.Controls.Add(top, 0, 0);

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Slot", HeaderText = BenefitLanguage.T("槽位", "Slot"), FillWeight = 11, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Item", HeaderText = BenefitLanguage.T("奖励道具（点击搜索） ▼", "Item (click to search) ▼"),
            FillWeight = 62, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Quantity", HeaderText = BenefitLanguage.T("数量", "Quantity"), FillWeight = 18,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        var subject = new DataGridViewComboBoxColumn
        {
            Name = "Subject", HeaderText = BenefitLanguage.T("发放条件", "Subject"),
            FillWeight = 35, DataSource = BenefitLanguage.RewardSubjects.ToArray(),
            ValueMember = nameof(BenefitLanguage.Choice.Value),
            DisplayMember = nameof(BenefitLanguage.Choice.Label),
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        grid.Columns.Add(subject);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Rare", HeaderText = "Rare", Visible = false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemId", Visible = false });

        grid.CellClick += (_, e) =>
        {
            if (!populating && e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["Item"]!.Index)
                ShowPicker(e.RowIndex);
        };
        grid.CellDoubleClick += (_, e) =>
        {
            if (!populating && e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["Item"]!.Index)
                ShowPicker(e.RowIndex);
        };
        grid.KeyDown += OnGridKeyDown;
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        };
        grid.CellEndEdit += (_, _) => UpdateCounter();
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            if (!populating)
                MessageBox.Show(this, BenefitLanguage.T("无效的发放条件，请选择 0～5。", "Invalid subject. Choose 0–5."));
        };
        grid.ContextMenuStrip = actions;
        AddContextMenu();
        outer.Controls.Add(grid, 0, 1);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(5, 5, 0, 0), AutoScroll = true,
        };
        outer.Controls.Add(controls, 0, 2);
        charmEnabled.AutoSize = true;
        charmEnabled.Text = BenefitLanguage.T("启用配置（本机保存，不会写入游戏）", "Enable saved profile (not in-game)");
        charmEnabled.Margin = new Padding(5, 10, 4, 0);
        controls.Controls.Add(charmEnabled);
        AddButton(controls, BenefitLanguage.T("保存追加配置／诊断", "Save Append / Diagnostics"), (_, _) => ExplainWriteStatus());
        AddButton(controls, BenefitLanguage.T("导入 JSON", "Import JSON"), (_, _) => ImportJson());
        AddButton(controls, BenefitLanguage.T("导出 JSON", "Export JSON"), (_, _) => ExportJson());
        AddButton(controls, BenefitLanguage.T("查看", "Preview"), (_, _) => Preview());

        var current = CharmBonusProfileStore.Load(CharmBonusProfileStore.DefaultPath);
        charmEnabled.Checked = current.Enabled;
        var initial = new RaidRewardEntry?[45];
        for (int i = 0; i < current.Items.Count; i++)
            initial[i] = new RaidRewardEntry(current.Items[i].ItemId, current.Items[i].Quantity, 0, 5);
        LoadSlots(initial);
        FormClosing += SaveOnClose;
    }

    private static void AddButton(FlowLayoutPanel target, string caption, EventHandler click)
    {
        var button = new Button { Text = caption, AutoSize = true, MinimumSize = new Size(106, 32), Margin = new Padding(4) };
        button.Click += click;
        target.Controls.Add(button);
    }

    private void AddContextMenu()
    {
        actions.Items.Add(BenefitLanguage.T("选择道具／搜索…", "Choose item / search..."), null,
            (_, _) => { if (grid.CurrentCell is not null) ShowPicker(grid.CurrentCell.RowIndex); });
        actions.Items.Add(BenefitLanguage.T("清空当前槽位（Delete）", "Clear current slot (Delete)"), null,
            (_, _) => ClearSelected());
        actions.Items.Add(BenefitLanguage.T("恢复默认20行", "Restore 20 default rows"), null, (_, _) => LoadSlots(defaults));
        actions.Items.Add(BenefitLanguage.T("清空所有45个槽位", "Clear all 45 slots"), null,
            (_, _) => { if (MessageBox.Show(this, BenefitLanguage.T("确定清空全部槽位？", "Clear all slots?"),
                BenefitLanguage.T("确认", "Confirm"), MessageBoxButtons.YesNo) == DialogResult.Yes)
                LoadSlots(new RaidRewardEntry?[45]); });
        actions.Items.Add(new ToolStripSeparator());
        actions.Items.Add(BenefitLanguage.T("当前行 Subject = 5（晶耀护符）", "Selected row Subject 5"), null,
            (_, _) => SetSelectedSubject(5));
        actions.Items.Add(BenefitLanguage.T("所有非空行 Subject = 5", "All filled rows Subject 5"), null,
            (_, _) => SetAllSubject(5));
        actions.Items.Add(BenefitLanguage.T("所有非空行 Subject = 0", "All filled rows Subject 0"), null,
            (_, _) => SetAllSubject(0));
        actions.Items.Add(BenefitLanguage.T("切换当前行 Rare 标记", "Toggle selected row rare flag"), null,
            (_, _) =>
            {
                if (grid.CurrentCell is null) return;
                var row = grid.Rows[grid.CurrentCell.RowIndex];
                if (Empty(row)) return;
                row.Cells["Rare"].Value = row.Cells["Rare"].Value is not true;
            });
        actions.Items.Add(new ToolStripSeparator());
        actions.Items.Add(BenefitLanguage.T("导入720字节 BIN", "Import 720-byte BIN"), null,
            (_, _) => ImportBin());
        actions.Items.Add(BenefitLanguage.T("导出720字节 BIN", "Export 720-byte BIN"), null,
            (_, _) => ExportBin());
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (grid.CurrentCell is null) return;
        if (e.KeyCode is Keys.Delete or Keys.Back && !grid.IsCurrentCellInEditMode)
        {
            ClearSelected();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode is Keys.F2 or Keys.Enter &&
                 grid.CurrentCell.OwningColumn.Name == "Item")
        {
            ShowPicker(grid.CurrentCell.RowIndex);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void ShowPicker(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
        picker?.Close();
        picker?.Dispose();
        picker = new RewardItemPickerPopup(catalog, option =>
        {
            var row = grid.Rows[rowIndex];
            row.Cells["ItemId"].Value = option.Id;
            row.Cells["Item"].Value = option.Name + $"  ·  #{option.Id}";
            if (row.Cells["Quantity"].Value is null) row.Cells["Quantity"].Value = 999;
            if (row.Cells["Subject"].Value is null) row.Cells["Subject"].Value = 5;
            if (row.Cells["Rare"].Value is null) row.Cells["Rare"].Value = false;
            UpdateCounter();
        });
        picker.ShowForCell(grid, rowIndex, grid.Columns["Item"]!.Index);
    }

    private static bool Empty(DataGridViewRow row) => row.Cells["ItemId"].Value is null;

    private void ClearSelected()
    {
        if (grid.CurrentCell is null) return;
        SetRow(grid.CurrentCell.RowIndex, null);
        UpdateCounter();
    }

    private void SetSelectedSubject(int subject)
    {
        grid.EndEdit();
        if (grid.CurrentCell is null) return;
        var row = grid.Rows[grid.CurrentCell.RowIndex];
        if (!Empty(row)) row.Cells["Subject"].Value = subject;
    }

    private void SetAllSubject(int subject)
    {
        grid.EndEdit();
        foreach (DataGridViewRow row in grid.Rows)
            if (!Empty(row)) row.Cells["Subject"].Value = subject;
    }

    private void SetRow(int index, RaidRewardEntry? entry)
    {
        var cells = grid.Rows[index].Cells;
        cells["Slot"].Value = $"{index+1:00}";
        cells["ItemId"].Value = entry?.ItemId;
        cells["Item"].Value = entry is null ? BenefitLanguage.T("选择奖励道具…", "Choose item…")
            : NameOf(entry.ItemId) + $"  ·  #{entry.ItemId}";
        cells["Quantity"].Value = entry?.Quantity;
        cells["Subject"].Value = entry?.Subject;
        cells["Rare"].Value = entry?.Rare == 1;
    }

    private string NameOf(int id)
    {
        if (catalog.TryFind(id, out var found)) return found!.Name;
        if (id >= 0 && id < names.Count) return names[id];
        return BenefitLanguage.T("未知道具", "Unknown item");
    }

    private void LoadSlots(IReadOnlyList<RaidRewardEntry?> source)
    {
        if (source.Count > 45) throw new ArgumentException("Maximum 45 slots");
        var slots = new RaidRewardEntry?[45];
        for (int i=0; i<source.Count; i++) slots[i] = source[i];
        RaidRewardEditor.EncodeSlots(slots);
        populating = true;
        try
        {
            grid.Rows.Clear();
            grid.Rows.Add(45);
            for (int i=0; i<45; i++) SetRow(i, slots[i]);
        }
        finally { populating = false; }
        UpdateCounter();
    }

    private IReadOnlyList<RaidRewardEntry?> ReadSlots(bool allowEmpty = true)
    {
        grid.EndEdit();
        var values = new RaidRewardEntry?[45];
        for (int i=0; i<45; i++)
        {
            var c = grid.Rows[i].Cells;
            if (c["ItemId"].Value is null) continue;
            if (!int.TryParse(c["ItemId"].Value?.ToString(), out int id) ||
                !int.TryParse(c["Quantity"].Value?.ToString(), out int amount) ||
                !int.TryParse(c["Subject"].Value?.ToString(), out int subject))
                throw new ArgumentException(BenefitLanguage.T($"第 {i+1} 槽的道具、数量或 Subject 不正确。", $"Invalid item, quantity or subject in slot {i+1}."));
            if (!catalog.TryFind(id, out _))
                throw new ArgumentException(BenefitLanguage.T($"第 {i+1} 槽的道具 ID {id} 不在朱／紫可用道具目录中。", $"Invalid SV item #{id} in slot {i+1}."));
            values[i] = new RaidRewardEntry(id, amount, c["Rare"].Value is true ? 1 : 0, subject);
        }
        RaidRewardEditor.EncodeSlots(values);
        if (!allowEmpty && values.All(r => r is null))
            throw new ArgumentException(BenefitLanguage.T("至少配置一行奖励。", "At least one reward is needed."));
        return values;
    }

    private void UpdateCounter()
    {
        if (populating) return;
        int count = grid.Rows.Cast<DataGridViewRow>().Count(r => !Empty(r));
        counter.Text = BenefitLanguage.T(
            $"晶耀护符追加配置 {count}/20 条；原版奖励仍由游戏生成。后25个旧格式槽不得写入追加档案。",
            $"Charm append {count}/20; game generates original drops. Last 25 legacy slots cannot be saved as bonuses.");
    }

    private CharmBonusProfile ReadCharmProfile(bool allowEmpty = true)
    {
        var slots = ReadSlots(allowEmpty);
        var rows = slots.Where(x => x is not null).Select(x => x!).ToArray();
        if (rows.Length > CharmBonusProfile.MaxItems)
            throw new ArgumentException("晶耀护符自定义奖励最多20条；不能覆盖原版奖励或静默截断。");
        if (rows.Any(x => x.Subject != 5 || x.Rare != 0))
            throw new ArgumentException("追加配置仅允许 Subject=5 且 Rare=0；固定／随机奖励仍由原版游戏生成。");
        return CharmBonusProfile.Create(charmEnabled.Checked,
            rows.Select(x => new CharmBonusItem(x.ItemId, x.Quantity)));
    }

    private static CharmBonusProfile SaveIfChanged(CharmBonusProfile pending)
    {
        string path = CharmBonusProfileStore.DefaultPath;
        if (File.Exists(path))
        {
            var current = CharmBonusProfileStore.Load(path);
            if (current.Enabled == pending.Enabled &&
                current.Items.SequenceEqual(pending.Items))
                return current;
        }
        return CharmBonusProfileStore.Save(path, pending);
    }

    private void SaveOnClose(object? sender, FormClosingEventArgs e)
    {
        picker?.Close();
        try
        {
            var pending = ReadCharmProfile();
            SavedCharmBonusProfile = SaveIfChanged(pending);
            AppliedSlots = null; // never ask MainWindow to save old 45-slot global profile
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var result = MessageBox.Show(this,
                BenefitLanguage.Error(ex.Message) + Environment.NewLine +
                BenefitLanguage.T("选择“是”继续编辑，“否”放弃本次修改。",
                    "Yes to continue editing; No to discard changes."),
                BenefitLanguage.T("追加配置检查", "Append profile validation"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes) e.Cancel = true;
        }
    }

    private void ExplainWriteStatus()
    {
        CharmBonusProfile pending;
        try { pending = ReadCharmProfile(allowEmpty:false); }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message),
                BenefitLanguage.T("追加配置检查", "Append profile validation"));
            return;
        }
        try
        {
            SavedCharmBonusProfile = SaveIfChanged(pending);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message,
                BenefitLanguage.T("保存追加配置失败", "Append profile save failed"));
            return;
        }
        // Diagnostic form accepts the legacy 45-slot row layout only as an
        // import/preview adapter. No append Hook exists or is installed here.
        var previewSlots = new RaidRewardEntry?[45];
        for (int i = 0; i < pending.Items.Count; i++)
            previewSlots[i] = new RaidRewardEntry(
                pending.Items[i].ItemId, pending.Items[i].Quantity, 0, 5);
        using var diagnostic = new RaidRewardDiagnosticForm(consoleConnection, previewSlots);
        diagnostic.ShowDialog(this);
    }

    private void ImportJson()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var data = JsonSerializer.Deserialize<List<RaidRewardEntry?>>(File.ReadAllText(dialog.FileName))
                ?? throw new ArgumentException("Empty JSON file");
            LoadSlots(data);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or IOException)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("导入失败", "Import failed"));
        }
    }

    private void ExportJson()
    {
        try
        {
            var slots = ReadSlots();
            using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "raid-rewards-45.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(slots, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("导出失败", "Export failed"));
        }
    }

    private void ImportBin()
    {
        using var dlg = new OpenFileDialog { Filter = "BIN (*.bin)|*.bin" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { LoadSlots(RaidRewardEditor.DecodeSlots(File.ReadAllBytes(dlg.FileName))); }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("导入失败", "Import failed"));
        }
    }

    private void ExportBin()
    {
        try
        {
            var slots = ReadSlots();
            using var dlg = new SaveFileDialog { Filter = "BIN (*.bin)|*.bin", FileName = "raid-rewards-720.bin" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dlg.FileName, RaidRewardEditor.EncodeSlots(slots));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("导出失败", "Export failed"));
        }
    }

    private void Preview()
    {
        try
        {
            var slots = ReadSlots();
            using var dialog = new Form
            {
                Text = BenefitLanguage.T("奖励查看（当前配置／游戏预测）", "Reward Preview"),
                Size = new Size(760, 610), MinimumSize = new Size(580, 400),
                StartPosition = FormStartPosition.CenterParent,
            };
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var current = new TabPage(BenefitLanguage.T("当前配置", "Current configuration"));
            var original = new TabPage(BenefitLanguage.T("游戏原始计算奖励", "Calculated game drops"));
            var configText = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
            var originalText = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
            var builder = new StringBuilder();
            for (int i=0; i<slots.Count; i++)
            {
                var r = slots[i];
                builder.AppendLine(r is null ? $"{i+1:00}  {BenefitLanguage.T("（空槽）", "(empty)")}"
                    : $"{i+1:00}  {NameOf(r.ItemId)} [#{r.ItemId}] ×{r.Quantity}  Subject {r.Subject}  Rare {r.Rare}");
            }
            configText.Text = builder.ToString();
            builder.Clear();
            foreach (var (item, count, subject) in calculated)
                builder.AppendLine($"{NameOf(item)} [#{item}] ×{count}  Subject {subject}");
            originalText.Text = builder.Length > 0 ? builder.ToString()
                : BenefitLanguage.T("该坑没有原始奖励数据。", "No predicted rewards.");
            current.Controls.Add(configText);
            original.Controls.Add(originalText);
            tabs.TabPages.Add(current);
            tabs.TabPages.Add(original);
            dialog.Controls.Add(tabs);
            dialog.ShowDialog(this);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, BenefitLanguage.Error(ex.Message), BenefitLanguage.T("配置检查", "Validation"));
        }
    }
}
