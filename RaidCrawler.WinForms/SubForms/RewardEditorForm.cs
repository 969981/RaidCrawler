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
    private readonly DataGridView grid = new();
    private readonly Label counter = new();

    public RewardEditorForm(IReadOnlyList<string> items, IReadOnlyList<string> moves,
        IReadOnlyList<(int, int, int)> calculated, int species, int teraType)
    {
        itemNames = items;
        moveNames = moves;
        this.calculated = calculated;
        defaults = RaidRewardEditor.FromCalculated(
            calculated.Select(r => (r.Item1, r.Item2, r.Item3)), species, teraType);

        Text = "Rewards - benefit (offline editor)";
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
            Text = "LOCAL PREVIEW / EXPORT ONLY - editing this table will NOT patch your Switch, raid, or Nintendo online room.",
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
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemId", HeaderText = "Item ID", FillWeight = 14 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "Item", ReadOnly = true, FillWeight = 37 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "Quantity", FillWeight = 19 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Rare", HeaderText = "Rare", FillWeight = 10 });
        var subject = new DataGridViewComboBoxColumn { Name = "Subject", HeaderText = "Subject (0-5)", FillWeight = 20 };
        for (int value = 0; value <= 5; value++) subject.Items.Add(value);
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
        Button(buttons, "Add row", (_, _) => AddRow(new RaidRewardEntry(1, 1, 0, 0)));
        Button(buttons, "Remove", (_, _) =>
        {
            if (grid.CurrentRow is not null) grid.Rows.Remove(grid.CurrentRow);
        });
        Button(buttons, "Reset defaults", (_, _) => Populate(defaults));
        Button(buttons, "View calculated", (_, _) =>
        {
            if (this.calculated.Count == 0) return;
            using var window = new RewardsView(itemNames, moveNames, this.calculated);
            window.ShowDialog(this);
        });
        Button(buttons, "Load JSON", (_, _) => LoadJson());
        Button(buttons, "Save JSON", (_, _) => SaveJson());
        Button(buttons, "Import .bin", (_, _) => ImportBinary());
        Button(buttons, "Export .bin", (_, _) => ExportBinary());
        Button(buttons, "Close", (_, _) => Close());
        Populate(defaults);
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
            return "(unknown - check item ID)";
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

    private void UpdateCounter()
    {
        counter.Text = $"RaidPoint: {grid.Rows.Count} / {RaidRewardEditor.MaxRows} rows. " +
            "Subject: 0=all, 1=host, 2=guest, 3=once, 4=Raid Power, 5=Glimmering Charm.";
    }

    private void AddRow(RaidRewardEntry entry)
    {
        if (grid.Rows.Count >= RaidRewardEditor.MaxRows)
        {
            MessageBox.Show(this, "Maximum of 45 RaidPoint reward rows.", "Reward capacity");
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
                throw new ArgumentException($"Row {i + 1}: item ID, quantity and subject must be numeric.");
            rows.Add(new RaidRewardEntry(item, amount, cells[3].Value is true ? 1 : 0, subject));
        }
        RaidRewardEditor.Validate(rows);
        if (rows.Count == 0)
            throw new ArgumentException("Add at least one reward for a custom replacement.");
        return rows;
    }

    private void Export(Action<IReadOnlyList<RaidRewardEntry>> writer)
    {
        try { writer(ReadRows()); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Reward editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveJson() => Export(rows =>
    {
        using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "raid-rewards.json" };
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
                ?? throw new InvalidDataException("No entries found.");
            Populate(rows);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidDataException or IOException)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ImportBinary()
    {
        using var dialog = new OpenFileDialog { Filter = "720-byte reward rows (*.bin)|*.bin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { Populate(RaidRewardEditor.Decode(File.ReadAllBytes(dialog.FileName))); }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportBinary() => Export(rows =>
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "720-byte reward rows (*.bin)|*.bin", FileName = "raid-reward-rows.bin",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllBytes(dialog.FileName, RaidRewardEditor.Encode(rows));
        MessageBox.Show(this, "Exported 720 raw reward bytes ONLY. This is not a complete RaidPoint, compressed packet, or live Switch patch.", "Exported");
    });
}
