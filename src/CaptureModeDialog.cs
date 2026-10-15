using System.Drawing;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed class CaptureModeDialog : Form
    {
        internal bool RecordVideo;
        internal readonly Button ImageButton, VideoButton, RetryButton, CancelCaptureButton;
        internal CaptureModeDialog(Rectangle region)
        {
            Text = Words.T("캡처 방식 선택", "Choose capture mode"); ClientSize = new Size(484, 234); BackColor = Style.Sidebar; Font = Style.Font(9); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            var title = new Label { Text = Words.T("어떻게 캡처할까요?", "How would you like to capture?"), Location = new Point(20, 18), Size = new Size(440, 32), Font = Style.Font(16, FontStyle.Bold), ForeColor = Style.Ink };
            var dimensions = new Label { Text = region.Width + " × " + region.Height + Words.T(" px · 선택한 영역", " px · Selected area"), Bounds = new Rectangle(22, 57, 440, 24), ForeColor = Style.Muted };
            var image = new SoftButton { Text = Words.T("이미지 캡처", "Capture image"), Glyph = "image", Tone = ButtonTone.Primary, AutoSize = false, Bounds = new Rectangle(20, 94, 214, 50), Font = Style.Font(10) };
            var video = new SoftButton { Text = Words.T("영상 녹화", "Record video"), Glyph = "video", Tone = ButtonTone.Normal, AutoSize = false, Bounds = new Rectangle(248, 94, 214, 50), Font = Style.Font(10) };
            image.Click += delegate { RecordVideo = false; DialogResult = DialogResult.OK; }; video.Click += delegate { RecordVideo = true; DialogResult = DialogResult.OK; };
            var hint = new Label { Text = Words.T("Enter: 이미지  ·  R: 영상  ·  Esc: 취소", "Enter: image  ·  R: video  ·  Esc: cancel"), Bounds = new Rectangle(22, 156, 440, 22), Font = Style.Font(8.5f), ForeColor = Style.Muted };
            var retry = Style.Button(Words.T("영역 다시 선택", "Select again"), delegate { DialogResult = DialogResult.Retry; }); retry.Location = new Point(18, 188);
            var cancel = Style.Button(Words.T("취소", "Cancel"), delegate { DialogResult = DialogResult.Cancel; }); cancel.Location = new Point(394, 188);
            ImageButton = image; VideoButton = video; RetryButton = retry; CancelCaptureButton = cancel;
            Controls.AddRange(new Control[] { title, dimensions, image, video, hint, retry, cancel }); AcceptButton = image; CancelButton = cancel;
            var work = Screen.FromRectangle(region).WorkingArea;
            int x = region.Left + (region.Width - Width) / 2, y = region.Bottom + 14;
            if (y + Height > work.Bottom - 10) y = region.Top - Height - 14;
            Location = new Point(System.Math.Max(work.Left + 8, System.Math.Min(work.Right - Width - 8, x)), System.Math.Max(work.Top + 8, System.Math.Min(work.Bottom - Height - 8, y)));
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        { if (keyData == Keys.R) { VideoButton.PerformClick(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
    }
}
