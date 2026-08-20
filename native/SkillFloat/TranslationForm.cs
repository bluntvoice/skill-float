using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SkillFloat
{
    internal sealed class TranslationForm : Form
    {
        private static readonly string[] Categories = { "开发与代码", "文档与内容", "设计与多媒体", "数据与自动化", "法律与专业", "沟通与协作", "其他" };
        private readonly IList<SkillItem> _skills;
        private readonly AliasStore _aliases;
        private readonly AiService _ai;
        private readonly CheckedListBox _list = new CheckedListBox();
        private readonly ComboBox _listCategory = new ComboBox();
        private readonly Label _invocation = Theme.Label("请选择一个 Skill。", Theme.Mono, Theme.Muted);
        private readonly TextBox _previewName = new TextBox();
        private readonly TextBox _previewDescription = new TextBox();
        private readonly ComboBox _previewCategory = new ComboBox();
        private readonly TextBox _previewTags = new TextBox();
        private readonly Label _draftState = Theme.Label("", Theme.Caption, Theme.Muted);
        private readonly Label _status = Theme.Label("", Theme.Small, Theme.Muted);
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Button _generate = Theme.Button("生成所选预览", true);
        private readonly Button _apply = Theme.Button("应用所选预览");
        private readonly Button _classifyAll = Theme.Button("补全分类");
        private readonly HashSet<string> _checked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<SkillItem> _filtered = new List<SkillItem>();
        private string _editingInvocation = "";
        private bool _loadingEditor;
        private bool _editorDirty;
        private bool _busy;
        private bool _updatingList;

        private sealed class CategoryChoice
        {
            public string Value { get; set; }
            public string Label { get; set; }
            public override string ToString() => Label;
        }

        public TranslationForm(IList<SkillItem> skills, AliasStore aliases, AiService ai)
        {
            _skills = skills;
            _aliases = aliases;
            _ai = ai;
            Theme.StyleForm(this);
            Text = "批量汉化与分类";
            Size = new Size(900, 740);
            MinimumSize = new Size(780, 650);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = Program.QaMode;
            Theme.ConstrainToWorkingArea(this);
            BuildInterface();
            RestoreBatchSelection();
            RefreshCategoryChoices();
            ReloadList();
            FormClosing += (_, __) => SaveEditorDraft();
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Theme.Surface
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var titleRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleRow.Controls.Add(Theme.Label("批量汉化与分类", Theme.Heading, Theme.Text), 0, 0);
            var helper = Theme.Label("勾选多个 Skill → 生成草稿 → 编辑预览 → 批量应用", Theme.Small, Theme.Muted);
            helper.TextAlign = ContentAlignment.MiddleRight;
            helper.Margin = new Padding(8, 7, 0, 0);
            titleRow.Controls.Add(helper, 1, 0);
            titleRow.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(titleRow);

            var settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.Soft, Padding = new Padding(10) };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var note = Theme.Label("AI 接口、模型与密钥统一在“设置”中管理。密钥由 Windows 当前用户加密保存。", Theme.Small, Theme.Muted);
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            settings.Controls.Add(note, 0, 0);
            var openSettings = Theme.Button("打开设置");
            openSettings.Width = 94;
            openSettings.Height = 28;
            openSettings.Click += (_, __) =>
            {
                using (var dialog = new SettingsForm()) dialog.ShowDialog(this);
            };
            settings.Controls.Add(openSettings, 1, 0);
            settings.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(settings);

            var selector = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, BackColor = Theme.Surface };
            var categoryLabel = Theme.Label("分类", Theme.Small, Theme.Secondary);
            categoryLabel.Margin = new Padding(0, 8, 4, 0);
            selector.Controls.Add(categoryLabel);
            _listCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            _listCategory.Width = 210;
            _listCategory.Font = Theme.Small;
            _listCategory.AccessibleName = "筛选批量汉化列表的分类";
            _listCategory.SelectedIndexChanged += (_, __) => { if (!_updatingList) ReloadList(); };
            selector.Controls.Add(_listCategory);
            var selectCurrent = Theme.Button("全选当前分类");
            selectCurrent.Width = 112;
            selectCurrent.Height = 28;
            selectCurrent.Click += (_, __) => SelectVisible(false);
            selector.Controls.Add(selectCurrent);
            var selectUntranslated = Theme.Button("仅选未汉化");
            selectUntranslated.Width = 104;
            selectUntranslated.Height = 28;
            selectUntranslated.Click += (_, __) => SelectVisible(true);
            selector.Controls.Add(selectUntranslated);
            var clear = Theme.Button("清空选择");
            clear.Width = 90;
            clear.Height = 28;
            clear.Click += (_, __) => ClearSelection();
            selector.Controls.Add(clear);
            root.Controls.Add(selector);

            var content = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 4,
                SplitterDistance = 300,
                BackColor = Theme.Border,
                Margin = new Padding(0, 8, 0, 0)
            };
            _list.Dock = DockStyle.Fill;
            _list.Font = Theme.Body;
            _list.BorderStyle = BorderStyle.None;
            _list.IntegralHeight = false;
            _list.CheckOnClick = true;
            _list.DisplayMember = "VisibleName";
            _list.AccessibleName = "可复选的 Skill 汉化列表";
            _list.ItemCheck += ListItemCheck;
            _list.SelectedIndexChanged += (_, __) => LoadSelectedEditor();
            content.Panel1.BackColor = Theme.Raised;
            content.Panel1.Padding = new Padding(1);
            content.Panel1.Controls.Add(_list);

            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11, Padding = new Padding(16), BackColor = Theme.Raised, AutoScroll = true };
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var previewTitle = Theme.Label("所选 Skill 的已保存预览", Theme.Strong, Theme.Text);
            editor.Controls.Add(previewTitle);
            _invocation.Margin = new Padding(0, 4, 0, 10);
            editor.Controls.Add(_invocation);
            AddFieldLabel(editor, "中文简称");
            ConfigureEditorBox(_previewName, false, 80);
            _previewName.AccessibleName = "预览中文简称";
            editor.Controls.Add(_previewName);
            AddFieldLabel(editor, "中文用途");
            ConfigureEditorBox(_previewDescription, true, 500);
            _previewDescription.AccessibleName = "预览中文用途";
            _previewDescription.Height = 76;
            editor.Controls.Add(_previewDescription);
            AddFieldLabel(editor, "分类");
            _previewCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            _previewCategory.Items.AddRange(Categories.Cast<object>().ToArray());
            _previewCategory.Dock = DockStyle.Top;
            _previewCategory.AccessibleName = "预览分类";
            _previewCategory.SelectedIndexChanged += (_, __) => MarkEditorDirty();
            editor.Controls.Add(_previewCategory);
            AddFieldLabel(editor, "标签");
            ConfigureEditorBox(_previewTags, false, 160);
            _previewTags.AccessibleName = "预览标签";
            editor.Controls.Add(_previewTags);
            _draftState.Margin = new Padding(0, 6, 0, 0);
            editor.Controls.Add(_draftState);
            content.Panel2.Controls.Add(editor);
            root.Controls.Add(content);
            Shown += (_, __) =>
            {
                content.Panel1MinSize = 260;
                content.SplitterDistance = Math.Min(300, Math.Max(260, content.ClientSize.Width - 420));
            };

            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 58, ColumnCount = 5, Padding = new Padding(0, 9, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var index = 1; index < 5; index++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _progress.Dock = DockStyle.Fill;
            _progress.Visible = false;
            feedback.Controls.Add(_status, 0, 0);
            feedback.Controls.Add(_progress, 0, 1);
            footer.Controls.Add(feedback, 0, 0);
            _classifyAll.Width = 90;
            _classifyAll.Click += async (_, __) => await ClassifyAllAsync();
            footer.Controls.Add(_classifyAll, 1, 0);
            _generate.Width = 126;
            _generate.Click += async (_, __) => await GenerateBatchAsync();
            footer.Controls.Add(_generate, 2, 0);
            _apply.Width = 126;
            _apply.Click += (_, __) => ApplyBatch();
            footer.Controls.Add(_apply, 3, 0);
            var close = Theme.Button("完成");
            close.Width = 78;
            close.DialogResult = DialogResult.OK;
            footer.Controls.Add(close, 4, 0);
            root.Controls.Add(footer);
            CancelButton = close;
        }

        private static void AddFieldLabel(TableLayoutPanel root, string text)
        {
            var label = Theme.Label(text, Theme.Small, Theme.Secondary);
            label.Margin = new Padding(0, 8, 0, 4);
            root.Controls.Add(label);
        }

        private void ConfigureEditorBox(TextBox box, bool multiline, int maxLength)
        {
            box.Dock = DockStyle.Top;
            box.Font = Theme.Body;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Multiline = multiline;
            box.MaxLength = maxLength;
            if (multiline) box.ScrollBars = ScrollBars.Vertical;
            box.TextChanged += (_, __) => MarkEditorDirty();
        }

        private void RestoreBatchSelection()
        {
            var drafts = Storage.LoadDrafts();
            if (drafts.drafts == null) return;
            var available = new HashSet<string>(_skills.Select(skill => skill.Invocation), StringComparer.OrdinalIgnoreCase);
            foreach (var invocation in drafts.drafts.Keys.Where(available.Contains)) _checked.Add(invocation);
        }

        private void RefreshCategoryChoices()
        {
            var previous = (_listCategory.SelectedItem as CategoryChoice)?.Value ?? "";
            var groups = _skills.GroupBy(skill => string.IsNullOrWhiteSpace(skill.Category) ? "未分类" : skill.Category)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);
            var order = Categories.Concat(new[] { "未分类" }).ToArray();
            _updatingList = true;
            try
            {
                _listCategory.Items.Clear();
                _listCategory.Items.Add(new CategoryChoice { Value = "", Label = "全部分类（" + _skills.Count + "）" });
                foreach (var category in order)
                {
                    int count;
                    if (!groups.TryGetValue(category, out count)) continue;
                    _listCategory.Items.Add(new CategoryChoice { Value = category, Label = category + "（" + count + "）" });
                }
                foreach (var pair in groups.Where(pair => !order.Contains(pair.Key)).OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
                    _listCategory.Items.Add(new CategoryChoice { Value = pair.Key, Label = pair.Key + "（" + pair.Value + "）" });
                _listCategory.SelectedIndex = 0;
                for (var index = 0; index < _listCategory.Items.Count; index++)
                {
                    var choice = (CategoryChoice)_listCategory.Items[index];
                    if (!choice.Value.Equals(previous, StringComparison.CurrentCultureIgnoreCase)) continue;
                    _listCategory.SelectedIndex = index;
                    break;
                }
            }
            finally { _updatingList = false; }
        }

        private void ReloadList()
        {
            SaveEditorDraft();
            var selectedInvocation = (_list.SelectedItem as SkillItem)?.Invocation ?? _editingInvocation;
            var category = (_listCategory.SelectedItem as CategoryChoice)?.Value ?? "";
            _filtered = _skills.Where(skill =>
                    category.Length == 0
                    || (category == "未分类" && string.IsNullOrWhiteSpace(skill.Category))
                    || string.Equals(skill.Category, category, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(skill => string.IsNullOrWhiteSpace(skill.LocalizedDescription))
                .ThenBy(skill => skill.VisibleName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _updatingList = true;
            try
            {
                _list.Items.Clear();
                foreach (var skill in _filtered) _list.Items.Add(skill, _checked.Contains(skill.Invocation));
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
                for (var index = 0; index < _list.Items.Count; index++)
                {
                    var skill = (SkillItem)_list.Items[index];
                    if (!skill.Invocation.Equals(selectedInvocation, StringComparison.OrdinalIgnoreCase)) continue;
                    _list.SelectedIndex = index;
                    break;
                }
            }
            finally { _updatingList = false; }
            UpdateSelectionStatus();
            LoadSelectedEditor();
        }

        private void ListItemCheck(object sender, ItemCheckEventArgs eventArgs)
        {
            if (_updatingList || eventArgs.Index < 0 || eventArgs.Index >= _list.Items.Count) return;
            var skill = (SkillItem)_list.Items[eventArgs.Index];
            if (eventArgs.NewValue == CheckState.Checked) _checked.Add(skill.Invocation);
            else _checked.Remove(skill.Invocation);
            BeginInvoke(new Action(() => UpdateSelectionStatus()));
        }

        private void SelectVisible(bool untranslatedOnly)
        {
            if (untranslatedOnly) _checked.Clear();
            _updatingList = true;
            try
            {
                for (var index = 0; index < _list.Items.Count; index++)
                {
                    var skill = (SkillItem)_list.Items[index];
                    var shouldCheck = !untranslatedOnly || string.IsNullOrWhiteSpace(skill.DisplayName) || string.IsNullOrWhiteSpace(skill.LocalizedDescription);
                    _list.SetItemChecked(index, shouldCheck);
                    if (shouldCheck) _checked.Add(skill.Invocation);
                    else _checked.Remove(skill.Invocation);
                }
            }
            finally { _updatingList = false; }
            UpdateSelectionStatus();
        }

        private void ClearSelection()
        {
            _checked.Clear();
            _updatingList = true;
            try { for (var index = 0; index < _list.Items.Count; index++) _list.SetItemChecked(index, false); }
            finally { _updatingList = false; }
            UpdateSelectionStatus();
        }

        private void UpdateSelectionStatus(bool preserveMessage = false)
        {
            if (_busy) return;
            var drafts = Storage.LoadDrafts();
            var draftCount = drafts.drafts == null ? 0 : _checked.Count(drafts.drafts.ContainsKey);
            var pendingCount = Math.Max(0, _checked.Count - draftCount);
            if (!preserveMessage)
            {
                _status.ForeColor = Theme.Muted;
                _status.Text = "已选择 " + _checked.Count + " 项 · " + draftCount + " 项已有预览 · " + pendingCount + " 项待生成";
            }
            _generate.Enabled = pendingCount > 0;
            _apply.Enabled = draftCount > 0;
        }

        private void LoadSelectedEditor()
        {
            SaveEditorDraft();
            var skill = _list.SelectedItem as SkillItem;
            _loadingEditor = true;
            try
            {
                _editingInvocation = skill?.Invocation ?? "";
                var enabled = skill != null;
                _previewName.Enabled = enabled;
                _previewDescription.Enabled = enabled;
                _previewCategory.Enabled = enabled;
                _previewTags.Enabled = enabled;
                if (!enabled)
                {
                    _invocation.Text = "当前分类没有 Skill。";
                    _previewName.Clear();
                    _previewDescription.Clear();
                    _previewCategory.SelectedIndex = -1;
                    _previewTags.Clear();
                    _draftState.Text = "";
                    return;
                }
                var drafts = Storage.LoadDrafts();
                TranslationDraft draft = null;
                var hasDraft = drafts.drafts != null && drafts.drafts.TryGetValue(skill.Invocation, out draft) && draft != null;
                var suggestion = hasDraft ? draft.suggestion : new TranslationSuggestion
                {
                    shortName = skill.DisplayName,
                    descriptionZh = skill.LocalizedDescription,
                    category = string.IsNullOrWhiteSpace(skill.Category) ? "其他" : skill.Category,
                    tags = skill.Tags ?? new List<string>(),
                    engine = "saved"
                };
                _invocation.Text = "$" + skill.Invocation + "  ·  " + skill.Source;
                _previewName.Text = suggestion?.shortName ?? "";
                _previewDescription.Text = suggestion?.descriptionZh ?? "";
                var category = suggestion == null || !Categories.Contains(suggestion.category) ? "其他" : suggestion.category;
                _previewCategory.SelectedItem = category;
                _previewTags.Text = string.Join("、", suggestion?.tags ?? new List<string>());
                _draftState.Text = hasDraft ? "此项预览已自动保存；修改后切换条目也会继续保存。" : "尚无 AI 预览；可勾选后批量生成。";
            }
            finally
            {
                _loadingEditor = false;
                _editorDirty = false;
            }
        }

        private void MarkEditorDirty()
        {
            if (!_loadingEditor && _editingInvocation.Length > 0) _editorDirty = true;
        }

        private void SaveEditorDraft()
        {
            if (_loadingEditor || !_editorDirty || _editingInvocation.Length == 0) return;
            var drafts = Storage.LoadDrafts();
            if (drafts.drafts == null) drafts.drafts = new Dictionary<string, TranslationDraft>(StringComparer.OrdinalIgnoreCase);
            drafts.drafts[_editingInvocation] = new TranslationDraft
            {
                invocation = _editingInvocation,
                generatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                suggestion = new TranslationSuggestion
                {
                    shortName = _previewName.Text.Trim(),
                    descriptionZh = _previewDescription.Text.Trim(),
                    category = Convert.ToString(_previewCategory.SelectedItem) ?? "其他",
                    tags = ParseTags(_previewTags.Text),
                    engine = "edited"
                }
            };
            Storage.SaveDrafts(drafts);
            _editorDirty = false;
            _draftState.Text = "修改后的预览已保存。";
            UpdateSelectionStatus(true);
        }

        private async Task GenerateBatchAsync()
        {
            if (_busy) return;
            SaveEditorDraft();
            var drafts = Storage.LoadDrafts();
            var selected = _skills.Where(skill => _checked.Contains(skill.Invocation)
                    && (drafts.drafts == null || !drafts.drafts.ContainsKey(skill.Invocation)))
                .ToList();
            if (_checked.Count == 0) { ShowError("请先勾选需要汉化的 Skill。"); return; }
            if (selected.Count == 0)
            {
                _status.ForeColor = Theme.PrimaryDark;
                _status.Text = "所选 Skill 均有已保存预览，不会重复生成。";
                UpdateSelectionStatus(true);
                return;
            }
            if (!_ai.IsConfigured) { ShowError("请先在设置中配置 AI 接口、模型和 API Key。"); return; }
            SetBusy(true, "准备批量生成预览…", selected.Count);
            var completed = 0;
            try
            {
                for (var offset = 0; offset < selected.Count; offset += 10)
                {
                    var batch = selected.Skip(offset).Take(10).ToList();
                    _status.Text = "正在生成并保存 " + (offset + 1) + "–" + Math.Min(offset + batch.Count, selected.Count) + "/" + selected.Count;
                    var results = await _ai.RecommendBatchAsync(batch, CancellationToken.None);
                    foreach (var skill in batch)
                    {
                        if (results.ContainsKey(skill.Invocation)) { completed++; continue; }
                        await _ai.RecommendAsync(skill, CancellationToken.None);
                        completed++;
                    }
                    _progress.Value = Math.Min(completed, _progress.Maximum);
                }
                LoadSelectedEditor();
                _status.ForeColor = Theme.PrimaryDark;
                _status.Text = "已生成并自动保存 " + completed + " 个预览，可逐项修改或取消勾选。";
            }
            catch (Exception error) { ShowError("已保存 " + completed + " 项；" + error.Message); }
            finally { SetBusy(false, _status.Text, 0); UpdateSelectionStatus(true); }
        }

        private void ApplyBatch()
        {
            if (_busy) return;
            SaveEditorDraft();
            var drafts = Storage.LoadDrafts();
            if (drafts.drafts == null) { ShowError("所选 Skill 暂无可应用的预览。"); return; }
            var applied = 0;
            foreach (var skill in _skills.Where(skill => _checked.Contains(skill.Invocation)).ToList())
            {
                TranslationDraft draft;
                if (!drafts.drafts.TryGetValue(skill.Invocation, out draft) || draft?.suggestion == null) continue;
                skill.DisplayName = draft.suggestion.shortName ?? "";
                skill.LocalizedDescription = draft.suggestion.descriptionZh ?? "";
                skill.Category = draft.suggestion.category ?? "其他";
                skill.Tags = draft.suggestion.tags ?? new List<string>();
                SaveAlias(skill);
                drafts.drafts.Remove(skill.Invocation);
                _checked.Remove(skill.Invocation);
                applied++;
            }
            if (applied == 0) { ShowError("所选 Skill 暂无可应用的预览。"); return; }
            Storage.SaveAliases(_aliases);
            Storage.SaveDrafts(drafts);
            RefreshCategoryChoices();
            ReloadList();
            _status.ForeColor = Theme.PrimaryDark;
            _status.Text = "已批量应用并保存 " + applied + " 个 Skill。";
        }

        private async Task ClassifyAllAsync()
        {
            if (_busy) return;
            SaveEditorDraft();
            if (!_ai.IsConfigured) { ShowError("请先在设置中配置 AI 接口、模型和 API Key。"); return; }
            var pending = _skills.Where(skill => string.IsNullOrWhiteSpace(skill.Category)).ToList();
            if (pending.Count == 0) { _status.Text = "所有 Skill 都已有分类。"; return; }
            SetBusy(true, "准备自动分类…", pending.Count);
            var completed = 0;
            try
            {
                for (var offset = 0; offset < pending.Count; offset += 20)
                {
                    var batch = pending.Skip(offset).Take(20).ToList();
                    _status.Text = "正在分类 " + Math.Min(offset + batch.Count, pending.Count) + "/" + pending.Count;
                    var results = await _ai.ClassifyBatchAsync(batch, CancellationToken.None);
                    foreach (var skill in batch)
                    {
                        TranslationSuggestion result;
                        if (!results.TryGetValue(skill.Invocation, out result)) result = await _ai.ClassifyAsync(skill, CancellationToken.None);
                        skill.Category = result.category;
                        skill.Tags = result.tags ?? new List<string>();
                        SaveAlias(skill);
                        completed++;
                    }
                    Storage.SaveAliases(_aliases);
                    _progress.Value = Math.Min(completed, _progress.Maximum);
                }
                RefreshCategoryChoices();
                ReloadList();
                _status.ForeColor = Theme.PrimaryDark;
                _status.Text = "已自动分类并保存 " + completed + " 个 Skill。";
            }
            catch (Exception error) { ShowError("已保存 " + completed + " 项；" + error.Message); }
            finally { SetBusy(false, _status.Text, 0); UpdateSelectionStatus(true); }
        }

        private void SaveAlias(SkillItem skill)
        {
            _aliases.skills[skill.Invocation] = new AliasEntry
            {
                displayName = skill.DisplayName,
                localizedDescription = skill.LocalizedDescription,
                favorite = skill.Favorite,
                category = skill.Category,
                tags = skill.Tags ?? new List<string>()
            };
        }

        private void SetBusy(bool value, string message, int maximum)
        {
            _busy = value;
            _generate.Enabled = !value;
            _apply.Enabled = !value;
            _classifyAll.Enabled = !value;
            _list.Enabled = !value;
            _listCategory.Enabled = !value;
            _progress.Visible = value && maximum > 0;
            if (_progress.Visible)
            {
                _progress.Minimum = 0;
                _progress.Maximum = Math.Max(1, maximum);
                _progress.Value = 0;
            }
            _status.Text = message;
        }

        private void ShowError(string message)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = message;
        }

        private static List<string> ParseTags(string value)
        {
            return (value ?? "").Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(8)
                .ToList();
        }
    }
}
