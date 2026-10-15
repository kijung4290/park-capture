using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ParkCapture
{
    internal static class ImageSizeRules
    {
        internal const int MaxSide = 16384;
        internal const long MaxPixels = 64000000;
        internal static void Validate(Size size)
        {
            if (size.Width < 1 || size.Height < 1 || size.Width > MaxSide || size.Height > MaxSide || (long)size.Width * size.Height > MaxPixels)
                throw new ArgumentException(Words.T("가로·세로는 1~16,384px, 전체는 6,400만 픽셀 이내로 지정해 주세요.", "Choose 1–16,384 pixels per side and no more than 64 million pixels in total."));
        }
        internal static Size Constrain(double width, double height)
        {
            width = Math.Max(1, width); height = Math.Max(1, height);
            double scale = Math.Min(1, Math.Min(MaxSide / width, MaxSide / height));
            scale = Math.Min(scale, Math.Sqrt(MaxPixels / (width * height)));
            return new Size(Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
        }
    }
    internal sealed partial class Document
    {
        internal bool ResizeImage(Size size)
        {
            ImageSizeRules.Validate(size); if (Image.Size == size) return false;
            var next = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb); next.SetResolution(96, 96);
            try
            {
                using (var g = Graphics.FromImage(next)) using (var attributes = new ImageAttributes())
                {
                    g.CompositingMode = CompositingMode.SourceCopy; g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(Image, new Rectangle(Point.Empty, size), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, attributes);
                }
                Checkpoint();
            }
            catch { next.Dispose(); throw; }
            float sx = size.Width / (float)Image.Width, sy = size.Height / (float)Image.Height, style = (float)Math.Sqrt(sx * sy);
            foreach (var m in Marks)
            {
                m.X1 *= sx; m.X2 *= sx; m.Y1 *= sy; m.Y2 *= sy;
                m.Width = Math.Max(.5f, m.Width * style); m.FontSize = Math.Max(8, m.FontSize * style);
            }
            Image.Dispose(); Image = next; baseDirty = true; Dirty = true; return true;
        }
    }
    internal enum FrameHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }
    internal static class FrameGeometry
    {
        internal static RectangleF Drag(RectangleF frame, Size image, float zoom, FrameHandle handle, Point delta, bool keepRatio)
        {
            bool left = handle == FrameHandle.TopLeft || handle == FrameHandle.Left || handle == FrameHandle.BottomLeft;
            bool right = handle == FrameHandle.TopRight || handle == FrameHandle.Right || handle == FrameHandle.BottomRight;
            bool top = handle == FrameHandle.TopLeft || handle == FrameHandle.Top || handle == FrameHandle.TopRight;
            bool bottom = handle == FrameHandle.BottomLeft || handle == FrameHandle.Bottom || handle == FrameHandle.BottomRight;
            double width = Math.Max(1, image.Width + (left ? -delta.X : right ? delta.X : 0) / zoom);
            double height = Math.Max(1, image.Height + (top ? -delta.Y : bottom ? delta.Y : 0) / zoom);
            if (keepRatio)
            {
                double sx = width / image.Width, sy = height / image.Height;
                double scale = !(left || right) ? sy : !(top || bottom) ? sx : Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy;
                width = image.Width * scale; height = image.Height * scale;
            }
            Size size = ImageSizeRules.Constrain(width, height); float w = size.Width * zoom, h = size.Height * zoom;
            float x = left ? frame.Right - w : right ? frame.Left : frame.Left + (frame.Width - w) / 2;
            float y = top ? frame.Bottom - h : bottom ? frame.Top : frame.Top + (frame.Height - h) / 2;
            return new RectangleF(x, y, w, h);
        }
    }
}
