using RaidCrawler.Core.Host;
using System.Globalization;
using System.Text;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// User-editable default reward profile (25 entries, 28 max). This window does
/// not write Switch memory, change the Native mailbox, or promise x999 awards.
/// </summary>
public sealed class HostRewardPresetWindow : Form
{
    public static readonly string PresetPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidCrawler", "host-reward-preset.json");

    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _add = new();
    private readonly Button _remove = new();
    private readonly Button _up = new();
    private readonly Button _down = new();
    private readonly Button _defaults = new();
    private readonly Button _save = new();

    public HostRewardPresetWindow()
    {
        Text = "房主额外奖励：默认25种 ×999（最多自定义28种）";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(660, 550);
        ClientSize = new Size(740, 740);

        var notice = new Label
        {
            Location = new Point(14, 12), Size = new Size(710, 55),
            Text = "默认25种道具，每种 ×999，领取条件 Subject=5（晶耀护符）。" +
                   "这里仅保存并离线校验预设：当前 Native N2 实机写入仍仅支持单行先机球 ×1–10。" +
                   "原版奖励和预设合计不能超过45行，×999实际到账未验证。"
        };

        _grid.Location = new Point(14, 72);
        _grid.Size = new Size(710, 530);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemName", HeaderText = "道具名称", FillWeight = 48
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ItemId", HeaderText = "道具 ID", FillWeight = 26
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Quantity", HeaderText = "数量 (1–999)", FillWeight = 26
        });
        _grid.RowsAdded += (_, _) => RefreshStatus();
        _grid.RowsRemoved += (_, _) => RefreshStatus();
        _grid.CellValueChanged += (_, _) => RefreshStatus();

        _status.Location = new Point(14, 612);
        _status.Size = new Size(710, 25);
        _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        AddButton(_add, "增加道具", 14, (_, _) =>
        {
            _grid.EndEdit();
            if (_grid.Rows.Count >= HostRewardPreset.MaxItems) return;
            int index = _grid.Rows.Add("自定义道具", "", HostRewardPreset.DefaultQuantity);
            _grid.CurrentCell = _grid.Rows[index].Cells[1];
            _grid.BeginEdit(true);
            RefreshStatus();
        });
        AddButton(_remove, "删除选中", 130, (_, _) =>
        {
            if (_grid.CurrentRow is null || _grid.Rows.Count <= 1) return;
            _grid.Rows.RemoveAt(_grid.CurrentRow.Index);
            RefreshStatus();
        });
        AddButton(_up, "上移", 246, (_, _) => MoveRow(-1));
        AddButton(_down, "下移", 362, (_, _) => MoveRow(1));
        AddButton(_defaults, "恢复默认25种", 478, (_, _) =>
        {
            if (MessageBox.Show(this, "用25种默认道具替换当前表格？保存前不会覆盖配置文件。",
                    "恢复默认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Populate(HostRewardPreset.CreateDefault());
        });

        _save.Text = "保存默认奖励";
        _save.Location = new Point(14, 686);
        _save.Size = new Size(215, 36);
        _save.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _save.Click += (_, _) => Save();

        var close = new Button
        {
            Text = "关闭", Location = new Point(242, 686), Size = new Size(130, 36),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        close.Click += (_, _) => Close();

        Controls.AddRange([notice, _grid, _status, _add, _remove, _up,
                           _down, _defaults, _save, close]);
        try
        {
            var profile = File.Exists(PresetPath)
                ? HostRewardPreset.FromJson(File.ReadAllText(PresetPath, Encoding.UTF8))
                : HostRewardPreset.CreateDefault();
            Populate(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Populate(HostRewardPreset.CreateDefault());
            MessageBox.Show(this, "配置读取失败，已加载未保存的默认25种：\n" + ex.Message,
                "读取预设", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddButton(Button button, string label, int x, EventHandler click)
    {
        button.Text = label;
        button.Location = new Point(x, 646);
        button.Size = new Size(110, 31);
        button.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        button.Click += click;
    }

    private void Populate(HostRewardPresetProfile profile)
    {
        _grid.Rows.Clear();
        foreach (var item in profile.Items)
            _grid.Rows.Add(item.Name, item.ItemId, item.Quantity);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_grid.IsDisposed || _status.IsDisposed) return;
        int count = _grid.Rows.Count;
        _status.Text = $"配置 {count}/{HostRewardPreset.MaxItems} 种，申请 {count} 个奖励槽位。" +
            $" 需原版有效奖励 ≤ {45 - count} 条；超限整单拒绝，不删原版奖励。";
        _add.Enabled = count < HostRewardPreset.MaxItems;
        _remove.Enabled = count > 1;
    }

    private void MoveRow(int direction)
    {
        _grid.EndEdit();
        int at = _grid.CurrentRow?.Index ?? -1;
        int target = at + direction;
        if (at < 0 || target < 0 || target >= _grid.Rows.Count) return;
        var values = _grid.Rows[at].Cells.Cast<DataGridViewCell>()
            .Select(cell => cell.Value).ToArray();
        _grid.Rows.RemoveAt(at);
        int index = _grid.Rows.Insert(target, values);
        _grid.CurrentCell = _grid.Rows[target].Cells[0];
    }

    private void Save()
    {
        try
        {
            _grid.EndEdit();
            var items = new List<HostRewardPresetItem>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                string name = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                string idText = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                string qtyText = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                if (!uint.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) ||
                    !uint.TryParse(qtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint qty))
                    throw new InvalidDataException($"第 {row.Index + 1} 行的道具 ID 或数量不是有效整数。");
                items.Add(new HostRewardPresetItem(name, id, qty));
            }
            var profile = new HostRewardPresetProfile(
                HostRewardPreset.Schema, HostRewardPreset.Subject, items.ToArray());
            string json = HostRewardPreset.ToJson(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(PresetPath)!);
            string temp = PresetPath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, PresetPath, true);
            MessageBox.Show(this, $"保存成功：{items.Count} 种道具，最多28种。\n" +
                "仅保存 PC 默认配置，没有向 Switch 下发；当前 Native N2 不支持一次追加该列表。",
                "预设已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "未保存：" + ex.Message, "配置校验失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
