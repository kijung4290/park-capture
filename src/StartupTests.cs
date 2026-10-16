using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace ParkCapture
{
    internal static class StartupTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

        internal static void Run(Action<string, Action> test, string root)
        {
            test("Startup: quoted executable, enable/disable and settings failure rollback in isolated registry key", delegate
            {
                string testKey = @"Software\ParkCapture\Tests\Startup-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(testKey))
                    {
                        string executable = Path.Combine(root, "한글 폴더 with spaces", "ParkCapture.exe");
                        key.SetValue("OtherApplication", "keep");
                        int saves = 0;
                        StartupRegistration.Apply(key, true, executable, delegate { saves++; });
                        Assert((string)key.GetValue(StartupRegistration.ValueName) == "\"" + executable + "\"", "Startup path not quoted correctly");
                        Assert(key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.String && saves == 1, "Enable did not persist settings");
                        StartupRegistration.Apply(key, false, executable, delegate { saves++; });
                        Assert(key.GetValue(StartupRegistration.ValueName) == null && saves == 2, "Disable left startup registration");
                        bool failed = false;
                        try { StartupRegistration.Apply(key, true, executable, delegate { throw new IOException("Simulated settings save failure"); }); }
                        catch (IOException) { failed = true; }
                        Assert(failed && key.GetValue(StartupRegistration.ValueName) == null, "Failed enable was not rolled back");
                        string old = @"%LOCALAPPDATA%\Old Capture\ParkCapture.exe";
                        key.SetValue(StartupRegistration.ValueName, old, RegistryValueKind.ExpandString);
                        foreach (bool enabled in new[] { true, false })
                        {
                            failed = false;
                            try { StartupRegistration.Apply(key, enabled, executable, delegate { throw new IOException("Simulated settings save failure"); }); }
                            catch (IOException) { failed = true; }
                            Assert(failed && (string)key.GetValue(StartupRegistration.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) == old, "Previous registration was not restored");
                            Assert(key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.ExpandString, "Rollback changed registry value kind");
                        }
                        Assert((string)key.GetValue("OtherApplication") == "keep", "Unrelated startup value changed");
                    }
                }
                finally { Registry.CurrentUser.DeleteSubKeyTree(testKey, false); }
            });
            test("Startup: settings dialog, XML persistence, legacy default and Korean/English layout", delegate
            {
                var serializer = new XmlSerializer(typeof(Settings));
                using (var reader = new StringReader("<Settings />"))
                    Assert(!((Settings)serializer.Deserialize(reader)).StartWithWindows, "Legacy settings unexpectedly enable startup");
                bool originalLanguage = Words.English;
                try
                {
                    foreach (bool english in new[] { false, true })
                    {
                        Words.English = english;
                        foreach (bool enabled in new[] { false, true })
                        using (var dialog = new SettingsDialog(new Settings { Folder = root, StartWithWindows = !enabled }))
                        {
                            var startup = dialog.Controls.OfType<CheckBox>().Single(c => c.AccessibleName == "Windows 시작 시 자동 실행 / Start with Windows");
                            Assert(startup.Checked == !enabled && dialog.ClientRectangle.Contains(startup.Bounds), "Startup checkbox state or layout is incorrect");
                            startup.Checked = enabled;
                            var save = dialog.Controls.OfType<Button>().Single(b => b.Text == Words.T("저장", "Save"));
                            Assert(dialog.ClientRectangle.Contains(save.Bounds), "Save button clipped");
                            save.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, new object[] { EventArgs.Empty });
                            Assert(dialog.DialogResult == DialogResult.OK && dialog.Result.StartWithWindows == enabled, "Dialog did not return startup selection");
                            string file = Path.Combine(root, "startup-settings.xml"); Storage.WriteXml(file, dialog.Result);
                            using (var stream = File.OpenRead(file)) Assert(((Settings)serializer.Deserialize(stream)).StartWithWindows == enabled, "Startup setting did not round-trip");
                            if (enabled) using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                            { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(root, english ? "startup-settings-en.png" : "startup-settings-ko.png")); }
                        }
                    }
                }
                finally { Words.English = originalLanguage; }
            });
        }
    }
}
