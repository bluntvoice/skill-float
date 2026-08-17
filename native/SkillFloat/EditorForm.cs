using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SkillFloat
{
    internal sealed class EditorForm : Form
    {
        private static readonly string[] Categories = { "", "开发与代码", "文档与内容", "设计与多媒体", "数据与自动化", "法律与专业", "沟通与协作", "其他" };
        private readonly SkillItem _skill;
        private readonly AiService _ai;
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _description = new TextBox();
        private readonly ComboBox _category = new ComboBox();
        private readonly TextBox _tags = new TextBox();
        private readonly Label _message = Theme.Label("", Theme.Small, Theme.Muted);
        private readonly Button _recommend = Theme.Button("生成 AI 推荐");

        public EditorForm(SkillItem skill, AiService ai)
        {
            _skill = skill;
            _ai = ai;
            Theme.StyleForm(this);
            Text = "汉化与重命名";
            Size = new Size(540, 640);
            MinimumSize = new Size(500, 580);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Theme.ConstrainToWorkingArea(this);
            BuildInterface();
            RestoreDraft();
        }

        private void BuildInterface()
        {
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Surface };
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            Controls.Add(shell);

            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 20, 20, 8), BackColor = Theme.Surface };
            var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Surface };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroller.Controls.Add(root);
            shell.Controls.Add(scroller, 0, 0);

            var heading = Theme.Label("汉化与重命名", Theme.Heading, Theme.Text);
            var invocation = Theme.Label("$" + _skill.Invocation, Theme.Mono, Theme.Muted);
            invocation.AutoSize = false;
            invocation.Height = 24;
            invocation.Dock = DockStyle.Top;
            invocation.AutoEllipsis = true;
            root.Controls.Add(heading);
            root.Controls.Add(invocation);
            _recommend.Height = 38;
            _recommend.Dock = DockStyle.Top;
            _recommend.Margin = new Padding(0, 14, 0, 8);
            _recommend.Click += async (_, __) => await RecommendAsync();
            root.Controls.Add(_recommend);
            _message.Margin = new Padding(0, 2, 0, 8);
            root.Controls.Add(_message);

            AddLabel(root, "中文名称");
            _name.Text = _skill.DisplayName;
            _name.MaxLength = 80;
            _name.Height = 34;
            _name.Dock = DockStyle.Top;
            root.Controls.Add(_name);
            root.Controls.Add(Theme.Label("只改变悬浮窗显示，真实调用名保持不变。", Theme.Caption, Theme.Muted));

            AddLabel(root, "中文用途");
            _description.Text = _skill.LocalizedDescription;
            _description.Multiline = true;
            _description.MaxLength = 500;
            _description.Height = 86;
            _description.ScrollBars = ScrollBars.Vertical;
            _description.Dock = DockStyle.Top;
            root.Controls.Add(_description);

            AddLabel(root, "分类");
            _category.DropDownStyle = ComboBoxStyle.DropDownList;
            _category.Items.AddRange(Categories.Cast<object>().ToArray());
            _category.SelectedItem = Categories.Contains(_skill.Category) ? _skill.Category : "";
            _category.Dock = DockStyle.Top;
            root.Controls.Add(_category);

            AddLabel(root, "标签");
            _tags.Text = string.Join("、", _skill.Tags);
            _tags.MaxLength = 160;
            _tags.Dock = DockStyle.Top;
            root.Controls.Add(_tags);
            root.Controls.Add(Theme.Label("用顿号或逗号分隔，最多 8 个。", Theme.Caption, Theme.Muted));

            var original = Theme.Label("原始说明\n" + (string.IsNullOrWhiteSpace(_skill.Description) ? "此 Skill 没有提供原始说明。" : _skill.Description), Theme.Small, Theme.Secondary);
            original.AutoSize = false;
            original.Height = 70;
            original.Dock = DockStyle.Top;
            original.Padding = new Padding(10);
            original.BackColor = Theme.Soft;
            root.Controls.Add(original);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(20, 10, 20, 8), BackColor = Theme.Surface, WrapContents = false };
            var save = Theme.Button("保存显示信息", true);
            save.Width = 132;
            save.DialogResult = DialogResult.OK;
            save.Click += (_, __) => SaveValues();
            var cancel = Theme.Button("取消");
            cancel.Width = 82;
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            shell.Controls.Add(actions, 0, 1);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static void AddLabel(TableLayoutPanel root, string text)
        {
            var label = Theme.Label(text, Theme.Strong, Theme.Text);
            label.Margin = new Padding(0, 10, 0, 5);
            root.Controls.Add(label);
        }

        private async System.Threading.Tasks.Task RecommendAsync()
        {
            _recommend.Enabled = false;
            _recommend.Text = "正在生成并保存预览…";
            _message.ForeColor = Theme.Muted;
            try
            {
                var suggestion = await _ai.RecommendAsync(_skill, CancellationToken.None);
                ApplySuggestion(suggestion);
                _message.Text = "推荐草稿已自动保存，关闭后重新打开仍可恢复。";
            }
            catch (Exception error)
            {
                _message.ForeColor = Theme.Danger;
                _message.Text = error.Message;
            }
            finally { _recommend.Enabled = true; _recommend.Text = "重新生成 AI 推荐"; }
        }

        private void RestoreDraft()
        {
            var drafts = Storage.LoadDrafts();
            TranslationDraft draft;
            if (drafts.drafts == null || !drafts.drafts.TryGetValue(_skill.Invocation, out draft) || draft == null) return;
            ApplySuggestion(draft.suggestion);
            _message.Text = "已恢复上次自动保存的推荐草稿。";
        }

        private void ApplySuggestion(TranslationSuggestion suggestion)
        {
            if (suggestion == null) return;
            _name.Text = suggestion.shortName;
            _description.Text = suggestion.descriptionZh;
            _category.SelectedItem = Categories.Contains(suggestion.category) ? suggestion.category : "其他";
            _tags.Text = string.Join("、", suggestion.tags ?? new System.Collections.Generic.List<string>());
        }

        private void SaveValues()
        {
            _skill.DisplayName = _name.Text.Trim();
            _skill.LocalizedDescription = _description.Text.Trim();
            _skill.Category = Convert.ToString(_category.SelectedItem) ?? "";
            _skill.Tags = _tags.Text.Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(8).ToList();
            var drafts = Storage.LoadDrafts();
            if (drafts.drafts != null && drafts.drafts.Remove(_skill.Invocation)) Storage.SaveDrafts(drafts);
        }
    }
}
