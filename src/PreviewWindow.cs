using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ParkCapture
{
    // Full-screen viewer for library captures: arrows/wheel move between images, Esc or a click outside the image closes.
    internal sealed class PreviewWindow : Form
    {
        private readonly IList<string> paths;
        private Image image;
        internal int Index { get; private set; }
        internal string CurrentPath { get { return paths[Index]; } }
        internal RectangleF ImageBounds { get; private set; }

        internal PreviewWindow(IList<string> paths, int index)
        {
            this.paths = paths; Index = Math.Max(0, Math.Min(paths.Count - 1, index));
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; KeyPreview = true;
            BackColor = Color.FromArgb(24, 24, 27); DoubleBuffered = true; Text = Words.T("크게 보기", "Preview");
            AccessibleName = Words.T("캡처 크게 보기", "Capture preview");
            LoadCurrent();
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Bounds = Owner != null ? Screen.FromControl(Owner).Bounds : Screen.PrimaryScreen.Bounds;
        }
        internal void Step(int step)
        {
            int next = Math.Max(0, Math.Min(paths.Count - 1, Index + step));
            if (next == Index) return;
            Index = next; LoadCurrent();
        }
        private void LoadCurrent()
        {
            if (image != null) image.Dispose();
            image = null;
            try
            {
                // Render the saved project so annotations appear at full resolution (preview.png is only a thumbnail).
                using (var doc = Document.Open(paths[Index])) image = doc.Render();
            }
            catch (Exception) { image = null; }
            Invalidate();
        }
        private Rectangle CloseBounds { get { return new Rectangle(ClientSize.Width - 64, 18, 44, 44); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new RectangleF(40, 76, ClientSize.Width - 80, ClientSize.Height - 76 - 72);
            if (image != null && area.Width > 0 && area.Height > 0)
            {
                // Fit the screen; small captures may grow up to 2x so they are actually "big".
                float scale = Math.Min(2f, Math.Min(area.Width / image.Width, area.Height / image.Height));
                float w = image.Width * scale, h = image.Height * scale;
                ImageBounds = new RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
                using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(shadow, ImageBounds.X + 4, ImageBounds.Y + 6, w, h);
                g.DrawImage(image, ImageBounds);
            }
            else
            {
                ImageBounds = RectangleF.Empty;
                using (var f = Style.Font(14)) TextRenderer.DrawText(g, Words.T("이미지를 열 수 없습니다.", "This image cannot be opened."), f, ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            string name = Path.GetFileName(CurrentPath);
            using (var f = Style.Font(11, FontStyle.Bold)) TextRenderer.DrawText(g, name, f, new Rectangle(40, 22, ClientSize.Width - 140, 32), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            string help = (Index + 1) + " / " + paths.Count + "   ·   " + Words.T("← → 이전·다음   ·   Esc 닫기", "← → previous/next   ·   Esc close");
            using (var f = Style.Font(10)) TextRenderer.DrawText(g, help, f, new Rectangle(0, ClientSize.Height - 56, ClientSize.Width, 32), Color.FromArgb(190, 190, 198), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var close = CloseBounds;
            using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillEllipse(b, close);
            using (var p = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            { g.DrawLine(p, close.X + 15, close.Y + 15, close.Right - 15, close.Bottom - 15); g.DrawLine(p, close.Right - 15, close.Y + 15, close.X + 15, close.Bottom - 15); }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (CloseBounds.Contains(e.Location) || !ImageBounds.Contains(e.Location)) Close();
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Step(e.Delta > 0 ? -1 : 1); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: case Keys.Space: case Keys.Enter: Close(); return true;
                case Keys.Left: case Keys.Up: case Keys.PageUp: Step(-1); return true;
                case Keys.Right: case Keys.Down: case Keys.PageDown: Step(1); return true;
                case Keys.Home: Step(-paths.Count); return true;
                case Keys.End: Step(paths.Count); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void Dispose(bool disposing) { if (disposing && image != null) { image.Dispose(); image = null; } base.Dispose(disposing); }
    }
}
