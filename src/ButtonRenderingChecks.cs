using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ParkCapture
{
    internal static class ButtonRenderingChecks
    {
        internal static void Run(string directory)
        {
            Directory.CreateDirectory(directory);
            var rows = new List<Bitmap>(); int width = 0, height = 12;
            try
            {
                foreach (float scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
                {
                    var controls = new List<SoftButton>();
                    try
                    {
                        foreach (string text in new[] { "선택", "화살표", "말풍선", "가리기", "더 보기", "Callout" })
                            controls.Add(new SoftButton { Tone = ButtonTone.Tool, Glyph = text == "선택" ? "select" : text == "화살표" ? "arrow" : text == "가리기" ? "mosaic" : "bubble", Text = text, Font = Style.Font(8.5f * scale), MinimumSize = new Size(60, 60) });
                        controls.Add(new SoftButton { Tone = ButtonTone.Normal, Glyph = "copy", Text = "복사", Font = Style.Font(9 * scale) });
                        controls.Add(new SoftButton { Tone = ButtonTone.Primary, Glyph = "save", Text = "이미지 저장", Font = Style.Font(9 * scale) });
                        int rowWidth = 92, rowHeight = 0;
                        foreach (var button in controls) { button.Size = button.GetPreferredSize(Size.Empty); rowWidth += button.Width + 12; rowHeight = Math.Max(rowHeight, button.Height); }
                        var row = new Bitmap(rowWidth + 12, rowHeight + 16);
                        using (var g = Graphics.FromImage(row))
                        {
                            g.Clear(Color.White); using (var font = new Font("Segoe UI", 10)) g.DrawString((scale * 100) + "%", font, Brushes.DimGray, 12, rowHeight / 2);
                            int x = 92;
                            foreach (var button in controls)
                            {
                                using (var tile = new Bitmap(button.Width, button.Height))
                                {
                                    button.DrawToBitmap(tile, new Rectangle(Point.Empty, button.Size));
                                    using (var graphics = Graphics.FromImage(tile))
                                    {
                                        Rectangle icon, label; button.ContentBounds(graphics, out icon, out label);
                                        if (icon.IntersectsWith(label)) throw new Exception("Icon overlaps label: " + button.Text + " at " + scale);
                                        if (!new Rectangle(Point.Empty, button.Size).Contains(label)) throw new Exception("Label escapes button: " + button.Text);
                                        Size measured = TextRenderer.MeasureText(graphics, button.Text, button.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                                        if (label.Height < measured.Height || label.Width < measured.Width) throw new Exception("Label clipped: " + button.Text + " at " + scale);
                                    }
                                    g.DrawImageUnscaled(tile, x, 8); x += button.Width + 12;
                                }
                            }
                        }
                        rows.Add(row); width = Math.Max(width, row.Width); height += row.Height + 10;
                    }
                    finally { foreach (var control in controls) control.Dispose(); }
                }
                using (var result = new Bitmap(width, height))
                {
                    using (var g = Graphics.FromImage(result)) { g.Clear(Color.White); int y = 6; foreach (var row in rows) { g.DrawImageUnscaled(row, 0, y); y += row.Height + 10; } }
                    Storage.SavePng(result, Path.Combine(directory, "button-text-scales.png"));
                }
                // Repaint the SAME buffer after changing the label, state and parent
                // background. It must be identical to a newly allocated buffer.
                using (var parent = new Panel { BackColor = Color.White }) using (var button = new SoftButton { Tone = ButtonTone.Tool, Glyph = "arrow", Text = "화살표", Selected = true, Size = new Size(100, 80), AutoSize = false })
                using (var reused = new Bitmap(100, 80)) using (var fresh = new Bitmap(100, 80))
                {
                    parent.Controls.Add(button); button.DrawToBitmap(reused, new Rectangle(0, 0, 100, 80));
                    for (int i = 0; i < 12; i++)
                    {
                        button.Text = i % 2 == 0 ? "선택" : "번호"; button.Selected = i % 3 == 0; button.Enabled = i % 4 != 0; parent.BackColor = i % 2 == 0 ? Style.Sidebar : Color.White;
                        button.DrawToBitmap(reused, new Rectangle(0, 0, 100, 80));
                        using (var g = Graphics.FromImage(fresh)) g.Clear(Color.Magenta);
                        button.DrawToBitmap(fresh, new Rectangle(0, 0, 100, 80));
                        for (int y = 0; y < 80; y++) for (int x = 0; x < 100; x++) if (fresh.GetPixel(x, y).ToArgb() != reused.GetPixel(x, y).ToArgb()) throw new Exception("Previous text layer remains after repaint.");
                    }
                }
            }
            finally { foreach (var row in rows) row.Dispose(); }
        }
    }
}
