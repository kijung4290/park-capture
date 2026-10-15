using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ParkCapture
{
    internal sealed class FontChoice
    {
        internal string Name, Label;
        public override string ToString() { return Label; }
    }
    internal static class AnnotationFonts
    {
        internal const string Default = "Pretendard", Legacy = "Malgun Gothic";
        private static readonly PrivateFontCollection bundled = new PrivateFontCollection();
        private static readonly List<IntPtr> memory = new List<IntPtr>(), handles = new List<IntPtr>();
        static AnnotationFonts()
        {
            foreach (string weight in new[] { "Regular", "Bold" })
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ParkCapture.Pretendard." + weight + ".ttf"))
                {
                    if (stream == null) throw new InvalidOperationException("Bundled Pretendard font is missing.");
                    byte[] bytes = new byte[stream.Length]; int offset = 0, read;
                    while ((read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
                    IntPtr data = Marshal.AllocCoTaskMem(bytes.Length); Marshal.Copy(bytes, 0, data, bytes.Length); memory.Add(data);
                    bundled.AddMemoryFont(data, bytes.Length); uint count = 0; handles.Add(AddFontMemResourceEx(data, (uint)bytes.Length, IntPtr.Zero, ref count));
                }
            }
            AppDomain.CurrentDomain.ProcessExit += delegate
            { bundled.Dispose(); foreach (var handle in handles) if (handle != IntPtr.Zero) RemoveFontMemResourceEx(handle); foreach (var data in memory) Marshal.FreeCoTaskMem(data); };
        }
        internal static Font Create(string name, float size, bool bold)
        {
            FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular; size = Math.Max(8, Math.Min(1024, size));
            if (name == Default) return new Font(bundled.Families.First(f => f.Name == Default), size, style, GraphicsUnit.Pixel);
            try { return new Font(string.IsNullOrWhiteSpace(name) ? Legacy : name, size, style, GraphicsUnit.Pixel); }
            catch (ArgumentException) { return new Font(Legacy, size, style, GraphicsUnit.Pixel); }
        }
        internal static IEnumerable<FontChoice> Choices()
        {
            yield return new FontChoice { Name = Default, Label = "프리텐다드" };
            yield return new FontChoice { Name = Legacy, Label = "맑은 고딕 (기본)" };
            foreach (string name in FontFamily.Families.Select(f => f.Name).Where(n => n != Default && n != Legacy).Distinct().OrderBy(n => n))
                yield return new FontChoice { Name = name, Label = name };
        }
        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint size, IntPtr reserved, ref uint count);
        [DllImport("gdi32.dll")] private static extern bool RemoveFontMemResourceEx(IntPtr handle);
    }
}
