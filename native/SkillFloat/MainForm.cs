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
        private const string HiddenCategoryValue = "__hidden__";
        private readonly AliasStore _aliases;
        private readonly AiService _ai = new AiService();
        private readonly RedrawListBox _list = new RedrawListBox();
        private readonly TextBox _search = new TextBox();
        private readonly ComboBox _categoryFilter = new ComboBox();
        private readonly Label _status = Theme.Label("正在读取 Skill…", Theme.Small, Theme.Muted);
        private readonly Label _count = Theme.Label("0 项", Theme.Small, Theme.Muted);
        private readonly Button _allButton = Theme.Button("全部");
        private readonly Button _favoriteFilterButton = Theme.Button("收藏");
        private readonly Button _favoriteButton = Theme.Button("收藏");
        private readonly Button _editButton = Theme.Button("编辑");
        private readonly Button _translateButton = Theme.Button("AI 汉化");
        private readonly Button _usageButton = Theme.Button("分类与使用");
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly ToolStripMenuItem _trayShortcut = new ToolStripMenuItem("快捷键：正在注册…") { Enabled = false };
        private readonly EventWaitHandle _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShutdownEventName);
        private readonly EventWaitHandle _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        private readonly System.Windows.Forms.Timer _controlTimer = new System.Windows.Forms.Timer { Interval = 250 };
        private List<SkillItem> _skills = new List<SkillItem>();
        private List<SkillItem> _visible = new List<SkillItem>();
        private UsageSummary _usage = new UsageSummary();
        private readonly FocusTargetTracker _focusTracker = new FocusTargetTracker();
        private AppSettings _appSettings;
        private GlobalHotkeyManager _hotkeyManager;
        private HotkeyRegistration _hotkey = new HotkeyRegistration();
        private bool _favoritesOnly;
        private bool _allowExit;
        private int _modalDepth;
        private bool _updatingCategories;

        private sealed class CategoryFilterItem
        {
            public string Value { get; set; }
            public string Label { get; set; }
            public override string ToString() => Label;
        }

        public MainForm()
        {
            _aliases = Storage.LoadAliases();
            _appSettings = Storage.LoadAppSettings();
            try { StartupManager.SetEnabled(_appSettings.startWithWindows); } catch { }
            Theme.StyleForm(this);
            Text = "Skill Float";
            Name = "SkillFloatMainWindow";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(680, 720);
            MinimumSize = new Size(560, 560);
            MaximizeBox = false;
            MinimizeBox = true;
            ControlBox = true;
            TopMost = true;
            ShowInTaskbar = true;
            KeyPreview = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Padding = new Padding(1);
            BackColor = Theme.BorderStrong;
            Theme.ConstrainToWorkingArea(this);
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
                if (!Visible) _focusTracker.ObserveForeground();
                if (_showEvent.WaitOne(0)) ShowPicker();
            };
            _controlTimer.Start();

            Shown += async (_, __) =>
            {
                if (Program.StartHidden) HideToTray();
                else _search.Focus();
                await RefreshUsageAsync().ConfigureAwait(true);
                if (!Program.QaMode && _appSettings.autoClassifyNewSkills) await AutoClassifyAsync().ConfigureAwait(true);
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
            RegisterHotkey();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _hotkeyManager?.Dispose();
            _hotkeyManager = null;
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                _focusTracker.CaptureImmediate();
                ShowPicker();
                return;
            }
            if (message.Msg == NativeMethods.WmShowSkillFloat)
            {
                _focusTracker.Remember(message.WParam);
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
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            Controls.Add(shell);

            var title = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(18, 13, 12, 10) };
            var mark = new Label { Text = "S", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = Color.White, BackColor = Theme.Primary, Size = new Size(38, 38), Location = new Point(18, 16) };
            var brand = Theme.Label("Skill Float", new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold), Theme.Text);
            brand.Location = new Point(68, 15);
            var subtitle = Theme.Label("轻按一下，调用所需能力", Theme.Small, Theme.Muted);
            subtitle.Location = new Point(68, 39);
            title.Controls.AddRange(new Control[] { mark, brand, subtitle });
            shell.Controls.Add(title, 0, 0);

            var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(18, 10, 18, 4) };
            _search.Dock = DockStyle.Top;
            _search.Height = 38;
            _search.Font = new Font("Microsoft YaHei UI", 10F);
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.AccessibleName = "搜索 Skill";
            _search.TextChanged += (_, __) => ApplyFilter();
            var filterRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, 2, 0, 2) };
            _allButton.Width = 74;
            _favoriteFilterButton.Width = 74;
            _allButton.Margin = new Padding(0, 4, 6, 4);
            _favoriteFilterButton.Margin = new Padding(0, 4, 6, 4);
            _allButton.Click += (_, __) =>
            {
                _favoritesOnly = false;
                if (_categoryFilter.Items.Count > 0) _categoryFilter.SelectedIndex = 0;
                ApplyFilter();
            };
            _favoriteFilterButton.Click += (_, __) => { _favoritesOnly = true; ApplyFilter(); };
            _categoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _categoryFilter.Width = 230;
            _categoryFilter.Height = 28;
            _categoryFilter.Font = Theme.Small;
            _categoryFilter.AccessibleName = "按分类筛选 Skill";
            _categoryFilter.Margin = new Padding(4, 7, 0, 0);
            _categoryFilter.SelectedIndexChanged += (_, __) => { if (!_updatingCategories) ApplyFilter(); };
            var categoryLabel = Theme.Label("分类", Theme.Small, Theme.Secondary);
            categoryLabel.Margin = new Padding(8, 10, 0, 0);
            filterRow.Controls.AddRange(new Control[] { _allButton, _favoriteFilterButton, categoryLabel, _categoryFilter });
            toolbar.Controls.Add(_search);
            toolbar.Controls.Add(filterRow);
            shell.Controls.Add(toolbar, 0, 1);

            var statusBar = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Soft, Padding = new Padding(18, 5, 18, 3), ColumnCount = 2, RowCount = 1 };
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _count.Dock = DockStyle.Fill;
            _count.TextAlign = ContentAlignment.MiddleRight;
            statusBar.Controls.Add(_status, 0, 0);
            statusBar.Controls.Add(_count, 1, 0);
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
            menu.Items.Add(_trayShortcut);
            menu.Items.Add("设置…", null, (_, __) => OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
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
            _usage = UsageScanner.Current(_skills, _appSettings);
            ApplyUsage();
            RefreshCategoryFilter();
            ApplyFilter();
            SetStatus("已读取 " + _skills.Count + " 个 Skill" + HotkeyStatusSuffix(), !_hotkey.Success);
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
            var selectedCategory = (_categoryFilter.SelectedItem as CategoryFilterItem)?.Value ?? "";
            var browsingHidden = selectedCategory == HiddenCategoryValue;
            var candidates = _skills.Where(skill =>
                (browsingHidden ? skill.Hidden : !skill.Hidden)
                && (!_favoritesOnly || browsingHidden || skill.Favorite)
                && (browsingHidden
                    || selectedCategory.Length == 0
                    || (selectedCategory == "未分类" && string.IsNullOrWhiteSpace(skill.Category))
                    || string.Equals(skill.Category, selectedCategory, StringComparison.CurrentCultureIgnoreCase)));
            _visible = SkillSearchRanker.Rank(candidates, _search.Text).ToList();
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

        private void RefreshCategoryFilter()
        {
            var previous = (_categoryFilter.SelectedItem as CategoryFilterItem)?.Value ?? "";
            var visibleSkills = _skills.Where(skill => !skill.Hidden).ToList();
            var hiddenCount = _skills.Count(skill => skill.Hidden);
            var groups = visibleSkills.GroupBy(skill => string.IsNullOrWhiteSpace(skill.Category) ? "未分类" : skill.Category)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);
            var order = new[] { "开发与代码", "文档与内容", "设计与多媒体", "数据与自动化", "法律与专业", "沟通与协作", "其他", "未分类" };
            _updatingCategories = true;
            try
            {
                _categoryFilter.Items.Clear();
                _categoryFilter.Items.Add(new CategoryFilterItem { Value = "", Label = "全部分类（" + visibleSkills.Count + "）" });
                foreach (var category in order)
                {
                    int count;
                    if (!groups.TryGetValue(category, out count)) continue;
                    _categoryFilter.Items.Add(new CategoryFilterItem { Value = category, Label = category + "（" + count + "）" });
                }
                foreach (var pair in groups.Where(pair => !order.Contains(pair.Key)).OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
                    _categoryFilter.Items.Add(new CategoryFilterItem { Value = pair.Key, Label = pair.Key + "（" + pair.Value + "）" });
                if (hiddenCount > 0) _categoryFilter.Items.Add(new CategoryFilterItem { Value = HiddenCategoryValue, Label = "已隐藏（" + hiddenCount + "）" });
                _categoryFilter.SelectedIndex = 0;
                for (var index = 0; index < _categoryFilter.Items.Count; index++)
                {
                    var item = (CategoryFilterItem)_categoryFilter.Items[index];
                    if (!item.Value.Equals(previous, StringComparison.CurrentCultureIgnoreCase)) continue;
                    _categoryFilter.SelectedIndex = index;
                    break;
                }
            }
            finally { _updatingCategories = false; }
        }

        private void DrawSkill(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _visible.Count) return;
            var skill = _visible[e.Index];
            var selected = (e.State & DrawItemState.Selected) != 0;
            var rowBounds = new Rectangle(0, e.Bounds.Top, _list.ClientSize.Width, e.Bounds.Height);
            using (var background = new SolidBrush(selected ? Color.FromArgb(237, 247, 244) : Theme.Surface)) e.Graphics.FillRectangle(background, rowBounds);
            if (selected) using (var accent = new SolidBrush(Theme.Primary)) e.Graphics.FillRectangle(accent, new Rectangle(rowBounds.Left, rowBounds.Top + 5, 3, rowBounds.Height - 10));
            var monogram = new Rectangle(rowBounds.Left + 14, rowBounds.Top + 16, 38, 38);
            using (var brush = new SolidBrush(Theme.PrimarySoft)) e.Graphics.FillRectangle(brush, monogram);
            TextRenderer.DrawText(e.Graphics, Initials(skill.VisibleName), Theme.Strong, monogram, Theme.PrimaryDark, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var left = rowBounds.Left + 64;
            var width = Math.Max(80, rowBounds.Width - 80);
            TextRenderer.DrawText(e.Graphics, (skill.Favorite ? "★ " : "") + skill.VisibleName, Theme.Strong, new Rectangle(left, rowBounds.Top + 8, width - 95, 20), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "调用 " + skill.UsageCount + " 次", Theme.Caption, new Rectangle(rowBounds.Right - 92, rowBounds.Top + 9, 76, 18), Theme.PrimaryDark, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, string.IsNullOrWhiteSpace(skill.VisibleDescription) ? "尚未添加用途说明" : skill.VisibleDescription, Theme.Small, new Rectangle(left, rowBounds.Top + 30, width - 10, 20), Theme.Secondary, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            var meta = "$" + skill.Invocation + "  ·  " + skill.Source + (skill.Hidden ? "  ·  已隐藏" : "") + (string.IsNullOrWhiteSpace(skill.Category) ? "" : "  ·  " + skill.Category);
            TextRenderer.DrawText(e.Graphics, meta, Theme.Caption, new Rectangle(left, rowBounds.Top + 53, width - 10, 17), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, left, rowBounds.Bottom - 1, rowBounds.Right - 14, rowBounds.Bottom - 1);
            if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, rowBounds);
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
            var previousWasText = false;
            try { previousWasText = Clipboard.ContainsText(); if (previousWasText) previous = Clipboard.GetText(); } catch { }
            try { Clipboard.SetText(text); }
            catch { SetStatus("写入剪贴板失败，请重试", true); return; }
            Hide();
            TopMost = false;
            await Task.Delay(80);
            var focusTarget = _focusTracker.Consume();
            var inserted = focusTarget != IntPtr.Zero && NativeMethods.IsWindow(focusTarget) && NativeMethods.SetForegroundWindow(focusTarget);
            if (inserted)
            {
                await Task.Delay(80);
                NativeMethods.SendPaste();
                await Task.Delay(320);
                try
                {
                    if (previousWasText) Clipboard.SetText(previous ?? "");
                    else Clipboard.Clear();
                }
                catch { }
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
            WithModal(new TranslationForm(_skills.Where(skill => !skill.Hidden).ToList(), _aliases, _ai), dialog =>
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
                RefreshCategoryFilter();
                ApplyFilter();
            });
        }

        private void OpenUsage()
        {
            WithModal(new UsageForm(_skills.Where(skill => !skill.Hidden).ToList(), _usage, async () => await RefreshUsageAsync()), dialog => dialog.ShowDialog(this));
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
                _usage = await Task.Run(() => UsageScanner.Refresh(_skills, (done, total, source) => BeginInvoke(new Action(() => SetStatus("正在读取 " + source + " · " + done + "/" + total))), CancellationToken.None, _appSettings));
                ApplyUsage();
                _list.Invalidate();
                SetStatus("统计已更新 · 共 " + _usage.Total + " 次调用" + (_hotkey.Success ? "" : " · 快捷键不可用，请从托盘打开"), !_hotkey.Success);
                TrimWorkingSet();
            }
            catch (Exception error) { SetStatus("读取调用历史失败：" + error.Message, true); TrimWorkingSet(); }
        }

        private async Task AutoClassifyAsync()
        {
            if (!_ai.IsConfigured) return;
            var pending = _skills.Where(skill => !skill.Hidden && (string.IsNullOrWhiteSpace(skill.Category) || skill.Tags.Count == 0)).ToList();
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
            RefreshCategoryFilter();
            ApplyFilter();
            TrimWorkingSet();
        }

        private void ListMouseUp(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Right) return;
            var index = _list.IndexFromPoint(eventArgs.Location);
            if (index < 0) return;
            _list.SelectedIndex = index;
            if (Selected == null) return;
            var menu = new ContextMenuStrip();
            if (Selected.Hidden) menu.Items.Add("恢复显示", null, (_, __) => SetHidden(Selected, false));
            else if (Selected.Source.Equals("本地 Skill", StringComparison.OrdinalIgnoreCase)) menu.Items.Add("移入回收站…", null, (_, __) => DeleteLocalSkill(Selected));
            else menu.Items.Add("从列表隐藏…", null, (_, __) => SetHidden(Selected, true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Selected.Favorite ? "取消收藏" : "收藏", null, (_, __) => ToggleFavorite());
            menu.Items.Add("编辑显示信息", null, (_, __) => OpenEditor());
            menu.Items.Add("复制调用名", null, (_, __) => Clipboard.SetText("$" + Selected.Invocation));
            menu.Show(_list, eventArgs.Location);
        }

        private void SetHidden(SkillItem skill, bool hidden)
        {
            if (skill == null) return;
            var action = hidden ? "隐藏" : "恢复显示";
            var detail = hidden
                ? "这不会删除任何文件，可在分类下拉框的“已隐藏”中恢复。"
                : "恢复后将重新出现在原分类中。";
            var result = MessageBox.Show(this,
                "即将" + action + "：\n\n" + skill.VisibleName + "\n$" + skill.Invocation + "\n来源：" + skill.Source + "\n路径：" + skill.SourcePath + "\n\n" + detail,
                action + " Skill",
                MessageBoxButtons.YesNo,
                hidden ? MessageBoxIcon.Question : MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes) return;
            var store = Storage.LoadHiddenSkills();
            var values = new HashSet<string>(store.skills, StringComparer.OrdinalIgnoreCase);
            if (hidden) values.Add(skill.Invocation);
            else values.Remove(skill.Invocation);
            store.skills = values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            Storage.SaveHiddenSkills(store);
            skill.Hidden = hidden;
            RefreshCategoryFilter();
            ApplyFilter();
            SetStatus(hidden ? "已隐藏 $" + skill.Invocation + "，文件未删除" : "已恢复显示 $" + skill.Invocation);
        }

        private void DeleteLocalSkill(SkillItem skill)
        {
            string directory, reason;
            int containedSkills;
            if (!SkillFileManager.TryGetDeletableDirectory(skill, out directory, out containedSkills, out reason))
            {
                MessageBox.Show(this, reason, "无法删除 Skill", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var nestedWarning = containedSkills > 1
                ? "\n\n注意：该目录共包含 " + containedSkills + " 个 SKILL.md，它们将随整个目录一起进入回收站。"
                : "";
            var result = MessageBox.Show(this,
                "即将把本地 Skill 目录移入 Windows 回收站：\n\n" + skill.VisibleName + "\n$" + skill.Invocation + "\n路径：" + directory + nestedWarning + "\n\n可从回收站恢复。是否继续？",
                "移入回收站",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes) return;
            try
            {
                string verifiedDirectory, verifyReason;
                int verifiedCount;
                if (!SkillFileManager.TryGetDeletableDirectory(skill, out verifiedDirectory, out verifiedCount, out verifyReason)
                    || !verifiedDirectory.Equals(directory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(verifyReason) ? "删除目标在确认期间发生变化，操作已取消。" : verifyReason);
                SkillFileManager.MoveToRecycleBin(directory);
                LoadSkills();
                SetStatus("已将 $" + skill.Invocation + " 移入回收站");
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "未能移入回收站：" + error.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MainKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (_modalDepth > 0) return;
            if (eventArgs.KeyCode == Keys.Escape) { HideToTray(); eventArgs.Handled = true; }
            else if (eventArgs.KeyCode == Keys.Enter && ActiveControl != _categoryFilter) { RunSelectedSkill(); eventArgs.Handled = true; }
            else if (eventArgs.Control && eventArgs.KeyCode == Keys.D) { ToggleFavorite(); eventArgs.Handled = true; }
            else if (eventArgs.KeyCode == Keys.F2) { OpenEditor(); eventArgs.Handled = true; }
            else if (eventArgs.Control && eventArgs.KeyCode == Keys.F) { _search.Focus(); _search.SelectAll(); eventArgs.Handled = true; }
            else if (eventArgs.Control && eventArgs.KeyCode == Keys.Oemcomma) { OpenSettings(); eventArgs.Handled = true; }
        }

        private void ShowPicker()
        {
            _focusTracker.ObserveForeground();
            Show();
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Activate();
            _search.Focus();
        }

        private void OpenSettings()
        {
            WithModal(new SettingsForm(), dialog =>
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _appSettings = Storage.LoadAppSettings();
                RegisterHotkey();
                _ = RefreshUsageAsync();
            });
        }

        private void RegisterHotkey()
        {
            if (!IsHandleCreated) return;
            _hotkeyManager?.Dispose();
            _hotkeyManager = new GlobalHotkeyManager(Handle, HotkeyId);
            _hotkey = _hotkeyManager.Register(_appSettings.globalShortcut);
            _trayShortcut.Text = _hotkey.Success ? "快捷键：" + _hotkey.DisplayName : "快捷键：不可用（请打开设置）";
            _tray.Text = _hotkey.Success ? "Skill Float · " + _hotkey.DisplayName : "Skill Float · 托盘入口可用";
            SetStatus(_hotkey.Success
                ? "已读取 " + _skills.Count + " 个 Skill · 唤出快捷键 " + _hotkey.DisplayName
                : _hotkey.Error, !_hotkey.Success);
        }

        private string HotkeyStatusSuffix()
        {
            return _hotkey.Success ? " · 唤出快捷键 " + _hotkey.DisplayName : " · 快捷键不可用，请从托盘打开";
        }

        private void HideToTray()
        {
            TopMost = false;
            Hide();
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
