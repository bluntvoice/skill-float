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
        private readonly IList<SkillItem> _skills;
        private readonly AliasStore _aliases;
        private readonly AiService _ai;
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _endpoint = new TextBox();
        private readonly TextBox _model = new TextBox();
        private readonly TextBox _apiKey = new TextBox();
        private readonly Label _preview = Theme.Label("请选择一个 Skill。", Theme.Body, Theme.Secondary);
        private readonly Label _status = Theme.Label("", Theme.Small, Theme.Muted);
        private readonly Button _generate = Theme.Button("生成并保存预览", true);
        private readonly Button _apply = Theme.Button("应用当前预览");
        private readonly Button _classifyAll = Theme.Button("分类未归类项");
        private TranslationSuggestion _suggestion;
        private bool _busy;

        public TranslationForm(IList<SkillItem> skills, AliasStore aliases, AiService ai)
        {
            _skills = skills;
            _aliases = aliases;
            _ai = ai;
            Theme.StyleForm(this);
            Text = "AI 汉化与分类";
            Size = new Size(760, 650);
            MinimumSize = new Size(700, 600);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            BuildInterface();
            LoadSettings();
            ReloadList();
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4, BackColor = Theme.Surface };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var heading = Theme.Label("AI 汉化与分类", Theme.Heading, Theme.Text);
            heading.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(heading);

            var settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, BackColor = Theme.Soft, Padding = new Padding(10) };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            settings.Controls.Add(Theme.Label("接口", Theme.Small, Theme.Secondary), 0, 0);
            _endpoint.Dock = DockStyle.Fill;
            _endpoint.Font = Theme.Small;
            settings.Controls.Add(_endpoint, 1, 0);
            settings.Controls.Add(Theme.Label("模型", Theme.Small, Theme.Secondary), 2, 0);
            _model.Dock = DockStyle.Fill;
            _model.Font = Theme.Small;
            settings.Controls.Add(_model, 3, 0);
            settings.Controls.Add(Theme.Label("API 密钥", Theme.Small, Theme.Secondary), 0, 1);
            _apiKey.Dock = DockStyle.Fill;
            _apiKey.UseSystemPasswordChar = true;
            _apiKey.Font = Theme.Small;
            settings.SetColumnSpan(_apiKey, 3);
            settings.Controls.Add(_apiKey, 1, 1);
            var saveSettings = Theme.Button("保存配置");
            saveSettings.Width = 90;
            saveSettings.Height = 28;
            saveSettings.Click += (_, __) => SaveSettings();
            settings.Controls.Add(saveSettings, 0, 2);
            settings.SetColumnSpan(saveSettings, 2);
            var note = Theme.Label("密钥使用 Windows 当前用户加密保存，升级不会删除。", Theme.Caption, Theme.Muted);
            settings.Controls.Add(note, 2, 2);
            settings.SetColumnSpan(note, 2);
            settings.Margin = new Padding(0, 0, 0, 12);
            root.Controls.Add(settings);

            var content = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 250, BackColor = Theme.Border, FixedPanel = FixedPanel.Panel1 };
            _list.Dock = DockStyle.Fill;
            _list.Font = Theme.Body;
            _list.BorderStyle = BorderStyle.None;
            _list.IntegralHeight = false;
            _list.SelectedIndexChanged += (_, __) => ShowDraft();
            content.Panel1.BackColor = Theme.Raised;
            content.Panel1.Padding = new Padding(1);
            content.Panel1.Controls.Add(_list);

            var previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Raised, Padding = new Padding(16) };
            _preview.Dock = DockStyle.Fill;
            _preview.AutoSize = false;
            _preview.Font = Theme.Body;
            _preview.Padding = new Padding(4);
            previewPanel.Controls.Add(_preview);
            var previewTitle = Theme.Label("已保存预览", Theme.Strong, Theme.Text);
            previewTitle.Dock = DockStyle.Top;
            previewTitle.Height = 28;
            previewPanel.Controls.Add(previewTitle);
            content.Panel2.Controls.Add(previewPanel);
            root.Controls.Add(content);

            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 5, Padding = new Padding(0, 9, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var index = 1; index < 5; index++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(_status, 0, 0);
            _classifyAll.Width = 116;
            _classifyAll.Click += async (_, __) => await ClassifyAllAsync();
            footer.Controls.Add(_classifyAll, 1, 0);
            _generate.Width = 132;
            _generate.Click += async (_, __) => await GenerateAsync();
            footer.Controls.Add(_generate, 2, 0);
            _apply.Width = 100;
            _apply.Click += (_, __) => ApplyCurrent();
            footer.Controls.Add(_apply, 3, 0);
            var close = Theme.Button("完成");
            close.Width = 78;
            close.DialogResult = DialogResult.OK;
            footer.Controls.Add(close, 4, 0);
            root.Controls.Add(footer);
            AcceptButton = close;
        }

        private void LoadSettings()
        {
            var settings = Storage.LoadSettings();
            _endpoint.Text = settings.endpoint ?? "https://api.openai.com/v1";
            _model.Text = settings.model ?? "";
            var key = Storage.LoadApiKey();
            _apiKey.Text = key;
        }

        private void SaveSettings()
        {
            Storage.SaveSettings(new TranslationSettings { endpoint = _endpoint.Text.Trim(), model = _model.Text.Trim() });
            Storage.SaveApiKey(_apiKey.Text);
            _status.ForeColor = Theme.PrimaryDark;
            _status.Text = "AI 配置已加密保存。";
        }

        private SkillItem SelectedSkill => _list.SelectedItem as SkillItem;

        private void ReloadList()
        {
            var selectedInvocation = SelectedSkill == null ? "" : SelectedSkill.Invocation;
            _list.BeginUpdate();
            _list.DataSource = null;
            _list.DisplayMember = "VisibleName";
            _list.DataSource = _skills.OrderBy(skill => string.IsNullOrWhiteSpace(skill.LocalizedDescription)).ThenBy(skill => skill.VisibleName, StringComparer.CurrentCultureIgnoreCase).ToList();
            _list.EndUpdate();
            if (!string.IsNullOrEmpty(selectedInvocation))
            {
                for (var index = 0; index < _list.Items.Count; index++)
                    if (((SkillItem)_list.Items[index]).Invocation == selectedInvocation) { _list.SelectedIndex = index; break; }
            }
            if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void ShowDraft()
        {
            var skill = SelectedSkill;
            _suggestion = null;
            if (skill == null) { _preview.Text = "请选择一个 Skill。"; return; }
            var drafts = Storage.LoadDrafts();
            TranslationDraft draft;
            if (drafts.drafts != null && drafts.drafts.TryGetValue(skill.Invocation, out draft) && draft != null)
            {
                _suggestion = draft.suggestion;
                _status.Text = "已恢复自动保存的预览。";
                _generate.Text = "重新生成预览";
            }
            else
            {
                _suggestion = new TranslationSuggestion
                {
                    shortName = skill.DisplayName,
                    descriptionZh = skill.LocalizedDescription,
                    category = string.IsNullOrWhiteSpace(skill.Category) ? "其他" : skill.Category,
                    tags = skill.Tags ?? new List<string>(),
                    engine = "saved"
                };
                _generate.Text = "生成并保存预览";
            }
            RenderPreview();
        }

        private void RenderPreview()
        {
            var skill = SelectedSkill;
            if (skill == null || _suggestion == null) return;
            _preview.Text = string.Join(Environment.NewLine + Environment.NewLine, new[]
            {
                "调用名  $" + skill.Invocation,
                "中文简称  " + Empty(_suggestion.shortName),
                "主要用途  " + Empty(_suggestion.descriptionZh),
                "分类  " + Empty(_suggestion.category),
                "标签  " + ((_suggestion.tags == null || _suggestion.tags.Count == 0) ? "—" : string.Join(" · ", _suggestion.tags)),
                string.IsNullOrWhiteSpace(_suggestion.notice) ? "" : "提示  " + _suggestion.notice
            }.Where(line => line.Length > 0));
        }

        private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        private async Task GenerateAsync()
        {
            if (_busy || SelectedSkill == null) return;
            SaveSettings();
            SetBusy(true, "正在生成，结果会立即保存…");
            try
            {
                _suggestion = await _ai.RecommendAsync(SelectedSkill, CancellationToken.None);
                RenderPreview();
                _status.ForeColor = Theme.PrimaryDark;
                _status.Text = "预览已自动保存，误关闭后仍可恢复。";
                _generate.Text = "重新生成预览";
            }
            catch (Exception error) { ShowError(error.Message); }
            finally { SetBusy(false, _status.Text); }
        }

        private async Task ClassifyAllAsync()
        {
            if (_busy) return;
            SaveSettings();
            var pending = _skills.Where(skill => string.IsNullOrWhiteSpace(skill.Category)).ToList();
            if (pending.Count == 0) { _status.Text = "所有 Skill 都已有分类。"; return; }
            SetBusy(true, "准备自动分类…");
            var completed = 0;
            try
            {
                for (var offset = 0; offset < pending.Count; offset += 20)
                {
                    var batch = pending.Skip(offset).Take(20).ToList();
                    _status.Text = "正在分类 " + Math.Min(offset + batch.Count, pending.Count) + "/" + pending.Count;
                    var results = await _ai.ClassifyBatchAsync(batch, CancellationToken.None);
                    var unresolved = new List<SkillItem>();
                    foreach (var skill in batch)
                    {
                        TranslationSuggestion result;
                        if (!results.TryGetValue(skill.Invocation, out result)) { unresolved.Add(skill); continue; }
                        skill.Category = result.category;
                        skill.Tags = result.tags ?? new List<string>();
                        SaveAlias(skill);
                        completed++;
                    }
                    foreach (var skill in unresolved)
                    {
                        var result = await _ai.ClassifyAsync(skill, CancellationToken.None);
                        skill.Category = result.category;
                        skill.Tags = result.tags ?? new List<string>();
                        SaveAlias(skill);
                        completed++;
                    }
                    Storage.SaveAliases(_aliases);
                }
                ReloadList();
                _status.ForeColor = Theme.PrimaryDark;
                _status.Text = "已自动分类并保存 " + completed + " 个 Skill。";
            }
            catch (Exception error) { ShowError("已保存 " + completed + " 个；" + error.Message); }
            finally { SetBusy(false, _status.Text); }
        }

        private void ApplyCurrent()
        {
            var skill = SelectedSkill;
            if (skill == null || _suggestion == null) return;
            skill.DisplayName = _suggestion.shortName ?? "";
            skill.LocalizedDescription = _suggestion.descriptionZh ?? "";
            skill.Category = _suggestion.category ?? "其他";
            skill.Tags = _suggestion.tags ?? new List<string>();
            SaveAlias(skill);
            Storage.SaveAliases(_aliases);
            var drafts = Storage.LoadDrafts();
            if (drafts.drafts != null && drafts.drafts.Remove(skill.Invocation)) Storage.SaveDrafts(drafts);
            _status.ForeColor = Theme.PrimaryDark;
            _status.Text = "当前预览已应用并保存。";
            ReloadList();
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

        private void SetBusy(bool value, string message)
        {
            _busy = value;
            _generate.Enabled = !value;
            _apply.Enabled = !value;
            _classifyAll.Enabled = !value;
            _list.Enabled = !value;
            _status.Text = message;
        }

        private void ShowError(string message)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = message;
        }
    }
}
