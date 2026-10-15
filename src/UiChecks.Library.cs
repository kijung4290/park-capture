using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ParkCapture
{
    internal static partial class UiChecks
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private const int MkShift = 0x4, MkControl = 0x8;
        // Real window messages, so the CaptureList message handling itself is exercised.
        private static void LibraryMouse(ListView library, int message, Point p, int keys)
        { SendMessage(library.Handle, message, new IntPtr(keys | (message == 0x0200 || message == 0x0201 ? 1 : 0)), new IntPtr((p.Y << 16) | (p.X & 0xFFFF))); }
        private static Point ItemCenter(ListView library, int index)
        { library.Items[index].EnsureVisible(); var r = library.Items[index].Bounds; return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
        private static void LibraryClick(ListView library, int index, int keys)
        { var p = ItemCenter(library, index); LibraryMouse(library, 0x0201, p, keys); LibraryMouse(library, 0x0202, p, keys); }
        private static int[] Selected(ListView library) { return library.SelectedIndices.Cast<int>().OrderBy(i => i).ToArray(); }

        private static void TestLibrarySelection(MainForm form, Editor editor, ListView library)
        {
            var load = typeof(MainForm).GetMethod("LoadLibrary", BindingFlags.Instance | BindingFlags.NonPublic);
            var open = typeof(MainForm).GetMethod("OpenFile", BindingFlags.Instance | BindingFlags.NonPublic);
            string folder = Field<Settings>(form, "settings").Folder, original = editor.Document == null ? null : editor.Document.PathName;
            var created = new List<string>(); var recycleFile = MainForm.RecycleFile; var recycleFolder = MainForm.RecycleFolder;
            try
            {
                Color[] colors = { Color.Tomato, Color.SeaGreen, Color.SteelBlue, Color.Goldenrod };
                for (int i = 0; i < colors.Length; i++)
                {
                    string path = Path.Combine(folder, "보관함 선택 검사 " + (i + 1) + ".png");
                    using (var image = new Bitmap(640, 360)) { using (var g = Graphics.FromImage(image)) g.Clear(colors[i]); Storage.SavePng(image, path); }
                    File.SetLastWriteTime(path, DateTime.Now.AddMinutes(10 - i)); created.Add(path);
                }
                load.Invoke(form, new object[] { null });
                Func<int, string> pathAt = i => (string)library.Items[i].Tag;
                Assert(library.Items.Count >= 5 && created.Select((p, i) => pathAt(i) == p).All(x => x), "Test captures are not listed first");

                // Plain click selects one and opens it.
                LibraryClick(library, 0, 0);
                Assert(Selected(library).SequenceEqual(new[] { 0 }) && editor.Document != null && editor.Document.PathName == created[0], "Click did not select and open a capture");
                // Drag from the first to the third item selects the range without switching the open capture.
                LibraryMouse(library, 0x0201, ItemCenter(library, 0), 0);
                var end = ItemCenter(library, 2); LibraryMouse(library, 0x0200, new Point(end.X, ItemCenter(library, 0).Y + 30), 0); LibraryMouse(library, 0x0200, ItemCenter(library, 2), 0); LibraryMouse(library, 0x0202, ItemCenter(library, 2), 0);
                Assert(Selected(library).SequenceEqual(new[] { 0, 1, 2 }), "Drag did not select the range: " + string.Join(",", Selected(library)));
                Assert(editor.Document.PathName == created[0], "Drag selection switched the open capture");
                // Ctrl+click toggles single items; Shift+click selects from the anchor.
                LibraryClick(library, 1, MkControl); Assert(Selected(library).SequenceEqual(new[] { 0, 2 }), "Ctrl+click did not deselect");
                LibraryClick(library, 3, MkControl); Assert(Selected(library).SequenceEqual(new[] { 0, 2, 3 }), "Ctrl+click did not add");
                LibraryClick(library, 1, 0); LibraryClick(library, 3, MkShift); Assert(Selected(library).SequenceEqual(new[] { 1, 2, 3 }), "Shift+click range failed");
                library.Focus(); Key(form, Keys.Control | Keys.A); Assert(library.SelectedIndices.Count == library.Items.Count, "Ctrl+A did not select all captures");

                // Delete asks first; cancelling keeps every file.
                MainForm.RecycleFile = File.Delete; MainForm.RecycleFolder = p => Directory.Delete(p, true);
                string asked = null; form.ConfirmDelete = q => { asked = q; return false; };
                LibraryClick(library, 0, 0); LibraryClick(library, 2, MkShift); library.Focus(); Key(form, Keys.Delete);
                Assert(asked != null && asked.Contains("3") && created.All(File.Exists), "Cancelled delete removed files");
                form.ConfirmDelete = q => { asked = q; return true; };
                int before = library.Items.Count; Key(form, Keys.Delete);
                Assert(!File.Exists(created[0]) && !File.Exists(created[1]) && !File.Exists(created[2]) && File.Exists(created[3]), "Delete did not remove exactly the selected captures");
                Assert(library.Items.Count == before - 3 && pathAt(0) == created[3], "Library did not refresh after delete");
                Assert(editor.Document != null && editor.Document.PathName == created[3] && Selected(library).SequenceEqual(new[] { 0 }), "Next capture was not opened after deleting the open one");

                // Double-click opens the large preview; arrow keys move and Esc closes.
                bool previewed = false; Exception failure = null;
                using (var timer = new Timer { Interval = 40 })
                {
                    timer.Tick += delegate
                    {
                        var preview = Application.OpenForms.OfType<PreviewWindow>().FirstOrDefault(); if (preview == null) return;
                        timer.Stop();
                        Func<Keys, bool> press = k => { object[] args = { Message.Create(preview.Handle, 0x100, IntPtr.Zero, IntPtr.Zero), k }; return (bool)typeof(PreviewWindow).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(preview, args); };
                        try
                        {
                            preview.Refresh();
                            Assert(preview.CurrentPath == created[3] && preview.ImageBounds.Width > 640 * 0.9f, "Preview did not show the capture large");
                            Assert(preview.Bounds == Screen.FromControl(form).Bounds, "Preview is not full screen");
                            if (form.PreviewPath != null) SaveControlPreview(preview, form.PreviewPath + ".library-preview.png");
                            press(Keys.Right); Assert(preview.Index == 1, "Right arrow did not move to the next capture");
                            press(Keys.Left); Assert(preview.Index == 0, "Left arrow did not move back");
                            previewed = true;
                        }
                        catch (Exception e) { failure = e; }
                        press(Keys.Escape);
                    };
                    timer.Start();
                    LibraryMouse(library, 0x0203, ItemCenter(library, 0), 0);
                }
                if (failure != null) throw failure;
                Assert(previewed && !Application.OpenForms.OfType<PreviewWindow>().Any(), "Double-click preview did not open and close");
            }
            finally
            {
                MainForm.RecycleFile = recycleFile; MainForm.RecycleFolder = recycleFolder; form.ConfirmDelete = null;
                foreach (string path in created) { try { File.Delete(path); string project = Storage.ProjectDir(path); if (Directory.Exists(project)) Directory.Delete(project, true); } catch { } }
                load.Invoke(form, new object[] { original });
                if (original != null) open.Invoke(form, new object[] { original });
            }
        }
    }
}
