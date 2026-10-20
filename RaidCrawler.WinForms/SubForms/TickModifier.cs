using RaidCrawler.Core.Connection;
using RaidCrawler.Core.Discord;

namespace RaidCrawler.WinForms.SubForms
{
    public partial class TickModifier : Form
    {
        readonly ConnectionWrapperAsync ConnectionWrapper;
        readonly NotificationHandler Webhook;
        readonly CancellationToken Token;
        public TickModifier(ulong tick, ConnectionWrapperAsync connectionWrapper, NotificationHandler webhook, CancellationToken token)
        {
            InitializeComponent();
            TB_Tick.Text = tick.ToString();
            ConnectionWrapper = connectionWrapper;
            Webhook = webhook;
            Token = token;
        }

        private async void B_Read_Click(object sender, EventArgs e)
        {
            try
            {
                var tick = await ConnectionWrapper.GetCurrentTime(Token).ConfigureAwait(false);

                if (InvokeRequired)
                    Invoke(() => TB_Tick.Text = tick.ToString());
                else
                    TB_Tick.Text = tick.ToString();
            }
            catch (Exception ex)
            {
                await this.DisplayMessageBox(Webhook, $"无法读取日期：{ex.Message}", Token).ConfigureAwait(false);
            }
        }

        private async void B_NTP_Click(object sender, EventArgs e)
        {
            try
            {
                await ConnectionWrapper.ResetTimeNTP(Token).ConfigureAwait(false);
                B_Read_Click(sender, e);
            }
            catch (Exception ex)
            {
                await this.DisplayMessageBox(Webhook, $"无法校准日期：{ex.Message}", Token).ConfigureAwait(false);
            }
        }

        private async void B_Write_Click(object sender, EventArgs e)
        {
            try
            {
                var success = ulong.TryParse(TB_Tick.Text, out var time);
                if (success)
                {
                    await ConnectionWrapper.SetCurrentTime(time, Token).ConfigureAwait(false);
                }
                else
                {
                    await this.DisplayMessageBox(Webhook, $"无法写入日期：{TB_Tick.Text} 无法解析为无符号整数！", Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await this.DisplayMessageBox(Webhook, $"无法写入日期：{ex.Message}", Token).ConfigureAwait(false);
            }
        }
    }
}
