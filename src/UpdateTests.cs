using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml.Serialization;
using ParkCapture.UpdateSupport;

namespace ParkCapture
{
    internal static class UpdateTests
    {
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Assert(rejected, message); }
        internal static void Run(Action<string, Action> test, string root)
        {
            test("Updates: trusted signature, tampered manifest rejection and strict version/asset validation", delegate
            {
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    rsa.PersistKeyInCsp = false;
                    var manifest = new UpdateManifest { schema = 1, version = "0.4.0", asset = "ParkCapture-0.4.0-portable.zip", size = 123, sha256 = new string('a', 64) };
                    byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(manifest));
                    byte[] signature = rsa.SignData(bytes, CryptoConfig.MapNameToOID("SHA256"));
                    Assert(UpdatePackage.Read(bytes, signature, rsa.ToXmlString(false)).version == manifest.version, "Valid manifest rejected");
                    bytes[bytes.Length - 2] ^= 1;
                    Reject(delegate { UpdatePackage.Read(bytes, signature, rsa.ToXmlString(false)); }, "Tampered manifest accepted");
                    manifest.asset = "../bad.exe"; bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(manifest)); signature = rsa.SignData(bytes, CryptoConfig.MapNameToOID("SHA256"));
                    Reject(delegate { UpdatePackage.Read(bytes, signature, rsa.ToXmlString(false)); }, "Unsafe asset accepted");
                    Reject(delegate { UpdatePackage.ParseVersion("1.2.3-beta"); }, "Prerelease version accepted");
                    Assert(UpdatePackage.ParseVersion("0.4.10") > UpdatePackage.ParseVersion("0.4.9"), "Version ordering is textual");
                }
            });
            test("Updates: signed package hash, archive allowlist, executable versions and traversal protection", delegate
            {
                string package = Path.Combine(root, "update-test.zip");
                using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
                {
                    foreach (string file in UpdatePackage.Files.Take(3)) archive.CreateEntryFromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file), file);
                    using (var writer = new StreamWriter(archive.CreateEntry("README.md").Open())) writer.Write("Park Capture test package");
                }
                var manifest = new UpdateManifest { schema = 1, version = Updates.CurrentVersion, size = new FileInfo(package).Length, sha256 = UpdatePackage.Hash(package) };
                UpdatePackage.Extract(package, Path.Combine(root, "valid-update"), manifest);
                manifest.version = "99.0.0";
                Reject(delegate { UpdatePackage.Extract(package, Path.Combine(root, "wrong-version-update"), manifest); }, "Mismatched executable version accepted");
                using (var archive = ZipFile.Open(package, ZipArchiveMode.Update)) using (var writer = new StreamWriter(archive.CreateEntry("../escaped.txt").Open())) writer.Write("bad");
                Reject(delegate { UpdatePackage.Verify(package, manifest); }, "Altered archive accepted");
                manifest.version = Updates.CurrentVersion; manifest.size = new FileInfo(package).Length; manifest.sha256 = UpdatePackage.Hash(package);
                Reject(delegate { UpdatePackage.Extract(package, Path.Combine(root, "unsafe-update"), manifest); }, "Archive traversal accepted");
                Assert(!File.Exists(Path.Combine(root, "escaped.txt")), "Archive wrote outside staging directory");
            });
            test("Updates: application-only replacement preserves captures and user settings", delegate
            {
                string source = Path.Combine(root, "install-new"), target = Path.Combine(root, "install-old"); Directory.CreateDirectory(source); Directory.CreateDirectory(target);
                foreach (string file in UpdatePackage.Files) { File.WriteAllText(Path.Combine(source, file), "new-" + file); File.WriteAllText(Path.Combine(target, file), "old-" + file); }
                File.WriteAllText(Path.Combine(target, "my-capture.png"), "capture"); File.WriteAllText(Path.Combine(target, "settings.xml"), "settings");
                UpdateInstaller.Install(source, target, Path.Combine(root, "install-backup"));
                foreach (string file in UpdatePackage.Files) Assert(File.ReadAllText(Path.Combine(target, file)) == "new-" + file, "File not updated: " + file);
                Assert(File.ReadAllText(Path.Combine(target, "my-capture.png")) == "capture" && File.ReadAllText(Path.Combine(target, "settings.xml")) == "settings", "Update changed user data");
            });
            test("Updates: a locked file rolls back earlier replacements without leftover temporary files", delegate
            {
                string source = Path.Combine(root, "rollback-new"), target = Path.Combine(root, "rollback-old"); Directory.CreateDirectory(source); Directory.CreateDirectory(target);
                foreach (string file in UpdatePackage.Files) { File.WriteAllText(Path.Combine(source, file), "new-" + file); File.WriteAllText(Path.Combine(target, file), "old-" + file); }
                bool failed = false;
                using (var locked = new FileStream(Path.Combine(target, "ParkCapture.exe.config"), FileMode.Open, FileAccess.Read, FileShare.Read))
                { try { UpdateInstaller.Install(source, target, Path.Combine(root, "rollback-backup")); } catch (IOException) { failed = true; } }
                Assert(failed, "Locked file did not block installation");
                foreach (string file in UpdatePackage.Files) Assert(File.ReadAllText(Path.Combine(target, file)) == "old-" + file, "Rollback did not restore " + file);
                Assert(!Directory.GetFiles(target, ".parkcapture-*").Any(), "Temporary installation files remain");
            });
            test("Updates: existing settings enable automatic checks and explicit opt-out survives serialization", delegate
            {
                var serializer = new XmlSerializer(typeof(Settings));
                using (var input = new StringReader("<Settings><Language>ko</Language><Hotkey>51</Hotkey></Settings>"))
                { var settings = (Settings)serializer.Deserialize(input); Assert(settings.AutoUpdate && settings.Hotkey == 51, "Legacy settings not preserved"); }
                using (var text = new StringWriter())
                { serializer.Serialize(text, new Settings { AutoUpdate = false }); using (var input = new StringReader(text.ToString())) Assert(!((Settings)serializer.Deserialize(input)).AutoUpdate, "Auto-update opt-out not retained"); }
            });
        }
    }
}
