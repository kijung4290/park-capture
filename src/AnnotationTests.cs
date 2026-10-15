using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace ParkCapture
{
    internal static class AnnotationTests
    {
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        internal static void Run(Action<string, Action> test, string root)
        {
            test("Annotation fonts: embedded Pretendard regular/bold and installed default fonts render Korean", delegate
            {
                foreach (string family in new[] { AnnotationFonts.Default, AnnotationFonts.Legacy, "Arial" }) foreach (bool bold in new[] { false, true })
                using (var font = AnnotationFonts.Create(family, 28, bold)) using (var bitmap = new Bitmap(420, 70)) using (var graphics = Graphics.FromImage(bitmap))
                {
                    if (family == AnnotationFonts.Default) Assert(font.FontFamily.Name == "Pretendard", "Bundled font fell back");
                    Assert(font.Bold == bold, "Font weight not honored");
                    graphics.Clear(Color.White); graphics.DrawString("교육 자료 · 튜토리얼 ABC 123", font, Brushes.Black, 2, 2);
                    bool drawn = false; for (int y = 0; y < bitmap.Height; y += 2) for (int x = 0; x < bitmap.Width; x += 2) if (bitmap.GetPixel(x, y).R < 100) drawn = true;
                    Assert(drawn, "Font produced no pixels");
                }
                using (var input = new StringReader("<Mark><Kind>Text</Kind><Text>legacy</Text></Mark>"))
                { var mark = (Mark)new XmlSerializer(typeof(Mark)).Deserialize(input); Assert(mark.FontName == AnnotationFonts.Legacy && mark.Bold, "Legacy text appearance changed"); }
            });
            test("Text boxes: editable content, fonts, color, wrapping frame and background survive undo/redo and reopen", delegate
            {
                string path = Path.Combine(root, "text-box.png"); using (var image = new Bitmap(450, 250)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.LightGray); Storage.SavePng(image, path); }
                byte[] original = File.ReadAllBytes(path);
                using (var doc = Document.Open(path))
                {
                    doc.Marks.Add(new Mark { Kind = Tool.TextBox, X1 = 20, Y1 = 20, X2 = 300, Y2 = 180, Text = "처음 내용\n여러 줄 입력", FontName = "Pretendard", FontSize = 26, Bold = false, Argb = Color.Blue.ToArgb() });
                    doc.Checkpoint(); var mark = doc.Marks[0]; mark.Text = "수정한 내용"; mark.FontName = AnnotationFonts.Legacy; mark.X2 = 350; mark.BoxFill = false; mark.BoxBorder = false;
                    Assert(doc.Undo() && doc.Marks[0].Text.StartsWith("처음") && doc.Marks[0].FontName == "Pretendard", "Text undo failed");
                    Assert(doc.Redo() && !doc.Marks[0].BoxFill && doc.Marks[0].X2 == 350, "Text redo failed"); doc.Save();
                    using (var rendered = doc.Render()) Assert(rendered.GetPixel(25, 170).ToArgb() == Color.LightGray.ToArgb(), "Transparent box painted a fill");
                }
                using (var doc = Document.Open(path))
                { var mark = doc.Marks[0]; Assert(mark.Kind == Tool.TextBox && mark.Text == "수정한 내용" && mark.FontName == AnnotationFonts.Legacy && !mark.Bold && !mark.BoxBorder && !mark.BoxFill && mark.Argb == Color.Blue.ToArgb(), "Saved text properties were lost"); }
                Assert(File.ReadAllBytes(path).SequenceEqual(original), "Editing text changed the source image");
            });
            test("Annotation handles: all eight directions resize boxes; number and text scale without changing color", delegate
            {
                var original = new Mark { Kind = Tool.TextBox, X1 = 100, Y1 = 100, X2 = 300, Y2 = 200, Argb = Color.Blue.ToArgb(), FontName = "Pretendard" };
                for (int i = 1; i <= 8; i++)
                {
                    var mark = original.Clone(); var handle = (FrameHandle)i;
                    bool left = i == 1 || i == 7 || i == 8, top = i == 1 || i == 2 || i == 3;
                    MarkGeometry.Resize(mark, original, handle, new PointF(left ? -30 : 30, top ? -20 : 20), false);
                    Assert(mark.Bounds.Width >= original.Bounds.Width && mark.Bounds.Height >= original.Bounds.Height && mark.Bounds != original.Bounds, "Handle did not enlarge text box: " + handle);
                    Assert(mark.FontSize == original.FontSize && mark.Argb == original.Argb && mark.FontName == original.FontName, "Box resize changed text styling");
                    if (left) Assert(Math.Abs(mark.Bounds.Right - original.Bounds.Right) < 1, "Left handle moved opposite edge");
                    if (top) Assert(Math.Abs(mark.Bounds.Bottom - original.Bounds.Bottom) < 1, "Top handle moved opposite edge");
                }
                foreach (Tool kind in new[] { Tool.Text, Tool.Number })
                {
                    var before = new Mark { Kind = kind, Text = "크기 조절", X1 = 100, Y1 = 100, FontName = "Pretendard" }; var after = before.Clone();
                    MarkGeometry.Resize(after, before, FrameHandle.BottomRight, new PointF(50, 40), false);
                    Assert(after.FontSize > before.FontSize && after.Bounds.Left >= before.Bounds.Left - 1 && after.Argb == before.Argb, "Text/number handle did not scale font");
                }
            });
        }
    }
}
