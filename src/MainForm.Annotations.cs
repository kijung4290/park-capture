using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed partial class MainForm
    {
        private FlowLayoutPanel annotationOptions, elementSizeOptions;
        private ComboBox fontFamilyInput;
        private CheckBox boldInput, boxBorderInput, boxFillInput;
        private Label fontFamilyLabel, numberLabel, annotationHint;
        private NumericUpDown elementWidthInput, elementHeightInput, numberInput;
        private SoftButton editTextButton, applyElementSizeButton;
        private Control[] annotationOrder;
        private FlowLayoutPanel BuildAnnotationOptions()
        {
            annotationOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            fontFamilyLabel = OptionLabel(Words.T("글꼴", "Font"));
            fontFamilyInput = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 320, Font = Style.Font(9), Margin = new Padding(0, 5, 8, 0), AccessibleName = "글꼴 / Font" };
            foreach (var choice in AnnotationFonts.Choices()) fontFamilyInput.Items.Add(choice); fontFamilyInput.SelectedIndex = 0;
            fontFamilyInput.SelectedIndexChanged += delegate { if (!updatingStyle && fontFamilyInput.SelectedItem != null) { editor.TextFontName = ((FontChoice)fontFamilyInput.SelectedItem).Name; editor.ApplyStyle(); } };
            boldInput = OptionCheck(Words.T("굵게", "Bold"), true, delegate { editor.TextBold = boldInput.Checked; editor.ApplyStyle(); });
            boxBorderInput = OptionCheck(Words.T("테두리", "Border"), true, delegate { editor.TextBoxBorder = boxBorderInput.Checked; editor.ApplyStyle(); });
            boxFillInput = OptionCheck(Words.T("흰 배경", "White fill"), true, delegate { editor.TextBoxFill = boxFillInput.Checked; editor.ApplyStyle(); });
            editTextButton = ActionButton(Words.T("내용 수정", "Edit text"), "", delegate { editor.EditSelectedText(); }); editTextButton.Margin = new Padding(0, 1, 8, 0);
            tips.SetToolTip(editTextButton, Words.T("글자·텍스트 상자·말풍선 내용을 다시 입력합니다. 더블클릭 또는 F2", "Edit text, text box or callout. Double-click or F2"));
            numberLabel = OptionLabel(Words.T("번호", "Number")); numberInput = OptionNumber(1, 9999, 1); numberInput.AccessibleName = "번호 값 / Number value";
            numberInput.ValueChanged += delegate { if (!updatingStyle) editor.SetSelectedNumber((int)numberInput.Value); };
            elementSizeOptions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            elementWidthInput = OptionNumber(4, 16384, 100); elementWidthInput.AccessibleName = "요소 가로 / Element width";
            elementHeightInput = OptionNumber(4, 16384, 60); elementHeightInput.AccessibleName = "요소 세로 / Element height";
            applyElementSizeButton = ActionButton(Words.T("크기 적용", "Apply size"), "", delegate { editor.ResizeSelected((float)elementWidthInput.Value, (float)elementHeightInput.Value); editor.Focus(); }); applyElementSizeButton.Margin = new Padding(6, 1, 0, 0);
            elementSizeOptions.Controls.AddRange(new Control[] { OptionLabel(Words.T("가로", "W")), elementWidthInput, OptionLabel(Words.T("세로", "H")), elementHeightInput, applyElementSizeButton });
            annotationHint = new Label { Text = Words.T("기존 요소 클릭: 수정 · Alt+드래그: 겹쳐 그리기 · Delete: 삭제", "Click to edit · Alt-drag to draw over elements · Delete to remove"), AutoSize = true, Font = Style.Font(8.5f), ForeColor = Style.Muted, Margin = new Padding(0, 10, 0, 0) };
            annotationOrder = new Control[] { fontFamilyLabel, fontFamilyInput, boldInput, boxBorderInput, boxFillInput, editTextButton, numberLabel, numberInput, elementSizeOptions, annotationHint };
            annotationOptions.Controls.AddRange(annotationOrder);
            return annotationOptions;
        }
        private static Label OptionLabel(string text)
        { return new Label { Text = text, AutoSize = true, Font = Style.Font(8.5f), ForeColor = Style.Muted, Margin = new Padding(0, 9, 5, 0) }; }
        private CheckBox OptionCheck(string text, bool value, Action change)
        {
            var control = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 8, 8, 0), Font = Style.Font(8.5f) };
            control.CheckedChanged += delegate { if (!updatingStyle) change(); }; return control;
        }
        private static NumericUpDown OptionNumber(int minimum, int maximum, int value)
        { return new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value, Width = 52, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 7, 0), Font = Style.Font(9) }; }
        private void UpdateAnnotationOptions(Mark selected, Tool styled, bool hasImage)
        {
            if (annotationOptions == null) return;
            annotationOptions.SuspendLayout();
            bool text = hasImage && (styled == Tool.Text || styled == Tool.TextBox || styled == Tool.Number || styled == Tool.Bubble);
            fontFamilyLabel.Visible = fontFamilyInput.Visible = boldInput.Visible = text;
            boxBorderInput.Visible = boxFillInput.Visible = hasImage && styled == Tool.TextBox;
            editTextButton.Visible = selected != null && selected.HasText && selected.Kind != Tool.Number;
            numberLabel.Visible = numberInput.Visible = selected != null && selected.Kind == Tool.Number;
            elementSizeOptions.Visible = selected != null && selected.Kind != Tool.Text && selected.Kind != Tool.Number;
            annotationHint.Visible = hasImage && selected == null && !text;
            updatingStyle = true;
            try
            {
                string font = selected == null ? editor.TextFontName : selected.FontName;
                FontChoice choice = fontFamilyInput.Items.Cast<FontChoice>().FirstOrDefault(f => f.Name == font);
                if (choice == null) { choice = new FontChoice { Name = font, Label = font + Words.T(" (대체 글꼴)", " (fallback)") }; fontFamilyInput.Items.Add(choice); }
                fontFamilyInput.SelectedItem = choice;
                boldInput.Checked = editor.TextBold; boxBorderInput.Checked = editor.TextBoxBorder; boxFillInput.Checked = editor.TextBoxFill;
                if (selected != null)
                {
                    numberInput.Value = Math.Max(numberInput.Minimum, Math.Min(numberInput.Maximum, selected.Number));
                    var bounds = selected.Bounds;
                    elementWidthInput.Value = Math.Max(elementWidthInput.Minimum, Math.Min(elementWidthInput.Maximum, (decimal)Math.Round(bounds.Width)));
                    elementHeightInput.Value = Math.Max(elementHeightInput.Minimum, Math.Min(elementHeightInput.Maximum, (decimal)Math.Round(bounds.Height)));
                }
            }
            finally
            {
                for (int i = 0; i < annotationOrder.Length; i++) annotationOptions.Controls.SetChildIndex(annotationOrder[i], i);
                updatingStyle = false; annotationOptions.ResumeLayout(true); annotationOptions.PerformLayout();
            }
        }
    }
}
