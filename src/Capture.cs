using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed class WindowArea
    {
        internal Rectangle Frame, Client;
    }
    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap screen;
        private readonly Rectangle desktop;
        private readonly List<WindowArea> windows = new List<WindowArea>();
        private Point cursor, anchor;
        private Rectangle hover, selection;
        private bool dragging, bodyOnly;
        private readonly bool chooseMode;
        internal CaptureModeDialog ActiveModeDialog;
        internal Rectangle SelectedScreen;
        internal bool Record;
        internal Bitmap Result;
        internal CaptureOverlay(bool chooseCaptureMode = false)
        {
            chooseMode = chooseCaptureMode;
            desktop = SystemInformation.VirtualScreen;
            uint own = (uint)Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                uint pid; Native.GetWindowThreadProcessId(window, out pid);
                if (pid == own || !Native.IsWindowVisible(window) || Native.IsIconic(window)) return true;
                int cloaked; if (Native.DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                Native.RECT frame, client;
                if (Native.DwmGetWindowAttribute(window, 9, out frame, 16) != 0 && !Native.GetWindowRect(window, out frame)) return true;
                Rectangle r = Rectangle.Intersect(frame.Rectangle, desktop);
                if (r.Width < 10 || r.Height < 10) return true;
                Rectangle c = r;
                if (Native.GetClientRect(window, out client)) { Point p = Point.Empty; Native.ClientToScreen(window, ref p); c = Rectangle.Intersect(new Rectangle(p, client.Rectangle.Size), desktop); }
                windows.Add(new WindowArea { Frame = r, Client = c.Width > 0 && c.Height > 0 ? c : r }); return true;
            }, IntPtr.Zero);
            screen = Native.ScreenBitmap(desktop);
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            Bounds = desktop; TopMost = true; ShowInTaskbar = false; KeyPreview = true; DoubleBuffered = true; Cursor = Cursors.Cross;
            Text = "박주임 캡처 - 영역 선택";
            cursor = PointToClient(Control.MousePosition); UpdateHover();
            Shown += delegate { Bounds = desktop; Activate(); Focus(); };
        }
        private void UpdateHover()
        {
            Point global = new Point(cursor.X + desktop.X, cursor.Y + desktop.Y); hover = Rectangle.Empty;
            foreach (var w in windows) if (w.Frame.Contains(global)) { hover = bodyOnly ? w.Client : w.Frame; hover.Offset(-desktop.X, -desktop.Y); break; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); cursor = e.Location;
            if (dragging) selection = Rect(anchor, cursor); else if (selection.IsEmpty) UpdateHover(); Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            anchor = e.Location; dragging = true; selection = Rectangle.Empty; Capture = true;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (!dragging) return; dragging = false; Capture = false;
            selection = Rect(anchor, e.Location);
            if (selection.Width < 4 && selection.Height < 4) { cursor = e.Location; UpdateHover(); selection = hover; }
            if (selection.Width < 1 || selection.Height < 1) selection = Rectangle.Empty;
            Invalidate();
            if (chooseMode && !selection.IsEmpty) BeginInvoke(new Action(ChooseCaptureMode));
        }
        private void ChooseCaptureMode()
        {
            if (IsDisposed || !Visible || ActiveModeDialog != null || selection.IsEmpty) return;
            Rectangle global = new Rectangle(selection.X + desktop.X, selection.Y + desktop.Y, selection.Width, selection.Height);
            using (var dialog = new CaptureModeDialog(global))
            {
                ActiveModeDialog = dialog; var result = dialog.ShowDialog(this); ActiveModeDialog = null;
                if (result == DialogResult.OK) Finish(dialog.RecordVideo);
                else if (result == DialogResult.Retry) { selection = Rectangle.Empty; UpdateHover(); Invalidate(); Activate(); Focus(); }
                else { DialogResult = DialogResult.Cancel; Close(); }
            }
        }
        private Rectangle Rect(Point a, Point b) { return Rectangle.Intersect(Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)), new Rectangle(Point.Empty, screen.Size)); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            if (key == Keys.Tab) { bodyOnly = !bodyOnly; if (selection.IsEmpty) UpdateHover(); Invalidate(); return true; }
            if (key == Keys.Enter && chooseMode) { if (selection.IsEmpty) selection = hover; ChooseCaptureMode(); return true; }
            if (key == Keys.Enter || key == Keys.R) { Finish(key == Keys.R); return true; }
            if (!selection.IsEmpty)
            {
                int n = (keyData & Keys.Shift) != 0 ? 10 : 1;
                Point delta = Point.Empty;
                if (key == Keys.Left) delta.X = -n; else if (key == Keys.Right) delta.X = n;
                else if (key == Keys.Up) delta.Y = -n; else if (key == Keys.Down) delta.Y = n;
                else return base.ProcessCmdKey(ref msg, keyData);
                selection.Offset(delta); selection.X = Math.Max(0, Math.Min(screen.Width - selection.Width, selection.X)); selection.Y = Math.Max(0, Math.Min(screen.Height - selection.Height, selection.Y)); Invalidate(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void Finish(bool record)
        {
            Rectangle r = selection.IsEmpty ? hover : selection;
            if (r.Width < 2 || r.Height < 2) return;
            SelectedScreen = new Rectangle(r.X + desktop.X, r.Y + desktop.Y, r.Width, r.Height); Record = record;
            if (!record) { Result = screen.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb); Result.SetResolution(96, 96); }
            DialogResult = DialogResult.OK; Close();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.DrawImageUnscaled(screen, 0, 0);
            Rectangle r = selection.IsEmpty ? hover : selection;
            using (var region = new Region(new Rectangle(Point.Empty, screen.Size))) using (var shade = new SolidBrush(Color.FromArgb(132, 10, 20, 38)))
            { if (!r.IsEmpty) region.Exclude(r); g.FillRegion(shade, region); }
            using (var p = new Pen(Color.FromArgb(160, Color.White))) { g.DrawLine(p, 0, cursor.Y, Width, cursor.Y); g.DrawLine(p, cursor.X, 0, cursor.X, Height); }
            if (!r.IsEmpty)
            {
                using (var p = new Pen(Style.Blue, 2)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                string label = r.Width + " × " + r.Height + " px";
                Box(g, label, Math.Max(8, r.X), Math.Max(8, r.Y - 36), false);
            }
            var monitor = Screen.FromPoint(new Point(cursor.X + desktop.X, cursor.Y + desktop.Y)).Bounds; monitor.Offset(-desktop.X, -desktop.Y);
            string hint = chooseMode ? Words.T("영역을 드래그하면 이미지 / 영상을 선택할 수 있어요  ·  Tab: 창/본문  ·  Esc: 취소", "Drag an area, then choose image or video  ·  Tab: frame/client  ·  Esc: cancel") : Words.T("드래그: 영역  ·  클릭: 창  ·  Enter: 캡처  ·  R: 녹화  ·  Tab: 창/본문  ·  Esc: 취소", "Drag: area  ·  Click: window  ·  Enter: capture  ·  R: record  ·  Tab: frame/client  ·  Esc: cancel");
            using (var font = Style.Font(10))
            {
                int w = (int)g.MeasureString(hint, font).Width + 36;
                Box(g, hint, monitor.X + Math.Max(8, (monitor.Width - w) / 2), monitor.Y + 20, false);
            }
            Magnifier(g, monitor);
        }
        private void Magnifier(Graphics g, Rectangle monitor)
        {
            int x = Math.Max(0, Math.Min(screen.Width - 1, cursor.X)), y = Math.Max(0, Math.Min(screen.Height - 1, cursor.Y));
            int px = Math.Max(monitor.Left + 8, Math.Min(monitor.Right - 186, cursor.X + 26));
            int py = Math.Max(monitor.Top + 70, Math.Min(monitor.Bottom - 158, cursor.Y + 28));
            g.FillRectangle(Brushes.White, px, py, 176, 145);
            const int cell = 9, count = 11; int gx = px + 38, gy = py + 8;
            g.SmoothingMode = SmoothingMode.None;
            for (int row = 0; row < count; row++) for (int col = 0; col < count; col++)
            {
                int sx = Math.Max(0, Math.Min(screen.Width - 1, x + col - 5)), sy = Math.Max(0, Math.Min(screen.Height - 1, y + row - 5));
                using (var b = new SolidBrush(screen.GetPixel(sx, sy))) g.FillRectangle(b, gx + col * cell, gy + row * cell, cell, cell);
                using (var p = new Pen(Color.FromArgb(45, 0, 0, 0))) g.DrawRectangle(p, gx + col * cell, gy + row * cell, cell, cell);
            }
            using (var p = new Pen(Style.Red, 2)) g.DrawRectangle(p, gx + 5 * cell, gy + 5 * cell, cell, cell);
            Color c = screen.GetPixel(x, y);
            string label = (x + desktop.X) + ", " + (y + desktop.Y) + "  #" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            using (var f = new Font("Segoe UI", 9)) using (var b = new SolidBrush(Style.Ink)) g.DrawString(label, f, b, px + 8, py + 115);
            using (var p = new Pen(Style.Line)) g.DrawRectangle(p, px, py, 175, 144);
        }
        private void Box(Graphics g, string text, int x, int y, bool unused)
        {
            using (var f = Style.Font(10)) { SizeF size = g.MeasureString(text, f); using (var b = new SolidBrush(Style.Ink)) g.FillRectangle(b, x, y, size.Width + 24, 30); g.DrawString(text, f, Brushes.White, x + 12, y + 5); }
        }
        protected override void Dispose(bool disposing) { if (disposing) screen.Dispose(); base.Dispose(disposing); }
    }
}
