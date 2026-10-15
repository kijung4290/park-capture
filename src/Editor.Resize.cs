using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed partial class Editor
    {
        internal bool LockImageAspect = true;
        private bool frameDragging;
        private FrameHandle frameHandle;
        private RectangleF frameStart, framePreview;
        private Point frameAnchor;
        internal bool IsFrameResizing { get { return frameDragging; } }
        internal RectangleF ImageFrame { get { return Document == null ? RectangleF.Empty : new RectangleF(origin.X, origin.Y, Document.Image.Width * zoom, Document.Image.Height * zoom); } }
        internal Size ResizePreviewSize { get { return frameDragging ? new Size(Math.Max(1, (int)Math.Round(framePreview.Width / zoom)), Math.Max(1, (int)Math.Round(framePreview.Height / zoom))) : Document == null ? Size.Empty : Document.Image.Size; } }
        private bool ShowImageFrame { get { return Document != null && (Tool == Tool.Resize || Tool == Tool.Select && selected < 0); } }
        private PointF[] FramePoints(RectangleF r)
        {
            float mx = r.X + r.Width / 2, my = r.Y + r.Height / 2;
            return new[] { new PointF(r.Left, r.Top), new PointF(mx, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, my), new PointF(r.Right, r.Bottom), new PointF(mx, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, my) };
        }
        private FrameHandle HitFrame(Point point)
        {
            if (!ShowImageFrame) return FrameHandle.None;
            RectangleF r = ImageFrame; float hit = Math.Max(7, DeviceDpi * 7 / 96f);
            PointF[] points = FramePoints(r);
            for (int i = 0; i < points.Length; i++) if (Math.Abs(point.X - points[i].X) <= hit && Math.Abs(point.Y - points[i].Y) <= hit) return (FrameHandle)(i + 1);
            if (point.X >= r.Left && point.X <= r.Right)
            { if (Math.Abs(point.Y - r.Top) <= hit / 2) return FrameHandle.Top; if (Math.Abs(point.Y - r.Bottom) <= hit / 2) return FrameHandle.Bottom; }
            if (point.Y >= r.Top && point.Y <= r.Bottom)
            { if (Math.Abs(point.X - r.Left) <= hit / 2) return FrameHandle.Left; if (Math.Abs(point.X - r.Right) <= hit / 2) return FrameHandle.Right; }
            return FrameHandle.None;
        }
        private void UpdateFrameCursor(Point point)
        {
            FrameHandle hit = HitFrame(point);
            Cursor = hit == FrameHandle.TopLeft || hit == FrameHandle.BottomRight ? Cursors.SizeNWSE : hit == FrameHandle.TopRight || hit == FrameHandle.BottomLeft ? Cursors.SizeNESW : hit == FrameHandle.Top || hit == FrameHandle.Bottom ? Cursors.SizeNS : hit == FrameHandle.Left || hit == FrameHandle.Right ? Cursors.SizeWE : Tool == Tool.Select || Tool == Tool.Resize ? Cursors.Default : Cursors.Cross;
        }
        private bool BeginFrameResize(Point point)
        {
            FrameHandle hit = HitFrame(point); if (hit == FrameHandle.None) return false;
            frameHandle = hit; frameAnchor = point; frameStart = ImageFrame; framePreview = frameStart; frameDragging = true;
            selected = -1; selection = Rectangle.Empty; draft = null; Capture = true; Invalidate(); ViewUpdate(); return true;
        }
        private void MoveFrameResize(Point point)
        {
            framePreview = FrameGeometry.Drag(frameStart, Document.Image.Size, zoom, frameHandle, new Point(point.X - frameAnchor.X, point.Y - frameAnchor.Y), LockImageAspect && (ModifierKeys & Keys.Alt) == 0);
            Invalidate(); ViewUpdate();
        }
        private void FinishFrameResize()
        {
            Size size = ResizePreviewSize; PointF location = framePreview.Location;
            frameDragging = false; Capture = false;
            try
            {
                if (Document.ResizeImage(size)) { origin = location; fit = false; Notify(); }
                else { Invalidate(); ViewUpdate(); }
            }
            catch (Exception e) { Invalidate(); ViewUpdate(); Program.Report(e); }
        }
        internal bool CancelFrameResize()
        {
            if (!frameDragging) return false;
            frameDragging = false; Capture = false; Invalidate(); ViewUpdate(); return true;
        }
        internal bool ResizeImage(Size size)
        {
            CancelFrameResize(); if (Document == null || !Document.ResizeImage(size)) return false;
            selected = -1; selection = Rectangle.Empty; Notify(); Fit(); return true;
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture && frameDragging) CancelFrameResize(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        { if ((keyData & Keys.KeyCode) == Keys.Escape && CancelFrameResize()) return true; return base.ProcessCmdKey(ref msg, keyData); }
        private void PaintImageFrame(Graphics g, RectangleF frame, Size size)
        {
            var saved = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias; float scale = Math.Max(1, DeviceDpi / 96f), edge = 8 * scale;
            using (var p = new Pen(Color.FromArgb(190, Style.Blue), scale)) g.DrawRectangle(p, frame.X, frame.Y, frame.Width, frame.Height);
            foreach (PointF point in FramePoints(frame))
            {
                var handle = new RectangleF(point.X - edge / 2, point.Y - edge / 2, edge, edge);
                using (var path = Glyphs.Round(handle, 2 * scale)) { g.FillPath(Brushes.White, path); using (var p = new Pen(Style.Blue, 1.25f * scale)) g.DrawPath(p, path); }
            }
            string label = size.Width + " × " + size.Height + " px" + (frameDragging ? "" : Words.T("  ·  테두리를 끌어 크기 조절", "  ·  Drag frame to resize"));
            using (var font = Style.Font(8.5f))
            {
                Size measured = TextRenderer.MeasureText(label, font); int width = measured.Width + 18, height = measured.Height + 8;
                float x = Math.Max(8, Math.Min(Width - width - 8, frame.Left + frame.Width / 2 - width / 2));
                float y = frame.Bottom + 13 * scale; if (y + height > Height - 4) y = Math.Max(4, frame.Top - height - 13 * scale);
                var badge = new RectangleF(x, y, width, height);
                using (var path = Glyphs.Round(badge, 7)) using (var b = new SolidBrush(frameDragging ? Style.Blue : Color.White)) g.FillPath(b, path);
                TextRenderer.DrawText(g, label, font, Rectangle.Round(badge), frameDragging ? Color.White : Style.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            g.Restore(saved);
        }
    }
}
