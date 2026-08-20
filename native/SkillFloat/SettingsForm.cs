using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SkillFloat
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly ComboBox _shortcut = new ComboBox();
        private readonly CheckBox _startup = new CheckBox();
        private readonly CheckBox _autoClassify = new CheckBox();
        private readonly CheckBox _scanCodex = new CheckBox();
        private readonly CheckBox _scanClaude = new CheckBox();
        private readonly CheckBox _scanOpenClaw = new CheckBox();
        private readonly TextBox _endpoint = new TextBox();
        private readonly TextBox _model = new TextBox();
        private readonly TextBox _apiKey = new TextBox();

        public SettingsForm()
        {
            _settings = Storage.LoadAppSettings();
            Theme.StyleForm(this);
            Text = "设置";
            Size = new Size(620, 570);
            MinimumSize = new Size(560, 520);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = Program.QaMode;
            Theme.ConstrainToWorkingArea(this);
            BuildInterface();
            LoadValues();
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(16), BackColor = Theme.Surface };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Body };
            tabs.TabPages.Add(BuildGeneralPage());
            tabs.TabPages.Add(BuildSkillPage());
            tabs.TabPages.Add(BuildAiPage());
            tabs.TabPages.Add(BuildDataPage());
            root.Controls.Add(tabs, 0, 0);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            var save = Theme.Button("保存", true);
            save.Width = 90;
            save.Click += (_, __) => SaveValues();
            var cancel = Theme.Button("取消");
            cancel.Width = 90;
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            root.Controls.Add(footer, 0, 1);
            Controls.Add(root);
            CancelButton = cancel;
        }

        private TabPage BuildGeneralPage()
        {
            var page = Page("常规");
            var panel = Stack(page);
            panel.Controls.Add(Heading("全局快捷键"));
            _shortcut.DropDownStyle = ComboBoxStyle.DropDownList;
            _shortcut.Width = 220;
            _shortcut.Items.AddRange(HotkeyChoice.Supported.Cast<object>().ToArray());
            panel.Controls.Add(_shortcut);
            panel.Controls.Add(Note("如首选组合被占用，程序会自动尝试其他组合并明确显示实际结果。"));
            _startup.Text = "登录 Windows 后启动 Skill Float";
            _startup.AutoSize = true;
            _startup.Margin = new Padding(0, 18, 0, 0);
            panel.Controls.Add(_startup);
            return page;
        }

        private TabPage BuildSkillPage()
        {
            var page = Page("Skill");
            var panel = Stack(page);
            panel.Controls.Add(Heading("分类行为"));
            _autoClassify.Text = "自动为新 Skill 分类";
            _autoClassify.AutoSize = true;
            panel.Controls.Add(_autoClassify);
            panel.Controls.Add(Note("默认关闭。开启后，仅在已配置 AI 且发现缺少分类或标签时请求接口。"));
            return page;
        }

        private TabPage BuildAiPage()
        {
            var page = Page("AI");
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(table, "接口地址", _endpoint, false);
            AddField(table, "模型", _model, false);
            AddField(table, "API Key", _apiKey, true);
            var note = Note("API Key 使用 Windows 当前用户范围加密保存；非本机接口必须使用 HTTPS。不会上传对话历史。 ");
            table.Controls.Add(note, 0, 3);
            table.SetColumnSpan(note, 2);
            page.Controls.Add(table);
            return page;
        }

        private TabPage BuildDataPage()
        {
            var page = Page("数据与隐私");
            var panel = Stack(page);
            panel.Controls.Add(Heading("调用统计来源"));
            SetupCheck(_scanCodex, "读取 Codex 本地调用记录");
            SetupCheck(_scanClaude, "读取 Claude Code 本地调用记录");
            SetupCheck(_scanOpenClaw, "读取 OpenClaw 本地调用记录");
            panel.Controls.Add(_scanCodex);
            panel.Controls.Add(_scanClaude);
            panel.Controls.Add(_scanOpenClaw);
            panel.Controls.Add(Note("仅在本机增量解析调用痕迹，不上传聊天正文，也不保存正文副本。"));
            var open = Theme.Button("打开用户数据目录");
            open.Width = 150;
            open.Margin = new Padding(0, 18, 0, 0);
            open.Click += (_, __) => { Directory.CreateDirectory(Storage.Root); Process.Start("explorer.exe", Storage.Root); };
            panel.Controls.Add(open);
            return page;
        }

        private void LoadValues()
        {
            _shortcut.SelectedItem = HotkeyChoice.Find(_settings.globalShortcut);
            _startup.Checked = _settings.startWithWindows;
            _autoClassify.Checked = _settings.autoClassifyNewSkills;
            _scanCodex.Checked = _settings.scanCodex;
            _scanClaude.Checked = _settings.scanClaudeCode;
            _scanOpenClaw.Checked = _settings.scanOpenClaw;
            var ai = Storage.LoadSettings();
            _endpoint.Text = ai.endpoint ?? "https://api.openai.com/v1";
            _model.Text = ai.model ?? "";
            _apiKey.Text = Storage.LoadApiKey();
        }

        private void SaveValues()
        {
            var endpoint = _endpoint.Text.Trim();
            Uri uri;
            if ((endpoint.Length == 0 || string.IsNullOrWhiteSpace(_model.Text)) && !string.IsNullOrWhiteSpace(_apiKey.Text))
            {
                MessageBox.Show(this, "保存 API Key 时必须同时填写接口地址和模型。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (endpoint.Length > 0 && (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttps && !(IsLocal(uri) && uri.Scheme == Uri.UriSchemeHttp))))
            {
                MessageBox.Show(this, "非本机 AI 接口必须是有效的 HTTPS 地址。", "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _settings.globalShortcut = ((_shortcut.SelectedItem as HotkeyChoice) ?? HotkeyChoice.Supported[0]).Name;
            _settings.startWithWindows = _startup.Checked;
            _settings.autoClassifyNewSkills = _autoClassify.Checked;
            _settings.scanCodex = _scanCodex.Checked;
            _settings.scanClaudeCode = _scanClaude.Checked;
            _settings.scanOpenClaw = _scanOpenClaw.Checked;
            Storage.SaveAppSettings(_settings);
            Storage.SaveSettings(new TranslationSettings { endpoint = endpoint, model = _model.Text.Trim() });
            Storage.SaveApiKey(_apiKey.Text);
            try { StartupManager.SetEnabled(_settings.startWithWindows); }
            catch (Exception error)
            {
                MessageBox.Show(this, "设置开机启动失败：" + error.Message, "部分设置未生效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool IsLocal(Uri uri) => uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        private static TabPage Page(string text) => new TabPage(text) { BackColor = Theme.Surface, Padding = new Padding(4) };
        private static FlowLayoutPanel Stack(Control parent)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14), BackColor = Theme.Surface };
            parent.Controls.Add(panel);
            return panel;
        }
        private static Label Heading(string text) { var label = Theme.Label(text, Theme.Strong, Theme.Text); label.Margin = new Padding(0, 2, 0, 12); return label; }
        private static Label Note(string text) { var label = Theme.Label(text, Theme.Small, Theme.Muted); label.AutoSize = true; label.MaximumSize = new Size(500, 0); label.Margin = new Padding(0, 8, 0, 4); return label; }
        private static void SetupCheck(CheckBox check, string text) { check.Text = text; check.AutoSize = true; check.Margin = new Padding(0, 5, 0, 5); }
        private static void AddField(TableLayoutPanel table, string label, TextBox box, bool password)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var fieldLabel = Theme.Label(label, Theme.Small, Theme.Secondary);
            fieldLabel.Margin = new Padding(0, 8, 8, 8);
            table.Controls.Add(fieldLabel, 0, row);
            box.Dock = DockStyle.Top;
            box.UseSystemPasswordChar = password;
            box.Margin = new Padding(0, 4, 0, 8);
            table.Controls.Add(box, 1, row);
        }
    }
}
