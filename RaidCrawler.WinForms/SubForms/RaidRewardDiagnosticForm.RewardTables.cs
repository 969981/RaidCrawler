using RaidCrawler.Core.Structures;

namespace RaidCrawler.WinForms.SubForms;

/// <summary>
/// Browse base reward resource definitions and the CURRENT EDITOR TEMPLATE.
/// This does not inspect the current raid's Lua BattleRewardData nor the
/// event/BCAT tables loaded in a Switch session.
/// </summary>
public sealed partial class RaidRewardDiagnosticForm
{
    private readonly Button baseRewardTableButton = new()
    {
        Text = "查看奖励表/模板（内置资源，只读）", AutoSize = true,
    };
    private IReadOnlyList<RaidRewardEntry?> tableViewerSlots = [];

    private void ConfigureBaseRewardTableViewer(FlowLayoutPanel actions,
        IReadOnlyList<RaidRewardEntry?> configuredSlots)
    {
        tableViewerSlots = configuredSlots.ToArray();
        actions.Controls.Add(baseRewardTableButton);
        baseRewardTableButton.Click += (_, _) => ShowBaseRewardTables();
    }

    private void ShowBaseRewardTables()
    {
        try
        {
            RaidRewardTableCatalog.TableAudit audit =
                RaidRewardTableCatalog.LoadBaseTables();

            var form = new Form
            {
                Text = "太晶坑奖励表 · 内置 Base / 本机模板（不代表实机）",
                StartPosition = FormStartPosition.CenterParent,
                MinimumSize = new Size(880, 520),
                Size = new Size(1100, 700),
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(8),
                ColumnCount = 1, RowCount = 3,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            form.Controls.Add(layout);

            var explanation = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Color.DarkRed,
                Text = $"仅显示随 RaidCrawler 打包的 BASE 奖励表：固定 {audit.FixedTables} 张、随机 {audit.LotteryTables} 张、" +
                    $"独立 TableName {audit.DistinctKeys} 个。" + Environment.NewLine +
                    "当前模板不等于游戏实时奖励；不含活动/BCAT 覆盖、单场随机抽取、Lua [3] 或结算背包数据。",
                TextAlign = ContentAlignment.MiddleLeft,
            };
            layout.Controls.Add(explanation, 0, 0);

            var selection = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
            };
            selection.Items.Add("当前编辑器配置：只读 20×999 等自定义模板，未写入游戏");
            foreach (ulong key in audit.Rows.Select(x => x.TableName).Distinct().OrderBy(x => x))
                selection.Items.Add($"TableName={key} (0x{key:X16})");
            layout.Controls.Add(selection, 0, 1);

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            };
            grid.Columns.Add("Origin", "数据来源");
            grid.Columns.Add("Slot", "槽位");
            grid.Columns.Add("ItemId", "道具 ID");
            grid.Columns.Add("Quantity", "数量");
            grid.Columns.Add("Category", "类别");
            grid.Columns.Add("Subject", "领取条件");
            grid.Columns.Add("Weight", "随机权重");
            grid.Columns.Add("Rare", "稀有标记");
            layout.Controls.Add(grid, 0, 2);

            var ordered = audit.Rows.Select(x => x.TableName).Distinct().OrderBy(x => x).ToArray();
            selection.SelectedIndexChanged += (_, _) =>
            {
                grid.Rows.Clear();
                if (selection.SelectedIndex == 0)
                {
                    for (int i = 0; i < tableViewerSlots.Count; i++)
                    {
                        var item = tableViewerSlots[i];
                        if (item is null) continue;
                        grid.Rows.Add("TEMPLATE_NOT_APPLIED", i, item.ItemId,
                            item.Quantity, "—", item.Subject, "—", item.Rare);
                    }
                    Append("reward-table-config-preview",
                        "展示当前窗口的自定义奖励配置，不代表读取了实机奖励列表。",
                        new { source = "WINDOW_CONFIG_ONLY", rows = tableViewerSlots.Count(x => x is not null),
                              configuredRows = tableViewerSlots.Select((row, index) => new { row, index })
                                  .Where(x => x.row is not null)
                                  .Select(x => new { slot = x.index,
                                      ItemId = x.row!.ItemId,
                                      Quantity = x.row.Quantity,
                                      Rare = x.row.Rare,
                                      Subject = x.row.Subject }).ToArray(),
                              currentRaidRewardsCaptured = false });
                }
                else
                {
                    ulong key = ordered[selection.SelectedIndex - 1];
                    var rows = RaidRewardTableCatalog.ForTable(audit, key);
                    foreach (var row in rows)
                        grid.Rows.Add(row.Source == "fixed" ? "BASE_FIXED" : "BASE_LOTTERY",
                            row.Slot, row.ItemId, row.Quantity, row.Category,
                            row.Subject?.ToString() ?? "—", row.Weight?.ToString() ?? "—",
                            row.Rare?.ToString() ?? "—");
                    Append("reward-table-base-preview",
                        $"展示内置基础 TableName={key}，表定义行 {rows.Count}；非当前坑实时抽奖结果。",
                        new { source = "EMBEDDED_BASE_RESOURCE", tableName = key.ToString(),
                              fixedRows = rows.Count(x => x.Source == "fixed"),
                              lotteryRows = rows.Count(x => x.Source == "lottery"),
                              rewardDefinitions = rows.Select(row => new {
                                  row.Source, row.Slot, row.ItemId, row.Quantity,
                                  row.Category, row.Subject, row.Weight, row.Rare
                              }).ToArray(),
                              currentRaidRewardsCaptured = false });
                }
            };

            Append("reward-table-base-audit",
                $"内置 Base 奖励表：固定 {audit.FixedTables} 张、随机 {audit.LotteryTables} 张，" +
                $"不同 TableName {audit.DistinctKeys} 个。不会访问 Switch。",
                new { source = audit.Provenance, fixedTableCount = audit.FixedTables,
                      lotteryTableCount = audit.LotteryTables, distinctTableNames = audit.DistinctKeys,
                      fixedSha256 = audit.FixedSha256, lotterySha256 = audit.LotterySha256,
                      isLiveRaidReward = false, liveLuaRewardRows = "NOT_CAPTURED" });
            selection.SelectedIndex = 0;
            form.ShowDialog(this);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException
                                     or System.Text.Json.JsonException)
        {
            Append("reward-table-error",
                "内置奖励表读取失败：" + ex.GetType().Name + "；没有对 Switch 进行操作。");
        }
    }
}
