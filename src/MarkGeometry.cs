using System;
using System.Drawing;

namespace ParkCapture
{
    internal static class MarkGeometry
    {
        internal static void Resize(Mark mark, Mark original, FrameHandle handle, PointF delta, bool constrain)
        {
            var bounds = original.Bounds;
            var size = new Size(Math.Max(1, (int)Math.Ceiling(bounds.Width)), Math.Max(1, (int)Math.Ceiling(bounds.Height)));
            bool scaleText = original.Kind == Tool.Text || original.Kind == Tool.Number;
            var next = FrameGeometry.Drag(bounds, size, 1, handle, Point.Round(delta), constrain || scaleText);
            if (scaleText)
            {
                mark.FontSize = Math.Max(8, Math.Min(512, original.FontSize * next.Width / Math.Max(1, bounds.Width)));
                var actual = mark.Bounds;
                bool left = handle == FrameHandle.Left || handle == FrameHandle.TopLeft || handle == FrameHandle.BottomLeft;
                bool top = handle == FrameHandle.Top || handle == FrameHandle.TopLeft || handle == FrameHandle.TopRight;
                float x = left ? bounds.Right - actual.Width : bounds.Left, y = top ? bounds.Bottom - actual.Height : bounds.Top;
                mark.X1 = original.Kind == Tool.Number ? x + actual.Width / 2 : x;
                mark.Y1 = original.Kind == Tool.Number ? y + actual.Height / 2 : y;
                mark.X2 = mark.X1; mark.Y2 = mark.Y1; return;
            }
            SetBounds(mark, original, next);
        }
        internal static void SetBounds(Mark mark, Mark original, RectangleF bounds)
        {
            float width = Math.Max(mark.Kind == Tool.Bubble ? 100 : mark.Kind == Tool.TextBox ? 60 : 4, bounds.Width);
            float height = Math.Max(mark.Kind == Tool.Bubble ? 50 : mark.Kind == Tool.TextBox ? 36 : 4, bounds.Height - (mark.Kind == Tool.Bubble ? 18 : 0));
            mark.X1 = original.X1 <= original.X2 ? bounds.Left : bounds.Left + width; mark.X2 = original.X1 <= original.X2 ? bounds.Left + width : bounds.Left;
            mark.Y1 = original.Y1 <= original.Y2 ? bounds.Top : bounds.Top + height; mark.Y2 = original.Y1 <= original.Y2 ? bounds.Top + height : bounds.Top;
        }
    }
}
