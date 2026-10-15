using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed partial class Editor : Control
    {
        internal Document Document;
        internal Tool Tool = Tool.Select;
        internal Color MarkColor { get; set; }
        internal float LineWidth = 3, TextSize = 24;
        internal event EventHandler Changed, ViewChanged;
        private Bitmap composite;
        private float zoom = 1;
        private PointF origin;
        private bool fit = true, dragging, pan, markDragChanged;
        private readonly ContextMenuStrip annotationMenu = new ContextMenuStrip();
        private Point mouseAnchor;
        private PointF start;
        private Mark draft, original;
        private int selected = -1;
        private Rectangle selection;
        internal float Zoom { get { return zoom; } }
        internal bool VideoMode;
        internal Mark SelectedMark { get { return Document != null && selected >= 0 && selected < Document.Marks.Count ? Document.Marks[selected] : null; } }
        internal bool HasAreaSelection { get { return !selection.IsEmpty; } }
        internal Editor()
        {
            MarkColor = Style.Red;
            DoubleBuffered = true; BackColor = Style.Paper; TabStop = true;
            SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            AccessibleName = "이미지 편집 캔버스 / Image editor";
            var delete = new ToolStripMenuItem(Words.T("요소 삭제", "Delete element"), null, delegate { DeleteSelected(); });
            delete.ShortcutKeyDisplayString = "Delete"; annotationMenu.Items.Add(delete);
            annotationMenu.Items.Add(Words.T("선택 해제", "Deselect"), null, delegate { ClearMarkSelection(); });
            var edit = annotationMenu.Items.Add(Words.T("내용 수정…", "Edit text…"), null, delegate { EditSelectedText(); });
            annotationMenu.Opening += delegate { edit.Visible = SelectedMark != null && SelectedMark.HasText && SelectedMark.Kind != Tool.Number; };
        }
        internal void SetDocument(Document doc)
        {
            CancelFrameResize(); Document = doc; selected = -1; draft = null; selection = Rectangle.Empty; fit = true; RefreshComposite(); Fit();
        }
        internal void RefreshComposite()
        {
            if (composite != null) { composite.Dispose(); composite = null; }
            if (Document != null) composite = Document.Render(); Invalidate();
        }
        internal void Fit()
        {
            fit = true;
            if (Document == null) { Invalidate(); return; }
            zoom = Math.Max(.01f, Math.Min(1, Math.Min((ClientSize.Width - 70f) / Document.Image.Width, (ClientSize.Height - 70f) / Document.Image.Height)));
            CenterImage();
        }
        private void CenterImage() { origin = new PointF((Width - Document.Image.Width * zoom) / 2, (Height - Document.Image.Height * zoom) / 2); Invalidate(); ViewUpdate(); }
        private void ViewUpdate() { if (ViewChanged != null) ViewChanged(this, EventArgs.Empty); }
        internal void ZoomBy(float factor)
        {
            if (Document == null) return;
            ZoomAt(factor, new Point(Width / 2, Height / 2));
        }
        private void ZoomAt(float factor, Point p)
        {
            PointF before = ToImage(p, false); zoom = Math.Max(.02f, Math.Min(16, zoom * factor)); fit = false;
            origin = new PointF(p.X - before.X * zoom, p.Y - before.Y * zoom); Invalidate(); ViewUpdate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (fit) Fit(); }
        internal void SetTool(Tool value) { CancelFrameResize(); Tool = value; selection = Rectangle.Empty; draft = null; selected = -1; Cursor = Tool == Tool.Select || Tool == Tool.Resize ? Cursors.Default : Cursors.Cross; Invalidate(); }
        internal void ApplyStyle()
        {
            var m = SelectedMark; if (m == null) return;
            if (m.Argb == MarkColor.ToArgb() && m.Width == LineWidth && m.FontSize == TextSize && m.FontName == TextFontName && m.Bold == TextBold && m.BoxBorder == TextBoxBorder && m.BoxFill == TextBoxFill) return;
            Document.Checkpoint(); m.Argb = MarkColor.ToArgb(); m.Width = LineWidth; m.FontSize = TextSize; m.FontName = TextFontName; m.Bold = TextBold; m.BoxBorder = TextBoxBorder; m.BoxFill = TextBoxFill; Notify();
        }
        internal void Undo() { CancelFrameResize(); if (Document != null && Document.Undo()) { selected = -1; selection = Rectangle.Empty; Notify(); Fit(); } }
        internal void Redo() { CancelFrameResize(); if (Document != null && Document.Redo()) { selected = -1; selection = Rectangle.Empty; Notify(); Fit(); } }
        internal void DeleteSelected()
        { if (SelectedMark == null) return; Document.Checkpoint(); Document.Marks.RemoveAt(selected); selected = -1; dragging = false; Capture = false; Focus(); Notify(); }
        internal bool ClearMarkSelection()
        { if (SelectedMark == null) return false; selected = -1; Invalidate(); ViewUpdate(); return true; }
        private void SelectMark(int index)
        {
            selected = index;
            if (SelectedMark != null) { var mark = SelectedMark; MarkColor = Color.FromArgb(mark.Argb); LineWidth = mark.Width; TextSize = mark.FontSize; TextFontName = mark.FontName; TextBold = mark.Bold; TextBoxBorder = mark.BoxBorder; TextBoxFill = mark.BoxFill; }
            Invalidate(); ViewUpdate();
        }
        private int HitMark(PointF point)
        {
            float tolerance = 6 / zoom;
            for (int i = Document.Marks.Count - 1; i >= 0; i--)
            {
                var mark = Document.Marks[i]; var bounds = mark.Bounds; bounds.Inflate(tolerance, tolerance);
                if (!bounds.Contains(point)) continue;
                if (mark.Kind == Tool.Arrow)
                {
                    float dx = mark.X2 - mark.X1, dy = mark.Y2 - mark.Y1, length = dx * dx + dy * dy;
                    float t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((point.X - mark.X1) * dx + (point.Y - mark.Y1) * dy) / length));
                    float x = point.X - mark.X1 - t * dx, y = point.Y - mark.Y1 - t * dy;
                    float distance = Math.Max(tolerance, mark.Width * 2.5f);
                    if (x * x + y * y > distance * distance) continue;
                }
                return i;
            }
            return -1;
        }
        private void Notify() { RefreshComposite(); if (Changed != null) Changed(this, EventArgs.Empty); ViewUpdate(); }
        private PointF ToImage(Point p, bool clamp = true)
        {
            float x = (p.X - origin.X) / zoom, y = (p.Y - origin.Y) / zoom;
            if (clamp && Document != null) { x = Math.Max(0, Math.Min(Document.Image.Width, x)); y = Math.Max(0, Math.Min(Document.Image.Height, y)); }
            return new PointF(x, y);
        }
        private bool InImage(Point p) { var v = ToImage(p, false); return v.X >= 0 && v.Y >= 0 && v.X < Document.Image.Width && v.Y < Document.Image.Height; }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (Document != null) ZoomAt(e.Delta > 0 ? 1.15f : 1 / 1.15f, e.Location); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus(); if (Document == null) return;
            if (e.Button == MouseButtons.Middle) { pan = true; mouseAnchor = e.Location; Capture = true; return; }
            if (e.Button == MouseButtons.Right)
            {
                SetTool(Tool.Select);
                SelectMark(InImage(e.Location) ? HitMark(ToImage(e.Location)) : -1);
                if (SelectedMark != null) annotationMenu.Show(this, e.Location);
                return;
            }
            if (e.Button == MouseButtons.Left && BeginMarkInteraction(e.Location)) return;
            if (e.Button == MouseButtons.Left && BeginFrameResize(e.Location)) return;
            if (Tool == Tool.Resize) return;
            if (e.Button != MouseButtons.Left || !InImage(e.Location)) return;
            start = ToImage(e.Location); mouseAnchor = e.Location;
            if (Tool == Tool.Select)
            {
                SelectMark(-1); return;
            }
            selected = -1;
            if (Tool == Tool.Number || Tool == Tool.Text)
            {
                string text = Tool == Tool.Text ? TextDialog.Ask(FindForm(), Words.T("입력할 글자", "Annotation text"), "", TextFontName, TextSize, TextBold) : "";
                if (Tool == Tool.Text && string.IsNullOrWhiteSpace(text)) return;
                var mark = NewMark(start); mark.Text = text;
                if (Tool == Tool.Number) { int max = 0; foreach (var m in Document.Marks) if (m.Kind == Tool.Number) max = Math.Max(max, m.Number); mark.Number = max + 1; }
                Document.Checkpoint(); Document.Marks.Add(mark); SelectMark(Document.Marks.Count - 1); Notify(); return;
            }
            dragging = true; Capture = true; selection = Rectangle.Empty; draft = NewMark(start); Invalidate();
        }
        private Mark NewMark(PointF p) { return new Mark { Kind = Tool, X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, Argb = MarkColor.ToArgb(), Width = LineWidth, FontSize = TextSize, FontName = TextFontName, Bold = TextBold, BoxBorder = TextBoxBorder, BoxFill = TextBoxFill }; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (Document == null) return;
            if (frameDragging) { MoveFrameResize(e.Location); return; }
            if (pan) { origin.X += e.X - mouseAnchor.X; origin.Y += e.Y - mouseAnchor.Y; mouseAnchor = e.Location; fit = false; Invalidate(); return; }
            if (!dragging) { if (!UpdateAnnotationCursor(e.Location)) UpdateFrameCursor(e.Location); return; }
            var p = ToImage(e.Location);
            if (Tool == Tool.Select && selected >= 0)
            {
                if (!markDragChanged)
                {
                    if (Math.Abs(e.X - mouseAnchor.X) < 3 && Math.Abs(e.Y - mouseAnchor.Y) < 3) return;
                    Document.Checkpoint(); markDragChanged = true;
                }
                var m = Document.Marks[selected]; float dx = p.X - start.X, dy = p.Y - start.Y;
                m.X1 = original.X1; m.Y1 = original.Y1; m.X2 = original.X2; m.Y2 = original.Y2;
                if (markHandle != FrameHandle.None) MarkGeometry.Resize(m, original, markHandle, new PointF(dx, dy), (ModifierKeys & Keys.Shift) != 0);
                else { m.X1 += dx; m.X2 += dx; m.Y1 += dy; m.Y2 += dy; }
                RefreshComposite(); return;
            }
            if (draft != null)
            {
                float dx = p.X - start.X, dy = p.Y - start.Y;
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    if (Tool == Tool.Arrow) { double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * Math.PI / 4; double len = Math.Sqrt(dx * dx + dy * dy); p = new PointF(start.X + (float)(Math.Cos(angle) * len), start.Y + (float)(Math.Sin(angle) * len)); }
                    else { float side = Math.Min(Math.Abs(dx), Math.Abs(dy)); p = new PointF(start.X + Math.Sign(dx) * side, start.Y + Math.Sign(dy) * side); }
                }
                draft.X2 = p.X; draft.Y2 = p.Y; Invalidate();
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (pan) { pan = false; Capture = false; return; }
            if (frameDragging) { FinishFrameResize(); return; }
            if (!dragging) return; dragging = false; Capture = false;
            if (Tool == Tool.Select) { original = null; if (markDragChanged) Notify(); else ViewUpdate(); return; }
            if (draft == null) return;
            var r = Rectangle.Ceiling(draft.Bounds);
            if ((Tool == Tool.Crop || Tool == Tool.CutHorizontal || Tool == Tool.CutVertical) && r.Width > 0 && r.Height > 0)
            {
                selection = r;
                if (Tool == Tool.CutHorizontal) selection = new Rectangle(0, r.Y, Document.Image.Width, r.Height);
                if (Tool == Tool.CutVertical) selection = new Rectangle(r.X, 0, r.Width, Document.Image.Height);
                draft = null; Invalidate(); ViewUpdate(); return;
            }
            if (r.Width < 2 && r.Height < 2) { draft = null; Invalidate(); return; }
            if (Tool == Tool.Bubble || Tool == Tool.TextBox)
            {
                if (Tool == Tool.TextBox && Math.Abs(draft.X2 - draft.X1) < 8 && Math.Abs(draft.Y2 - draft.Y1) < 8) { draft.X2 = Math.Min(Document.Image.Width, draft.X1 + 280); draft.Y2 = Math.Min(Document.Image.Height, draft.Y1 + 110); }
                string text = TextDialog.Ask(FindForm(), Tool == Tool.Bubble ? Words.T("말풍선 내용", "Callout text") : Words.T("텍스트 상자", "Text box"), "", TextFontName, TextSize, TextBold);
                if (string.IsNullOrWhiteSpace(text)) { draft = null; Invalidate(); return; } draft.Text = text;
            }
            Document.Checkpoint(); Document.Marks.Add(draft); draft = null; SelectMark(Document.Marks.Count - 1); Notify();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) EditSelectedText();
        }
        internal bool ApplySelection()
        {
            if (selection.IsEmpty || Document == null) return false;
            if (Tool == Tool.Crop) Document.Crop(selection);
            else if (Tool == Tool.CutHorizontal || Tool == Tool.CutVertical) Document.CutBand(selection, Tool == Tool.CutHorizontal);
            else return false;
            selection = Rectangle.Empty; selected = -1; Notify(); Fit(); return true;
        }
        internal bool MoveSelection(Keys key, int amount)
        {
            if (Document == null || selected < 0) return false;
            if (key != Keys.Left && key != Keys.Right && key != Keys.Up && key != Keys.Down) return false;
            Document.Checkpoint(); var m = Document.Marks[selected]; float dx = key == Keys.Left ? -amount : key == Keys.Right ? amount : 0, dy = key == Keys.Up ? -amount : key == Keys.Down ? amount : 0;
            m.X1 += dx; m.X2 += dx; m.Y1 += dy; m.Y2 += dy; Notify(); return true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics;
            if (Document == null)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var icon = new RectangleF(Width / 2f - 38, Height / 2f - 124, 76, 76);
                using (var path = Glyphs.Round(icon, 22)) using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
                Glyphs.Draw(g, VideoMode ? "play" : "capture", new RectangleF(icon.X + 20, icon.Y + 20, 36, 36), Style.Blue);
                using (var f = Style.Font(20, FontStyle.Bold)) using (var b = new SolidBrush(Style.Ink)) using (var format = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(VideoMode ? Words.T("필요한 순간만 남겨보세요", "Keep the moments you need") : Words.T("캡처하고, 가볍게 정리하세요", "A little capture. A clearer picture."), f, b, new RectangleF(20, Height / 2 - 20, Width - 40, 55), format);
                using (var f = Style.Font(10)) using (var b = new SolidBrush(Style.Muted)) using (var format = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(VideoMode ? Words.T("영상을 재생하거나 구간을 골라 MP4 · GIF로 저장하세요.", "Play your recording or save a selected range as MP4 or GIF.") : Words.T("화면을 새로 캡처하거나 이미지를 여기로 끌어오세요.", "Capture your screen or drop an image here."), f, b, new RectangleF(20, Height / 2 + 31, Width - 40, 35), format);
                return;
            }
            if (composite == null) RefreshComposite();
            if (frameDragging)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(composite, framePreview);
                PaintImageFrame(g, framePreview, ResizePreviewSize); return;
            }
            var state = g.Save();
            g.TranslateTransform(origin.X, origin.Y); g.ScaleTransform(zoom, zoom);
            for (int i = 10; i >= 2; i -= 2)
            {
                using (var shadow = new SolidBrush(Color.FromArgb(3, 0, 0, 0))) using (var path = Glyphs.Round(new RectangleF(-i / zoom, (3 - i) / zoom, Document.Image.Width + 2 * i / zoom, Document.Image.Height + 2 * i / zoom), (i + 2) / zoom)) g.FillPath(shadow, path);
            }
            g.InterpolationMode = zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(composite, new Rectangle(0, 0, composite.Width, composite.Height), 0, 0, composite.Width, composite.Height, GraphicsUnit.Pixel);
            g.SetClip(new Rectangle(0, 0, Document.Image.Width, Document.Image.Height));
            if (draft != null)
            {
                if (Tool == Tool.Mosaic || Tool == Tool.Crop || Tool == Tool.CutHorizontal || Tool == Tool.CutVertical) DrawSelection(g, draft.Bounds);
                else Renderer.Draw(g, draft);
            }
            if (!selection.IsEmpty) DrawSelection(g, selection);
            if (selected >= 0 && selected < Document.Marks.Count)
            {
                var r = Document.Marks[selected].Bounds; using (var p = new Pen(Style.Blue, 1.5f / zoom) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                foreach (PointF point in FramePoints(r))
                {
                    g.FillRectangle(Brushes.White, point.X - 4 / zoom, point.Y - 4 / zoom, 8 / zoom, 8 / zoom);
                    using (var p = new Pen(Style.Blue, 1 / zoom)) g.DrawRectangle(p, point.X - 4 / zoom, point.Y - 4 / zoom, 8 / zoom, 8 / zoom);
                }
            }
            g.Restore(state);
            if (ShowImageFrame) PaintImageFrame(g, ImageFrame, Document.Image.Size);
        }
        private void DrawSelection(Graphics g, RectangleF r)
        { using (var b = new SolidBrush(Color.FromArgb(45, Style.Blue))) g.FillRectangle(b, r); using (var p = new Pen(Style.Blue, 1.5f / zoom) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
        protected override void Dispose(bool disposing) { if (disposing) { if (composite != null) composite.Dispose(); annotationMenu.Dispose(); } base.Dispose(disposing); }
    }

    internal static class TextDialog
    {
        internal static string Ask(IWin32Window owner, string title, string initial, string fontName = "Malgun Gothic", float fontSize = 24, bool bold = true)
        {
            using (var form = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(520, 300), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, Font = Style.Font(10), BackColor = Style.Sidebar })
            {
                var input = new TextBox { Multiline = true, Text = initial, Location = new Point(18, 18), Size = new Size(484, 204), AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, Font = AnnotationFonts.Create(fontName, Math.Min(26, fontSize), bold), AccessibleName = "주석 내용 / Annotation text" };
                var hint = new Label { Text = Words.T("여러 줄 입력 · Ctrl+Enter 저장 · Esc 취소", "Multiple lines · Ctrl+Enter saves · Esc cancels"), ForeColor = Style.Muted, Font = Style.Font(8.5f), Bounds = new Rectangle(18, 230, 484, 20) };
                var ok = Style.Button(Words.T("저장", "Save"), delegate { form.DialogResult = DialogResult.OK; }, true); ok.Location = new Point(326, 254);
                var cancel = Style.Button(Words.T("취소", "Cancel"), delegate { form.DialogResult = DialogResult.Cancel; }); cancel.Location = new Point(418, 254);
                input.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; form.DialogResult = DialogResult.OK; } };
                form.Controls.AddRange(new Control[] { input, hint, ok, cancel }); form.CancelButton = cancel; form.Shown += delegate { input.Focus(); input.SelectAll(); };
                return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
            }
        }
    }
}
