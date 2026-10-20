using SysBot.Base;

namespace RaidCrawler.WinForms.SubForms
{
    partial class ConfigWindow
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ConfigWindow));
            FocusWindow = new CheckBox();
            EnableAlert = new CheckBox();
            PlayTone = new CheckBox();
            LabelMatchFound = new Label();
            AlertMessage = new TextBox();
            L_AdvanceDate = new Label();
            L_BaseDelay = new Label();
            SystemDDownPresses = new NumericUpDown();
            L_DdownInput = new Label();
            NavigateToSettings = new NumericUpDown();
            OpenSettings = new NumericUpDown();
            OpenHome = new NumericUpDown();
            L_OpenHOME = new Label();
            L_NavigateSettings = new Label();
            L_OpenSettingsDelay = new Label();
            L_ScrollSystem = new Label();
            Hold = new NumericUpDown();
            L_SubmenuDelay = new Label();
            Submenu = new NumericUpDown();
            L_DateChangeDelay = new Label();
            DateChange = new NumericUpDown();
            L_ReturnHomeDelay = new Label();
            ReturnHome = new NumericUpDown();
            L_ReOpenGameDelay = new Label();
            ReturnGame = new NumericUpDown();
            L_DaysToSkip = new Label();
            DaysToSkip = new NumericUpDown();
            UseTouch = new CheckBox();
            DiscordWebhook = new TextBox();
            EnableDiscordNotifications = new CheckBox();
            label13 = new Label();
            ExperimentalView = new CheckBox();
            tabControl1 = new TabControl();
            tabGeneral = new TabPage();
            LocationGroup = new GroupBox();
            KitakamiScanCheck = new CheckBox();
            PaldeaScanCheck = new CheckBox();
            Protocol_dropdown = new ComboBox();
            Protocol_label = new Label();
            label23 = new Label();
            LabelEventProgress = new Label();
            EventProgress = new ComboBox();
            LabelGame = new Label();
            Game = new ComboBox();
            LabelStoryProgress = new Label();
            StoryProgress = new ComboBox();
            tabMatch = new TabPage();
            tabAdvanceDate = new TabPage();
            ZyroMethod = new CheckBox();
            UseMapTrick = new CheckBox();
            ExtraOverworldWait = new NumericUpDown();
            L_ExtraOverworldWait = new Label();
            RelaunchDelay = new NumericUpDown();
            L_RelaunchDelay = new Label();
            SystemReset = new NumericUpDown();
            L_SystemReset = new Label();
            UseSetStick = new CheckBox();
            DodgeSystemUpdate = new CheckBox();
            SaveGameDelay = new NumericUpDown();
            L_SaveGame = new Label();
            SaveGame = new CheckBox();
            L_OvershootHold = new Label();
            SystemOvershoot = new NumericUpDown();
            UseOvershoot = new CheckBox();
            BaseDelay = new NumericUpDown();
            tabWebhook = new TabPage();
            EmojiConfig = new Button();
            labelWebhooks = new Label();
            label21 = new Label();
            DiscordMessageContent = new TextBox();
            label14 = new Label();
            btnTestWebHook = new Button();
            denToggle = new CheckBox();
            LocationSettings_label = new Label();
            IVstyle = new ComboBox();
            IVverbose = new CheckBox();
            label19 = new Label();
            label18 = new Label();
            EnableEmoji = new CheckBox();
            tabExperimental = new TabPage();
            InstanceName = new TextBox();
            label17 = new Label();
            tabAbout = new TabPage();
            linkLabel1 = new LinkLabel();
            labelAppName = new Label();
            picAppIcon = new PictureBox();
            labelAppVersion = new Label();
            BlueberryScanCheck = new CheckBox();
            ((System.ComponentModel.ISupportInitialize)SystemDDownPresses).BeginInit();
            ((System.ComponentModel.ISupportInitialize)NavigateToSettings).BeginInit();
            ((System.ComponentModel.ISupportInitialize)OpenSettings).BeginInit();
            ((System.ComponentModel.ISupportInitialize)OpenHome).BeginInit();
            ((System.ComponentModel.ISupportInitialize)Hold).BeginInit();
            ((System.ComponentModel.ISupportInitialize)Submenu).BeginInit();
            ((System.ComponentModel.ISupportInitialize)DateChange).BeginInit();
            ((System.ComponentModel.ISupportInitialize)ReturnHome).BeginInit();
            ((System.ComponentModel.ISupportInitialize)ReturnGame).BeginInit();
            ((System.ComponentModel.ISupportInitialize)DaysToSkip).BeginInit();
            tabControl1.SuspendLayout();
            tabGeneral.SuspendLayout();
            LocationGroup.SuspendLayout();
            tabMatch.SuspendLayout();
            tabAdvanceDate.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)ExtraOverworldWait).BeginInit();
            ((System.ComponentModel.ISupportInitialize)RelaunchDelay).BeginInit();
            ((System.ComponentModel.ISupportInitialize)SystemReset).BeginInit();
            ((System.ComponentModel.ISupportInitialize)SaveGameDelay).BeginInit();
            ((System.ComponentModel.ISupportInitialize)SystemOvershoot).BeginInit();
            ((System.ComponentModel.ISupportInitialize)BaseDelay).BeginInit();
            tabWebhook.SuspendLayout();
            tabExperimental.SuspendLayout();
            tabAbout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picAppIcon).BeginInit();
            SuspendLayout();
            // 
            // FocusWindow
            // 
            FocusWindow.AutoSize = true;
            FocusWindow.Location = new Point(8, 44);
            FocusWindow.Name = "FocusWindow";
            FocusWindow.Size = new Size(123, 19);
            FocusWindow.TabIndex = 1;
            FocusWindow.Text = "聚焦 RaidCrawler 窗口";
            FocusWindow.UseVisualStyleBackColor = true;
            // 
            // EnableAlert
            // 
            EnableAlert.AutoSize = true;
            EnableAlert.Location = new Point(8, 65);
            EnableAlert.Name = "EnableAlert";
            EnableAlert.Size = new Size(293, 19);
            EnableAlert.TabIndex = 2;
            EnableAlert.Text = "找到目标时显示提示窗口：";
            EnableAlert.UseVisualStyleBackColor = true;
            EnableAlert.CheckedChanged += EnableAlert_CheckedChanged;
            // 
            // PlayTone
            // 
            PlayTone.AutoSize = true;
            PlayTone.Location = new Point(8, 23);
            PlayTone.Name = "PlayTone";
            PlayTone.Size = new Size(84, 19);
            PlayTone.TabIndex = 0;
            PlayTone.Text = "播放提示音";
            PlayTone.UseVisualStyleBackColor = true;
            // 
            // LabelMatchFound
            // 
            LabelMatchFound.AutoSize = true;
            LabelMatchFound.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            LabelMatchFound.Location = new Point(6, 3);
            LabelMatchFound.Name = "LabelMatchFound";
            LabelMatchFound.Size = new Size(137, 15);
            LabelMatchFound.TabIndex = 3;
            LabelMatchFound.Text = "找到匹配目标时：";
            // 
            // AlertMessage
            // 
            AlertMessage.Location = new Point(8, 84);
            AlertMessage.Name = "AlertMessage";
            AlertMessage.Size = new Size(357, 23);
            AlertMessage.TabIndex = 4;
            // 
            // L_AdvanceDate
            // 
            L_AdvanceDate.AutoSize = true;
            L_AdvanceDate.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            L_AdvanceDate.Location = new Point(6, 3);
            L_AdvanceDate.Name = "L_AdvanceDate";
            L_AdvanceDate.Size = new Size(233, 15);
            L_AdvanceDate.TabIndex = 6;
            L_AdvanceDate.Text = "日期推进设置（单位：毫秒）";
            // 
            // L_BaseDelay
            // 
            L_BaseDelay.AutoSize = true;
            L_BaseDelay.Location = new Point(8, 178);
            L_BaseDelay.Name = "L_BaseDelay";
            L_BaseDelay.Size = new Size(196, 15);
            L_BaseDelay.TabIndex = 8;
            L_BaseDelay.Text = "所有操作额外等待：";
            // 
            // SystemDDownPresses
            // 
            SystemDDownPresses.Location = new Point(296, 321);
            SystemDDownPresses.Maximum = new decimal(new int[] { 99, 0, 0, 0 });
            SystemDDownPresses.Name = "SystemDDownPresses";
            SystemDDownPresses.Size = new Size(68, 23);
            SystemDDownPresses.TabIndex = 10;
            SystemDDownPresses.Value = new decimal(new int[] { 38, 0, 0, 0 });
            // 
            // L_DdownInput
            // 
            L_DdownInput.AutoSize = true;
            L_DdownInput.Location = new Point(8, 323);
            L_DdownInput.Name = "L_DdownInput";
            L_DdownInput.Size = new Size(228, 15);
            L_DdownInput.TabIndex = 11;
            L_DdownInput.Text = "进入日期与时间所需的向下次数：";
            // 
            // NavigateToSettings
            // 
            NavigateToSettings.Location = new Point(296, 234);
            NavigateToSettings.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            NavigateToSettings.Name = "NavigateToSettings";
            NavigateToSettings.Size = new Size(68, 23);
            NavigateToSettings.TabIndex = 16;
            NavigateToSettings.Value = new decimal(new int[] { 100, 0, 0, 0 });
            // 
            // OpenSettings
            // 
            OpenSettings.Location = new Point(296, 263);
            OpenSettings.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            OpenSettings.Name = "OpenSettings";
            OpenSettings.Size = new Size(68, 23);
            OpenSettings.TabIndex = 17;
            OpenSettings.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            // 
            // OpenHome
            // 
            OpenHome.Location = new Point(296, 205);
            OpenHome.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            OpenHome.Name = "OpenHome";
            OpenHome.Size = new Size(68, 23);
            OpenHome.TabIndex = 18;
            OpenHome.Value = new decimal(new int[] { 1800, 0, 0, 0 });
            // 
            // L_OpenHOME
            // 
            L_OpenHOME.AutoSize = true;
            L_OpenHOME.Location = new Point(8, 207);
            L_OpenHOME.Name = "L_OpenHOME";
            L_OpenHOME.Size = new Size(140, 15);
            L_OpenHOME.TabIndex = 19;
            L_OpenHOME.Text = "打开 HOME 菜单等待：";
            // 
            // L_NavigateSettings
            // 
            L_NavigateSettings.AutoSize = true;
            L_NavigateSettings.Location = new Point(8, 236);
            L_NavigateSettings.Name = "L_NavigateSettings";
            L_NavigateSettings.Size = new Size(146, 15);
            L_NavigateSettings.TabIndex = 20;
            L_NavigateSettings.Text = "进入系统设置等待：";
            // 
            // L_OpenSettingsDelay
            // 
            L_OpenSettingsDelay.AutoSize = true;
            L_OpenSettingsDelay.Location = new Point(8, 265);
            L_OpenSettingsDelay.Name = "L_OpenSettingsDelay";
            L_OpenSettingsDelay.Size = new Size(114, 15);
            L_OpenSettingsDelay.TabIndex = 21;
            L_OpenSettingsDelay.Text = "打开设置等待：";
            // 
            // L_ScrollSystem
            // 
            L_ScrollSystem.AutoSize = true;
            L_ScrollSystem.Location = new Point(8, 294);
            L_ScrollSystem.Name = "L_ScrollSystem";
            L_ScrollSystem.Size = new Size(187, 15);
            L_ScrollSystem.TabIndex = 23;
            L_ScrollSystem.Text = "滚动到“主机”选项的时间：";
            // 
            // Hold
            // 
            Hold.Location = new Point(296, 292);
            Hold.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            Hold.Name = "Hold";
            Hold.Size = new Size(68, 23);
            Hold.TabIndex = 22;
            Hold.Value = new decimal(new int[] { 1700, 0, 0, 0 });
            // 
            // L_SubmenuDelay
            // 
            L_SubmenuDelay.AutoSize = true;
            L_SubmenuDelay.Location = new Point(8, 352);
            L_SubmenuDelay.Name = "L_SubmenuDelay";
            L_SubmenuDelay.Size = new Size(123, 15);
            L_SubmenuDelay.TabIndex = 25;
            L_SubmenuDelay.Text = "打开子菜单等待：";
            // 
            // Submenu
            // 
            Submenu.Location = new Point(296, 350);
            Submenu.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            Submenu.Name = "Submenu";
            Submenu.Size = new Size(68, 23);
            Submenu.TabIndex = 24;
            Submenu.Value = new decimal(new int[] { 800, 0, 0, 0 });
            // 
            // L_DateChangeDelay
            // 
            L_DateChangeDelay.AutoSize = true;
            L_DateChangeDelay.Location = new Point(8, 381);
            L_DateChangeDelay.Name = "L_DateChangeDelay";
            L_DateChangeDelay.Size = new Size(138, 15);
            L_DateChangeDelay.TabIndex = 27;
            L_DateChangeDelay.Text = "修改日期等待：";
            // 
            // DateChange
            // 
            DateChange.Location = new Point(296, 379);
            DateChange.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            DateChange.Name = "DateChange";
            DateChange.Size = new Size(68, 23);
            DateChange.TabIndex = 26;
            DateChange.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // L_ReturnHomeDelay
            // 
            L_ReturnHomeDelay.AutoSize = true;
            L_ReturnHomeDelay.Location = new Point(8, 439);
            L_ReturnHomeDelay.Name = "L_ReturnHomeDelay";
            L_ReturnHomeDelay.Size = new Size(160, 15);
            L_ReturnHomeDelay.TabIndex = 29;
            L_ReturnHomeDelay.Text = "返回 HOME 菜单等待：";
            // 
            // ReturnHome
            // 
            ReturnHome.Location = new Point(296, 437);
            ReturnHome.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            ReturnHome.Name = "ReturnHome";
            ReturnHome.Size = new Size(68, 23);
            ReturnHome.TabIndex = 28;
            ReturnHome.Value = new decimal(new int[] { 2500, 0, 0, 0 });
            // 
            // L_ReOpenGameDelay
            // 
            L_ReOpenGameDelay.AutoSize = true;
            L_ReOpenGameDelay.Location = new Point(8, 468);
            L_ReOpenGameDelay.Name = "L_ReOpenGameDelay";
            L_ReOpenGameDelay.Size = new Size(119, 15);
            L_ReOpenGameDelay.TabIndex = 31;
            L_ReOpenGameDelay.Text = "重新进入游戏等待：";
            // 
            // ReturnGame
            // 
            ReturnGame.Location = new Point(296, 466);
            ReturnGame.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            ReturnGame.Name = "ReturnGame";
            ReturnGame.Size = new Size(68, 23);
            ReturnGame.TabIndex = 30;
            ReturnGame.Value = new decimal(new int[] { 4000, 0, 0, 0 });
            // 
            // L_DaysToSkip
            // 
            L_DaysToSkip.AutoSize = true;
            L_DaysToSkip.Location = new Point(8, 410);
            L_DaysToSkip.Name = "L_DaysToSkip";
            L_DaysToSkip.Size = new Size(179, 15);
            L_DaysToSkip.TabIndex = 33;
            L_DaysToSkip.Text = "每次跳过的天数/月数：";
            // 
            // DaysToSkip
            // 
            DaysToSkip.Location = new Point(296, 408);
            DaysToSkip.Maximum = new decimal(new int[] { 99, 0, 0, 0 });
            DaysToSkip.Name = "DaysToSkip";
            DaysToSkip.Size = new Size(68, 23);
            DaysToSkip.TabIndex = 32;
            // 
            // UseTouch
            // 
            UseTouch.AutoSize = true;
            UseTouch.Location = new Point(8, 61);
            UseTouch.Name = "UseTouch";
            UseTouch.Size = new Size(267, 19);
            UseTouch.TabIndex = 36;
            UseTouch.Text = "使用触屏操作（更快，实验性）";
            UseTouch.UseVisualStyleBackColor = true;
            // 
            // DiscordWebhook
            // 
            DiscordWebhook.Location = new Point(8, 138);
            DiscordWebhook.Name = "DiscordWebhook";
            DiscordWebhook.Size = new Size(357, 23);
            DiscordWebhook.TabIndex = 37;
            // 
            // EnableDiscordNotifications
            // 
            EnableDiscordNotifications.AutoSize = true;
            EnableDiscordNotifications.Location = new Point(8, 118);
            EnableDiscordNotifications.Name = "EnableDiscordNotifications";
            EnableDiscordNotifications.Size = new Size(303, 19);
            EnableDiscordNotifications.TabIndex = 38;
            EnableDiscordNotifications.Text = "向 Discord Webhook 发送通知（逗号分隔）";
            EnableDiscordNotifications.UseVisualStyleBackColor = true;
            EnableDiscordNotifications.Click += EnableDiscordNotifications_Click;
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label13.Location = new Point(6, 3);
            label13.Name = "label13";
            label13.Size = new Size(84, 15);
            label13.TabIndex = 39;
            label13.Text = "实验功能：";
            // 
            // ExperimentalView
            // 
            ExperimentalView.AutoSize = true;
            ExperimentalView.Location = new Point(8, 23);
            ExperimentalView.Name = "ExperimentalView";
            ExperimentalView.Size = new Size(189, 19);
            ExperimentalView.TabIndex = 40;
            ExperimentalView.Text = "启用直播太晶坑展示界面";
            ExperimentalView.UseVisualStyleBackColor = true;
            ExperimentalView.Click += StreamerView_Clicked;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabGeneral);
            tabControl1.Controls.Add(tabMatch);
            tabControl1.Controls.Add(tabAdvanceDate);
            tabControl1.Controls.Add(tabWebhook);
            tabControl1.Controls.Add(tabExperimental);
            tabControl1.Controls.Add(tabAbout);
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Location = new Point(0, 0);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(380, 668);
            tabControl1.TabIndex = 41;
            // 
            // tabGeneral
            // 
            tabGeneral.Controls.Add(LocationGroup);
            tabGeneral.Controls.Add(Protocol_dropdown);
            tabGeneral.Controls.Add(Protocol_label);
            tabGeneral.Controls.Add(label23);
            tabGeneral.Controls.Add(LabelEventProgress);
            tabGeneral.Controls.Add(EventProgress);
            tabGeneral.Controls.Add(LabelGame);
            tabGeneral.Controls.Add(Game);
            tabGeneral.Controls.Add(LabelStoryProgress);
            tabGeneral.Controls.Add(StoryProgress);
            tabGeneral.Location = new Point(4, 24);
            tabGeneral.Name = "tabGeneral";
            tabGeneral.Padding = new Padding(3);
            tabGeneral.Size = new Size(372, 640);
            tabGeneral.TabIndex = 5;
            tabGeneral.Text = "常规";
            tabGeneral.UseVisualStyleBackColor = true;
            // 
            // LocationGroup
            // 
            LocationGroup.Controls.Add(BlueberryScanCheck);
            LocationGroup.Controls.Add(KitakamiScanCheck);
            LocationGroup.Controls.Add(PaldeaScanCheck);
            LocationGroup.Location = new Point(8, 137);
            LocationGroup.Name = "LocationGroup";
            LocationGroup.Size = new Size(200, 93);
            LocationGroup.TabIndex = 115;
            LocationGroup.TabStop = false;
            LocationGroup.Text = "扫描区域";
            // 
            // KitakamiScanCheck
            // 
            KitakamiScanCheck.AutoSize = true;
            KitakamiScanCheck.Checked = true;
            KitakamiScanCheck.CheckState = CheckState.Checked;
            KitakamiScanCheck.Location = new Point(6, 47);
            KitakamiScanCheck.Name = "KitakamiScanCheck";
            KitakamiScanCheck.Size = new Size(72, 19);
            KitakamiScanCheck.TabIndex = 113;
            KitakamiScanCheck.Text = "北上乡";
            KitakamiScanCheck.UseVisualStyleBackColor = true;
            // 
            // PaldeaScanCheck
            // 
            PaldeaScanCheck.AutoSize = true;
            PaldeaScanCheck.Checked = true;
            PaldeaScanCheck.CheckState = CheckState.Checked;
            PaldeaScanCheck.Location = new Point(6, 22);
            PaldeaScanCheck.Name = "PaldeaScanCheck";
            PaldeaScanCheck.Size = new Size(61, 19);
            PaldeaScanCheck.TabIndex = 112;
            PaldeaScanCheck.Text = "帕底亚";
            PaldeaScanCheck.UseVisualStyleBackColor = true;
            // 
            // Protocol_dropdown
            // 
            Protocol_dropdown.FormattingEnabled = true;
            Protocol_dropdown.Items.AddRange(new object[] { SwitchProtocol.WiFi, SwitchProtocol.USB });
            Protocol_dropdown.Location = new Point(159, 108);
            Protocol_dropdown.MaxDropDownItems = 2;
            Protocol_dropdown.Name = "Protocol_dropdown";
            Protocol_dropdown.Size = new Size(48, 23);
            Protocol_dropdown.TabIndex = 111;
            Protocol_dropdown.Text = "w";
            Protocol_dropdown.SelectedValueChanged += Protocol_Changed;
            // 
            // Protocol_label
            // 
            Protocol_label.AutoSize = true;
            Protocol_label.Location = new Point(7, 111);
            Protocol_label.Name = "Protocol_label";
            Protocol_label.Size = new Size(120, 15);
            Protocol_label.TabIndex = 110;
            Protocol_label.Text = "连接协议：";
            // 
            // label23
            // 
            label23.AutoSize = true;
            label23.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label23.Location = new Point(6, 3);
            label23.Name = "label23";
            label23.Size = new Size(118, 15);
            label23.TabIndex = 109;
            label23.Text = "应用程序设置";
            // 
            // LabelEventProgress
            // 
            LabelEventProgress.AutoSize = true;
            LabelEventProgress.Location = new Point(7, 83);
            LabelEventProgress.Name = "LabelEventProgress";
            LabelEventProgress.Size = new Size(117, 15);
            LabelEventProgress.TabIndex = 108;
            LabelEventProgress.Text = "活动进度等级：";
            // 
            // EventProgress
            // 
            EventProgress.FormattingEnabled = true;
            EventProgress.Items.AddRange(new object[] { "1", "2", "3", "4" });
            EventProgress.Location = new Point(159, 80);
            EventProgress.Name = "EventProgress";
            EventProgress.Size = new Size(48, 23);
            EventProgress.TabIndex = 107;
            EventProgress.Text = "w";
            // 
            // LabelGame
            // 
            LabelGame.AutoSize = true;
            LabelGame.Location = new Point(8, 28);
            LabelGame.Name = "LabelGame";
            LabelGame.Size = new Size(41, 15);
            LabelGame.TabIndex = 106;
            LabelGame.Text = "游戏版本：";
            // 
            // Game
            // 
            Game.FormattingEnabled = true;
            Game.Items.AddRange(new object[] { "Scarlet", "Violet" });
            Game.Location = new Point(111, 25);
            Game.Name = "Game";
            Game.Size = new Size(96, 23);
            Game.TabIndex = 105;
            Game.Text = "w";
            Game.SelectedIndexChanged += Game_SelectedIndexChanged;
            // 
            // LabelStoryProgress
            // 
            LabelStoryProgress.AutoSize = true;
            LabelStoryProgress.Location = new Point(8, 55);
            LabelStoryProgress.Name = "LabelStoryProgress";
            LabelStoryProgress.Size = new Size(115, 15);
            LabelStoryProgress.TabIndex = 104;
            LabelStoryProgress.Text = "剧情进度等级：";
            // 
            // StoryProgress
            // 
            StoryProgress.FormattingEnabled = true;
            StoryProgress.Items.AddRange(new object[] { "1", "2", "3", "4", "5" });
            StoryProgress.Location = new Point(159, 52);
            StoryProgress.Name = "StoryProgress";
            StoryProgress.Size = new Size(48, 23);
            StoryProgress.TabIndex = 103;
            StoryProgress.Text = "w";
            // 
            // tabMatch
            // 
            tabMatch.Controls.Add(LabelMatchFound);
            tabMatch.Controls.Add(FocusWindow);
            tabMatch.Controls.Add(EnableAlert);
            tabMatch.Controls.Add(EnableDiscordNotifications);
            tabMatch.Controls.Add(PlayTone);
            tabMatch.Controls.Add(DiscordWebhook);
            tabMatch.Controls.Add(AlertMessage);
            tabMatch.Location = new Point(4, 24);
            tabMatch.Name = "tabMatch";
            tabMatch.Padding = new Padding(3);
            tabMatch.Size = new Size(372, 640);
            tabMatch.TabIndex = 0;
            tabMatch.Text = "匹配通知";
            tabMatch.UseVisualStyleBackColor = true;
            // 
            // tabAdvanceDate
            // 
            tabAdvanceDate.Controls.Add(ZyroMethod);
            tabAdvanceDate.Controls.Add(UseMapTrick);
            tabAdvanceDate.Controls.Add(ExtraOverworldWait);
            tabAdvanceDate.Controls.Add(L_ExtraOverworldWait);
            tabAdvanceDate.Controls.Add(RelaunchDelay);
            tabAdvanceDate.Controls.Add(L_RelaunchDelay);
            tabAdvanceDate.Controls.Add(SystemReset);
            tabAdvanceDate.Controls.Add(L_SystemReset);
            tabAdvanceDate.Controls.Add(UseSetStick);
            tabAdvanceDate.Controls.Add(DodgeSystemUpdate);
            tabAdvanceDate.Controls.Add(SaveGameDelay);
            tabAdvanceDate.Controls.Add(L_SaveGame);
            tabAdvanceDate.Controls.Add(SaveGame);
            tabAdvanceDate.Controls.Add(L_OvershootHold);
            tabAdvanceDate.Controls.Add(SystemOvershoot);
            tabAdvanceDate.Controls.Add(UseOvershoot);
            tabAdvanceDate.Controls.Add(L_AdvanceDate);
            tabAdvanceDate.Controls.Add(L_BaseDelay);
            tabAdvanceDate.Controls.Add(UseTouch);
            tabAdvanceDate.Controls.Add(SystemDDownPresses);
            tabAdvanceDate.Controls.Add(L_DdownInput);
            tabAdvanceDate.Controls.Add(L_DaysToSkip);
            tabAdvanceDate.Controls.Add(NavigateToSettings);
            tabAdvanceDate.Controls.Add(DaysToSkip);
            tabAdvanceDate.Controls.Add(OpenSettings);
            tabAdvanceDate.Controls.Add(L_ReOpenGameDelay);
            tabAdvanceDate.Controls.Add(OpenHome);
            tabAdvanceDate.Controls.Add(ReturnGame);
            tabAdvanceDate.Controls.Add(L_OpenHOME);
            tabAdvanceDate.Controls.Add(L_ReturnHomeDelay);
            tabAdvanceDate.Controls.Add(L_NavigateSettings);
            tabAdvanceDate.Controls.Add(ReturnHome);
            tabAdvanceDate.Controls.Add(L_OpenSettingsDelay);
            tabAdvanceDate.Controls.Add(L_DateChangeDelay);
            tabAdvanceDate.Controls.Add(Hold);
            tabAdvanceDate.Controls.Add(DateChange);
            tabAdvanceDate.Controls.Add(L_ScrollSystem);
            tabAdvanceDate.Controls.Add(L_SubmenuDelay);
            tabAdvanceDate.Controls.Add(Submenu);
            tabAdvanceDate.Controls.Add(BaseDelay);
            tabAdvanceDate.Location = new Point(4, 24);
            tabAdvanceDate.Name = "tabAdvanceDate";
            tabAdvanceDate.Padding = new Padding(3);
            tabAdvanceDate.Size = new Size(372, 640);
            tabAdvanceDate.TabIndex = 1;
            tabAdvanceDate.Text = "日期推进";
            tabAdvanceDate.UseVisualStyleBackColor = true;
            // 
            // ZyroMethod
            // 
            ZyroMethod.AutoSize = true;
            ZyroMethod.Location = new Point(8, 21);
            ZyroMethod.Name = "ZyroMethod";
            ZyroMethod.Size = new Size(332, 19);
            ZyroMethod.TabIndex = 53;
            ZyroMethod.Text = "使用 Zyro 版 usb-botbase 加速（约 4–5 倍）";
            ZyroMethod.UseVisualStyleBackColor = true;
            // 
            // UseMapTrick
            // 
            UseMapTrick.AutoSize = true;
            UseMapTrick.Location = new Point(8, 41);
            UseMapTrick.Name = "UseMapTrick";
            UseMapTrick.Size = new Size(164, 19);
            UseMapTrick.TabIndex = 52;
            UseMapTrick.Text = "使用地图技巧减少卡顿";
            UseMapTrick.UseVisualStyleBackColor = true;
            // 
            // ExtraOverworldWait
            // 
            ExtraOverworldWait.Location = new Point(296, 555);
            ExtraOverworldWait.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            ExtraOverworldWait.Name = "ExtraOverworldWait";
            ExtraOverworldWait.Size = new Size(68, 23);
            ExtraOverworldWait.TabIndex = 51;
            // 
            // L_ExtraOverworldWait
            // 
            L_ExtraOverworldWait.AutoSize = true;
            L_ExtraOverworldWait.Location = new Point(8, 557);
            L_ExtraOverworldWait.Name = "L_ExtraOverworldWait";
            L_ExtraOverworldWait.Size = new Size(218, 15);
            L_ExtraOverworldWait.TabIndex = 50;
            L_ExtraOverworldWait.Text = "进入野外额外等待：";
            L_ExtraOverworldWait.UseMnemonic = false;
            // 
            // RelaunchDelay
            // 
            RelaunchDelay.Location = new Point(296, 525);
            RelaunchDelay.Maximum = new decimal(new int[] { 20000, 0, 0, 0 });
            RelaunchDelay.Name = "RelaunchDelay";
            RelaunchDelay.Size = new Size(68, 23);
            RelaunchDelay.TabIndex = 49;
            // 
            // L_RelaunchDelay
            // 
            L_RelaunchDelay.AutoSize = true;
            L_RelaunchDelay.Location = new Point(8, 527);
            L_RelaunchDelay.Name = "L_RelaunchDelay";
            L_RelaunchDelay.Size = new Size(198, 15);
            L_RelaunchDelay.TabIndex = 48;
            L_RelaunchDelay.Text = "重启游戏额外等待：";
            // 
            // SystemReset
            // 
            SystemReset.Location = new Point(296, 495);
            SystemReset.Maximum = new decimal(new int[] { 2000, 0, 0, 0 });
            SystemReset.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            SystemReset.Name = "SystemReset";
            SystemReset.Size = new Size(68, 23);
            SystemReset.TabIndex = 47;
            SystemReset.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // L_SystemReset
            // 
            L_SystemReset.AutoSize = true;
            L_SystemReset.Location = new Point(8, 497);
            L_SystemReset.Name = "L_SystemReset";
            L_SystemReset.Size = new Size(203, 15);
            L_SystemReset.TabIndex = 46;
            L_SystemReset.Text = "跳过指定次数后重启游戏：";
            // 
            // UseSetStick
            // 
            UseSetStick.AutoSize = true;
            UseSetStick.Location = new Point(8, 145);
            UseSetStick.Name = "UseSetStick";
            UseSetStick.Size = new Size(222, 19);
            UseSetStick.TabIndex = 45;
            UseSetStick.Text = "使用 SetStick 替代按住按键";
            UseSetStick.UseVisualStyleBackColor = true;
            UseSetStick.CheckedChanged += UseSetStick_CheckedChanged;
            // 
            // DodgeSystemUpdate
            // 
            DodgeSystemUpdate.AutoSize = true;
            DodgeSystemUpdate.Location = new Point(8, 124);
            DodgeSystemUpdate.Name = "DodgeSystemUpdate";
            DodgeSystemUpdate.Size = new Size(184, 19);
            DodgeSystemUpdate.TabIndex = 44;
            DodgeSystemUpdate.Text = "避开系统更新提示";
            DodgeSystemUpdate.UseVisualStyleBackColor = true;
            // 
            // SaveGameDelay
            // 
            SaveGameDelay.Location = new Point(296, 614);
            SaveGameDelay.Name = "SaveGameDelay";
            SaveGameDelay.Size = new Size(68, 23);
            SaveGameDelay.TabIndex = 43;
            // 
            // L_SaveGame
            // 
            L_SaveGame.AutoSize = true;
            L_SaveGame.Location = new Point(8, 616);
            L_SaveGame.Name = "L_SaveGame";
            L_SaveGame.Size = new Size(186, 15);
            L_SaveGame.TabIndex = 42;
            L_SaveGame.Text = "等待游戏保存的时间：";
            // 
            // SaveGame
            // 
            SaveGame.AutoSize = true;
            SaveGame.Location = new Point(8, 103);
            SaveGame.Name = "SaveGame";
            SaveGame.Size = new Size(164, 19);
            SaveGame.TabIndex = 41;
            SaveGame.Text = "匹配后保存游戏";
            SaveGame.UseVisualStyleBackColor = true;
            SaveGame.CheckedChanged += SaveGame_CheckedChanged;
            // 
            // L_OvershootHold
            // 
            L_OvershootHold.AutoSize = true;
            L_OvershootHold.Location = new Point(8, 587);
            L_OvershootHold.Name = "L_OvershootHold";
            L_OvershootHold.Size = new Size(236, 15);
            L_OvershootHold.TabIndex = 40;
            L_OvershootHold.Text = "越过“日期与时间”的按住时长：";
            // 
            // SystemOvershoot
            // 
            SystemOvershoot.Location = new Point(296, 585);
            SystemOvershoot.Maximum = new decimal(new int[] { 1200, 0, 0, 0 });
            SystemOvershoot.Name = "SystemOvershoot";
            SystemOvershoot.Size = new Size(68, 23);
            SystemOvershoot.TabIndex = 39;
            SystemOvershoot.Value = new decimal(new int[] { 950, 0, 0, 0 });
            // 
            // UseOvershoot
            // 
            UseOvershoot.AutoSize = true;
            UseOvershoot.Location = new Point(8, 82);
            UseOvershoot.Name = "UseOvershoot";
            UseOvershoot.Size = new Size(355, 19);
            UseOvershoot.TabIndex = 38;
            UseOvershoot.Text = "使用越位操作替代向下按键（更快，实验性）";
            UseOvershoot.UseVisualStyleBackColor = true;
            UseOvershoot.CheckedChanged += UseOvershoot_CheckedChanged;
            // 
            // BaseDelay
            // 
            BaseDelay.Location = new Point(296, 176);
            BaseDelay.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            BaseDelay.Name = "BaseDelay";
            BaseDelay.Size = new Size(68, 23);
            BaseDelay.TabIndex = 9;
            // 
            // tabWebhook
            // 
            tabWebhook.Controls.Add(EmojiConfig);
            tabWebhook.Controls.Add(labelWebhooks);
            tabWebhook.Controls.Add(label21);
            tabWebhook.Controls.Add(DiscordMessageContent);
            tabWebhook.Controls.Add(label14);
            tabWebhook.Controls.Add(btnTestWebHook);
            tabWebhook.Controls.Add(denToggle);
            tabWebhook.Controls.Add(LocationSettings_label);
            tabWebhook.Controls.Add(IVstyle);
            tabWebhook.Controls.Add(IVverbose);
            tabWebhook.Controls.Add(label19);
            tabWebhook.Controls.Add(label18);
            tabWebhook.Controls.Add(EnableEmoji);
            tabWebhook.Location = new Point(4, 24);
            tabWebhook.Name = "tabWebhook";
            tabWebhook.Size = new Size(372, 640);
            tabWebhook.TabIndex = 3;
            tabWebhook.Text = "Webhook 通知";
            tabWebhook.UseVisualStyleBackColor = true;
            // 
            // EmojiConfig
            // 
            EmojiConfig.Location = new Point(7, 94);
            EmojiConfig.Name = "EmojiConfig";
            EmojiConfig.Size = new Size(100, 23);
            EmojiConfig.TabIndex = 46;
            EmojiConfig.Text = "表情配置";
            EmojiConfig.UseVisualStyleBackColor = true;
            EmojiConfig.Click += EmojiConfig_Click;
            // 
            // labelWebhooks
            // 
            labelWebhooks.AutoSize = true;
            labelWebhooks.Location = new Point(10, 618);
            labelWebhooks.Name = "labelWebhooks";
            labelWebhooks.Size = new Size(85, 15);
            labelWebhooks.TabIndex = 44;
            labelWebhooks.Text = "Webhook 状态：";
            // 
            // label21
            // 
            label21.AutoSize = true;
            label21.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label21.Location = new Point(8, 3);
            label21.Name = "label21";
            label21.Size = new Size(100, 15);
            label21.TabIndex = 43;
            label21.Text = "通知常规设置";
            // 
            // DiscordMessageContent
            // 
            DiscordMessageContent.Location = new Point(7, 40);
            DiscordMessageContent.Name = "DiscordMessageContent";
            DiscordMessageContent.Size = new Size(357, 23);
            DiscordMessageContent.TabIndex = 42;
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(7, 22);
            label14.Name = "label14";
            label14.Size = new Size(285, 15);
            label14.TabIndex = 41;
            label14.Text = "通知正文（可用 <@用户数字ID> 提及）";
            // 
            // btnTestWebHook
            // 
            btnTestWebHook.Location = new Point(260, 614);
            btnTestWebHook.Name = "btnTestWebHook";
            btnTestWebHook.Size = new Size(104, 23);
            btnTestWebHook.TabIndex = 22;
            btnTestWebHook.Text = "测试 Webhook";
            btnTestWebHook.UseVisualStyleBackColor = true;
            btnTestWebHook.Click += BtnTestWebHook_Click;
            // 
            // denToggle
            // 
            denToggle.AutoSize = true;
            denToggle.Checked = true;
            denToggle.CheckState = CheckState.Checked;
            denToggle.Location = new Point(8, 249);
            denToggle.Name = "denToggle";
            denToggle.Size = new Size(79, 19);
            denToggle.TabIndex = 21;
            denToggle.Text = "显示坑位编号";
            denToggle.UseVisualStyleBackColor = true;
            // 
            // LocationSettings_label
            // 
            LocationSettings_label.AutoSize = true;
            LocationSettings_label.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            LocationSettings_label.Location = new Point(7, 231);
            LocationSettings_label.Name = "LocationSettings_label";
            LocationSettings_label.Size = new Size(103, 15);
            LocationSettings_label.TabIndex = 20;
            LocationSettings_label.Text = "位置设置";
            // 
            // IVstyle
            // 
            IVstyle.FormattingEnabled = true;
            IVstyle.Items.AddRange(new object[] { "表情", "高亮数字", "数字" });
            IVstyle.Location = new Point(8, 196);
            IVstyle.Name = "IVstyle";
            IVstyle.Size = new Size(121, 23);
            IVstyle.TabIndex = 8;
            // 
            // IVverbose
            // 
            IVverbose.AutoSize = true;
            IVverbose.Location = new Point(8, 147);
            IVverbose.Name = "IVverbose";
            IVverbose.Size = new Size(85, 19);
            IVverbose.TabIndex = 5;
            IVverbose.Text = "显示详细个体值";
            IVverbose.UseVisualStyleBackColor = true;
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label19.Location = new Point(7, 178);
            label19.Name = "label19";
            label19.Size = new Size(48, 15);
            label19.TabIndex = 2;
            label19.Text = "个体值显示方式";
            // 
            // label18
            // 
            label18.AutoSize = true;
            label18.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label18.Location = new Point(7, 129);
            label18.Name = "label18";
            label18.Size = new Size(68, 15);
            label18.TabIndex = 1;
            label18.Text = "个体值设置";
            // 
            // EnableEmoji
            // 
            EnableEmoji.AutoSize = true;
            EnableEmoji.Checked = true;
            EnableEmoji.CheckState = CheckState.Checked;
            EnableEmoji.Location = new Point(8, 69);
            EnableEmoji.Name = "EnableEmoji";
            EnableEmoji.Size = new Size(94, 19);
            EnableEmoji.TabIndex = 0;
            EnableEmoji.Text = "启用表情";
            EnableEmoji.UseVisualStyleBackColor = true;
            // 
            // tabExperimental
            // 
            tabExperimental.Controls.Add(InstanceName);
            tabExperimental.Controls.Add(label17);
            tabExperimental.Controls.Add(label13);
            tabExperimental.Controls.Add(ExperimentalView);
            tabExperimental.Location = new Point(4, 24);
            tabExperimental.Name = "tabExperimental";
            tabExperimental.Padding = new Padding(3);
            tabExperimental.Size = new Size(372, 640);
            tabExperimental.TabIndex = 2;
            tabExperimental.Text = "实验性";
            tabExperimental.UseVisualStyleBackColor = true;
            // 
            // InstanceName
            // 
            InstanceName.Location = new Point(6, 63);
            InstanceName.Name = "InstanceName";
            InstanceName.Size = new Size(358, 23);
            InstanceName.TabIndex = 42;
            // 
            // label17
            // 
            label17.AutoSize = true;
            label17.Location = new Point(6, 45);
            label17.Name = "label17";
            label17.Size = new Size(89, 15);
            label17.TabIndex = 41;
            label17.Text = "实例名称：";
            // 
            // tabAbout
            // 
            tabAbout.Controls.Add(linkLabel1);
            tabAbout.Controls.Add(labelAppName);
            tabAbout.Controls.Add(picAppIcon);
            tabAbout.Controls.Add(labelAppVersion);
            tabAbout.Location = new Point(4, 24);
            tabAbout.Name = "tabAbout";
            tabAbout.Padding = new Padding(3);
            tabAbout.Size = new Size(372, 640);
            tabAbout.TabIndex = 4;
            tabAbout.Text = "关于";
            tabAbout.UseVisualStyleBackColor = true;
            // 
            // linkLabel1
            // 
            linkLabel1.AutoSize = true;
            linkLabel1.Location = new Point(55, 273);
            linkLabel1.Name = "linkLabel1";
            linkLabel1.Size = new Size(253, 15);
            linkLabel1.TabIndex = 4;
            linkLabel1.TabStop = true;
            linkLabel1.Text = "https://github.com/LegoFigure11/RaidCrawler";
            linkLabel1.LinkClicked += LinkLabel1_LinkClicked;
            // 
            // labelAppName
            // 
            labelAppName.AutoSize = true;
            labelAppName.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            labelAppName.Location = new Point(135, 164);
            labelAppName.Name = "labelAppName";
            labelAppName.Size = new Size(119, 25);
            labelAppName.TabIndex = 2;
            labelAppName.Text = "RaidCrawler";
            labelAppName.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // picAppIcon
            // 
            picAppIcon.Image = (Image)resources.GetObject("picAppIcon.Image");
            picAppIcon.Location = new Point(106, 159);
            picAppIcon.Name = "picAppIcon";
            picAppIcon.Size = new Size(32, 32);
            picAppIcon.TabIndex = 1;
            picAppIcon.TabStop = false;
            // 
            // labelAppVersion
            // 
            labelAppVersion.AutoSize = true;
            labelAppVersion.Location = new Point(155, 206);
            labelAppVersion.Name = "labelAppVersion";
            labelAppVersion.Size = new Size(78, 15);
            labelAppVersion.TabIndex = 0;
            labelAppVersion.Text = "v0.0.0-000000";
            labelAppVersion.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // BlueberryScanCheck
            // 
            BlueberryScanCheck.AutoSize = true;
            BlueberryScanCheck.Checked = true;
            BlueberryScanCheck.CheckState = CheckState.Checked;
            BlueberryScanCheck.Location = new Point(6, 72);
            BlueberryScanCheck.Name = "BlueberryScanCheck";
            BlueberryScanCheck.Size = new Size(76, 19);
            BlueberryScanCheck.TabIndex = 114;
            BlueberryScanCheck.Text = "蓝莓学园";
            BlueberryScanCheck.UseVisualStyleBackColor = true;
            // 
            // ConfigWindow
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(380, 668);
            Controls.Add(tabControl1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "ConfigWindow";
            StartPosition = FormStartPosition.CenterParent;
            Text = "RaidCrawler 设置";
            FormClosing += Config_Closing;
            ((System.ComponentModel.ISupportInitialize)SystemDDownPresses).EndInit();
            ((System.ComponentModel.ISupportInitialize)NavigateToSettings).EndInit();
            ((System.ComponentModel.ISupportInitialize)OpenSettings).EndInit();
            ((System.ComponentModel.ISupportInitialize)OpenHome).EndInit();
            ((System.ComponentModel.ISupportInitialize)Hold).EndInit();
            ((System.ComponentModel.ISupportInitialize)Submenu).EndInit();
            ((System.ComponentModel.ISupportInitialize)DateChange).EndInit();
            ((System.ComponentModel.ISupportInitialize)ReturnHome).EndInit();
            ((System.ComponentModel.ISupportInitialize)ReturnGame).EndInit();
            ((System.ComponentModel.ISupportInitialize)DaysToSkip).EndInit();
            tabControl1.ResumeLayout(false);
            tabGeneral.ResumeLayout(false);
            tabGeneral.PerformLayout();
            LocationGroup.ResumeLayout(false);
            LocationGroup.PerformLayout();
            tabMatch.ResumeLayout(false);
            tabMatch.PerformLayout();
            tabAdvanceDate.ResumeLayout(false);
            tabAdvanceDate.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)ExtraOverworldWait).EndInit();
            ((System.ComponentModel.ISupportInitialize)RelaunchDelay).EndInit();
            ((System.ComponentModel.ISupportInitialize)SystemReset).EndInit();
            ((System.ComponentModel.ISupportInitialize)SaveGameDelay).EndInit();
            ((System.ComponentModel.ISupportInitialize)SystemOvershoot).EndInit();
            ((System.ComponentModel.ISupportInitialize)BaseDelay).EndInit();
            tabWebhook.ResumeLayout(false);
            tabWebhook.PerformLayout();
            tabExperimental.ResumeLayout(false);
            tabExperimental.PerformLayout();
            tabAbout.ResumeLayout(false);
            tabAbout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)picAppIcon).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private CheckBox FocusWindow;
        private CheckBox EnableAlert;
        private CheckBox PlayTone;
        private Label LabelMatchFound;
        private TextBox AlertMessage;
        private Label L_AdvanceDate;
        private Label L_BaseDelay;
        private NumericUpDown SystemDDownPresses;
        private Label L_DdownInput;
        private NumericUpDown NavigateToSettings;
        private NumericUpDown OpenSettings;
        private NumericUpDown OpenHome;
        private Label L_OpenHOME;
        private Label L_NavigateSettings;
        private Label L_OpenSettingsDelay;
        private Label L_ScrollSystem;
        private NumericUpDown Hold;
        private Label L_SubmenuDelay;
        private NumericUpDown Submenu;
        private Label L_DateChangeDelay;
        private NumericUpDown DateChange;
        private Label L_ReturnHomeDelay;
        private NumericUpDown ReturnHome;
        private Label L_ReOpenGameDelay;
        private NumericUpDown ReturnGame;
        private Label L_DaysToSkip;
        private NumericUpDown DaysToSkip;
        private CheckBox UseTouch;
        private TextBox DiscordWebhook;
        private CheckBox EnableDiscordNotifications;
        private Label label13;
        private CheckBox ExperimentalView;
        private TabControl tabControl1;
        private TabPage tabMatch;
        private TabPage tabAdvanceDate;
        private TabPage tabExperimental;
        private Label L_OvershootHold;
        private NumericUpDown SystemOvershoot;
        private CheckBox UseOvershoot;
        private TextBox InstanceName;
        private Label label17;
        private TabPage tabWebhook;
        private CheckBox EnableEmoji;
        private Label label19;
        private Label label18;
        private ComboBox IVstyle;
        private CheckBox IVverbose;
        private CheckBox denToggle;
        private Label LocationSettings_label;
        private Button btnTestWebHook;
        private Label label21;
        private TextBox DiscordMessageContent;
        private Label label14;
        private Label labelWebhooks;
        private TabPage tabAbout;
        private Label labelAppVersion;
        private LinkLabel linkLabel1;
        private Label labelAppName;
        private PictureBox picAppIcon;
        private TabPage tabGeneral;
        private Label label23;
        private Label LabelEventProgress;
        private ComboBox EventProgress;
        private Label LabelGame;
        private ComboBox Game;
        private Label LabelStoryProgress;
        private ComboBox StoryProgress;
        private Button EmojiConfig;
        private ComboBox Protocol_dropdown;
        private Label Protocol_label;
        private NumericUpDown BaseDelay;
        private NumericUpDown SaveGameDelay;
        private Label L_SaveGame;
        private CheckBox SaveGame;
        private CheckBox DodgeSystemUpdate;
        private CheckBox UseSetStick;
        private CheckBox PaldeaScanCheck;
        private GroupBox LocationGroup;
        private CheckBox KitakamiScanCheck;
        private NumericUpDown SystemReset;
        private Label L_SystemReset;
        private NumericUpDown RelaunchDelay;
        private Label L_RelaunchDelay;
        private NumericUpDown ExtraOverworldWait;
        private Label L_ExtraOverworldWait;
        private CheckBox UseMapTrick;
        private CheckBox ZyroMethod;
        private CheckBox BlueberryScanCheck;
    }
}
