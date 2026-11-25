using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("Disk Migration Kit")]
[assembly: AssemblyDescription("Verifiable dynamic-disk to basic-disk migration launcher")]
[assembly: AssemblyCompany("E-Disk Migration Kit")]
[assembly: AssemblyProduct("Disk Migration Kit")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace EDiskMigration
{
    internal static class Lang
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EDiskMigrationKit", "settings.ini");

        public static bool Chinese { get; private set; }

        public static void Load()
        {
            Chinese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
            try
            {
                if (File.Exists(SettingsPath))
                {
                    foreach (string line in File.ReadAllLines(SettingsPath))
                    {
                        if (line.StartsWith("language=", StringComparison.OrdinalIgnoreCase))
                        {
                            Chinese = line.Substring("language=".Length).Trim() != "en";
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        public static void Save(bool chinese)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, "language=" + (chinese ? "zh" : "en") + Environment.NewLine);
            }
            catch { }
        }

        public static string T(string zh, string en)
        {
            return Chinese ? zh : en;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Lang.Load();
            if (args != null && args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                return SelfTest();
            }
            if (args != null && args.Any(a => string.Equals(a, "--uicheck", StringComparison.OrdinalIgnoreCase)))
            {
                return UiCheck();
            }

            if (!IsAdministrator())
            {
                RelaunchElevated();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        internal static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static void RelaunchElevated()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Lang.T("需要管理员权限才能执行磁盘操作。\r\n\r\n", "Administrator permission is required for disk operations.\r\n\r\n") + ex.Message,
                    Lang.T("管理员权限", "Administrator"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        internal static string FindScriptsDir(string baseDir)
        {
            string candidate = Path.Combine(baseDir, "scripts");
            if (File.Exists(Path.Combine(candidate, "00-Preflight.ps1")))
            {
                return candidate;
            }
            string parent = Path.GetDirectoryName(baseDir);
            if (!string.IsNullOrEmpty(parent))
            {
                candidate = Path.Combine(parent, "scripts");
                if (File.Exists(Path.Combine(candidate, "00-Preflight.ps1")))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static int UiCheck()
        {
            string file = Path.Combine(Path.GetTempPath(), "EDiskMigration-uicheck.txt");
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string title;
                using (MainForm form = new MainForm())
                {
                    // Constructing the form exercises disk/volume enumeration and layout.
                    title = form.Text;
                }
                File.WriteAllText(file, "PASS\r\n" + title + "\r\n" + DateTime.Now.ToString("o") + "\r\n", new UTF8Encoding(true));
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file, "FAIL\r\n" + ex.ToString(), new UTF8Encoding(true)); } catch { }
                return 1;
            }
        }

        private static int SelfTest()
        {
            StringBuilder sb = new StringBuilder();            bool ok = true;
            string baseDir = Path.GetDirectoryName(Application.ExecutablePath);
            string scripts = FindScriptsDir(baseDir);
            string docs = Path.Combine(baseDir, "docs");
            if (!Directory.Exists(docs) && !string.IsNullOrEmpty(Path.GetDirectoryName(baseDir)))
            {
                docs = Path.Combine(Path.GetDirectoryName(baseDir), "docs");
            }

            sb.AppendLine("E: Disk Migration Kit self-test");
            sb.AppendLine("Time: " + DateTime.Now.ToString("o"));
            sb.AppendLine("Executable: " + Application.ExecutablePath);
            sb.AppendLine("Base directory: " + baseDir);
            sb.AppendLine("Administrator: " + IsAdministrator());
            sb.AppendLine("PowerShell 5.1: " + File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")));
            sb.AppendLine("Scripts directory: " + (scripts == null ? "MISSING" : scripts));
            sb.AppendLine("Docs directory: " + (Directory.Exists(docs) ? docs : "MISSING"));
            if (scripts == null)
            {
                ok = false;
            }
            else
            {
                foreach (string name in new string[] {
                    "00-Preflight.ps1", "01-CopyData.ps1", "02-CompareTrees.ps1",
                    "03-RebuildBasicVolume.ps1", "04-RestoreData.ps1", "05-PostCheck.ps1",
                    "Common.ps1" })
                {
                    bool exists = File.Exists(Path.Combine(scripts, name));
                    sb.AppendLine("  " + name + ": " + (exists ? "OK" : "MISSING"));
                    if (!exists) { ok = false; }
                }
            }
            sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
            string file = Path.Combine(Path.GetTempPath(), "EDiskMigration-selftest.txt");
            try { File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true)); } catch { }
            return ok ? 0 : 1;
        }
    }

    internal sealed class DiskInfo
    {
        public int Index;
        public string Model;
        public string Serial;
        public string Firmware;
        public ulong Size;
        public bool IsBoot;

        public override string ToString()
        {
            return string.Format(Lang.T(
                "磁盘 {0} | {1} | SN {2} | {3}{4}",
                "Disk {0} | {1} | SN {2} | {3}{4}"),
                Index, Model, Serial, FormatSize(Size), IsBoot ? "  [BOOT]" : "");
        }

        public static string FormatSize(ulong bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            return string.Format("{0:N1} GiB", gb);
        }
    }

    internal sealed class VolumeInfo
    {
        public string Letter;
        public string Label;
        public string FileSystem;
        public long Size;
        public long Free;

        public override string ToString()
        {
            return string.Format(
                "{0} | {1} | {2} | {3} free",
                Letter, string.IsNullOrEmpty(Label) ? "(no label)" : Label,
                FileSystem, DiskInfo.FormatSize((ulong)Math.Max(0, Free)));
        }
    }

    internal sealed class ConfirmRebuildForm : Form
    {
        private readonly TextBox serialBox;
        private readonly CheckBox acknowledge;
        private readonly Button okButton;
        private readonly string expectedSerial;

        public ConfirmRebuildForm(DiskInfo disk, string sourceLetter)
        {
            expectedSerial = disk.Serial;
            Text = Lang.T("确认清空目标盘", "Confirm target disk wipe");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(600, 360);
            Font = new Font("Microsoft YaHei UI", 9F);

            Label title = new Label();
            title.Text = Lang.T("危险操作：目标盘将被清空", "DANGER: the target disk will be erased");
            title.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            title.ForeColor = Color.DarkRed;
            title.SetBounds(20, 16, 560, 32);

            Label detail = new Label();
            detail.Text = string.Format(Lang.T(
                "目标磁盘：磁盘 {0}\r\n型号：{1}\r\n序列号：{2}\r\n固件：{3}\r\n容量：{4}\r\n源卷：{5}\r\n\r\n" +
                "将执行：dismount → diskpart clean → 初始化 MBR → 建分区 → 格式化 NTFS。\r\n" +
                "此操作不可撤销。请确认目标盘不是系统盘，并且暂存副本已通过 0 mismatch 校验。",
                "Target disk: disk {0}\r\nModel: {1}\r\nSerial: {2}\r\nFirmware: {3}\r\nSize: {4}\r\nSource volume: {5}\r\n\r\n" +
                "Will run: dismount -> diskpart clean -> initialize MBR -> create partition -> format NTFS.\r\n" +
                "This cannot be undone. Confirm the target is not the system disk and the staging copy passed 0-mismatch verification."),
                disk.Index, disk.Model, disk.Serial, disk.Firmware,
                DiskInfo.FormatSize(disk.Size), sourceLetter);
            detail.SetBounds(20, 56, 560, 180);

            Label typeLabel = new Label();
            typeLabel.Text = Lang.T("请手动输入上面的序列号：", "Type the serial number above:");
            typeLabel.SetBounds(20, 246, 220, 24);
            serialBox = new TextBox();
            serialBox.SetBounds(240, 242, 340, 26);

            acknowledge = new CheckBox();
            acknowledge.Text = Lang.T(
                "我确认目标盘数据将被清空，并且已有可恢复的暂存副本。",
                "I confirm the target will be erased and a recoverable staging copy exists.");
            acknowledge.SetBounds(20, 282, 560, 26);

            okButton = new Button();
            okButton.Text = Lang.T("清空并重建", "Erase and rebuild");
            okButton.SetBounds(360, 318, 110, 30);
            okButton.Enabled = false;
            okButton.Click += OkButtonClick;

            Button cancel = new Button();
            cancel.Text = Lang.T("取消", "Cancel");
            cancel.SetBounds(480, 318, 80, 30);
            cancel.DialogResult = DialogResult.Cancel;

            serialBox.TextChanged += delegate { UpdateOk(); };
            acknowledge.CheckedChanged += delegate { UpdateOk(); };

            Controls.Add(title);
            Controls.Add(detail);
            Controls.Add(typeLabel);
            Controls.Add(serialBox);
            Controls.Add(acknowledge);
            Controls.Add(okButton);
            Controls.Add(cancel);
            AcceptButton = okButton;
            CancelButton = cancel;
        }

        private void UpdateOk()
        {
            okButton.Enabled = acknowledge.Checked &&
                string.Equals(serialBox.Text.Trim(), expectedSerial, StringComparison.Ordinal);
        }

        private void OkButtonClick(object sender, EventArgs e)
        {
            if (!string.Equals(serialBox.Text.Trim(), expectedSerial, StringComparison.Ordinal))
            {
                MessageBox.Show(Lang.T("序列号不匹配。", "Serial number mismatch."),
                    Lang.T("确认失败", "Confirmation failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly string appBase;
        private readonly string scriptsDir;
        private readonly string docsDir;
        private readonly string workDir;
        private readonly string configPath;

        private ComboBox diskCombo;
        private ComboBox volumeCombo;
        private TextBox stagingBox;
        private TextBox recoveryBox;
        private Button recoveryBrowseButton;
        private CheckBox sameRootCheck;
        private Label targetVolumeLabel;
        private TextBox logBox;
        private ProgressBar progressBar;
        private Label statusLabel;

        private Button preflightButton;
        private Button copyButton;
        private Button verifySourceButton;
        private Button rebuildButton;
        private Button restoreButton;
        private Button verifyTargetButton;
        private Button postcheckButton;
        private Button cancelButton;

        private Process currentProcess;
        private string currentStep;
        private bool busy;
        private bool preflightOk;
        private bool copyOk;
        private bool verifySourceOk;
        private bool rebuildOk;
        private bool restoreOk;
        private bool verifyTargetOk;


        public MainForm()
        {
            appBase = Path.GetDirectoryName(Application.ExecutablePath);
            scriptsDir = Program.FindScriptsDir(appBase);
            docsDir = FindDocsDir();
            workDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EDiskMigrationKit");
            Directory.CreateDirectory(workDir);
            configPath = Path.Combine(workDir, "config.psd1");

            BuildUi();
            LoadDisks();
            LoadVolumes();
            UpdateButtons();
            AppendLog(Lang.T("Disk Migration Kit 1.0", "Disk Migration Kit 1.0"));
            AppendLog(Lang.T("工作目录: ", "Working folder: ") + workDir);
            if (scriptsDir == null)
            {
                AppendLog(Lang.T("错误: 找不到 scripts 目录。请完整解压压缩包。", "ERROR: scripts directory not found. Extract the full package."));
                MessageBox.Show(Lang.T("找不到 scripts 目录。请完整解压后再运行。", "scripts directory not found. Extract the full package and retry."),
                    Lang.T("文件缺失", "Missing files"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                AppendLog(Lang.T("脚本目录: ", "Scripts: ") + scriptsDir);
            }
            AppendLog(Lang.T("先做预检（只读），不要跳步。", "Start with the read-only preflight. Do not skip steps."));
        }

        private void BuildUi()
        {
            Text = Lang.T("Disk Migration Kit - 动态盘转基本盘", "Disk Migration Kit - Dynamic to Basic");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(920, 680);
            Size = new Size(1120, 800);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AutoScaleMode = AutoScaleMode.Dpi;

            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem languageMenu = new ToolStripMenuItem("Language / 语言");
            ToolStripMenuItem zhItem = new ToolStripMenuItem("中文");
            ToolStripMenuItem enItem = new ToolStripMenuItem("English");
            zhItem.Checked = Lang.Chinese;
            enItem.Checked = !Lang.Chinese;
            zhItem.Click += delegate { SwitchLanguage(true); };
            enItem.Click += delegate { SwitchLanguage(false); };
            languageMenu.DropDownItems.Add(zhItem);
            languageMenu.DropDownItems.Add(enItem);
            menu.Items.Add(languageMenu);
            MainMenuStrip = menu;

            TableLayoutPanel main = new TableLayoutPanel();
            main.Dock = DockStyle.Fill;
            main.ColumnCount = 1;
            main.RowCount = 6;
            main.Padding = new Padding(10);
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 224));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            Label warning = new Label();
            warning.Dock = DockStyle.Fill;
            warning.BackColor = Color.FromArgb(255, 235, 235);
            warning.ForeColor = Color.DarkRed;
            warning.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            warning.Padding = new Padding(10);
            warning.TextAlign = ContentAlignment.MiddleLeft;
            warning.Text = Lang.T(
                "警告：本工具会清空目标物理磁盘。仅用于非系统盘/非启动盘。" +
                "请先备份，并在虚拟机或可丢弃的磁盘上验证。不要跳步，不要在唯一数据上试。",
                "WARNING: This tool erases the target physical disk. Use only on a non-system/non-boot disk. " +
                "Back up first and test on a VM or disposable disk. Do not skip steps.");
            main.Controls.Add(warning, 0, 0);

            GroupBox configGroup = new GroupBox();
            configGroup.Text = Lang.T("1. 选择目标盘和路径", "1. Target disk and folders");
            configGroup.Dock = DockStyle.Fill;
            TableLayoutPanel cfg = new TableLayoutPanel();
            cfg.Dock = DockStyle.Fill;
            cfg.ColumnCount = 3;
            cfg.RowCount = 5;
            cfg.Padding = new Padding(8);
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cfg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            cfg.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            Label diskLabel = new Label();
            diskLabel.Text = Lang.T("目标磁盘", "Target disk");
            diskLabel.TextAlign = ContentAlignment.MiddleLeft;
            diskLabel.Dock = DockStyle.Fill;
            diskCombo = new ComboBox();
            diskCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            diskCombo.Dock = DockStyle.Fill;
            cfg.Controls.Add(diskLabel, 0, 0);
            cfg.Controls.Add(diskCombo, 1, 0);
            cfg.SetColumnSpan(diskCombo, 2);

            Label sourceLabel = new Label();
            sourceLabel.Text = Lang.T("源卷", "Source volume");
            sourceLabel.TextAlign = ContentAlignment.MiddleLeft;
            sourceLabel.Dock = DockStyle.Fill;
            volumeCombo = new ComboBox();
            volumeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            volumeCombo.Dock = DockStyle.Fill;
            volumeCombo.SelectedIndexChanged += delegate { UpdateTargetVolumeLabel(); };
            targetVolumeLabel = new Label();
            targetVolumeLabel.Text = Lang.T("目标卷: -", "Target: -");
            targetVolumeLabel.TextAlign = ContentAlignment.MiddleLeft;
            targetVolumeLabel.Dock = DockStyle.Fill;
            cfg.Controls.Add(sourceLabel, 0, 1);
            cfg.Controls.Add(volumeCombo, 1, 1);
            cfg.Controls.Add(targetVolumeLabel, 2, 1);

            Label stagingLabel = new Label();
            stagingLabel.Text = Lang.T("工作目录", "Working folder");
            stagingLabel.TextAlign = ContentAlignment.MiddleLeft;
            stagingLabel.Dock = DockStyle.Fill;
            stagingBox = new TextBox();
            stagingBox.Dock = DockStyle.Fill;
            stagingBox.TextChanged += delegate { SyncSameRoot(); };
            Button browseStaging = new Button();
            browseStaging.Text = Lang.T("浏览...", "Browse...");
            browseStaging.Dock = DockStyle.Fill;
            browseStaging.Click += delegate { BrowseFolder(stagingBox); };
            cfg.Controls.Add(stagingLabel, 0, 2);
            cfg.Controls.Add(stagingBox, 1, 2);
            cfg.Controls.Add(browseStaging, 2, 2);

            Label recoveryLabel = new Label();
            recoveryLabel.Text = Lang.T("恢复目录", "Recovery folder");
            recoveryLabel.TextAlign = ContentAlignment.MiddleLeft;
            recoveryLabel.Dock = DockStyle.Fill;
            recoveryBox = new TextBox();
            recoveryBox.Dock = DockStyle.Fill;
            recoveryBrowseButton = new Button();
            recoveryBrowseButton.Text = Lang.T("浏览...", "Browse...");
            recoveryBrowseButton.Dock = DockStyle.Fill;
            recoveryBrowseButton.Click += delegate { BrowseFolder(recoveryBox); };
            cfg.Controls.Add(recoveryLabel, 0, 3);
            cfg.Controls.Add(recoveryBox, 1, 3);
            cfg.Controls.Add(recoveryBrowseButton, 2, 3);

            sameRootCheck = new CheckBox();
            sameRootCheck.Text = Lang.T(
                "暂存和恢复使用同一个根目录（适合只有两块盘）",
                "Use one root folder for staging and recovery (two-disk setups)");
            sameRootCheck.Dock = DockStyle.Fill;
            sameRootCheck.Checked = true;
            sameRootCheck.CheckedChanged += delegate { SyncSameRoot(); };
            cfg.Controls.Add(sameRootCheck, 0, 4);
            cfg.SetColumnSpan(sameRootCheck, 3);

            configGroup.Controls.Add(cfg);
            main.Controls.Add(configGroup, 0, 1);
            SyncSameRoot();

            GroupBox stepsGroup = new GroupBox();
            stepsGroup.Text = Lang.T("2. 按顺序执行（不可跳步）", "2. Run in order (do not skip)");
            stepsGroup.Dock = DockStyle.Fill;
            FlowLayoutPanel steps = new FlowLayoutPanel();
            steps.Dock = DockStyle.Fill;
            steps.Padding = new Padding(6);
            steps.WrapContents = true;
            preflightButton = CreateStepButton(Lang.T("1. 只读预检", "1. Read-only preflight"), StepPreflight);
            copyButton = CreateStepButton(Lang.T("2. 复制到暂存", "2. Copy to staging"), StepCopy);
            verifySourceButton = CreateStepButton(Lang.T("3. 校验源→暂存", "3. Verify source -> staging"), StepVerifySource);
            rebuildButton = CreateStepButton(Lang.T("4. 重建基本盘", "4. Rebuild as basic"), StepRebuild);
            rebuildButton.ForeColor = Color.DarkRed;
            restoreButton = CreateStepButton(Lang.T("5. 回拷到新盘", "5. Restore to new disk"), StepRestore);
            verifyTargetButton = CreateStepButton(Lang.T("6. 校验暂存→新盘", "6. Verify staging -> new disk"), StepVerifyTarget);
            postcheckButton = CreateStepButton(Lang.T("7. 最终验收", "7. Final post-check"), StepPostcheck);
            cancelButton = CreateStepButton(Lang.T("停止当前步骤", "Stop current step"), StepCancel);
            cancelButton.ForeColor = Color.DarkOrange;
            steps.Controls.Add(preflightButton);
            steps.Controls.Add(copyButton);
            steps.Controls.Add(verifySourceButton);
            steps.Controls.Add(rebuildButton);
            steps.Controls.Add(restoreButton);
            steps.Controls.Add(verifyTargetButton);
            steps.Controls.Add(postcheckButton);
            steps.Controls.Add(cancelButton);
            stepsGroup.Controls.Add(steps);
            main.Controls.Add(stepsGroup, 0, 2);

            TableLayoutPanel progressPanel = new TableLayoutPanel();
            progressPanel.Dock = DockStyle.Fill;
            progressPanel.ColumnCount = 2;
            progressPanel.RowCount = 1;
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusLabel = new Label();
            statusLabel.Text = Lang.T("就绪。请选择目标盘、源卷和目录。", "Ready. Select the target disk, source volume and folders.");
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Dock = DockStyle.Fill;
            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Style = ProgressBarStyle.Blocks;
            progressPanel.Controls.Add(statusLabel, 0, 0);
            progressPanel.Controls.Add(progressBar, 1, 0);
            main.Controls.Add(progressPanel, 0, 3);

            GroupBox logGroup = new GroupBox();
            logGroup.Text = Lang.T("运行日志", "Run log");
            logGroup.Dock = DockStyle.Fill;
            logBox = new TextBox();
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.WordWrap = false;
            logBox.BackColor = Color.FromArgb(24, 24, 24);
            logBox.ForeColor = Color.Gainsboro;
            logBox.Font = new Font("Consolas", 9F);
            logGroup.Controls.Add(logBox);
            main.Controls.Add(logGroup, 0, 4);

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.FlowDirection = FlowDirection.LeftToRight;
            Button docsButton = new Button();
            docsButton.Text = Lang.T("打开运行手册", "Open runbook");
            docsButton.Width = 130;
            docsButton.Click += delegate { OpenDocs(); };
            Button workButton = new Button();
            workButton.Text = Lang.T("打开工作目录", "Open work folder");
            workButton.Width = 130;
            workButton.Click += delegate { OpenPath(workDir); };
            Button helpButton = new Button();
            helpButton.Text = Lang.T("安全说明", "Safety");
            helpButton.Width = 100;
            helpButton.Click += delegate { ShowSafetyHelp(); };
            bottom.Controls.Add(docsButton);
            bottom.Controls.Add(workButton);
            bottom.Controls.Add(helpButton);
            main.Controls.Add(bottom, 0, 5);

            Controls.Add(menu);
            Controls.Add(main);
            FormClosing += MainFormClosing;
        }

        private void SwitchLanguage(bool chinese)
        {
            if (Lang.Chinese == chinese) return;
            Lang.Save(chinese);
            Application.Restart();
        }

        private void SyncSameRoot()
        {
            if (sameRootCheck == null || recoveryBox == null || recoveryBrowseButton == null) return;
            if (sameRootCheck.Checked)
            {
                recoveryBox.Text = stagingBox.Text;
                recoveryBox.ReadOnly = true;
                recoveryBrowseButton.Enabled = false;
            }
            else
            {
                recoveryBox.ReadOnly = false;
                recoveryBrowseButton.Enabled = true;
            }
        }

        private Button CreateStepButton(string text, EventHandler handler)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = 152;
            b.Height = 34;
            b.Margin = new Padding(3);
            b.Click += handler;
            return b;
        }

        private string FindDocsDir()
        {
            string candidate = Path.Combine(appBase, "docs");
            if (Directory.Exists(candidate)) return candidate;
            string parent = Path.GetDirectoryName(appBase);
            if (!string.IsNullOrEmpty(parent))
            {
                candidate = Path.Combine(parent, "docs");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        private void LoadDisks()
        {
            diskCombo.Items.Clear();
            HashSet<int> bootDisks = new HashSet<int>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskPartition"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        try
                        {
                            bool boot = Convert.ToBoolean(mo["Bootable"]) || Convert.ToBoolean(mo["BootPartition"]);
                            if (boot) bootDisks.Add(Convert.ToInt32(mo["DiskIndex"]));
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch { }

            List<DiskInfo> disks = new List<DiskInfo>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        try
                        {
                            DiskInfo d = new DiskInfo();
                            d.Index = Convert.ToInt32(mo["Index"]);
                            d.Model = Convert.ToString(mo["Model"]).Trim();
                            d.Serial = Convert.ToString(mo["SerialNumber"]).Trim();
                            d.Firmware = Convert.ToString(mo["FirmwareRevision"]).Trim();
                            d.Size = Convert.ToUInt64(mo["Size"]);
                            d.IsBoot = bootDisks.Contains(d.Index);
                            disks.Add(d);
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("枚举磁盘失败: ", "Failed to enumerate disks: ") + ex.Message,
                    Lang.T("错误", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            foreach (DiskInfo d in disks.OrderBy(x => x.Index))
            {
                diskCombo.Items.Add(d);
            }
            if (diskCombo.Items.Count > 0)
            {
                int select = 0;
                for (int i = 0; i < diskCombo.Items.Count; i++)
                {
                    DiskInfo d = diskCombo.Items[i] as DiskInfo;
                    if (d != null && !d.IsBoot) { select = i; break; }
                }
                diskCombo.SelectedIndex = select;
            }
        }

        private void LoadVolumes()
        {
            volumeCombo.Items.Clear();
            List<VolumeInfo> volumes = new List<VolumeInfo>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    string fs = string.Empty;
                    try { fs = drive.DriveFormat; } catch { }
                    if (!string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase)) continue;
                    VolumeInfo v = new VolumeInfo();
                    v.Letter = drive.Name.Substring(0, 2);
                    try { v.Label = drive.VolumeLabel; } catch { v.Label = string.Empty; }
                    v.FileSystem = fs;
                    v.Size = drive.TotalSize;
                    v.Free = drive.AvailableFreeSpace;
                    volumes.Add(v);
                }
                catch { }
            }
            foreach (VolumeInfo v in volumes.OrderBy(x => x.Letter))
            {
                volumeCombo.Items.Add(v);
            }
            if (volumeCombo.Items.Count > 0)
            {
                int select = 0;
                for (int i = 0; i < volumeCombo.Items.Count; i++)
                {
                    VolumeInfo v = volumeCombo.Items[i] as VolumeInfo;
                    if (v != null && string.Equals(v.Letter, "E:", StringComparison.OrdinalIgnoreCase))
                    {
                        select = i; break;
                    }
                }
                volumeCombo.SelectedIndex = select;
            }
            UpdateTargetVolumeLabel();
        }

        private void UpdateTargetVolumeLabel()
        {
            VolumeInfo v = volumeCombo.SelectedItem as VolumeInfo;
            targetVolumeLabel.Text = Lang.T("目标卷: ", "Target: ") + (v == null ? "-" : v.Letter);
        }

        private void BrowseFolder(TextBox box)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Lang.T("选择目录", "Select a folder");
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(box.Text)) dialog.SelectedPath = box.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    box.Text = dialog.SelectedPath;
                }
            }
        }

        private static string PsQuote(string value)
        {
            if (value == null) value = string.Empty;
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string NormalizeDir(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return full;
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private bool BuildConfig()
        {
            DiskInfo disk = diskCombo.SelectedItem as DiskInfo;
            VolumeInfo volume = volumeCombo.SelectedItem as VolumeInfo;
            if (disk == null)
            {
                MessageBox.Show(Lang.T("请选择目标磁盘。", "Select the target disk."),
                    Lang.T("配置", "Configuration"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (disk.IsBoot)
            {
                MessageBox.Show(Lang.T("选中的磁盘包含启动分区，拒绝继续。", "The selected disk contains a boot partition. Refusing to continue."),
                    Lang.T("安全门禁", "Safety gate"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (volume == null)
            {
                MessageBox.Show(Lang.T("请选择源卷。", "Select the source volume."),
                    Lang.T("配置", "Configuration"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            string staging = stagingBox.Text.Trim();
            string recovery = sameRootCheck.Checked ? staging : recoveryBox.Text.Trim();
            if (staging.Length == 0 || recovery.Length == 0)
            {
                MessageBox.Show(Lang.T("请选择工作目录（以及恢复目录，如果分开）。",
                        "Select the working folder (and the recovery folder if separate)."),
                    Lang.T("配置", "Configuration"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                staging = NormalizeDir(staging);
                recovery = NormalizeDir(recovery);
                string dataRoot = NormalizeDir(Path.Combine(staging, "DATA"));
                if (string.Equals(recovery, dataRoot, StringComparison.OrdinalIgnoreCase) ||
                    recovery.StartsWith(dataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(Lang.T(
                        "恢复目录不能指向暂存的 DATA 子目录，否则日志会混入被校验的数据树。请选择工作目录的上级或其他目录。",
                        "The recovery folder must not be inside the staging DATA tree; logs would contaminate the verified data. Choose the working folder itself or another folder."),
                        Lang.T("配置", "Configuration"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                Directory.CreateDirectory(staging);
                if (!string.Equals(staging, recovery, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(recovery);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("无法创建目录: ", "Cannot create folder: ") + ex.Message,
                    Lang.T("配置", "Configuration"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string source = volume.Letter;
            string label = source.TrimEnd(':');
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@{");
            sb.AppendLine("    TargetDiskNumber = " + disk.Index);
            sb.AppendLine("    TargetModel      = " + PsQuote(disk.Model));
            sb.AppendLine("    TargetSerial     = " + PsQuote(disk.Serial));
            sb.AppendLine("    TargetFirmware   = " + PsQuote(disk.Firmware));
            sb.AppendLine("    SourceVolume     = " + PsQuote(source));
            sb.AppendLine("    StagingRoot      = " + PsQuote(staging));
            sb.AppendLine("    RecoveryRoot     = " + PsQuote(recovery));
            sb.AppendLine("    TargetVolume     = " + PsQuote(source));
            sb.AppendLine("    TargetLabel      = " + PsQuote(label));
            sb.AppendLine("    TargetFileSystem = 'NTFS'");
            sb.AppendLine("    KeyFile          = ''");
            sb.AppendLine("    KeyFileSha256    = ''");
            sb.AppendLine("    CancelFlag       = ''");
            sb.AppendLine("}");
            try
            {
                File.WriteAllText(configPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("无法写入配置: ", "Cannot write configuration: ") + ex.Message,
                    Lang.T("配置", "Configuration"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        private void StartStep(string step)
        {
            if (busy) return;
            if (scriptsDir == null)
            {
                MessageBox.Show(Lang.T("找不到 scripts 目录，请完整解压。", "scripts directory not found. Extract the full package."),
                    Lang.T("文件缺失", "Missing files"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!BuildConfig()) return;

            DiskInfo disk = diskCombo.SelectedItem as DiskInfo;
            VolumeInfo volume = volumeCombo.SelectedItem as VolumeInfo;

            if (step == "rebuild")
            {
                using (ConfirmRebuildForm dialog = new ConfirmRebuildForm(disk, volume.Letter))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                }
            }

            string script = null;
            string extra = string.Empty;
            switch (step)
            {
                case "preflight": script = "00-Preflight.ps1"; break;
                case "copy": script = "01-CopyData.ps1"; break;
                case "verifySource": script = "02-CompareTrees.ps1"; extra = " -Direction SourceToStaging"; break;
                case "rebuild": script = "03-RebuildBasicVolume.ps1"; extra = " -Execute -ConfirmSerial " + QuoteArg(disk.Serial) + " -IUnderstandThisErasesTheTargetDisk"; break;
                case "restore": script = "04-RestoreData.ps1"; break;
                case "verifyTarget": script = "02-CompareTrees.ps1"; extra = " -Direction StagingToTarget"; break;
                case "postcheck": script = "05-PostCheck.ps1"; break;
            }
            if (script == null) return;
            string scriptPath = Path.Combine(scriptsDir, script);
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(Lang.T("脚本不存在: ", "Script not found: ") + scriptPath,
                    Lang.T("文件缺失", "Missing files"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ResetDependent(step);
            currentStep = step;
            AppendLog(string.Empty);
            AppendLog("============================================================");
            AppendLog(Lang.T("开始步骤: ", "Starting step: ") + StepText(step));
            AppendLog(Lang.T("脚本: ", "Script: ") + scriptPath);
            SetBusy(true, StepText(step) + " ...");

            string arguments = string.Format(
                "-NoProfile -ExecutionPolicy Bypass -File {0} -Config {1}{2}",
                QuoteArg(scriptPath), QuoteArg(configPath), extra);
            StartProcess(scriptPath, arguments, step);
        }

        private string StepText(string step)
        {
            switch (step)
            {
                case "preflight": return Lang.T("只读预检", "Read-only preflight");
                case "copy": return Lang.T("复制到暂存盘", "Copy to staging");
                case "verifySource": return Lang.T("校验源 → 暂存", "Verify source -> staging");
                case "rebuild": return Lang.T("重建基本盘", "Rebuild as basic");
                case "restore": return Lang.T("回拷到新盘", "Restore to new disk");
                case "verifyTarget": return Lang.T("校验暂存 → 新盘", "Verify staging -> new disk");
                case "postcheck": return Lang.T("最终验收", "Final post-check");
                default: return step;
            }
        }

        private void StartProcess(string scriptPath, string arguments, string step)
        {
            try
            {
                string ps = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe");
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = ps;
                psi.Arguments = arguments;
                psi.WorkingDirectory = appBase;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(936);
                psi.StandardErrorEncoding = Encoding.GetEncoding(936);

                Process process = new Process();
                process.StartInfo = psi;
                process.EnableRaisingEvents = true;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendLog(e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendLog("[stderr] " + e.Data);
                };
                process.Exited += delegate
                {
                    int code = -1;
                    try { code = process.ExitCode; } catch { }
                    try { process.WaitForExit(); } catch { }
                    BeginInvoke(new Action(delegate { FinishStep(step, code, process); }));
                };
                currentProcess = process;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                currentProcess = null;
                SetBusy(false, Lang.T("启动失败", "Start failed"));
                AppendLog(Lang.T("启动失败: ", "Start failed: ") + ex.Message);
                MessageBox.Show(Lang.T("启动 PowerShell 失败:\r\n", "Failed to start PowerShell:\r\n") + ex.Message,
                    Lang.T("错误", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FinishStep(string step, int code, Process process)
        {
            string outputPath = null;
            try { if (process != null && process.StartInfo != null) outputPath = process.StartInfo.FileName; } catch { }
            if (process != null) process.Dispose();
            currentProcess = null;
            busy = false;

            AppendLog("------------------------------------------------------------");
            AppendLog(string.Format(Lang.T("步骤结束: {0}，退出码: {1}", "Step finished: {0}, exit code: {1}"), StepText(step), code));

            if (code == 0)
            {
                switch (step)
                {
                    case "preflight": preflightOk = true; break;
                    case "copy": copyOk = true; break;
                    case "verifySource": verifySourceOk = true; break;
                    case "rebuild": rebuildOk = true; break;
                    case "restore": restoreOk = true; break;
                    case "verifyTarget": verifyTargetOk = true; break;
                    case "postcheck": break;
                }
                SetBusy(false, Lang.T("完成: ", "Finished: ") + StepText(step));
                AppendLog(Lang.T("结果: 成功", "Result: success"));
                if (step == "rebuild")
                {
                    MessageBox.Show(Lang.T(
                        "目标盘已重建为 MBR/NTFS 基本卷。\r\n下一步：回拷数据，然后再次全量校验。",
                        "The target disk is now an MBR/NTFS basic volume.\r\nNext: restore the data, then verify again."),
                        Lang.T("重建完成", "Rebuild complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                if (step == "postcheck")
                {
                    MessageBox.Show(Lang.T(
                        "迁移完成并通过最终验收。\r\n请重启一次，并保留暂存副本直到确认稳定。",
                        "Migration complete and post-check passed.\r\nReboot once and keep the staging copy until you are confident."),
                        Lang.T("迁移完成", "Migration complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                SetBusy(false, Lang.T("失败: ", "Failed: ") + StepText(step));
                AppendLog(Lang.T("结果: 失败，请查看日志。", "Result: failed. Check the log."));
                MessageBox.Show(Lang.T("步骤失败，退出码 ", "Step failed, exit code ") + code + Lang.T("。\r\n请查看日志，不要跳步。", ".\r\nCheck the log. Do not skip steps."),
                    Lang.T("步骤失败", "Step failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateButtons();
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            statusLabel.Text = status;
            progressBar.Style = value ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            progressBar.MarqueeAnimationSpeed = value ? 30 : 0;
            UpdateButtons();
        }

        private void ResetDependent(string step)
        {
            switch (step)
            {
                case "preflight":
                    preflightOk = false; copyOk = false; verifySourceOk = false;
                    rebuildOk = false; restoreOk = false; verifyTargetOk = false;
                    break;
                case "copy":
                    copyOk = false; verifySourceOk = false; rebuildOk = false;
                    restoreOk = false; verifyTargetOk = false;
                    break;
                case "verifySource":
                    verifySourceOk = false; rebuildOk = false; restoreOk = false;
                    verifyTargetOk = false;
                    break;
                case "rebuild":
                    rebuildOk = false; restoreOk = false; verifyTargetOk = false;
                    break;
                case "restore":
                    restoreOk = false; verifyTargetOk = false;
                    break;
                case "verifyTarget":
                    verifyTargetOk = false;
                    break;
            }
        }

        private void UpdateButtons()
        {
            bool idle = !busy;
            preflightButton.Enabled = idle;
            copyButton.Enabled = idle && preflightOk;
            verifySourceButton.Enabled = idle && copyOk;
            rebuildButton.Enabled = idle && verifySourceOk;
            restoreButton.Enabled = idle && rebuildOk;
            verifyTargetButton.Enabled = idle && restoreOk;
            postcheckButton.Enabled = idle && verifyTargetOk;
            cancelButton.Enabled = busy;
        }

        private void StepPreflight(object sender, EventArgs e) { StartStep("preflight"); }
        private void StepCopy(object sender, EventArgs e) { StartStep("copy"); }
        private void StepVerifySource(object sender, EventArgs e) { StartStep("verifySource"); }
        private void StepRebuild(object sender, EventArgs e) { StartStep("rebuild"); }
        private void StepRestore(object sender, EventArgs e) { StartStep("restore"); }
        private void StepVerifyTarget(object sender, EventArgs e) { StartStep("verifyTarget"); }
        private void StepPostcheck(object sender, EventArgs e) { StartStep("postcheck"); }

        private void StepCancel(object sender, EventArgs e)
        {
            if (!busy || currentProcess == null) return;
            DialogResult result = MessageBox.Show(
                Lang.T(
                    "停止当前步骤？\r\n\r\n" +
                    "注意：强制停止可能导致中间状态（尤其是重建/回拷阶段）。\r\n" +
                    "本工具不会自动回滚。停止后必须手动检查磁盘状态。",
                    "Stop the current step?\r\n\r\n" +
                    "Force-stopping can leave an intermediate state (especially during rebuild/restore).\r\n" +
                    "This tool does not roll back automatically. Inspect the disk state manually."),
                Lang.T("停止当前步骤", "Stop current step"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes) return;
            try
            {
                int pid = currentProcess.Id;
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe",
                    string.Format("/PID {0} /T /F", pid));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                AppendLog(Lang.T("已请求停止进程树 PID=", "Requested process-tree stop, PID=") + pid);
                statusLabel.Text = Lang.T("正在停止...", "Stopping...");
            }
            catch (Exception ex)
            {
                AppendLog(Lang.T("停止失败: ", "Stop failed: ") + ex.Message);
            }
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!busy || currentProcess == null) return;
            DialogResult result = MessageBox.Show(
                Lang.T(
                    "当前步骤仍在运行。关闭窗口会强制停止它，可能留下中间状态。\r\n\r\n确定关闭？",
                    "The current step is still running. Closing the window will force-stop it and may leave an intermediate state.\r\n\r\nClose anyway?"),
                Lang.T("确认关闭", "Confirm close"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            try
            {
                int pid = currentProcess.Id;
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe",
                    string.Format("/PID {0} /T /F", pid));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }

        private void AppendLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (logBox.InvokeRequired)
            {
                try { logBox.BeginInvoke(new Action<string>(AppendLog), text); } catch { }
                return;
            }
            logBox.AppendText(text + Environment.NewLine);
        }

        private void OpenDocs()
        {
            string path = null;
            if (docsDir != null)
            {
                string runbook = Path.Combine(docsDir, "04-runbook.md");
                if (File.Exists(runbook)) path = runbook;
                if (path == null)
                {
                    string readme = Path.Combine(docsDir, "..", "README.md");
                    if (File.Exists(readme)) path = readme;
                }
            }
            if (path == null)
            {
                MessageBox.Show(Lang.T("找不到 docs 目录，请确认完整解压。", "docs directory not found. Extract the full package."),
                    Lang.T("文件缺失", "Missing files"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            OpenPath(path);
        }

        private void OpenPath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
                else
                {
                    Process.Start("notepad.exe", "\"" + path + "\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("打开失败: ", "Open failed: ") + ex.Message,
                    Lang.T("错误", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowSafetyHelp()
        {
            MessageBox.Show(
                Lang.T(
                    "安全要点：\r\n\r\n" +
                    "1. 只用于非系统盘/非启动盘。\r\n" +
                    "2. 先备份；没有恢复副本不要开始。\r\n" +
                    "3. 必须按 1→7 顺序执行，不能跳步。\r\n" +
                    "4. 重建前必须完成源→暂存全量校验（0 mismatch）。\r\n" +
                    "5. 重建会清空目标盘，需要输入完整序列号确认。\r\n" +
                    "6. 若失败，先读日志和 docs/07-recovery-playbook.md，不要盲目重试。\r\n" +
                    "7. 迁移成功后保留暂存副本，至少等重启和关键应用验证之后。",
                    "Safety rules:\r\n\r\n" +
                    "1. Use only on a non-system/non-boot disk.\r\n" +
                    "2. Back up first; do not start without a recovery copy.\r\n" +
                    "3. Follow steps 1-7 in order. Do not skip.\r\n" +
                    "4. The source -> staging full byte comparison must be 0 mismatch before rebuild.\r\n" +
                    "5. Rebuild erases the target disk; typing the full serial is required.\r\n" +
                    "6. On failure, read the log and docs/07-recovery-playbook.md. Do not retry blindly.\r\n" +
                    "7. Keep the staging copy until a reboot and key applications have been verified."),
                Lang.T("安全说明", "Safety"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
