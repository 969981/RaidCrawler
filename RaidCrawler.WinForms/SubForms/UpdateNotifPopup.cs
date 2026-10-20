namespace RaidCrawler.WinForms.SubForms;

public partial class UpdateNotifPopup : Form
{
    private Version cv;
    private Version nv;
    public UpdateNotifPopup(Version currentVersion, Version newVersion)
    {
        cv = currentVersion;
        nv = newVersion;
        InitializeComponent();
    }

    private void UpdateNotifPopup_Load(object sender, EventArgs e)
    {
        L_Version.Text = $"当前：v{cv.Major}.{cv.Minor}.{cv.Build} | 最新：v{nv.Major}.{nv.Minor}.{nv.Build}";
        B_Download.Focus();
        CenterToScreen();
    }
}
