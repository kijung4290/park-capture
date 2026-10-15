using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ParkCapture
{
    // In-process integration checks. No external window/input automation is used.
    internal static class UiChecks
    {
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Mouse(Control control, string method, Point point)
        { control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) }); }
        private static void Key(Form form, Keys key)
        { object[] args = { Message.Create(form.Handle, 0x100, IntPtr.Zero, IntPtr.Zero), key }; form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
        private static Point ImagePoint(Editor editor, float x, float y)
        { return new Point((int)((editor.Width - editor.Document.Image.Width * editor.Zoom) / 2 + x * editor.Zoom), (int)((editor.Height - editor.Document.Image.Height * editor.Zoom) / 2 + y * editor.Zoom)); }
        private static void Drag(Editor editor, Point a, Point b)
        { Mouse(editor, "OnMouseDown", a); Mouse(editor, "OnMouseMove", b); Mouse(editor, "OnMouseUp", b); }
        private static Task RecordingAction(RecordingForm recorder, string name)
        { return (Task)typeof(RecordingForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(recorder, null); }
        private static T Field<T>(MainForm form, string name)
        { return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
        internal static async Task<string> Run(MainForm form, Editor editor, Dictionary<Tool, Button> buttons, ListView library)
        {
            var results = new List<string>();
            string root = Path.Combine(Path.GetTempPath(), "ParkCapture-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                Assert(library.Width >= 220, "Library pane is too narrow");
                foreach (var button in buttons.Values) if (button.Visible) Assert(button.Parent.ClientRectangle.Contains(button.Bounds), "Toolbar clipped: " + button.Text);
                results.Add("PASS Library width and all tool buttons visible");
                var search = Field<TextBox>(form, "search"); int libraryCount = library.Items.Count;
                search.Text = "no-match-" + Guid.NewGuid().ToString("N"); Assert(library.Items.Count == 0, "Search did not filter results"); search.Clear(); Assert(library.Items.Count == libraryCount, "Clearing search did not restore results");
                var filters = Field<List<SoftButton>>(form, "filterButtons"); filters[2].PerformClick(); Assert(library.Items.Count == 0 && filters[2].Selected, "Video filter failed"); filters[0].PerformClick(); Assert(library.Items.Count == libraryCount && filters[0].Selected, "All filter failed");
                results.Add("PASS Search and image/video filter controls update the library");
                int count = editor.Document.Marks.Count;
                buttons[Tool.Arrow].PerformClick(); Assert(editor.Tool == Tool.Arrow, "Toolbar did not select arrow");
                Assert(Field<FlowLayoutPanel>(form, "styleOptions").Visible && !Field<SoftButton>(form, "applyButton").Visible, "Arrow options not shown");
                Drag(editor, ImagePoint(editor, 100, 220), ImagePoint(editor, 230, 310));
                Assert(editor.Document.Marks.Count == count + 1 && editor.Document.Marks[count].Kind == Tool.Arrow, "Drawing did not create arrow");
                Key(form, Keys.Control | Keys.Z); Assert(editor.Document.Marks.Count == count, "Ctrl+Z did not undo");
                Key(form, Keys.Control | Keys.Y); Assert(editor.Document.Marks.Count == count + 1, "Ctrl+Y did not redo");
                results.Add("PASS Actual toolbar, canvas drag, Ctrl+Z and Ctrl+Y event wiring");
                Size original = editor.Document.Image.Size;
                buttons[Tool.Crop].PerformClick();
                var apply = Field<SoftButton>(form, "applyButton"); Assert(apply.Visible && !apply.Enabled && !Field<FlowLayoutPanel>(form, "styleOptions").Visible, "Crop options state incorrect before selection");
                Drag(editor, ImagePoint(editor, 80, 100), ImagePoint(editor, 400, 300)); Assert(apply.Enabled, "Crop apply did not enable after region selection"); Key(form, Keys.Enter);
                Assert(editor.Document.Image.Width < original.Width && editor.Document.Image.Height < original.Height, "Enter did not apply crop");
                Key(form, Keys.Control | Keys.Z); Assert(editor.Document.Image.Size == original, "Crop undo did not restore size");
                Key(form, Keys.Control | Keys.Z); Assert(editor.Document.Marks.Count == count, "Drawing undo did not restore original marks");
                buttons[Tool.Select].PerformClick();
                results.Add("PASS Crop drag, Enter apply and undo restore through form shortcuts");
                Assert(!Field<SoftButton>(form, "applyButton").Visible, "Crop apply remained visible after choosing Select");
                buttons[Tool.Resize].PerformClick();
                RectangleF frame = editor.ImageFrame; Point corner = Point.Round(new PointF(frame.Right, frame.Bottom));
                Point target = Point.Round(new PointF(frame.Left + frame.Width * .8f, frame.Top + frame.Height * .8f));
                Mouse(editor, "OnMouseDown", corner); Mouse(editor, "OnMouseMove", target);
                Assert(editor.IsFrameResizing && editor.Document.Image.Size == original, "Resize preview changed committed image");
                Key(form, Keys.Escape); Assert(!editor.IsFrameResizing && editor.Document.Image.Size == original, "Esc did not cancel resize preview");
                Drag(editor, corner, target); Assert(editor.Document.Image.Width < original.Width && editor.Document.Image.Height < original.Height, "Frame drag did not resize image");
                Key(form, Keys.Control | Keys.Z); Assert(editor.Document.Image.Size == original, "Frame resize undo failed");
                var widthInput = Field<NumericUpDown>(form, "imageWidthInput"); var heightInput = Field<NumericUpDown>(form, "imageHeightInput");
                widthInput.Value = 500; Assert(heightInput.Value == 310, "Aspect-linked size inputs not synchronized");
                apply.PerformClick(); Assert(editor.Document.Image.Size == new Size(500, 310), "Pixel size apply ignored input");
                Key(form, Keys.Control | Keys.Z); Assert(editor.Document.Image.Size == original, "Pixel resize undo failed");
                Field<CheckBox>(form, "keepAspect").Checked = false;
                frame = editor.ImageFrame; Point right = Point.Round(new PointF(frame.Right, frame.Top + frame.Height / 2));
                Drag(editor, right, new Point(right.X - 40, right.Y)); Assert(editor.Document.Image.Height == original.Height && editor.Document.Image.Width < original.Width, "Unlocked edge resize changed unrelated dimension");
                Key(form, Keys.Control | Keys.Z); Field<CheckBox>(form, "keepAspect").Checked = true; buttons[Tool.Select].PerformClick();
                results.Add("PASS Image frame drag, Esc cancel, undo, ratio-linked pixel input and free edge resize");
                TestAnnotationDeletion(form, editor, buttons, root);
                results.Add("PASS Seven annotation types: selection, delete button, Delete key, undo/redo and saved deletion");
                results.Add("PASS Selection preserves undo/redo; Esc deselects; right-click deletes in drawing mode; compact delete controls fit");
                var footerLinks = Descendants(form).OfType<LinkLabel>().ToList();
                Assert(footerLinks.Any(l => l.Text == "blog.naver.com/parkjuim90" && (string)l.Links[0].LinkData == Branding.BlogUrl), "Blog footer link missing");
                Assert(footerLinks.Any(l => l.Text == Branding.Email && (string)l.Links[0].LinkData == "mailto:" + Branding.Email), "Email footer link missing");
                results.Add("PASS Footer blog/email labels and destinations");
                using (var updateDialog = new UpdateDialog(new AvailableUpdate { Manifest = new UpdateSupport.UpdateManifest { version = Updates.CurrentVersion } }))
                {
                    updateDialog.Shown += delegate
                    {
                        Assert(updateDialog.Controls.OfType<FlowLayoutPanel>().SelectMany(p => p.Controls.OfType<Button>()).All(b => b.Parent.ClientRectangle.Contains(b.Bounds)), "Update dialog buttons clipped");
                        if (form.PreviewPath != null) SaveControlPreview(updateDialog, form.PreviewPath + ".update.png");
                        Descendants(updateDialog).OfType<Button>().First(b => b.Text == Words.T("나중에", "Later")).PerformClick();
                    };
                    Assert(updateDialog.ShowDialog(form) == DialogResult.Cancel && updateDialog.PreparedStage == null, "Later unexpectedly installed an update");
                }
                results.Add("PASS Update prompt layout and Later safely defers without downloading or restarting");
                using (var settingsDialog = new SettingsDialog(new Settings { Folder = root, Hotkey = (int)Keys.D3 }))
                {
                    settingsDialog.Shown += delegate
                    {
                        var shortcut = Descendants(settingsDialog).OfType<ComboBox>().First(c => c.AccessibleName == "단축키 / Shortcut key");
                        Assert(shortcut.SelectedItem is ShortcutKeyChoice && shortcut.SelectedItem.ToString() != "D3", "Settings displays internal D3 code");
                        if (form.PreviewPath != null) SaveControlPreview(settingsDialog, form.PreviewPath + ".settings.png");
                        foreach (ShortcutKeyChoice choice in shortcut.Items) if (choice.Key == Keys.NumPad3) shortcut.SelectedItem = choice;
                        Descendants(settingsDialog).OfType<Button>().First(b => b.Text == Words.T("저장", "Save")).PerformClick();
                    };
                    Assert(settingsDialog.ShowDialog(form) == DialogResult.OK && settingsDialog.Result.Hotkey == (int)Keys.NumPad3, "Settings did not save distinct numpad virtual key");
                }
                results.Add("PASS Shortcut settings show friendly labels and preserve numpad key mapping");
                Size fullSize = form.Size; form.Size = new Size(1100, 720); form.PerformLayout();
                foreach (var button in buttons.Values) if (button.Visible) Assert(button.Parent.ClientRectangle.Contains(button.Bounds), "Compact toolbar clipped: " + button.Text);
                var save = Field<SoftButton>(form, "exportButton"); Assert(save.Parent.RectangleToScreen(save.Bounds).Right <= form.RectangleToScreen(form.ClientRectangle).Right, "Save button clipped");
                form.Size = fullSize; form.PerformLayout(); results.Add("PASS Contextual options, crop apply state and compact window layout");
                await Task.Delay(120);
                var region = new Rectangle(editor.PointToScreen(new Point(80, 120)), new Size(160, 96));
                using (var expected = Native.ScreenBitmap(region)) using (var overlay = new CaptureOverlay())
                {
                    overlay.Shown += delegate
                    {
                        overlay.BeginInvoke(new Action(delegate
                        {
                            Point a = overlay.PointToClient(region.Location), b = new Point(a.X + region.Width, a.Y + region.Height);
                            Mouse(overlay, "OnMouseDown", a); Mouse(overlay, "OnMouseMove", b); Mouse(overlay, "OnMouseUp", b); Key(overlay, Keys.Enter);
                        }));
                    };
                    Assert(overlay.ShowDialog(form) == DialogResult.OK && overlay.SelectedScreen == region, "Capture screen coordinates differ");
                    using (var image = overlay.Result) { Assert(image.Size == region.Size, "Capture result resized"); Assert(image.GetPixel(10, 10).ToArgb() == expected.GetPixel(10, 10).ToArgb(), "Frozen pixels differ"); }
                }
                results.Add("PASS Frozen-screen overlay, region selection, Enter and physical pixel crop");
                TestCaptureChoice(form, region, 0); TestCaptureChoice(form, region, 1); TestCaptureChoice(form, region, 2); TestCaptureChoice(form, region, 3);
                results.Add("PASS Region selection automatically opens mode popup: image, video, retry and cancel");
                string ffmpeg = Video.FindFFmpeg(new Settings());
                if (ffmpeg != null)
                {
                    string output;
                    using (var recorder = new RecordingForm(ffmpeg, region, root))
                    {
                        recorder.Show(form); await Task.Delay(1400);
                        await RecordingAction(recorder, "TogglePause"); await Task.Delay(160);
                        await RecordingAction(recorder, "TogglePause"); await Task.Delay(600);
                        await RecordingAction(recorder, "Finish"); output = recorder.OutputPath;
                    }
                    Assert(output != null && File.Exists(output), "Recording controller did not save MP4");
                    double duration = await Video.Duration(ffmpeg, output); Assert(duration > .8, "Recording too short");
                    string videoFrame = Path.Combine(root, "frame.png");
                    await Video.Run(ffmpeg, "-y -i " + MainForm.Quote(output) + " -frames:v 1 " + MainForm.Quote(videoFrame), CancellationToken.None);
                    using (var image = Image.FromFile(videoFrame)) Assert(image.Size == region.Size, "Recording resolution differs");
                    results.Add("PASS Actual gdigrab recording, pause/resume segments, finish and MP4 resolution");
                }
                else results.Add("SKIP Recording controller: FFmpeg not installed");
                form.Activate(); editor.Fit(); form.Refresh();
                results.Add("RESULT: PASS");
            }
            catch (Exception e) { results.Add("RESULT: FAIL\r\n" + e); }
            finally { try { Directory.Delete(root, true); } catch { } }
            return string.Join("\r\n", results);
        }
        private static IEnumerable<Control> Descendants(Control parent)
        { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Descendants(child)) yield return descendant; } }
        private static void TestAnnotationDeletion(MainForm form, Editor editor, Dictionary<Tool, Button> buttons, string root)
        {
            Document previous = editor.Document; Size fullSize = form.Size;
            var documentField = typeof(MainForm).GetField("doc", BindingFlags.Instance | BindingFlags.NonPublic);
            string source = Path.Combine(root, "annotations.png");
            using (var image = new Bitmap(1000, 620))
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White); Storage.SavePng(image, source);
                using (var doc = new Document(image, source))
                {
                    doc.Marks.AddRange(new[] {
                        new Mark { Kind = Tool.Number, X1 = 150, Y1 = 120, Number = 1 },
                        new Mark { Kind = Tool.Text, X1 = 400, Y1 = 100, Text = "선택해서 삭제" },
                        new Mark { Kind = Tool.Bubble, X1 = 700, Y1 = 80, X2 = 920, Y2 = 170, Text = "필요 없는 요소는\n클릭해서 지워요" },
                        new Mark { Kind = Tool.Arrow, X1 = 100, Y1 = 300, X2 = 280, Y2 = 400 },
                        new Mark { Kind = Tool.Rectangle, X1 = 400, Y1 = 280, X2 = 550, Y2 = 380 },
                        new Mark { Kind = Tool.Ellipse, X1 = 700, Y1 = 280, X2 = 860, Y2 = 380 },
                        new Mark { Kind = Tool.Mosaic, X1 = 100, Y1 = 480, X2 = 300, Y2 = 550 }
                    });
                    documentField.SetValue(form, doc); editor.SetDocument(doc);
                    var delete = Field<SoftButton>(form, "deleteMarkButton");
                    try
                    {
                        buttons[Tool.Select].PerformClick();
                        Assert(delete.Visible && !delete.Enabled, "Delete must be discoverable but disabled without a selection");
                        foreach (Tool kind in new[] { Tool.Number, Tool.Text, Tool.Bubble, Tool.Arrow, Tool.Rectangle, Tool.Ellipse, Tool.Mosaic })
                        {
                            var mark = doc.Marks.First(m => m.Kind == kind); var bounds = mark.Bounds;
                            Point point = ImagePoint(editor, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                            doc.Dirty = false; Mouse(editor, "OnMouseDown", point); Mouse(editor, "OnMouseUp", point);
                            Assert(editor.SelectedMark == mark && !doc.Dirty, "Selecting changed the document or selected the wrong " + kind);
                            Assert(delete.Visible && delete.Enabled, "Delete button missing for " + kind);
                            if (kind == Tool.Number) Assert(!doc.CanUndo, "Selection added a history entry");
                            if (kind == Tool.Bubble)
                            {
                                form.Size = new Size(1100, 720); form.PerformLayout();
                                Assert(delete.Parent.ClientRectangle.Contains(delete.Bounds), "Compact delete button clipped");
                                var options = Field<FlowLayoutPanel>(form, "styleOptions");
                                Assert(options.Right <= delete.Left && options.Parent.ClientRectangle.Contains(options.Bounds), "Style options overlap delete");
                                foreach (Control control in options.Controls) if (control.Visible) Assert(options.ClientRectangle.Contains(control.Bounds), "Selected bubble option clipped");
                                if (form.PreviewPath != null) SaveControlPreview(form, form.PreviewPath + ".element-selected.png");
                                form.Size = fullSize; form.PerformLayout();
                            }
                            int count = doc.Marks.Count; delete.PerformClick();
                            Assert(doc.Marks.Count == count - 1 && !doc.Marks.Any(m => m.Kind == kind) && editor.SelectedMark == null && !delete.Enabled, "Delete button did not remove only " + kind);
                            Key(form, Keys.Control | Keys.Z); Assert(doc.Marks.Count == count, "Undo failed for " + kind);
                            bounds = doc.Marks.First(m => m.Kind == kind).Bounds; point = ImagePoint(editor, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                            Mouse(editor, "OnMouseDown", point); Mouse(editor, "OnMouseUp", point);
                            Assert(doc.CanRedo, "Clicking an element destroyed redo history");
                            Key(form, Keys.Control | Keys.Y); Assert(doc.Marks.Count == count - 1, "Redo failed for " + kind);
                            Key(form, Keys.Control | Keys.Z);
                            Mouse(editor, "OnMouseDown", point); Mouse(editor, "OnMouseUp", point); Key(form, Keys.Delete);
                            Assert(doc.Marks.Count == count - 1, "Delete key failed for " + kind);
                            Key(form, Keys.Control | Keys.Z);
                        }
                        Point number = ImagePoint(editor, 150, 120);
                        Mouse(editor, "OnMouseDown", number); Mouse(editor, "OnMouseUp", number); Key(form, Keys.Escape);
                        Assert(editor.SelectedMark == null && !delete.Enabled, "Esc did not clear annotation selection");
                        Key(form, Keys.Delete); Assert(doc.Marks.Count == 7, "Delete with no selection removed an element");
                        Drag(editor, number, new Point(number.X + 25, number.Y + 12));
                        Assert(doc.Marks[0].X1 > 150, "Moving a selected annotation failed");
                        Key(form, Keys.Control | Keys.Z); Assert(doc.Marks[0].X1 == 150, "One undo did not restore annotation drag");
                        // A diagonal arrow's empty bounding-box corner must not hide an underlying element.
                        Mouse(editor, "OnMouseDown", ImagePoint(editor, 105, 395)); Mouse(editor, "OnMouseUp", ImagePoint(editor, 105, 395));
                        Assert(editor.SelectedMark == null, "Arrow selected far away from its visible line");
                        buttons[Tool.Number].PerformClick();
                        typeof(Editor).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, new object[] { new MouseEventArgs(MouseButtons.Right, 1, number.X, number.Y, 0) });
                        var menu = (ContextMenuStrip)typeof(Editor).GetField("annotationMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                        Assert(editor.Tool == Tool.Select && editor.SelectedMark != null && menu.Visible && ((SoftButton)buttons[Tool.Select]).Selected, "Right-click did not select existing annotation from drawing mode");
                        menu.Items[0].PerformClick(); menu.Close();
                        Assert(doc.Marks.Count == 6 && !doc.Marks.Any(m => m.Kind == Tool.Number), "Context menu did not delete annotation");
                        doc.Save();
                        using (var reopened = Document.Open(source)) Assert(reopened.Marks.Count == 6 && !reopened.Marks.Any(m => m.Kind == Tool.Number), "Deleted element returned after reopening");
                        Key(form, Keys.Control | Keys.Z);
                        Mouse(editor, "OnMouseDown", number); Mouse(editor, "OnMouseUp", number); Key(form, Keys.Back);
                        Assert(doc.Marks.Count == 6 && !doc.Marks.Any(m => m.Kind == Tool.Number), "Backspace did not delete the selected element");
                    }
                    finally { form.Size = fullSize; documentField.SetValue(form, previous); editor.SetDocument(previous); buttons[Tool.Select].PerformClick(); }
                }
            }
        }
        private static void TestCaptureChoice(MainForm form, Rectangle region, int mode)
        {
            using (var overlay = new CaptureOverlay(true)) using (var timer = new System.Windows.Forms.Timer { Interval = 40 })
            {
                bool retried = false, awaitingReselect = false, timedOut = false; DateTime deadline = DateTime.UtcNow.AddSeconds(8);
                Action select = delegate
                {
                    Point a = overlay.PointToClient(region.Location); if (retried) a.Offset(5, 5);
                    Mouse(overlay, "OnMouseDown", a); Mouse(overlay, "OnMouseMove", new Point(a.X + region.Width, a.Y + region.Height)); Mouse(overlay, "OnMouseUp", new Point(a.X + region.Width, a.Y + region.Height));
                };
                overlay.Shown += delegate { overlay.BeginInvoke(select); timer.Start(); };
                timer.Tick += delegate
                {
                    if (DateTime.UtcNow > deadline) { timedOut = true; if (overlay.ActiveModeDialog != null) overlay.ActiveModeDialog.CancelCaptureButton.PerformClick(); else { overlay.DialogResult = DialogResult.Cancel; overlay.Close(); } return; }
                    var dialog = overlay.ActiveModeDialog;
                    if (dialog == null) { if (awaitingReselect) { awaitingReselect = false; select(); } return; }
                    if (mode == 2 && !retried) { retried = true; awaitingReselect = true; dialog.RetryButton.PerformClick(); return; }
                    if (mode == 0 && form.PreviewPath != null) SaveControlPreview(dialog, form.PreviewPath + ".capture-mode.png");
                    if (mode == 3) dialog.CancelCaptureButton.PerformClick(); else if (mode == 1) Key(dialog, Keys.R); else dialog.ImageButton.PerformClick();
                };
                var result = overlay.ShowDialog(form); timer.Stop(); Assert(!timedOut, "Capture popup did not complete");
                if (mode == 3) Assert(result == DialogResult.Cancel && overlay.Result == null, "Cancel created a capture");
                else
                {
                    Assert(result == DialogResult.OK && overlay.Record == (mode == 1), "Capture mode choice not propagated");
                    Rectangle expected = region; if (mode == 2) expected.Offset(5, 5);
                    Assert(overlay.SelectedScreen == expected, "Popup changed selected region");
                    if (mode == 1) Assert(overlay.Result == null, "Video choice created an image");
                    else using (var image = overlay.Result) Assert(image.Size == region.Size, "Popup image capture has wrong dimensions");
                }
            }
        }
        private static void SaveControlPreview(Control control, string path)
        { using (var bitmap = new Bitmap(control.Width, control.Height)) { control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size)); Storage.SavePng(bitmap, path); } }
    }
}
