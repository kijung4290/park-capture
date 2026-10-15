using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ParkCapture
{
    internal static partial class UiChecks
    {
        private static void TextInput(MainForm main, Action action, string initial, string replacement, bool cancel)
        {
            bool handled = false, timedOut = false; Exception failure = null; DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            using (var timer = new Timer { Interval = 30 })
            {
                timer.Tick += delegate
                {
                    Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Controls.OfType<TextBox>().Any(t => t.AccessibleName == "주석 내용 / Annotation text"));
                    if (dialog == null) { if (DateTime.UtcNow > deadline) { timedOut = true; foreach (Form window in Application.OpenForms.Cast<Form>().Where(f => f != main).ToArray()) window.Close(); } return; }
                    try
                    {
                        var input = dialog.Controls.OfType<TextBox>().Single(); Assert(input.Text == initial, "Text dialog did not load saved content"); input.Text = replacement;
                        if (main.PreviewPath != null) SaveControlPreview(dialog, main.PreviewPath + ".text-dialog.png");
                        handled = true; dialog.Controls.OfType<Button>().First(b => b.Text == (cancel ? Words.T("취소", "Cancel") : Words.T("저장", "Save"))).PerformClick();
                    }
                    catch (Exception error) { failure = error; dialog.DialogResult = DialogResult.Cancel; }
                    timer.Stop();
                };
                timer.Start(); action(); timer.Stop();
            }
            Assert(!timedOut && handled && failure == null, "Text dialog failed: " + failure);
        }
        private static void TestAnnotationEditing(MainForm form, Editor editor, Dictionary<Tool, Button> buttons, string root)
        {
            Document previous = editor.Document; Size fullSize = form.Size;
            var documentField = typeof(MainForm).GetField("doc", BindingFlags.Instance | BindingFlags.NonPublic);
            string source = Path.Combine(root, "editable-annotations.png");
            using (var image = new Bitmap(1000, 620))
            {
                using (var g = Graphics.FromImage(image)) g.Clear(Color.White); Storage.SavePng(image, source);
                using (var doc = new Document(image, source))
                {
                    documentField.SetValue(form, doc); editor.SetDocument(doc);
                    try
                    {
                        buttons[Tool.TextBox].PerformClick();
                        var family = Field<ComboBox>(form, "fontFamilyInput"); family.SelectedItem = family.Items.Cast<FontChoice>().First(c => c.Name == AnnotationFonts.Default);
                        TextInput(form, delegate { Drag(editor, ImagePoint(editor, 150, 90), ImagePoint(editor, 480, 210)); }, "", "클릭해서 다시 수정할 수 있어요.\n여러 줄도 입력할 수 있습니다.", false);
                        Assert(doc.Marks.Count == 1 && editor.SelectedMark.Kind == Tool.TextBox, "Drawing did not add an editable text box");
                        var box = doc.Marks[0]; Assert(box.FontName == "Pretendard", "New text does not use bundled font");
                        family.SelectedItem = family.Items.Cast<FontChoice>().First(c => c.Name == AnnotationFonts.Legacy);
                        Field<CheckBox>(form, "boldInput").Checked = false; Field<NumericUpDown>(form, "fontInput").Value = 30;
                        Field<List<SoftButton>>(form, "swatches")[1].PerformClick();
                        Assert(box.FontName == AnnotationFonts.Legacy && !box.Bold && box.FontSize == 30 && box.Argb == Style.Blue.ToArgb(), "Selected text font/weight/size/color controls failed");
                        family.SelectedItem = family.Items.Cast<FontChoice>().First(c => c.Name == AnnotationFonts.Default);
                        Field<CheckBox>(form, "boxBorderInput").Checked = false; Field<CheckBox>(form, "boxFillInput").Checked = false;
                        Assert(!box.BoxBorder && !box.BoxFill, "Text box background toggles failed");
                        Field<CheckBox>(form, "boxBorderInput").Checked = true; Field<CheckBox>(form, "boxFillInput").Checked = true;
                        Field<NumericUpDown>(form, "elementWidthInput").Value = 360; Field<NumericUpDown>(form, "elementHeightInput").Value = 150;
                        Field<SoftButton>(form, "applyElementSizeButton").PerformClick(); Assert(box.Bounds.Size == new SizeF(360, 150), "Text box dimensions not applied");
                        string first = box.Text;
                        TextInput(form, delegate { Field<SoftButton>(form, "editTextButton").PerformClick(); }, first, "내용 수정 버튼으로 변경했습니다.", false);
                        Assert(box.Text == "내용 수정 버튼으로 변경했습니다.", "Edit text button did not save");
                        Key(form, Keys.Control | Keys.Z); Assert(doc.Marks[0].Text == first, "Undo did not restore text content");
                        buttons[Tool.Number].PerformClick(); Point center = ImagePoint(editor, 300, 150);
                        Mouse(editor, "OnMouseDown", center); Mouse(editor, "OnMouseUp", center);
                        Assert(editor.Tool == Tool.Select && doc.Marks.Count == 1 && editor.SelectedMark.Kind == Tool.TextBox, "Drawing mode click added a new mark instead of selecting existing box");
                        TextInput(form, delegate { editor.Focus(); Key(form, Keys.F2); }, first, "F2로 수정한 글자", false);
                        Assert(doc.Marks[0].Text == "F2로 수정한 글자", "F2 editing failed");
                        TextInput(form, delegate { Mouse(editor, "OnMouseDoubleClick", center); }, "F2로 수정한 글자", "더블클릭으로 수정\n프리텐다드 글꼴입니다.", false);
                        string saved = doc.Marks[0].Text;
                        TextInput(form, delegate { Field<SoftButton>(form, "editTextButton").PerformClick(); }, saved, "취소할 내용", true);
                        Assert(doc.Marks[0].Text == saved, "Cancel changed text");
                        doc.Marks.AddRange(new[] {
                            new Mark { Kind = Tool.Text, X1 = 620, Y1 = 100, Text = "글자도 클릭해서 수정", FontName = "Pretendard" },
                            new Mark { Kind = Tool.Bubble, X1 = 620, Y1 = 240, X2 = 900, Y2 = 330, Text = "말풍선도 수정할 수 있어요", FontName = "Pretendard" },
                            new Mark { Kind = Tool.Number, X1 = 100, Y1 = 315 },
                            new Mark { Kind = Tool.Rectangle, X1 = 100, Y1 = 440, X2 = 280, Y2 = 530 },
                            new Mark { Kind = Tool.Arrow, X1 = 600, Y1 = 530, X2 = 450, Y2 = 430 },
                            new Mark { Kind = Tool.Ellipse, X1 = 730, Y1 = 440, X2 = 880, Y2 = 530 },
                            new Mark { Kind = Tool.Mosaic, X1 = 320, Y1 = 290, X2 = 480, Y2 = 370 }
                        }); editor.RefreshComposite();
                        foreach (Tool kind in new[] { Tool.Text, Tool.Bubble, Tool.Number, Tool.Rectangle, Tool.Arrow, Tool.Ellipse, Tool.Mosaic })
                        {
                            var mark = doc.Marks.First(m => m.Kind == kind); var bounds = mark.Bounds;
                            buttons[Tool.Number].PerformClick(); Point point = ImagePoint(editor, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                            Mouse(editor, "OnMouseDown", point); Mouse(editor, "OnMouseUp", point);
                            Assert(editor.SelectedMark == mark && editor.Tool == Tool.Select && doc.Marks.Count == 8, "Direct click failed for " + kind);
                            if (kind != Tool.Mosaic) { Field<List<SoftButton>>(form, "swatches")[1].PerformClick(); Assert(mark.Argb == Style.Blue.ToArgb(), "Color edit failed for " + kind); }
                            if (mark.HasText)
                            {
                                Field<NumericUpDown>(form, "fontInput").Value = 32; Assert(mark.FontSize == 32, "Font size edit failed for " + kind);
                                if (kind == Tool.Number) { Field<NumericUpDown>(form, "numberInput").Value = 7; Assert(mark.Number == 7, "Number value edit failed"); }
                                else TextInput(form, delegate { Field<SoftButton>(form, "editTextButton").PerformClick(); }, mark.Text, kind == Tool.Text ? "수정한 글자" : "수정한 말풍선", false);
                            }
                            if (kind != Tool.Number && kind != Tool.Text)
                            {
                                Field<NumericUpDown>(form, "elementWidthInput").Value = (decimal)Math.Round(mark.Bounds.Width) + 20;
                                Field<NumericUpDown>(form, "elementHeightInput").Value = (decimal)Math.Round(mark.Bounds.Height) + 10;
                                Field<SoftButton>(form, "applyElementSizeButton").PerformClick(); Assert(mark.Bounds.Width > bounds.Width && mark.Bounds.Height > bounds.Height, "Shape size edit failed for " + kind);
                            }
                            var before = mark.Clone(); Point handle = ImagePoint(editor, mark.Bounds.Right, mark.Bounds.Bottom);
                            Drag(editor, handle, new Point(handle.X + 15, handle.Y + 12));
                            Assert(mark.Bounds.Width > before.Bounds.Width && doc.Marks.Count == 8, "Resize handle failed for " + kind);
                            if (kind == Tool.Arrow) Assert(mark.X1 > mark.X2 && mark.Y1 > mark.Y2, "Arrow orientation changed");
                        }
                        buttons[Tool.Select].PerformClick(); form.Size = new Size(1100, 720); form.PerformLayout();
                        center = ImagePoint(editor, 300, 150); Mouse(editor, "OnMouseDown", center); Mouse(editor, "OnMouseUp", center);
                        var options = Field<FlowLayoutPanel>(form, "annotationOptions");
                        Assert(Field<Label>(form, "fontFamilyLabel").Left < family.Left && family.Left < Field<CheckBox>(form, "boldInput").Left && Field<CheckBox>(form, "boxBorderInput").Left < Field<CheckBox>(form, "boxFillInput").Left, "Font options are not in their intended order");
                        int previousRight = 0;
                        foreach (Control child in options.Controls) if (child.Visible) { Assert(options.ClientRectangle.Contains(child.Bounds), "Compact text option clipped: " + child.Text); Assert(child.Left >= previousRight, "Text options are out of order or overlap: " + child.Text); previousRight = child.Right; }
                        var sizes = Field<FlowLayoutPanel>(form, "elementSizeOptions"); foreach (Control child in sizes.Controls) if (child.Visible) Assert(sizes.ClientRectangle.Contains(child.Bounds), "Compact size input clipped");
                        if (form.PreviewPath != null) SaveControlPreview(form, form.PreviewPath + ".text-box.png");
                        doc.Save(); using (var reopened = Document.Open(source)) Assert(reopened.Marks.Count == 8 && reopened.Marks[0].FontName == "Pretendard" && reopened.Marks[0].Text == saved && !reopened.Marks[0].Bold, "Edited annotations did not survive reopening");
                    }
                    finally { form.Size = fullSize; documentField.SetValue(form, previous); editor.SetDocument(previous); buttons[Tool.Select].PerformClick(); }
                }
            }
        }
    }
}
