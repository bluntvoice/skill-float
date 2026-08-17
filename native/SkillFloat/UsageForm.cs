using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SkillFloat
{
    internal sealed class UsageForm : Form
    {
        private readonly IList<SkillItem> _skills;
        private UsageSummary _summary;
        private readonly Func<Task> _refresh;
        private readonly Label _total = Theme.Label("0", new Font("Segoe UI", 24F, FontStyle.Bold), Theme.PrimaryDark);
        private readonly Label _used = Theme.Label("0 个 Skill", Theme.Small, Theme.Secondary);
        private readonly ListView _list = new ListView();
        private readonly Label _source = Theme.Label("", Theme.Small, Theme.Muted);
        private readonly Button _refreshButton = Theme.Button("刷新统计", true);

        public UsageForm(IList<SkillItem> skills, UsageSummary summary, Func<Task> refresh)
        {
            _skills = skills;
            _summary = summary ?? new UsageSummary();
            _refresh = refresh;
            Theme.StyleForm(this);
            Text = "Skill 使用统计";
            Size = new Size(620, 620);
            MinimumSize = new Size(560, 520);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            Theme.ConstrainToWorkingArea(this);
            BuildInterface();
            Render();
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 5, BackColor = Theme.Surface };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            root.Controls.Add(Theme.Label("Skill 使用统计", Theme.Heading, Theme.Text));

            var metric = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Soft, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 10, 0, 8) };
            metric.Controls.Add(_total);
            _used.Margin = new Padding(12, 25, 0, 0);
            metric.Controls.Add(_used);
            root.Controls.Add(metric);
            _source.AutoSize = false;
            _source.Dock = DockStyle.Fill;
            _source.Height = 42;
            _source.TextAlign = ContentAlignment.MiddleLeft;
            _source.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(_source);

            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.Font = Theme.Body;
            _list.ShowItemToolTips = true;
            _list.Columns.Add("Skill", 210);
            _list.Columns.Add("分类", 105);
            _list.Columns.Add("调用次数", 90, HorizontalAlignment.Right);
            _list.Columns.Add("来源", 110);
            root.Controls.Add(_list);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var close = Theme.Button("完成");
            close.Width = 78;
            close.DialogResult = DialogResult.OK;
            _refreshButton.Width = 100;
            _refreshButton.Click += async (_, __) => await RefreshAsync();
            footer.Controls.Add(close);
            footer.Controls.Add(_refreshButton);
            root.Controls.Add(footer);
            AcceptButton = close;
        }

        private async Task RefreshAsync()
        {
            _refreshButton.Enabled = false;
            _refreshButton.Text = "正在读取…";
            try
            {
                await _refresh();
                _summary = UsageScanner.Current(_skills);
                Render();
            }
            catch (Exception error) { _source.ForeColor = Theme.Danger; _source.Text = error.Message; }
            finally { _refreshButton.Enabled = true; _refreshButton.Text = "刷新统计"; }
        }

        private void Render()
        {
            _total.Text = _summary.Total.ToString("N0");
            _used.Text = _summary.UsedSkills + " 个 Skill 被调用";
            var sources = (_summary.Sources ?? new List<UsageSourceSummary>())
                .Select(item => item.Name + " " + item.Count.ToString("N0") + " 次" + (item.Detected ? "" : "（未发现记录）"));
            _source.ForeColor = Theme.Muted;
            _source.Text = "来源：" + string.Join("  ·  ", sources);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var skill in _skills.OrderByDescending(item => item.UsageCount).ThenBy(item => item.VisibleName, StringComparer.CurrentCultureIgnoreCase))
            {
                var sourceText = skill.UsageSources == null || skill.UsageSources.Count == 0
                    ? "—"
                    : string.Join("/", skill.UsageSources.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).Select(pair => pair.Key));
                var row = new ListViewItem(skill.VisibleName);
                row.ToolTipText = skill.VisibleName + "\n$" + skill.Invocation + "\n" + skill.VisibleDescription;
                row.SubItems.Add(string.IsNullOrWhiteSpace(skill.Category) ? "未分类" : skill.Category);
                row.SubItems.Add(skill.UsageCount.ToString("N0"));
                row.SubItems.Add(sourceText);
                if (skill.UsageCount == 0) row.ForeColor = Theme.Muted;
                _list.Items.Add(row);
            }
            _list.EndUpdate();
        }
    }
}
