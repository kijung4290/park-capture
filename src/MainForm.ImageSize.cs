using System;
using System.Drawing;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed partial class MainForm
    {
        private FlowLayoutPanel BuildImageSizeOptions()
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0) };
            var width = new Label { Text = Words.T("가로", "W"), AutoSize = true, Font = Style.Font(8.5f), ForeColor = Style.Muted, Margin = new Padding(2, 6, 5, 0) };
            var height = new Label { Text = Words.T("세로", "H"), AutoSize = true, Font = Style.Font(8.5f), ForeColor = Style.Muted, Margin = new Padding(10, 6, 5, 0) };
            imageWidthInput = new NumericUpDown { Minimum = 1, Maximum = ImageSizeRules.MaxSide, Width = 70, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 0), AccessibleName = Words.T("이미지 가로 픽셀", "Image width in pixels") };
            imageHeightInput = new NumericUpDown { Minimum = 1, Maximum = ImageSizeRules.MaxSide, Width = 70, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 0), AccessibleName = Words.T("이미지 세로 픽셀", "Image height in pixels") };
            keepAspect = new CheckBox { Text = Words.T("비율 유지", "Keep ratio"), Checked = true, AutoSize = true, Margin = new Padding(12, 5, 0, 0), ForeColor = Style.Ink, Font = Style.Font(8.5f) };
            imageWidthInput.ValueChanged += delegate { ImageSizeInputChanged(true); };
            imageHeightInput.ValueChanged += delegate { ImageSizeInputChanged(false); };
            keepAspect.CheckedChanged += delegate { if (editor != null) editor.LockImageAspect = keepAspect.Checked; if (keepAspect.Checked) ImageSizeInputChanged(true); };
            flow.Controls.AddRange(new Control[] { width, imageWidthInput, height, imageHeightInput, keepAspect }); return flow;
        }
        private void SynchronizeSizeInputs()
        {
            if (doc == null || syncingSize || sizeInputDocument == doc && sizeInputSource == doc.Image.Size) return;
            syncingSize = true;
            imageWidthInput.Value = Math.Min(ImageSizeRules.MaxSide, doc.Image.Width); imageHeightInput.Value = Math.Min(ImageSizeRules.MaxSide, doc.Image.Height);
            sizeInputDocument = doc; sizeInputSource = doc.Image.Size; syncingSize = false;
        }
        private void ImageSizeInputChanged(bool widthChanged)
        {
            if (syncingSize || doc == null) return;
            syncingSize = true;
            if (keepAspect.Checked)
            {
                if (widthChanged) imageHeightInput.Value = Math.Max(1, Math.Min(ImageSizeRules.MaxSide, Math.Round(imageWidthInput.Value * doc.Image.Height / doc.Image.Width)));
                else imageWidthInput.Value = Math.Max(1, Math.Min(ImageSizeRules.MaxSide, Math.Round(imageHeightInput.Value * doc.Image.Width / doc.Image.Height)));
            }
            syncingSize = false; UpdateStatus();
        }
        private void ApplyEditorAction()
        {
            if (editor == null || doc == null) return;
            if (editor.Tool == Tool.Resize)
            {
                try { editor.ResizeImage(new Size((int)imageWidthInput.Value, (int)imageHeightInput.Value)); }
                catch (Exception e) { Program.Report(e); }
            }
            else editor.ApplySelection();
        }
        private Control BuildBrandFooter()
        {
            var surface = new Surface { Dock = DockStyle.Fill, BackColor = Color.White, TopLine = true, Margin = Padding.Empty, Padding = new Padding(16, 0, 16, 0) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 2, 0, 0) };
            flow.Controls.Add(new Label { Text = Words.T("만든 사람 · 박주임", "Made by Parkjuim"), AutoSize = true, Font = Style.Font(8), ForeColor = Style.Muted, Margin = new Padding(0, 6, 22, 0) });
            flow.Controls.Add(BrandLink("blog.naver.com/parkjuim90", Branding.BlogUrl));
            flow.Controls.Add(new Label { Text = "·", AutoSize = true, ForeColor = Style.Disabled, Margin = new Padding(12, 5, 12, 0) });
            flow.Controls.Add(BrandLink(Branding.Email, "mailto:" + Branding.Email)); surface.Controls.Add(flow); return surface;
        }
        private LinkLabel BrandLink(string caption, string destination)
        {
            var link = new LinkLabel { Text = caption, AutoSize = true, Font = new Font("Segoe UI", 8.5f), LinkBehavior = LinkBehavior.HoverUnderline, LinkColor = Style.Muted, ActiveLinkColor = Style.Blue, VisitedLinkColor = Style.Muted, Cursor = Cursors.Hand, Margin = new Padding(0, 5, 0, 0), AccessibleName = caption };
            link.Links.Clear(); link.Links.Add(0, caption.Length, destination); link.LinkClicked += delegate { Branding.Open(destination); };
            tips.SetToolTip(link, destination); return link;
        }
    }
}
