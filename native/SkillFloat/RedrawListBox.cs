using System;
using System.Windows.Forms;

namespace SkillFloat
{
    internal sealed class RedrawListBox : ListBox
    {
        public RedrawListBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RedrawNow();
        }

        public void RedrawNow()
        {
            if (!IsHandleCreated || IsDisposed) return;
            NativeMethods.RedrawWindow(
                Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RedrawInvalidate
                | NativeMethods.RedrawErase
                | NativeMethods.RedrawFrame
                | NativeMethods.RedrawUpdateNow);
        }
    }
}
