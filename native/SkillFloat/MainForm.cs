using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SkillFloat
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 73;
        private readonly AliasStore _aliases;
        private readonly AiService _ai = new AiService();
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _search = new TextBox();
        private readonly Label _status = Theme.Label("正在读取 Skill…", Theme.Small, Theme.Muted);
        private readonly Label _count = Theme.Label("0 项", Theme.Small, Theme.Muted);
        private readonly Button _allButton = Theme.Button("全部");
        private readonly Button _favoriteFilterButton = Theme.Button("收藏");
        private readonly Button _favoriteButton = Theme.Button("收藏");
        private readonly Button _editButton = Theme.Button("编辑");
        private readonly Button _translateButton = Theme.Button("AI 汉化");
        private readonly Button _usageButton = Theme.Button("分类与使用");
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly EventWaitHandle _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShutdownEventName);
        private readonly EventWaitHandle _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        private readonly System.Windows.Forms.Timer _controlTimer = new System.Windows.Forms.Timer { Interval = 250 };
        private List<SkillItem> _skills = new List<SkillItem>();
        private List<SkillItem> _visible = new List<SkillItem>();
        private UsageSummary _usage = new UsageSummary();
        private IntPtr _focusTarget = IntPtr.Zero;
        private bool _favoritesOnly;
        private bool _allowExit;
        private int _modalDepth;
        private string _shortcut = "Alt+S";

        public MainForm()
        {
            _aliases = Storage.LoadAliases();
            Theme.StyleForm(this);
            Text = "Skill Float";
            Name = "SkillFloatMainWindow";
            FormBorderStyle = Program.QaMode ? FormBorderStyle.FixedSingle : FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(680, 720);
            MinimumSize = new Size(560, 560);
            TopMost = true;
            ShowInTaskbar = Program.QaMode;
            KeyPreview = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Padding = new Padding(1);
            BackColor = Theme.BorderStrong;
            BuildInterface();
            BuildTray();
            LoadSkills();
            _controlTimer.Tick += (_, __) =>
            {
                if (_shutdownEvent.WaitOne(0))
                {
                    _allowExit = true;
                    _tray.Visible = false;
                    Close();
                    return;
                }
                if (_showEvent.WaitOne(0)) ShowPicker();
            };
            _controlTimer.Start();

            Shown += async (_, __) =>
            {
                _search.Focus();
                await RefreshUsageAsync().ConfigureAwait(true);
                if (!Program.QaMode) await AutoClassifyAsync().ConfigureAwait(true);
            };
            Deactivate += (_, __) =>
            {
                if (!Program.QaMode && _modalDepth == 0 && Visible) MinimizeForFocusLoss();
            };
            FormClosing += (_, eventArgs) =>
            {
                if (_allowExit) return;
                eventArgs.Cancel = true;
                HideToTray();
            };
            FormClosed += (_, __) =>
            {
                _controlTimer.Stop();
                _controlTimer.Dispose();
                _shutdownEvent.Dispose();
                _showEvent.Dispose();
                _tray.Dispose();
            };
            KeyDown += MainKeyDown;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var candidates = new[]
            {
                Tuple.Create(NativeMethods.ModAlt, Keys.S, "Alt+S"),
                Tuple.Create(NativeMethods.ModAlt | NativeMethods.ModShift, Keys.S, "Alt+Shift+S"),
                Tuple.Create(NativeMethods.ModControl | NativeMethods.ModAlt, Keys.S, "Ctrl+Alt+S")
            };
            foreach (var candidate in candidates)
            {
                if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, candidate.Item1, (int)candidate.Item2)) continue;
                _shortcut = candidate.Item3;
                break;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                _focusTarget = NativeMethods.GetForegroundWindow();
                ShowPicker();
                return;
            }
            if (message.Msg == NativeMethods.WmShowSkillFloat)
            {
                ShowPicker();
                return;
            }
            if (message.Msg == NativeMethods.WmShutdownSkillFloat)
            {
                _allowExit = true;
                _tray.Visible = false;
                Close();
                return;
            }
            base.WndProc(ref message);
        }

        private void BuildInterface()
        {
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, RowCount = 5, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(shell);

            var title = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(18, 13, 12, 10) };
            title.MouseDown += (_, eventArgs) => { if (eventArgs.Button == MouseButtons.Left) NativeMethods.BeginWindowDrag(this); };
            var mark = new Label { Text = "S", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = Color.White, BackColor = Theme.Primary, Size = new Size(38, 38), Location = new Point(18, 16) };
            mark.MouseDown += (_, eventArgs) => { if (eventArgs.Button == MouseButtons.Left) NativeMethods.BeginWindowDrag(this); };
            var brand = Theme.Label("Skill Float", new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold), Theme.Text);
            brand.Location = new Point(68, 15);
            var subtitle = Theme.Label("轻按一下，调用所需能力", Theme.Small, Theme.Muted);
            subtitle.Location = new Point(68, 39);
            var minimize = Theme.Button("—");
            minimize.AccessibleName = "最小化悬浮窗";
            minimize.Size = new Size(38, 38);
            minimize.Location = new Point(ClientSize.Width - 92, 14);
            minimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            minimize.Click += (_, __) => MinimizeForFocusLoss();
            var close = Theme.Button("×");
            close.AccessibleName = "隐藏悬浮窗";
            close.Font = new Font("Segoe UI", 13F);
            close.Size = new Size(38, 38);
            close.Location = new Point(ClientSize.Width - 50, 14);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += (_, __) => HideToTray();
            title.Controls.AddRange(new Control[] { mark, brand, subtitle, minimize, close });
            shell.Controls.Add(title, 0, 0);

            var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(18, 10, 18, 8) };
            _search.Dock = DockStyle.Top;
            _search.Height = 38;
            _search.Font = new Font("Microsoft YaHei UI", 10F);
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.AccessibleName = "搜索 Skill";
            _search.TextChanged += (_, __) => ApplyFilter();
            var filterRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, 4, 0, 0) };
            _allButton.Width = 74;
            _favoriteFilterButton.Width = 74;
            _allButton.Click += (_, __) => { _favoritesOnly = false; ApplyFilter(); };
            _favoriteFilterButton.Click += (_, __) => { _favoritesOnly = true; ApplyFilter(); };
            filterRow.Controls.AddRange(new Control[] { _allButton, _favoriteFilterButton });
            toolbar.Controls.Add(_search);
            toolbar.Controls.Add(filterRow);
            shell.Controls.Add(toolbar, 0, 1);

            var statusBar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Soft, Padding = new Padding(18, 7, 18, 5) };
            _status.Location = new Point(18, 8);
            _count.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _count.Location = new Point(ClientSize.Width - 76, 8);
            statusBar.Controls.Add(_status);
            statusBar.Controls.Add(_count);
            shell.Controls.Add(statusBar, 0, 2);

            _list.Dock = DockStyle.Fill;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Surface;
            _list.ForeColor = Theme.Text;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 78;
            _list.IntegralHeight = false;
            _list.Font = Theme.Body;
            _list.DrawItem += DrawSkill;
            _list.DoubleClick += (_, __) => RunSelectedSkill();
            _list.SelectedIndexChanged += (_, __) => UpdateActionState();
            _list.MouseUp += ListMouseUp;
            shell.Controls.Add(_list, 0, 3);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, BackColor = Color.FromArgb(247, 250, 249), Padding = new Padding(12, 8, 12, 8) };
            for (var index = 0; index < 4; index++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
            _favoriteButton.Click += (_, __) => ToggleFavorite();
            _editButton.Click += (_, __) => OpenEditor();
            _translateButton.Click += (_, __) => OpenTranslation();
            _usageButton.Click += (_, __) => OpenUsage();
            var call = Theme.Button("插入", true);
            call.Click += (_, __) => RunSelectedSkill();
            actions.Controls.Add(_favoriteButton, 0, 0);
            actions.Controls.Add(_editButton, 1, 0);
            actions.Controls.Add(_translateButton, 2, 0);
            actions.Controls.Add(_usageButton, 3, 0);
            actions.Controls.Add(call, 4, 0);
            foreach (Control control in actions.Controls) control.Dock = DockStyle.Fill;
            shell.Controls.Add(actions, 0, 4);
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开 Skill Float", null, (_, __) => ShowPicker());
            menu.Items.Add("退出", null, (_, __) => { _allowExit = true; _tray.Visible = false; Close(); });
            _tray.Icon = Icon;
            _tray.Text = "Skill Float · 快速调用 Skill";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += (_, __) => ShowPicker();
        }

        private void LoadSkills()
        {
            _skills = SkillDiscovery.Discover(_aliases);
            _usage = UsageScanner.Current(_skills);
            ApplyUsage();
            ApplyFilter();
            SetStatus("已读取 " + _skills.Count + " 个 Skill · 唤出快捷键 " + _shortcut);
        }

        private void ApplyUsage()
        {
            foreach (var skill in _skills)
            {
                long count;
                skill.UsageCount = _usage.Counts.TryGetValue(skill.Invocation, out count) ? count : 0;
                Dictionary<string, long> sources;
                skill.UsageSources = _usage.SourceCounts.TryGetValue(skill.Invocation, out sources) ? sources : new Dictionary<string, long>();
            }
        }

        private void ApplyFilter()
        {
            var terms = _search.Text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            _visible = _skills.Where(skill => (!_favoritesOnly || skill.Favorite) && terms.All(term => SearchText(skill).Contains(term))).OrderByDescending(skill => skill.Favorite).ThenBy(skill => skill.VisibleName, StringComparer.CurrentCultureIgnoreCase).ToList();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var skill in _visible) _list.Items.Add(skill);
            _list.EndUpdate();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _count.Text = _visible.Count + " 项";
            _allButton.BackColor = _favoritesOnly ? Theme.Raised : Theme.PrimarySoft;
            _favoriteFilterButton.BackColor = _favoritesOnly ? Theme.PrimarySoft : Theme.Raised;
            UpdateActionState();
        }

        private static string SearchText(SkillItem skill) => string.Join(" ", new[] { skill.Invocation, skill.Name, skill.DisplayName, skill.Description, skill.LocalizedDescription, skill.Source, skill.Category, string.Join(" ", skill.Tags) }).ToLowerInvariant();

        private void DrawSkill(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _visible.Count) return;
            var skill = _visible[e.Index];
            var selected = (e.State & DrawItemState.Selected) != 0;
            using (var background = new SolidBrush(selected ? Color.FromArgb(237, 247, 244) : Theme.Surface)) e.Graphics.FillRectangle(background, e.Bounds);
            if (selected) using (var accent = new SolidBrush(Theme.Primary)) e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.Left, e.Bounds.Top + 5, 3, e.Bounds.Height - 10));
            var monogram = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 16, 38, 38);
            using (var brush = new SolidBrush(Theme.PrimarySoft)) e.Graphics.FillRectangle(brush, monogram);
            TextRenderer.DrawText(e.Graphics, Initials(skill.VisibleName), Theme.Strong, monogram, Theme.PrimaryDark, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var left = e.Bounds.Left + 64;
            var width = Math.Max(80, e.Bounds.Width - 80);
            TextRenderer.DrawText(e.Graphics, (skill.Favorite ? "★ " : "") + skill.VisibleName, Theme.Strong, new Rectangle(left, e.Bounds.Top + 8, width - 95, 20), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "调用 " + skill.UsageCount + " 次", Theme.Caption, new Rectangle(e.Bounds.Right - 92, e.Bounds.Top + 9, 76, 18), Theme.PrimaryDark, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, string.IsNullOrWhiteSpace(skill.VisibleDescription) ? "尚未添加用途说明" : skill.VisibleDescription, Theme.Small, new Rectangle(left, e.Bounds.Top + 30, width - 10, 20), Theme.Secondary, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            var meta = "$" + skill.Invocation + "  ·  " + skill.Source + (string.IsNullOrWhiteSpace(skill.Category) ? "" : "  ·  " + skill.Category);
            TextRenderer.DrawText(e.Graphics, meta, Theme.Caption, new Rectangle(left, e.Bounds.Top + 53, width - 10, 17), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, left, e.Bounds.Bottom - 1, e.Bounds.Right - 14, e.Bounds.Bottom - 1);
            e.DrawFocusRectangle();
        }

        private static string Initials(string text)
        {
            var words = (text ?? "").Split(new[] { ' ', '-', '_', ':', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "SK";
            if (words.Length == 1) return new string(words[0].Take(2).ToArray()).ToUpperInvariant();
            return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
        }

        private SkillItem Selected => _list.SelectedItem as SkillItem;

        private void UpdateActionState()
        {
            var selected = Selected;
            _favoriteButton.Enabled = selected != null;
            _editButton.Enabled = selected != null;
            _favoriteButton.Text = selected != null && selected.Favorite ? "取消收藏" : "收藏";
        }

        private async void RunSelectedSkill()
        {
            var skill = Selected;
            if (skill == null) return;
            var text = "$" + skill.Invocation + " ";
            string previous = null;
            try { previous = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch { }
            try { Clipboard.SetText(text); }
            catch { SetStatus("写入剪贴板失败，请重试", true); return; }
            Hide();
            TopMost = false;
            await Task.Delay(80);
            var inserted = _focusTarget != IntPtr.Zero && NativeMethods.IsWindow(_focusTarget) && NativeMethods.SetForegroundWindow(_focusTarget);
            if (inserted)
            {
                await Task.Delay(80);
                NativeMethods.SendPaste();
                await Task.Delay(320);
                if (previous != null) try { Clipboard.SetText(previous); } catch { }
            }
            else
            {
                ShowPicker();
                SetStatus("未找到唤出前的输入框，调用文本已复制到剪贴板", true);
            }
            UsageScanner.RecordLocal(skill.Invocation);
            skill.UsageCount++;
            _list.Invalidate();
        }

        private void ToggleFavorite()
        {
            var skill = Selected;
            if (skill == null) return;
            skill.Favorite = !skill.Favorite;
            SaveSkill(skill);
            ApplyFilter();
        }

        private void OpenEditor()
        {
            var skill = Selected;
            if (skill == null) return;
            WithModal(new EditorForm(skill, _ai), dialog =>
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SaveSkill(skill);
                ApplyFilter();
                SetStatus("显示信息已保存");
            });
        }

        private void OpenTranslation()
        {
            WithModal(new TranslationForm(_skills, _aliases, _ai), dialog =>
            {
                dialog.ShowDialog(this);
                foreach (var skill in _skills)
                {
                    AliasEntry entry;
                    if (!_aliases.skills.TryGetValue(skill.Invocation, out entry)) continue;
                    skill.DisplayName = entry.displayName ?? "";
                    skill.LocalizedDescription = entry.localizedDescription ?? "";
                    skill.Category = entry.category ?? "";
                    skill.Tags = entry.tags ?? new List<string>();
                }
                ApplyFilter();
            });
        }

        private void OpenUsage()
        {
            WithModal(new UsageForm(_skills, _usage, async () => await RefreshUsageAsync()), dialog => dialog.ShowDialog(this));
        }

        private void WithModal<T>(T dialog, Action<T> action) where T : Form
        {
            _modalDepth++;
            try { action(dialog); }
            finally { dialog.Dispose(); _modalDepth--; Activate(); }
        }

        private void SaveSkill(SkillItem skill)
        {
            _aliases.skills[skill.Invocation] = new AliasEntry
            {
                displayName = skill.DisplayName,
                localizedDescription = skill.LocalizedDescription,
                favorite = skill.Favorite,
                category = skill.Category,
                tags = skill.Tags.Distinct(StringComparer.CurrentCultureIgnoreCase).Take(8).ToList()
            };
            Storage.SaveAliases(_aliases);
        }

        private async Task RefreshUsageAsync()
        {
            try
            {
                SetStatus("正在读取本地调用历史…");
                _usage = await Task.Run(() => UsageScanner.Refresh(_skills, (done, total, source) => BeginInvoke(new Action(() => SetStatus("正在读取 " + source + " · " + done + "/" + total))), CancellationToken.None));
                ApplyUsage();
                _list.Invalidate();
                SetStatus("统计已更新 · 共 " + _usage.Total + " 次调用");
                TrimWorkingSet();
            }
            catch (Exception error) { SetStatus("读取调用历史失败：" + error.Message, true); TrimWorkingSet(); }
        }

        private async Task AutoClassifyAsync()
        {
            if (!_ai.IsConfigured) return;
            var pending = _skills.Where(skill => string.IsNullOrWhiteSpace(skill.Category) || skill.Tags.Count == 0).ToList();
            if (pending.Count == 0) return;
            var completed = 0;
            for (var offset = 0; offset < pending.Count; offset += 20)
            {
                try
                {
                    var batch = pending.Skip(offset).Take(20).ToList();
                    SetStatus("AI 正在自动分类 · " + Math.Min(offset + batch.Count, pending.Count) + "/" + pending.Count);
                    var results = await _ai.ClassifyBatchAsync(batch, CancellationToken.None);
                    var unresolved = new List<SkillItem>();
                    foreach (var skill in batch)
                    {
                        TranslationSuggestion result;
                        if (!results.TryGetValue(skill.Invocation, out result)) { unresolved.Add(skill); continue; }
                        skill.Category = result.category;
                        skill.Tags = result.tags;
                        SaveSkill(skill);
                        completed++;
                    }
                    foreach (var skill in unresolved)
                    {
                        var result = await _ai.ClassifyAsync(skill, CancellationToken.None);
                        skill.Category = result.category;
                        skill.Tags = result.tags;
                        SaveSkill(skill);
                        completed++;
                    }
                    _list.Invalidate();
                    TrimWorkingSet();
                }
                catch (Exception error)
                {
                    SetStatus("自动分类已暂停：" + error.Message, true);
                    TrimWorkingSet();
                    return;
                }
            }
            SetStatus("AI 已自动分类 " + completed + " 个 Skill");
            TrimWorkingSet();
        }

        private void ListMouseUp(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Right || Selected == null) return;
            var menu = new ContextMenuStrip();
            menu.Items.Add(Selected.Favorite ? "取消收藏" : "收藏", null, (_, __) => ToggleFavorite());
            menu.Items.Add("编辑显示信息", null, (_, __) => OpenEditor());
            menu.Items.Add("复制调用名", null, (_, __) => Clipboard.SetText("$" + Selected.Invocation));
            menu.Show(_list, eventArgs.Location);
        }

        private void MainKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (_modalDepth > 0) return;
            if (eventArgs.KeyCode == Keys.Escape) { HideToTray(); eventArgs.Handled = true; }
            else if (eventArgs.KeyCode == Keys.Enter) { RunSelectedSkill(); eventArgs.Handled = true; }
            else if (eventArgs.Control && eventArgs.KeyCode == Keys.D) { ToggleFavorite(); eventArgs.Handled = true; }
            else if (eventArgs.KeyCode == Keys.F2) { OpenEditor(); eventArgs.Handled = true; }
            else if (eventArgs.Control && eventArgs.KeyCode == Keys.F) { _search.Focus(); _search.SelectAll(); eventArgs.Handled = true; }
        }

        private void ShowPicker()
        {
            if (_focusTarget == IntPtr.Zero) _focusTarget = NativeMethods.GetForegroundWindow();
            Show();
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Activate();
            _search.Focus();
        }

        private void HideToTray()
        {
            TopMost = false;
            Hide();
            TrimWorkingSet();
        }

        private void MinimizeForFocusLoss()
        {
            TopMost = false;
            WindowState = FormWindowState.Minimized;
            TrimWorkingSet();
        }

        private static void TrimWorkingSet()
        {
            try { NativeMethods.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1)); } catch { }
        }

        private void SetStatus(string text, bool error = false)
        {
            _status.Text = text;
            _status.ForeColor = error ? Theme.Danger : Theme.Muted;
        }
    }
}
