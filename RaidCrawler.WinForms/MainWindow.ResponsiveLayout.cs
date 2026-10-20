using System.Drawing;
using System.Windows.Forms;

namespace RaidCrawler.WinForms;

/// <summary>
/// Responsive WinForms shell over the original designer controls.
/// Reparents existing controls, preserving all original event handlers and
/// Enable/Visible/Text/Image state management; no duplicate actions.
/// The designer remains the source of behavior. This owns layout only.
/// </summary>
public partial class MainWindow
{
    private static readonly Color UiBackground = Color.FromArgb(245, 248, 252);
    private static readonly Color UiAccent = Color.FromArgb(31, 102, 192);
    private static readonly Color UiText = Color.FromArgb(34, 49, 69);
    private static readonly Color UiMuted = Color.FromArgb(102, 119, 139);
    private FlowLayoutPanel? _responsiveSidebar;
    private FlowLayoutPanel? _responsiveDetails;
    private readonly List<Control> _sidebarSections = [];
    private readonly List<Control> _detailSections = [];

    private int Px(int value) =>
        (int)Math.Round(value * Math.Max(1f, DeviceDpi / 96f));

    private static TableLayoutPanel Rows(int[] widths, int rowCount, int rowHeight)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(3, 4, 3, 4),
            ColumnCount = widths.Length,
            RowCount = rowCount,
            BackColor = Color.White,
        };
        for (int i = 0; i < widths.Length; i++)
            grid.ColumnStyles.Add(widths[i] == 0
                ? new ColumnStyle(SizeType.Percent, 100f / widths.Count(x => x == 0))
                : new ColumnStyle(SizeType.Absolute, widths[i]));
        for (int i = 0; i < rowCount; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        return grid;
    }

    private static void Put(TableLayoutPanel grid, Control control,
        int column, int row, int span = 1)
    {
        control.Dock = DockStyle.Fill;
        control.Anchor = AnchorStyles.None;
        control.Margin = new Padding(4, 4, 4, 4);
        if (control is Label label)
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleRight;
            label.ForeColor = UiText;
        }
        if (control is TextBox input)
        {
            input.BorderStyle = BorderStyle.FixedSingle;
            if (input.ReadOnly)
                input.BackColor = Color.FromArgb(249, 251, 254);
        }
        grid.Controls.Add(control, column, row);
        if (span > 1) grid.SetColumnSpan(control, span);
    }

    private GroupBox Section(string title, int height, Control contents)
    {
        var group = new GroupBox
        {
            Text = "  " + title + "  ",
            Height = Px(height),
            BackColor = Color.White,
            ForeColor = UiText,
            Margin = new Padding(0, 0, 0, Px(11)),
            Padding = new Padding(Px(9), Px(19), Px(9), Px(7)),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
        };
        contents.Dock = DockStyle.Fill;
        group.Controls.Add(contents);
        return group;
    }

    private void InitializeResponsiveMainLayout(Button nativeProbe, Button sysbotProbe)
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Font; // preserve WinForms accessibility scaling
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiBackground;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(Px(920), Px(610));
        ClientSize = new Size(Px(1070), Px(690));

        var root = new TableLayoutPanel
        {
            Name = "ResponsiveMainLayout",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(Px(10), Px(10), Px(10), Px(9)),
            BackColor = UiBackground,
            ColumnCount = 2,
            RowCount = 1,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(332)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _responsiveSidebar = new FlowLayoutPanel
        {
            Name = "NavigationSidebar",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0, 0, Px(10), 0),
            Padding = new Padding(Px(3)),
            BackColor = UiBackground,
        };
        _responsiveDetails = new FlowLayoutPanel
        {
            Name = "RaidDetailsContent",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = new Padding(Px(3)),
            BackColor = UiBackground,
        };
        root.Controls.Add(_responsiveSidebar, 0, 0);
        root.Controls.Add(_responsiveDetails, 1, 0);

        // Connection: both WiFi and USB fields remain in the layout.
        // Existing protocol-switch logic continues to control visibility.
        var connect = Rows([Px(106), 0], 3, Px(34));
        Put(connect, LabelSwitchIP, 0, 0);
        Put(connect, InputSwitchIP, 1, 0);
        Put(connect, USB_Port_label, 0, 1);
        Put(connect, USB_Port_TB, 1, 1);
        var connectionButtons = Rows([0, 0], 1, Px(34));
        Put(connectionButtons, ButtonConnect, 0, 0);
        Put(connectionButtons, ButtonDisconnect, 1, 0);
        Put(connect, connectionButtons, 0, 2, 2);
        AddSidebar(Section("设备连接", 144, connect));

        var browse = Rows([0], 2, Px(36));
        LabelLoadedRaids.Font = new Font(Font, FontStyle.Bold);
        LabelLoadedRaids.TextAlign = ContentAlignment.MiddleLeft;
        Put(browse, LabelLoadedRaids, 0, 0);
        LabelLoadedRaids.TextAlign = ContentAlignment.MiddleLeft;
        var navigation = Rows([Px(49), 0, Px(49)], 1, Px(32));
        Put(navigation, ButtonPrevious, 0, 0);
        Put(navigation, ComboIndex, 1, 0);
        Put(navigation, ButtonNext, 2, 0);
        ButtonPrevious.Text = "‹";
        ButtonNext.Text = "›";
        Put(browse, navigation, 0, 1);
        AddSidebar(Section("坑位浏览", 116, browse));

        // These six controls were originally children of groupBox1. Moving
        // them preserves Click handlers and all enabled state transitions.
        var operations = Rows([0, 0], 3, Px(37));
        Put(operations, ButtonReadRaids, 0, 0);
        Put(operations, ButtonViewRAM, 1, 0);
        Put(operations, SendScreenshot, 0, 1);
        Put(operations, ButtonDownloadEvents, 1, 1);
        Put(operations, btnOpenMap, 0, 2);
        Put(operations, Rewards, 1, 2);
        AddSidebar(Section("太晶坑操作", 156, operations));

        var automation = Rows([0, 0], 4, Px(36));
        Put(automation, StopFilter, 0, 0);
        Put(automation, CheckEnableFilters, 1, 0);
        Put(automation, LabelSandwichBonus, 0, 1);
        Put(automation, RaidBoost, 1, 1);
        var dateSlot = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        ButtonAdvanceDate.Dock = DockStyle.Fill;
        StopAdvance_Button.Dock = DockStyle.Fill;
        dateSlot.Controls.Add(ButtonAdvanceDate);
        dateSlot.Controls.Add(StopAdvance_Button);
        StopAdvance_Button.Visible = false; // original runtime toggles this
        Put(automation, dateSlot, 0, 2);
        Put(automation, B_DateTools, 1, 2);
        Put(automation, ConfigSettings, 0, 3, 2);
        AddSidebar(Section("筛选与自动化", 193, automation));

        // Keep the native-only research entry, but no longer place it outside
        // the 580px designer form. Pure sys-botbase is the primary entry.
        var experiments = Rows([0, 0], 1, Px(42));
        Put(experiments, sysbotProbe, 0, 0);
        Put(experiments, nativeProbe, 1, 0);
        AddSidebar(Section("房主研究工具", 87, experiments));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4, RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(Px(10), Px(6), Px(10), Px(6)),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(86)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(71)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(87)));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var titlePanel = new Panel { Dock = DockStyle.Fill };
        var headline = new Label
        {
            Text = "太晶坑详情", Dock = DockStyle.Top, Height = Px(36),
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            ForeColor = UiText,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var subtitle = new Label
        {
            Text = "连接 Switch 并读取坑位后查看详细信息",
            Dock = DockStyle.Bottom, Height = Px(22),
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = UiMuted,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        titlePanel.Controls.Add(headline);
        titlePanel.Controls.Add(subtitle);
        Put(header, titlePanel, 0, 0);
        Put(header, labelEvent, 1, 0);
        labelEvent.TextAlign = ContentAlignment.MiddleCenter;
        Put(header, GemIcon, 2, 0);
        Put(header, Sprite, 3, 0);
        Sprite.SizeMode = PictureBoxSizeMode.Zoom;
        AddDetails(Section("当前坑位", 111, header));

        var info = Rows([Px(96), 0, Px(94), 0], 10, Px(38));
        Put(info, LabelSeed, 0, 0);
        Put(info, Seed, 1, 0);
        Put(info, LabelEC, 2, 0);
        Put(info, EC, 3, 0);
        Put(info, LabelPID, 0, 1);
        Put(info, PID, 1, 1, 3);
        Put(info, LabelSpecies, 0, 2);
        Put(info, Species, 1, 2, 3);
        Put(info, LabelAbility, 0, 3);
        Put(info, Ability, 1, 3, 3);
        Put(info, LabelTeraType, 0, 4);
        Put(info, TeraType, 1, 4);
        Put(info, LabelDifficulty, 2, 4);
        Put(info, Difficulty, 3, 4);
        Put(info, LabelGender, 0, 5);
        Put(info, Gender, 1, 5);
        Put(info, LabelNature, 2, 5);
        Put(info, Nature, 3, 5);
        Put(info, LabelIVs, 0, 6);
        Put(info, IVs, 1, 6, 3);
        Put(info, LabelUNK_2, 0, 7);
        Put(info, Area, 1, 7, 3);
        Put(info, LabelMoves, 0, 8);
        Put(info, Move1, 1, 8);
        Put(info, FieldLabel("招式 2："), 2, 8);
        Put(info, Move2, 3, 8);
        Put(info, FieldLabel("招式 3："), 0, 9);
        Put(info, Move3, 1, 9);
        Put(info, FieldLabel("招式 4："), 2, 9);
        Put(info, Move4, 3, 9);
        AddDetails(Section("宝可梦与战斗信息", 438, info));

        var hint = new Label
        {
            Text = "提示：部分功能在连接设备或读取太晶坑后才会启用。" +
                   "「纯 sys-botbase」无需向 Switch 安装额外插件。",
            ForeColor = UiMuted,
            BackColor = UiBackground,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = Px(40), Margin = new Padding(Px(8), 0, Px(8), 0),
        };
        AddDetails(hint);

        StyleMainButtons(ButtonConnect, sysbotProbe);
        Controls.Add(root);
        root.BringToFront();
        statusStrip1.Dock = DockStyle.Bottom;
        statusStrip1.BackColor = Color.White;
        statusStrip1.BringToFront();

        _responsiveSidebar.SizeChanged += (_, _) => FitFlowChildren(_responsiveSidebar, _sidebarSections);
        _responsiveDetails.SizeChanged += (_, _) => FitFlowChildren(_responsiveDetails, _detailSections);
        FitFlowChildren(_responsiveSidebar, _sidebarSections);
        FitFlowChildren(_responsiveDetails, _detailSections);
        ResumeLayout(true);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        ForeColor = UiText,
        Font = new Font("Microsoft YaHei UI", 9F),
        TextAlign = ContentAlignment.MiddleRight,
    };

    private void AddSidebar(Control control)
    {
        _sidebarSections.Add(control);
        _responsiveSidebar!.Controls.Add(control);
    }

    private void AddDetails(Control control)
    {
        _detailSections.Add(control);
        _responsiveDetails!.Controls.Add(control);
    }

    private static void FitFlowChildren(FlowLayoutPanel panel, List<Control> children)
    {
        int width = Math.Max(280, panel.ClientSize.Width - panel.Padding.Horizontal - 8);
        foreach (var control in children)
            if (control.Width != width)
                control.Width = width;
    }

    private void StyleMainButtons(Button connect, Button sysbot)
    {
        foreach (var button in new[] { connect, sysbot })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = UiAccent;
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            button.UseVisualStyleBackColor = false;
        }
        toolTip.SetToolTip(sysbot, "仅使用现有 sys-botbase 的房主只读诊断和奖励实验");
        toolTip.SetToolTip(ButtonDisconnect, "断开当前 Switch 连接");
        toolTip.SetToolTip(ComboIndex, "选择当前要查看的太晶坑");
    }
}
