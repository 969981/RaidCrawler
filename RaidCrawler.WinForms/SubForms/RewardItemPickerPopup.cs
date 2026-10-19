using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>Compact WinForms item search popup anchored to an editor cell.</summary>
internal sealed class RewardItemPickerPopup : ToolStripDropDown
{
    private readonly RaidRewardItemCatalog catalog;
    private readonly Action<RaidRewardItemOption> choose;
    private readonly TextBox filter = new();
    private readonly ListBox results = new();
    private readonly Label hint = new();

    public RewardItemPickerPopup(RaidRewardItemCatalog catalog, Action<RaidRewardItemOption> choose)
    {
        this.catalog = catalog;
        this.choose = choose;
        AutoClose = true;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        var content = new Panel { Size = new Size(475, 345), BackColor = SystemColors.Window };
        filter.SetBounds(8, 8, 458, 29);
        filter.PlaceholderText = BenefitLanguage.T("搜索中文、英文、ID；支持模糊匹配", "Search Chinese / English / item ID");
        results.SetBounds(8, 42, 458, 268);
        results.IntegralHeight = false;
        results.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10);
        hint.SetBounds(9, 317, 450, 20);
        hint.ForeColor = SystemColors.GrayText;
        content.Controls.AddRange([filter, results, hint]);
        Items.Add(new ToolStripControlHost(content) {
            Size = content.Size, Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false
        });
        filter.TextChanged += (_, _) => RefreshList();
        filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { SelectCurrent(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Down && results.Items.Count > 0)
            {
                results.Focus();
                results.SelectedIndex = 0;
                e.SuppressKeyPress = true;
            }
        };
        results.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { SelectCurrent(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { Close(); e.SuppressKeyPress = true; }
        };
        results.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            int index = results.IndexFromPoint(e.Location);
            if (index < 0 || index >= results.Items.Count) return;
            results.SelectedIndex = index;
            SelectCurrent();
        };
        RefreshList();
    }

    private void RefreshList()
    {
        var matches = catalog.Search(filter.Text, 100);
        results.BeginUpdate();
        try
        {
            results.Items.Clear();
            foreach (var option in matches)
                results.Items.Add(option);
            if (results.Items.Count > 0) results.SelectedIndex = 0;
        }
        finally { results.EndUpdate(); }
        hint.Text = BenefitLanguage.T(
            $"找到 {matches.Count} 项（最多显示 100）｜回车确认，Esc 取消",
            $"{matches.Count} results (max 100) | Enter to select, Esc to close");
    }

    private void SelectCurrent()
    {
        if (results.SelectedItem is not RaidRewardItemOption item) return;
        choose(item);
        Close();
    }

    public void ShowForCell(DataGridView table, int row, int column)
    {
        var rect = table.GetCellDisplayRectangle(column, row, false);
        var anchor = new Point(Math.Max(0, rect.Left), Math.Max(0, rect.Bottom));
        Show(table, anchor);
        filter.Focus();
        filter.SelectAll();
    }
}
