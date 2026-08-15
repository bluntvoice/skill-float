using System.Drawing;
using System.Windows.Forms;

namespace SkillFloat
{
    internal static class Theme
    {
        public static readonly Color Surface = Color.FromArgb(251, 253, 252);
        public static readonly Color Raised = Color.White;
        public static readonly Color Soft = Color.FromArgb(241, 247, 245);
        public static readonly Color Text = Color.FromArgb(23, 52, 50);
        public static readonly Color Secondary = Color.FromArgb(83, 107, 104);
        public static readonly Color Muted = Color.FromArgb(96, 118, 114);
        public static readonly Color Border = Color.FromArgb(216, 229, 226);
        public static readonly Color BorderStrong = Color.FromArgb(191, 212, 207);
        public static readonly Color Primary = Color.FromArgb(20, 125, 115);
        public static readonly Color PrimaryDark = Color.FromArgb(14, 101, 94);
        public static readonly Color PrimarySoft = Color.FromArgb(225, 241, 237);
        public static readonly Color Danger = Color.FromArgb(183, 59, 69);
        public static readonly Font Body = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Caption = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Strong = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Heading = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Mono = new Font("Consolas", 8F, FontStyle.Regular, GraphicsUnit.Point);

        public static Button Button(string text, bool primary = false)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = false,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                Font = Strong,
                Cursor = Cursors.Hand,
                BackColor = primary ? Primary : Raised,
                ForeColor = primary ? Color.White : PrimaryDark,
                TabStop = true
            };
            button.FlatAppearance.BorderColor = primary ? Primary : BorderStrong;
            button.FlatAppearance.MouseOverBackColor = primary ? PrimaryDark : PrimarySoft;
            button.FlatAppearance.MouseDownBackColor = primary ? PrimaryDark : Color.FromArgb(214, 235, 230);
            return button;
        }

        public static Label Label(string text, Font font = null, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, BackColor = Color.Transparent };
        }

        public static TextBox TextBox(string placeholder = "")
        {
            return new TextBox
            {
                Font = Body,
                ForeColor = Text,
                BackColor = Raised,
                BorderStyle = BorderStyle.FixedSingle,
                Tag = placeholder
            };
        }

        public static void StyleForm(Form form)
        {
            form.Font = Body;
            form.ForeColor = Text;
            form.BackColor = Surface;
            form.StartPosition = FormStartPosition.CenterParent;
        }
    }
}
