using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace ParkCapture
{
    public enum Tool { Select, Crop, Number, Text, Bubble, Arrow, Rectangle, Ellipse, Mosaic, CutHorizontal, CutVertical, Resize }
    public class Mark
    {
        public Tool Kind;
        public float X1, Y1, X2, Y2;
        public int Argb = Style.Red.ToArgb();
        public float Width = 3, FontSize = 24;
        public int Number = 1;
        public string Text = "";
        public Mark Clone() { return (Mark)MemberwiseClone(); }
        public RectangleF Bounds
        {
            get
            {
                if (Kind == Tool.Number) return new RectangleF(X1 - FontSize * .75f, Y1 - FontSize * .75f, FontSize * 1.5f, FontSize * 1.5f);
                if (Kind == Tool.Text)
                {
                    using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp)) using (var f = new Font("Malgun Gothic", FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    { var size = g.MeasureString(Text, f); return new RectangleF(X1, Y1, size.Width, size.Height); }
                }
                if (Kind == Tool.Bubble) return new RectangleF(Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Max(100, Math.Abs(X2 - X1)), Math.Max(50, Math.Abs(Y2 - Y1)) + 18);
                return RectangleF.FromLTRB(Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Max(X1, X2), Math.Max(Y1, Y2));
            }
        }
    }
    public class ProjectData
    {
        public int Version = 1;
        public List<Mark> Marks = new List<Mark>();
        public string BaseFile = "";
    }
    internal sealed class Snapshot : IDisposable
    {
        internal Bitmap Image;
        internal List<Mark> Marks;
        internal Snapshot(Bitmap image, List<Mark> marks) { Image = new Bitmap(image); Marks = marks.Select(m => m.Clone()).ToList(); }
        public void Dispose() { Image.Dispose(); }
    }
    internal sealed partial class Document : IDisposable
    {
        internal Bitmap Image;
        internal List<Mark> Marks = new List<Mark>();
        internal string PathName;
        internal bool Dirty;
        internal bool CanUndo { get { return undo.Count > 0; } }
        internal bool CanRedo { get { return redo.Count > 0; } }
        private string baseFile = "";
        private bool baseDirty = true;
        private readonly List<Snapshot> undo = new List<Snapshot>(), redo = new List<Snapshot>();
        internal Document(Image image, string path) { Image = new Bitmap(image); Image.SetResolution(96, 96); PathName = path; }
        internal void Checkpoint()
        {
            undo.Add(new Snapshot(Image, Marks));
            // Keep at most 16 steps / approximately 192 MiB of raster history.
            long bytes = undo.Sum(x => (long)x.Image.Width * x.Image.Height * 4);
            while (undo.Count > 1 && (undo.Count > 16 || bytes > 201326592)) { bytes -= (long)undo[0].Image.Width * undo[0].Image.Height * 4; undo[0].Dispose(); undo.RemoveAt(0); }
            foreach (var s in redo) s.Dispose(); redo.Clear(); Dirty = true;
        }
        internal bool Undo() { return Restore(undo, redo); }
        internal bool Redo() { return Restore(redo, undo); }
        private bool Restore(List<Snapshot> from, List<Snapshot> to)
        {
            if (from.Count == 0) return false;
            to.Add(new Snapshot(Image, Marks));
            var s = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            Image.Dispose(); Image = s.Image; Marks = s.Marks; Dirty = true; baseDirty = true; return true;
        }
        internal Bitmap Render() { return Renderer.Render(Image, Marks); }
        internal void Crop(Rectangle rect)
        {
            rect.Intersect(new Rectangle(Point.Empty, Image.Size)); if (rect.Width < 1 || rect.Height < 1) return;
            Checkpoint(); using (var flat = Render()) { var next = flat.Clone(rect, PixelFormat.Format32bppArgb); Image.Dispose(); Image = next; }
            Marks.Clear(); baseDirty = true;
        }
        internal void CutBand(Rectangle rect, bool horizontal)
        {
            rect.Intersect(new Rectangle(Point.Empty, Image.Size));
            int cut = horizontal ? rect.Height : rect.Width;
            int total = horizontal ? Image.Height : Image.Width;
            if (cut <= 0 || cut >= total) return;
            Checkpoint(); using (var flat = Render())
            {
                var next = new Bitmap(Image.Width - (horizontal ? 0 : cut), Image.Height - (horizontal ? cut : 0), PixelFormat.Format32bppArgb);
                next.SetResolution(96, 96);
                using (var g = Graphics.FromImage(next))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    if (horizontal)
                    {
                        if (rect.Top > 0) g.DrawImage(flat, new Rectangle(0, 0, flat.Width, rect.Top), new Rectangle(0, 0, flat.Width, rect.Top), GraphicsUnit.Pixel);
                        if (rect.Bottom < flat.Height) g.DrawImage(flat, new Rectangle(0, rect.Top, flat.Width, flat.Height - rect.Bottom), new Rectangle(0, rect.Bottom, flat.Width, flat.Height - rect.Bottom), GraphicsUnit.Pixel);
                    }
                    else
                    {
                        if (rect.Left > 0) g.DrawImage(flat, new Rectangle(0, 0, rect.Left, flat.Height), new Rectangle(0, 0, rect.Left, flat.Height), GraphicsUnit.Pixel);
                        if (rect.Right < flat.Width) g.DrawImage(flat, new Rectangle(rect.Left, 0, flat.Width - rect.Right, flat.Height), new Rectangle(rect.Right, 0, flat.Width - rect.Right, flat.Height), GraphicsUnit.Pixel);
                    }
                }
                Image.Dispose(); Image = next; Marks.Clear(); baseDirty = true;
            }
        }
        internal void Save()
        {
            string dir = Storage.ProjectDir(PathName); Directory.CreateDirectory(dir);
            bool writeBase = baseDirty || string.IsNullOrEmpty(baseFile) || !File.Exists(System.IO.Path.Combine(dir, baseFile));
            string baseName = writeBase ? Guid.NewGuid().ToString("N") + ".png" : baseFile;
            string imagePath = System.IO.Path.Combine(dir, baseName);
            try
            {
                if (writeBase) Storage.SavePng(Image, imagePath);
                Storage.WriteXml(System.IO.Path.Combine(dir, "project.xml"), new ProjectData { BaseFile = baseName, Marks = Marks });
            }
            catch { if (writeBase && File.Exists(imagePath)) File.Delete(imagePath); throw; }
            baseFile = baseName; baseDirty = false; Dirty = false;
            // A raster is written only after crop/undo changes, not for every annotation.
            // Commit XML before removing older, app-generated raster files.
            foreach (string old in Directory.GetFiles(dir, "*.png"))
            {
                Guid id;
                if (old != imagePath && Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(old), "N", out id)) try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        internal static Document Open(string path)
        {
            string folder = Storage.ProjectDir(path), project = System.IO.Path.Combine(folder, "project.xml");
            if (File.Exists(project))
            {
                ProjectData data;
                using (var f = File.OpenRead(project)) data = (ProjectData)new XmlSerializer(typeof(ProjectData)).Deserialize(f);
                if (data.Version != 1 || System.IO.Path.GetFileName(data.BaseFile) != data.BaseFile || !data.BaseFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported project file.");
                using (var image = System.Drawing.Image.FromFile(System.IO.Path.Combine(folder, data.BaseFile)))
                { var doc = new Document(image, path); doc.Marks = data.Marks ?? new List<Mark>(); doc.baseFile = data.BaseFile; doc.baseDirty = false; return doc; }
            }
            using (var image = System.Drawing.Image.FromFile(path)) return new Document(image, path);
        }
        public void Dispose() { Image.Dispose(); foreach (var s in undo) s.Dispose(); foreach (var s in redo) s.Dispose(); }
    }

    internal static class Renderer
    {
        internal static Bitmap Render(Bitmap source, IEnumerable<Mark> marks)
        {
            var b = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb); b.SetResolution(96, 96);
            using (var g = Graphics.FromImage(b)) g.DrawImageUnscaled(source, 0, 0);
            foreach (var m in marks)
            {
                if (m.Kind == Tool.Mosaic) { Mosaic(b, Rectangle.Ceiling(m.Bounds), Math.Max(8, (int)m.Width * 4)); continue; }
                using (var g = Graphics.FromImage(b)) Draw(g, m);
            }
            return b;
        }
        internal static void Draw(Graphics g, Mark m)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Color color = Color.FromArgb(m.Argb); var r = m.Bounds;
            using (var pen = new Pen(color, Math.Max(1, m.Width))) using (var brush = new SolidBrush(color)) using (var font = new Font("Malgun Gothic", Math.Max(8, m.FontSize), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                pen.LineJoin = LineJoin.Round;
                switch (m.Kind)
                {
                    case Tool.Arrow:
                        using (var cap = new AdjustableArrowCap(4, 5, true)) { pen.CustomEndCap = cap; g.DrawLine(pen, m.X1, m.Y1, m.X2, m.Y2); } break;
                    case Tool.Rectangle: if (r.Width > 0 && r.Height > 0) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height); break;
                    case Tool.Ellipse: if (r.Width > 0 && r.Height > 0) g.DrawEllipse(pen, r); break;
                    case Tool.Text: g.DrawString(m.Text, font, brush, m.X1, m.Y1); break;
                    case Tool.Number:
                        g.FillEllipse(brush, r); using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(m.Number.ToString(), font, Brushes.White, r, format); break;
                    case Tool.Bubble:
                        if (r.Width < 1 || r.Height < 1) break;
                        var box = new RectangleF(r.X, r.Y, r.Width, r.Height - 18);
                        using (var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255))) g.FillRectangle(white, box);
                        g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                        g.DrawLines(pen, new[] { new PointF(box.X + 15, box.Bottom), new PointF(box.X + 6, box.Bottom + 18), new PointF(box.X + 36, box.Bottom) });
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter }) g.DrawString(m.Text, font, brush, box, format); break;
                }
            }
        }
        internal static void Mosaic(Bitmap b, Rectangle rect, int block)
        {
            rect.Intersect(new Rectangle(Point.Empty, b.Size)); if (rect.Width < 1 || rect.Height < 1) return;
            // Opaque block averages. Export uses the modified bitmap, with no original pixels underneath.
            using (var patch = b.Clone(rect, PixelFormat.Format32bppArgb)) using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.None; g.CompositingMode = CompositingMode.SourceCopy;
                for (int y = 0; y < patch.Height; y += block) for (int x = 0; x < patch.Width; x += block)
                {
                    int w = Math.Min(block, patch.Width - x), h = Math.Min(block, patch.Height - y);
                    long red = 0, green = 0, blue = 0; int count = 0;
                    for (int py = y; py < y + h; py += 2) for (int px = x; px < x + w; px += 2) { var c = patch.GetPixel(px, py); red += c.R; green += c.G; blue += c.B; count++; }
                    using (var brush = new SolidBrush(Color.FromArgb((int)(red / count), (int)(green / count), (int)(blue / count)))) g.FillRectangle(brush, rect.X + x, rect.Y + y, w, h);
                }
            }
        }
    }

    internal static class Storage
    {
        internal static string ProjectDir(string path) { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), ".parkcapture", System.IO.Path.GetFileName(path)); }
        internal static void WriteXml<T>(string path, T value)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var f = File.Create(temp)) new XmlSerializer(typeof(T)).Serialize(f, value);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static void SavePng(Bitmap image, string path) { image.SetResolution(96, 96); image.Save(path, ImageFormat.Png); }
        internal static void Export(Bitmap image, string path)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (System.IO.Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    using (var white = new Bitmap(image.Width, image.Height))
                    {
                        white.SetResolution(96, 96); using (var g = Graphics.FromImage(white)) { g.Clear(Color.White); g.DrawImageUnscaled(image, 0, 0); }
                        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
                        using (var p = new EncoderParameters(1)) { p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L); white.Save(temp, codec, p); }
                    }
                }
                else SavePng(image, temp);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static void Copy(Bitmap image)
        {
            // PNG for supporting apps, CF_DIB/Bitmap fallback for Hancom and Office.
            var data = new DataObject();
            using (var png = new MemoryStream())
            {
                image.SetResolution(96, 96); image.Save(png, ImageFormat.Png); png.Position = 0;
                data.SetData("PNG", false, png); data.SetData(DataFormats.Bitmap, true, image);
                Clipboard.SetDataObject(data, true, 6, 100);
            }
        }
        internal static string NewPath(string folder, string extension)
        { Directory.CreateDirectory(folder); return System.IO.Path.Combine(folder, "Capture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 4) + extension); }
    }
}
