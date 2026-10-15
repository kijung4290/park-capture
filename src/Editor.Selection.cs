using System;
using System.Drawing;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed partial class Editor
    {
        internal string TextFontName = AnnotationFonts.Default;
        internal bool TextBold = true, TextBoxBorder = true, TextBoxFill = true;
        private FrameHandle markHandle;
        private bool AllowsDirectSelection { get { return Tool != Tool.Crop && Tool != Tool.Resize && Tool != Tool.CutHorizontal && Tool != Tool.CutVertical; } }
        private FrameHandle HitMarkHandle(Point point)
        {
            if (SelectedMark == null) return FrameHandle.None;
            var bounds = SelectedMark.Bounds;
            var frame = new RectangleF(origin.X + bounds.X * zoom, origin.Y + bounds.Y * zoom, bounds.Width * zoom, bounds.Height * zoom);
            PointF[] points = FramePoints(frame); float tolerance = Math.Max(7, DeviceDpi * 7 / 96f);
            for (int i = 0; i < points.Length; i++) if (Math.Abs(point.X - points[i].X) <= tolerance && Math.Abs(point.Y - points[i].Y) <= tolerance) return (FrameHandle)(i + 1);
            return FrameHandle.None;
        }
        private bool BeginMarkInteraction(Point point)
        {
            if (!AllowsDirectSelection || (ModifierKeys & Keys.Alt) != 0) return false;
            markHandle = HitMarkHandle(point);
            int hit = markHandle != FrameHandle.None ? selected : InImage(point) ? HitMark(ToImage(point)) : -1;
            if (hit < 0) return false;
            Tool = Tool.Select; draft = null; selection = Rectangle.Empty;
            SelectMark(hit); start = ToImage(point, false); mouseAnchor = point; original = SelectedMark.Clone();
            markDragChanged = false; dragging = true; Capture = true; return true;
        }
        private bool UpdateAnnotationCursor(Point point)
        {
            if (!AllowsDirectSelection || (ModifierKeys & Keys.Alt) != 0) return false;
            FrameHandle handle = HitMarkHandle(point);
            if (handle != FrameHandle.None)
            {
                Cursor = handle == FrameHandle.TopLeft || handle == FrameHandle.BottomRight ? Cursors.SizeNWSE : handle == FrameHandle.TopRight || handle == FrameHandle.BottomLeft ? Cursors.SizeNESW : handle == FrameHandle.Top || handle == FrameHandle.Bottom ? Cursors.SizeNS : Cursors.SizeWE;
                return true;
            }
            if (InImage(point) && HitMark(ToImage(point)) >= 0) { Cursor = Cursors.Hand; return true; }
            return false;
        }
        internal void SetSelectedText(string text)
        {
            var mark = SelectedMark;
            if (mark == null || !mark.HasText || mark.Kind == Tool.Number || text == null || text == mark.Text) return;
            Document.Checkpoint(); mark.Text = text; Notify();
        }
        internal bool EditSelectedText()
        {
            var mark = SelectedMark;
            if (mark == null || !mark.HasText || mark.Kind == Tool.Number) return false;
            string text = TextDialog.Ask(FindForm(), Words.T("내용 수정", "Edit text"), mark.Text, mark.FontName, mark.FontSize, mark.Bold);
            SetSelectedText(text); Focus(); return true;
        }
        internal void ResizeSelected(float width, float height)
        {
            var mark = SelectedMark;
            if (mark == null || mark.Kind == Tool.Text || mark.Kind == Tool.Number) return;
            var bounds = mark.Bounds;
            if (Math.Abs(bounds.Width - width) < .5f && Math.Abs(bounds.Height - height) < .5f) return;
            Document.Checkpoint(); MarkGeometry.SetBounds(mark, mark.Clone(), new RectangleF(bounds.X, bounds.Y, width, height)); Notify();
        }
        internal void SetSelectedNumber(int number)
        {
            var mark = SelectedMark;
            if (mark == null || mark.Kind != Tool.Number || mark.Number == number) return;
            Document.Checkpoint(); mark.Number = number; Notify();
        }
    }
}
