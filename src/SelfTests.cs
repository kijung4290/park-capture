using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ParkCapture
{
    internal static class SelfTests
    {
        private static readonly List<string> report = new List<string>();
        private static int failures;
        private static void Assert(bool value, string description) { if (!value) throw new Exception(description); }
        private static void Test(string name, Action test)
        { try { test(); report.Add("PASS " + name); } catch (Exception e) { failures++; report.Add("FAIL " + name + "\n" + e); } }
        private static Bitmap Pattern(int width = 120, int height = 80)
        {
            var b = new Bitmap(width, height); b.SetResolution(96, 96);
            using (var g = Graphics.FromImage(b)) { g.Clear(Color.White); g.FillRectangle(Brushes.Blue, 0, 0, width / 2, height / 2); g.FillRectangle(Brushes.Red, width / 2, 0, width - width / 2, height / 2); g.FillRectangle(Brushes.Green, 0, height / 2, width / 2, height - height / 2); }
            return b;
        }
        internal static int Run(string reportPath)
        {
            string root = Path.Combine(Path.GetTempPath(), "ParkCapture-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                UpdateTests.Run(Test, root);
                Test("Buttons: enlarged text, icon spacing, complete labels and repaint regression", delegate { ButtonRenderingChecks.Run(root); });
                Test("Image resize: pixels, editable marks, undo/redo, saved dimensions and original preservation", delegate
                {
                    string path = Path.Combine(root, "resize-original.png");
                    using (var b = Pattern()) Storage.SavePng(b, path); byte[] original = File.ReadAllBytes(path);
                    using (var d = Document.Open(path))
                    {
                        d.Marks.Add(new Mark { Kind = Tool.Arrow, X1 = 20, Y1 = 10, X2 = 100, Y2 = 40, Width = 4, FontSize = 24 });
                        Assert(d.ResizeImage(new Size(60, 40)), "Resize was not applied");
                        Assert(d.Image.Size == new Size(60, 40) && d.Marks.Count == 1 && d.Marks[0].X1 == 10 && d.Marks[0].Y2 == 20, "Image or annotations not scaled");
                        Assert(d.Marks[0].Width == 2 && d.Marks[0].FontSize == 12, "Annotation style not scaled");
                        Assert(d.Image.GetPixel(5, 5).ToArgb() == Color.Blue.ToArgb(), "Unexpected interior resampling pixels");
                        d.Undo(); Assert(d.Image.Size == new Size(120, 80) && d.Marks[0].X1 == 20, "Resize undo failed");
                        d.Redo(); d.Save(); using (var rendered = d.Render()) Storage.Export(rendered, Path.Combine(root, "resized-export.png"));
                    }
                    using (var d = Document.Open(path)) Assert(d.Image.Size == new Size(60, 40) && d.Marks.Count == 1, "Resized project did not reopen");
                    using (var exported = Image.FromFile(Path.Combine(root, "resized-export.png"))) Assert(exported.Size == new Size(60, 40), "Export ignored new dimensions");
                    Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Resize overwrote original capture");
                    using (var b = Pattern()) using (var d = new Document(b, path))
                    {
                        bool rejected = false; try { d.ResizeImage(new Size(16000, 16000)); } catch (ArgumentException) { rejected = true; }
                        Assert(rejected && !d.CanUndo && !d.Dirty && d.Image.Size == b.Size, "Invalid size mutated document");
                    }
                    using (var alpha = new Bitmap(96, 64))
                    {
                        using (var g = Graphics.FromImage(alpha)) { g.Clear(Color.Transparent); g.FillRectangle(Brushes.Red, 48, 0, 48, 64); }
                        using (var d = new Document(alpha, path)) { d.ResizeImage(new Size(48, 32)); Assert(d.Image.GetPixel(2, 10).A == 0 && d.Image.GetPixel(45, 10).A == 255, "Resize lost transparency"); }
                    }
                });
                Test("Resize frame: all eight handles preserve ratio and opposite anchors", delegate
                {
                    var frame = new RectangleF(100, 90, 500, 310); var size = new Size(1000, 620);
                    foreach (FrameHandle handle in Enum.GetValues(typeof(FrameHandle)))
                    {
                        if (handle == FrameHandle.None) continue;
                        var r = FrameGeometry.Drag(frame, size, .5f, handle, new Point(45, 30), true);
                        Assert(Math.Abs(r.Width / r.Height - 1000f / 620) < .01, "Aspect ratio changed: " + handle);
                    }
                    var left = FrameGeometry.Drag(frame, size, .5f, FrameHandle.Left, new Point(50, 0), false);
                    Assert(Math.Abs(left.Right - frame.Right) < .01 && left.Height == frame.Height && left.Width == 450, "Free left edge did not hold opposite edge");
                    var topLeft = FrameGeometry.Drag(frame, size, .5f, FrameHandle.TopLeft, new Point(50, 30), true);
                    Assert(Math.Abs(topLeft.Right - frame.Right) < .01 && Math.Abs(topLeft.Bottom - frame.Bottom) < .01, "Opposite corner moved");
                    Size constrained = ImageSizeRules.Constrain(50000, 50000); Assert((long)constrained.Width * constrained.Height <= ImageSizeRules.MaxPixels, "Oversized frame not constrained");
                });
                Test("Character logo is embedded and number-row/numpad shortcut labels are distinct", delegate
                {
                    Assert(Branding.Logo != null && Branding.Logo.Width > 0, "Character logo missing from executable");
                    bool english = Words.English;
                    try
                    {
                        Words.English = false;
                        Assert(ShortcutKeys.Label(System.Windows.Forms.Keys.D3, true) == "숫자 3 (위쪽)", "Number row label still shows D3");
                        Assert(ShortcutKeys.Label(System.Windows.Forms.Keys.NumPad3, true).Contains("오른쪽"), "Numpad not distinguished");
                        Assert(MainForm.HotkeyText(new Settings { Hotkey = (int)System.Windows.Forms.Keys.D3 }) == "Ctrl+Shift+3", "Stored D3 mapping changed");
                        Words.English = true; Assert(ShortcutKeys.Label(System.Windows.Forms.Keys.D3, true) == "3 (top row)", "English shortcut label incorrect");
                    }
                    finally { Words.English = english; }
                });
                Test("Arrow, ellipse, text, number, callout and box render at original size", delegate
                {
                    using (var b = Pattern(600, 400))
                    {
                        var marks = new List<Mark>();
                        foreach (Tool tool in new[] { Tool.Arrow, Tool.Ellipse, Tool.Text, Tool.Number, Tool.Bubble, Tool.Rectangle }) marks.Add(new Mark { Kind = tool, X1 = 60 + 70 * marks.Count, Y1 = 180, X2 = 150 + 70 * marks.Count, Y2 = 260, Text = "테스트", Argb = Color.Black.ToArgb() });
                        using (var rendered = Renderer.Render(b, marks)) { Assert(rendered.Size == b.Size, "Dimensions changed"); int count = 0; for (int x = 0; x < 600; x += 3) for (int y = 140; y < 320; y += 3) if (rendered.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) count++; Assert(count > 100, "Annotations missing"); }
                    }
                });
                Test("Mosaic averages blocks, stays in region, and flattens original pixels", delegate
                {
                    using (var b = Pattern()) using (var rendered = Renderer.Render(b, new[] { new Mark { Kind = Tool.Mosaic, X1 = 48, Y1 = 20, X2 = 72, Y2 = 60, Width = 4 } }))
                    { Assert(rendered.GetPixel(49, 25).ToArgb() == rendered.GetPixel(62, 25).ToArgb(), "Block not flattened"); Assert(rendered.GetPixel(49, 25).ToArgb() != b.GetPixel(49, 25).ToArgb(), "Original pixels survived mosaic"); Assert(rendered.GetPixel(5, 5).ToArgb() == b.GetPixel(5, 5).ToArgb(), "Outside region changed"); Assert(rendered.GetPixel(49, 25).A == 255, "Mosaic not opaque"); }
                });
                Test("Crop is pixel exact and undo/redo restores pixels and annotations", delegate
                {
                    using (var b = Pattern()) using (var d = new Document(b, Path.Combine(root, "crop.png")))
                    {
                        d.Marks.Add(new Mark { Kind = Tool.Number, X1 = 80, Y1 = 60 }); d.Crop(new Rectangle(10, 10, 40, 25));
                        Assert(d.Image.Size == new Size(40, 25) && d.Marks.Count == 0, "Crop failed"); Assert(d.Image.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "Crop origin mismatch");
                        Assert(d.Undo() && d.Image.Size == b.Size && d.Marks.Count == 1, "Undo failed"); Assert(d.Redo() && d.Image.Size == new Size(40, 25), "Redo failed"); Assert(b.Width == 120, "Source mutated");
                    }
                });
                Test("Horizontal and vertical band cuts stitch exact edges", delegate
                {
                    using (var b = Pattern()) using (var d = new Document(b, Path.Combine(root, "cut.png")))
                    {
                        d.CutBand(new Rectangle(0, 30, 120, 20), true); Assert(d.Image.Size == new Size(120, 60), "Wrong height"); Assert(d.Image.GetPixel(4, 30).ToArgb() == b.GetPixel(4, 50).ToArgb(), "Horizontal join wrong");
                        d.Undo(); d.CutBand(new Rectangle(50, 0, 20, 80), false); Assert(d.Image.Size == new Size(100, 80), "Wrong width"); Assert(d.Image.GetPixel(50, 4).ToArgb() == b.GetPixel(70, 4).ToArgb(), "Vertical join wrong");
                    }
                });
                Test("Invalid or full-image band cuts do not corrupt the document", delegate
                {
                    using (var b = Pattern()) using (var d = new Document(b, Path.Combine(root, "invalid.png")))
                    { d.CutBand(new Rectangle(0, 0, 120, 80), true); d.CutBand(Rectangle.Empty, false); d.Crop(new Rectangle(900, 900, 10, 10)); Assert(d.Image.Size == b.Size, "Invalid cut changed image"); }
                });
                Test("Project save/reopen preserves source, base pixels and editable marks", delegate
                {
                    string path = Path.Combine(root, "source.png");
                    using (var b = Pattern()) Storage.SavePng(b, path); byte[] original = File.ReadAllBytes(path);
                    using (var d = Document.Open(path)) { d.Crop(new Rectangle(10, 10, 75, 50)); d.Checkpoint(); d.Marks.Add(new Mark { Kind = Tool.Text, Text = "한글 주석", X1 = 4, Y1 = 5 }); d.Save(); }
                    using (var d = Document.Open(path)) { Assert(d.Image.Size == new Size(75, 50), "Edited base missing"); Assert(d.Marks.Count == 1 && d.Marks[0].Text == "한글 주석", "Editable marks missing"); d.Save(); }
                    Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Original source overwritten");
                });
                Test("PNG/JPEG export preserves pixel dimensions and 96 DPI", delegate
                {
                    using (var b = Pattern(333, 211))
                    {
                        foreach (string ext in new[] { ".png", ".jpg" })
                        { string p = Path.Combine(root, "export" + ext); Storage.Export(b, p); Storage.Export(b, p); using (var read = Image.FromFile(p)) { Assert(read.Size == b.Size, "Export resized image"); Assert(Math.Abs(read.HorizontalResolution - 96) < 1, "Wrong DPI"); } }
                    }
                });
                Test("Wide images above 4K keep their full width", delegate
                { using (var b = Pattern(5201, 111)) using (var r = Renderer.Render(b, new[] { new Mark { Kind = Tool.Rectangle, X1 = 5100, Y1 = 20, X2 = 5180, Y2 = 80 } })) Assert(r.Size == new Size(5201, 111), "Wide capture resized"); });
                if (Environment.GetEnvironmentVariable("PARKCAPTURE_HEADLESS_TESTS") == "1") report.Add("SKIP Physical screen capture: headless CI; tested locally with an interactive display");
                else Test("Physical screen capture has requested dimensions", delegate
                { var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds; using (var b = Native.ScreenBitmap(new Rectangle(screen.X + 10, screen.Y + 10, 64, 48))) Assert(b.Size == new Size(64, 48), "Screen capture wrong size"); });
                string ffmpeg = Video.FindFFmpeg(new Settings());
                if (ffmpeg == null) report.Add("SKIP Video checks: FFmpeg unavailable");
                else Test("Video: generate, read duration, trim/speed MP4, GIF dimensions and cancellation", delegate
                {
                    Task.Run(async delegate
                    {
                        string input = Path.Combine(root, "input.mp4"), mp4 = Path.Combine(root, "trim.mp4"), gif = Path.Combine(root, "clip.gif");
                        await Video.Run(ffmpeg, "-y -f lavfi -i testsrc2=size=160x96:rate=30 -t 2 -an -c:v libx264 -pix_fmt yuv420p " + MainForm.Quote(input), CancellationToken.None);
                        double duration = await Video.Duration(ffmpeg, input); Assert(duration > 1.9 && duration < 2.2, "Duration incorrect");
                        await Video.Export(ffmpeg, input, mp4, .25, 1.25, 2, false, 30, CancellationToken.None);
                        double trimmed = await Video.Duration(ffmpeg, mp4); Assert(trimmed >= .4 && trimmed < .75, "Trim/speed incorrect: " + trimmed);
                        await Video.Export(ffmpeg, input, gif, .25, 1.25, 1, true, 15, CancellationToken.None);
                        using (var image = Image.FromFile(gif)) { Assert(image.Size == new Size(160, 96), "GIF resized"); Assert(image.GetFrameCount(FrameDimension.Time) > 1, "GIF not animated"); }
                        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); bool canceled = false; try { await Video.Export(ffmpeg, input, Path.Combine(root, "cancel.mp4"), 0, 2, 1, false, 30, cancel.Token); } catch (OperationCanceledException) { canceled = true; } Assert(canceled && !File.Exists(Path.Combine(root, "cancel.mp4")), "Cancellation did not clean partial output"); }
                        Assert(!Directory.GetFiles(root, ".parkcapture-*").Any(), "Temporary video output leaked");
                    }).GetAwaiter().GetResult();
                });
            }
            catch (Exception e) { failures++; report.Add(e.ToString()); }
            finally
            {
                report.Add("RESULT: " + (failures == 0 ? "PASS" : "FAIL") + " (" + failures + " failures)");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))); File.WriteAllLines(reportPath, report, System.Text.Encoding.UTF8);
                // This unique directory is created above exclusively for this test run.
                try { Directory.Delete(root, true); } catch { }
            }
            return failures == 0 ? 0 : 1;
        }
    }
}
