using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace ParkCapture
{
    internal sealed partial class MainForm : Form
    {
        private Settings settings;
        private Editor editor;
        private Document doc;
        private ListView library;
        private ImageList thumbnails;
        private Label status, title, count;
        private ComboBox filter;
        private readonly Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();
        private readonly NotifyIcon tray;
        private readonly Timer saveTimer = new Timer { Interval = 900 };
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 10000 };
        private bool quitting, capturing, loading;
        private string selectedVideo;
        private RecordingForm recorder;
        internal string PreviewPath;
        internal MainForm()
        {
            settings = Settings.Load(); Words.English = settings.Language == "en";
            if (Program.Demo) { settings.Folder = Path.Combine(Path.GetTempPath(), "ParkCapture-demo-polished"); settings.CloseToTray = false; Directory.CreateDirectory(settings.Folder); }
            Text = Words.T("박주임 캡처", "Park Capture") + " · " + Updates.CurrentVersion; Font = Style.Font(9); BackColor = Style.Paper;
            StartPosition = FormStartPosition.CenterScreen; Size = new Size(1400, 900); MinimumSize = new Size(1100, 720); AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;
            Icon = Branding.AppIcon;
            BuildUI();
            var menu = new ContextMenuStrip();
            menu.Items.Add(Words.T("보관함 열기", "Open library"), null, delegate { ShowLibrary(); });
            menu.Items.Add(Words.T("영역 / 창 캡처", "Capture area / window"), null, async delegate { await StartCapture(false); });
            menu.Items.Add(Words.T("영상 녹화", "Record video"), null, async delegate { await StartCapture(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Words.T("설정", "Settings"), null, delegate { ShowSettings(); });
            menu.Items.Add(Words.T("업데이트 확인…", "Check for updates…"), null, async delegate { ShowLibrary(); await CheckForUpdates(true); });
            menu.Items.Add(Words.T("종료", "Quit"), null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = Icon, Text = "박주임 캡처 / Park Capture", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { ShowLibrary(); };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveCurrent(); };
            Shown += delegate
            {
                if (!Native.RegisterHotKey(Handle, 1, settings.Modifiers | 0x4000, (uint)settings.Hotkey)) SetStatus(Words.T("전역 단축키가 다른 프로그램에서 사용 중입니다. 설정에서 바꿔 주세요.", "Global shortcut is in use. Change it in Settings."));
                LoadLibrary(null);
                InitializeUpdates();
                if (Program.Demo) CreateDemo();
                else if (library.Items.Count > 0) { library.Items[0].Selected = true; OpenFile((string)library.Items[0].Tag); }
                if (PreviewPath != null)
                {
                    var previewTimer = new Timer { Interval = 900 };
                    previewTimer.Tick += async delegate
                    {
                        previewTimer.Stop(); previewTimer.Dispose();
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PreviewPath)));
                            string checks = await UiChecks.Run(this, editor, toolButtons, library);
                            SavePreview(PreviewPath);
                            ChooseTool(Tool.Arrow); SavePreview(PreviewPath + ".annotate.png");
                            ChooseTool(Tool.Crop); SavePreview(PreviewPath + ".crop.png");
                            ChooseTool(Tool.Resize); SavePreview(PreviewPath + ".resize.png");
                            ChooseTool(Tool.Select);
                            File.WriteAllText(PreviewPath + ".txt", checks + "\r\nUI rendered: " + Width + "x" + Height + "\r\nEditor: " + editor.ClientSize + "\r\nTools: " + toolButtons.Count + "\r\nLibrary items: " + library.Items.Count);
                            Size = new Size(1100, 720); PerformLayout();
                            SavePreview(PreviewPath + ".compact.png");
                            var previewDoc = doc; string previewTitle = title.Text;
                            doc = null; editor.SetDocument(null); UpdateStatus(); SavePreview(PreviewPath + ".empty.png");
                            doc = previewDoc; editor.SetDocument(doc); title.Text = previewTitle; UpdateStatus();
                        }
                        catch (Exception e) { File.WriteAllText(PreviewPath + ".txt", e.ToString()); }
                        quitting = true; Close();
                    }; previewTimer.Start();
                }
            };
            FormClosing += OnClosing;
        }
        internal static string Quote(string value) { if (value.Contains("\"")) throw new ArgumentException("Invalid path."); return "\"" + value + "\""; }
        private void ShowLibrary() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        private async Task StartCapture(bool forceVideo)
        {
            if (capturing || recorder != null || updateInstalling) return;
            if (!SaveCurrent()) return;
            if (forceVideo && Video.FindFFmpeg(settings) == null) { MessageBox.Show(this, Video.MissingMessage, Text); return; }
            capturing = true;
            try
            {
                Hide(); await Task.Delay(220);
                using (var overlay = new CaptureOverlay(!forceVideo))
                {
                    if (overlay.ShowDialog() != DialogResult.OK) { ShowLibrary(); return; }
                    if (forceVideo || overlay.Record)
                    {
                        if (overlay.Result != null) overlay.Result.Dispose();
                        string ffmpeg = Video.FindFFmpeg(settings);
                        if (ffmpeg == null) { ShowLibrary(); MessageBox.Show(this, Video.MissingMessage, Text); return; }
                        recorder = new RecordingForm(ffmpeg, overlay.SelectedScreen, settings.Folder);
                        recorder.FormClosed += delegate
                        {
                            string path = recorder.OutputPath; recorder = null;
                            ShowLibrary(); LoadLibrary(path); if (path != null && File.Exists(path)) OpenFile(path);
                        };
                        recorder.Show(); return;
                    }
                    using (var image = overlay.Result) AddImage(image, settings.OpenAfterCapture);
                }
            }
            catch (Exception e) { ShowLibrary(); Program.Report(e); }
            finally { capturing = false; }
        }
        private void AddImage(Bitmap image, bool show)
        {
            string path = Storage.NewPath(settings.Folder, ".png"); Storage.SavePng(image, path);
            string clipboardNote = "";
            try { Storage.Copy(image); } catch (Exception e) { clipboardNote = Words.T("클립보드 복사 실패: ", "Clipboard copy failed: ") + e.Message; }
            LoadLibrary(path); OpenFile(path);
            if (show) ShowLibrary(); else { tray.BalloonTipTitle = Words.T("캡처 저장 완료", "Capture saved"); tray.BalloonTipText = Words.T("클립보드에 복사했습니다.", "Copied to clipboard."); tray.ShowBalloonTip(1800); }
            if (clipboardNote.Length > 0) SetStatus(clipboardNote);
        }
        private bool SaveCurrent()
        {
            saveTimer.Stop(); if (doc == null || !doc.Dirty) return true;
            try
            {
                doc.Save();
                string projectDir = Storage.ProjectDir(doc.PathName);
                // Save a small preview without changing the original capture.
                using (var rendered = doc.Render()) using (var thumb = MakeThumb(rendered))
                {
                    Storage.Export(thumb, Path.Combine(projectDir, "preview.png"));
                    foreach (ListViewItem item in library.Items) if ((string)item.Tag == doc.PathName && item.ImageIndex >= 0 && item.ImageIndex < thumbnails.Images.Count) thumbnails.Images[item.ImageIndex] = thumb;
                    library.Invalidate();
                }
                UpdateStatus(); return true;
            }
            catch (Exception e) { Program.Report(e); SetStatus(Words.T("저장 실패 — 현재 편집은 창에 남아 있습니다.", "Save failed — edits are retained in this window.")); return false; }
        }
        private void OpenFile(string path)
        {
            if (doc != null && string.Equals(doc.PathName, path, StringComparison.OrdinalIgnoreCase)) return;
            if (selectedVideo != null && selectedVideo == path) return;
            if (!SaveCurrent()) return;
            try
            {
                if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    editor.SetDocument(null); if (doc != null) doc.Dispose(); doc = null; selectedVideo = path;
                    title.Text = Path.GetFileName(path); SetStatus(Words.T("영상 선택됨 · 내보내기를 눌러 재생 / 구간 편집", "Video selected · Export opens playback and trimming")); return;
                }
                var next = Document.Open(path);
                editor.SetDocument(null); if (doc != null) doc.Dispose(); doc = next; selectedVideo = null;
                editor.SetDocument(doc); title.Text = Path.GetFileName(path); UpdateStatus();
            }
            catch (Exception e) { Program.Report(e); }
        }
        private void LoadLibrary(string select)
        {
            try
            {
                Directory.CreateDirectory(settings.Folder); loading = true; library.BeginUpdate(); library.Items.Clear(); library.Groups.Clear(); thumbnails.Images.Clear();
                string[] groupNames = { Words.T("오늘", "Today"), Words.T("어제", "Yesterday"), Words.T("최근 7일", "Last 7 days"), Words.T("이전", "Earlier") };
                foreach (string group in groupNames) library.Groups.Add(new ListViewGroup(group));
                var files = new DirectoryInfo(settings.Folder).EnumerateFiles().Where(f => IsImage(f.FullName) || f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)).OrderByDescending(f => f.LastWriteTime).ToList();
                int shown = 0;
                foreach (var f in files)
                {
                    bool video = f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
                    if ((filter.SelectedIndex == 1 && video) || (filter.SelectedIndex == 2 && !video)) continue;
                    string query = search == null ? "" : search.Text.Trim();
                    if (query.Length > 0 && f.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 && f.LastWriteTime.ToString("yyyy.MM.dd HH:mm:ss").IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // Thumbnails are bounded; captures remain on disk and are all counted below.
                    if (shown >= 500) break;
                    using (var thumb = LoadThumb(f.FullName, video)) thumbnails.Images.Add(thumb);
                    int days = (DateTime.Today - f.LastWriteTime.Date).Days;
                    var item = new ListViewItem(f.LastWriteTime.ToString("MM.dd  HH:mm:ss") + (video ? "  MP4" : ""), shown) { Tag = f.FullName, ToolTipText = f.Name, Group = library.Groups[days <= 0 ? 0 : days == 1 ? 1 : days < 7 ? 2 : 3] };
                    library.Items.Add(item); if (string.Equals(f.FullName, select, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
                    shown++;
                }
                count.Text = Words.T("보관함", "Library") + "  " + shown;
                if (files.Count > 500) SetStatus(Words.T("최근 500개 표시 · 이전 파일은 저장 폴더에서 열 수 있습니다.", "Showing latest 500 · Older files are available in the folder."));
                if (library.SelectedItems.Count > 0) library.SelectedItems[0].EnsureVisible();
            }
            catch (Exception e) { Program.Report(e); }
            finally { library.EndUpdate(); loading = false; }
        }
        private static bool IsImage(string path) { string ext = Path.GetExtension(path).ToLowerInvariant(); return new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(ext); }
        private Bitmap LoadThumb(string path, bool video)
        {
            try
            {
                string preview = Path.Combine(Storage.ProjectDir(path), "preview.png");
                if (!video) using (var image = Image.FromFile(File.Exists(preview) ? preview : path)) return MakeThumb(image);
            }
            catch { }
            var b = new Bitmap(206, 114); using (var g = Graphics.FromImage(b)) { g.Clear(Style.Paper); using (var f = Style.Font(18, FontStyle.Bold)) using (var brush = new SolidBrush(Style.Blue)) g.DrawString(video ? "▶  MP4" : "IMAGE", f, brush, 35, 36); } return b;
        }
        internal static Bitmap MakeThumb(Image image)
        {
            var b = new Bitmap(206, 114); using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(238, 241, 247)); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float scale = Math.Min(198f / image.Width, 106f / image.Height); int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
                g.DrawImage(image, new Rectangle((206 - w) / 2, (114 - h) / 2, w, h));
            }
            return b;
        }
        private void ImportImage()
        {
            using (var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = Words.T("이미지 열기", "Open image") }) if (dialog.ShowDialog(this) == DialogResult.OK) ImportPath(dialog.FileName);
        }
        private void ImportPath(string path)
        {
            if (!IsImage(path) || !SaveCurrent()) return;
            try { using (var image = Image.FromFile(path)) using (var bitmap = new Bitmap(image)) AddImage(bitmap, true); }
            catch (Exception e) { Program.Report(e); }
        }
        private void CopyImage()
        {
            if (doc == null) return;
            try { using (var image = doc.Render()) Storage.Copy(image); SetStatus(Words.T("현재 이미지 크기로 클립보드에 복사했습니다.", "Copied at current image dimensions.")); }
            catch (Exception e) { Program.Report(e); }
        }
        private void ExportImage()
        {
            if (doc == null) return;
            using (var dialog = new SaveFileDialog { Filter = "PNG image|*.png|JPEG image|*.jpg", FileName = Path.GetFileNameWithoutExtension(doc.PathName) + "_edited.png", AddExtension = true, DefaultExt = "png", OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(doc.PathName), StringComparison.OrdinalIgnoreCase)) { MessageBox.Show(this, Words.T("원본 보존을 위해 다른 파일 이름을 선택해 주세요.", "Choose another filename to preserve the original."), Text); return; }
                try { using (var image = doc.Render()) Storage.Export(image, dialog.FileName); SetStatus(Words.T("내보내기 완료: ", "Exported: ") + dialog.FileName); }
                catch (Exception e) { Program.Report(e); }
            }
        }
        private void EditVideo()
        {
            if (selectedVideo == null) return;
            string ffmpeg = Video.FindFFmpeg(settings); if (ffmpeg == null) { MessageBox.Show(this, Video.MissingMessage, Text); return; }
            using (var dialog = new VideoDialog(ffmpeg, selectedVideo)) dialog.ShowDialog(this);
            LoadLibrary(selectedVideo);
        }
        private void ShowFile() { string path = doc == null ? selectedVideo : doc.PathName; if (path != null) Process.Start("explorer.exe", "/select," + Quote(path)); }
        private void RecycleSelected()
        {
            string path = doc == null ? selectedVideo : doc.PathName; if (path == null) return;
            if (MessageBox.Show(this, Words.T("선택한 파일과 편집 내용을 휴지통으로 옮길까요?", "Move the selected file and edits to the Recycle Bin?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!SaveCurrent()) return;
            try
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                string folder = Storage.ProjectDir(path); if (Directory.Exists(folder)) FileSystem.DeleteDirectory(folder, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                editor.SetDocument(null); if (doc != null) doc.Dispose(); doc = null; selectedVideo = null; title.Text = ""; LoadLibrary(null); UpdateStatus();
            }
            catch (Exception e) { Program.Report(e); }
        }
        private void ShowSettings()
        {
            if (recorder != null || !SaveCurrent()) return;
            using (var form = new SettingsDialog(settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var next = form.Result;
                Native.UnregisterHotKey(Handle, 1);
                if (!Native.RegisterHotKey(Handle, 1, next.Modifiers | 0x4000, (uint)next.Hotkey))
                { Native.RegisterHotKey(Handle, 1, settings.Modifiers | 0x4000, (uint)settings.Hotkey); MessageBox.Show(this, Words.T("해당 단축키를 사용할 수 없습니다. 다른 키를 선택해 주세요.", "That shortcut is unavailable. Choose another key."), Text); return; }
                bool languageChange = settings.Language != next.Language;
                settings = next; settings.Save();
                editor.SetDocument(null); if (doc != null) doc.Dispose(); doc = null; selectedVideo = null;
                LoadLibrary(null); UpdateStatus();
                if (languageChange) MessageBox.Show(this, Words.T("언어 설정은 프로그램을 완전히 종료한 후 다시 실행하면 적용됩니다.", "Quit the app from its tray menu and reopen to apply the language."), Text);
            }
        }
        internal static string HotkeyText(Settings value)
        { return ((value.Modifiers & 2) != 0 ? "Ctrl+" : "") + ((value.Modifiers & 4) != 0 ? "Shift+" : "") + ((value.Modifiers & 1) != 0 ? "Alt+" : "") + ShortcutKeys.Label((Keys)value.Hotkey, false); }
        protected override void WndProc(ref Message m)
        { if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { BeginInvoke(new Action(async delegate { await StartCapture(false); })); } base.WndProc(ref m); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (keyData == (Keys.Control | Keys.Z)) { editor.Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { editor.Redo(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { CopyImage(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveCurrent(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { ImportImage(); return true; }
            if (keyData == (Keys.Control | Keys.V))
            {
                try { if (Clipboard.ContainsImage() && SaveCurrent()) using (var image = Clipboard.GetImage()) using (var bitmap = new Bitmap(image)) AddImage(bitmap, true); } catch (Exception e) { Program.Report(e); } return true;
            }
            if (keyData == (Keys.Control | Keys.D0)) { editor.Fit(); return true; }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add)) { editor.ZoomBy(1.2f); return true; }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract)) { editor.ZoomBy(1 / 1.2f); return true; }
            if (key == Keys.Enter && editor.Tool == Tool.Resize && (imageWidthInput.Focused || imageHeightInput.Focused)) { ApplyEditorAction(); return true; }
            if (editor.Focused)
            {
                if (key == Keys.Escape && editor.CancelFrameResize()) return true;
                if (key == Keys.Escape && editor.ClearMarkSelection()) return true;
                if (key == Keys.Enter && editor.Tool == Tool.Resize) { ApplyEditorAction(); return true; }
                if (key == Keys.Enter && editor.ApplySelection()) return true;
                if ((key == Keys.F2 || key == Keys.Enter) && editor.EditSelectedText()) return true;
                if (keyData == Keys.Delete || keyData == Keys.Back) { editor.DeleteSelected(); return true; }
                if (editor.MoveSelection(key, (keyData & Keys.Shift) != 0 ? 10 : 1)) return true;
                if ((keyData & (Keys.Control | Keys.Alt)) == 0)
                {
                    var map = new Dictionary<Keys, Tool> { { Keys.V, Tool.Select }, { Keys.C, Tool.Crop }, { Keys.S, Tool.Resize }, { Keys.N, Tool.Number }, { Keys.T, Tool.Text }, { Keys.X, Tool.TextBox }, { Keys.B, Tool.Bubble }, { Keys.A, Tool.Arrow }, { Keys.R, Tool.Rectangle }, { Keys.O, Tool.Ellipse }, { Keys.M, Tool.Mosaic } };
                    if (map.ContainsKey(key)) { ChooseTool(map[key]); return true; }
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown) { SaveCurrent(); return; }
            if (!SaveCurrent()) { e.Cancel = true; return; }
            if (settings.CloseToTray && !quitting) { e.Cancel = true; Hide(); tray.BalloonTipTitle = Text; tray.BalloonTipText = Words.T("알림 영역에서 실행 중입니다. 종료는 아이콘 메뉴를 사용하세요.", "Running in the tray. Use its menu to quit."); tray.ShowBalloonTip(2000); }
            else if (recorder != null) { e.Cancel = true; MessageBox.Show(this, Words.T("녹화를 먼저 마쳐 주세요.", "Finish recording before quitting."), Text); quitting = false; }
        }
        private void Quit() { quitting = true; Close(); }
        private void CreateDemo()
        {
            using (var image = new Bitmap(1000, 620))
            {
                using (var g = Graphics.FromImage(image))
                {
                    g.Clear(Color.White);
                    using (var f = new Font("Segoe UI", 11, FontStyle.Bold)) using (var b = new SolidBrush(Style.Blue)) g.DrawString("QUICK GUIDE", f, b, 44, 30);
                    using (var f = Style.Font(27, FontStyle.Bold)) using (var b = new SolidBrush(Style.Ink)) g.DrawString("알아보기 쉬운 업무 안내", f, b, 42, 70);
                    using (var f = Style.Font(13)) using (var b = new SolidBrush(Style.Muted)) g.DrawString("필요한 곳에 번호와 화살표를 더해 전달하세요.", f, b, 46, 134);
                    using (var p = new Pen(Style.Line)) g.DrawLine(p, 46, 193, 954, 193);
                    for (int i = 0; i < 4; i++)
                    {
                        using (var b = new SolidBrush(i == 0 ? Style.BlueWash : Color.FromArgb(248, 248, 250))) using (var cardPath = Glyphs.Round(new RectangleF(42, 230 + i * 76, 916, 58), 10)) g.FillPath(b, cardPath);
                        using (var f = Style.Font(13)) using (var b = new SolidBrush(Style.Ink)) g.DrawString(new[] { "공유할 화면을 캡처하세요", "필요한 설명을 간단히 표시하세요", "가릴 정보는 모자이크로 처리하세요", "복사해서 문서에 붙여 넣으세요" }[i], f, b, 66, 245 + i * 76);
                    }
                }
                string path = Path.Combine(settings.Folder, "업무 안내 예시.png"); if (!File.Exists(path)) Storage.SavePng(image, path);
                LoadLibrary(path); OpenFile(path);
                if (doc.Marks.Count == 0) { doc.Checkpoint(); doc.Marks.Add(new Mark { Kind = Tool.Number, X1 = 895, Y1 = 259, Number = 1 }); doc.Marks.Add(new Mark { Kind = Tool.Arrow, X1 = 790, Y1 = 180, X2 = 875, Y2 = 235, Width = 4 }); SaveCurrent(); }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Native.UnregisterHotKey(Handle, 1); saveTimer.Dispose(); updateTimer.Dispose(); tips.Dispose(); tray.Visible = false; tray.Dispose(); thumbnails.Dispose(); if (doc != null) doc.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class SettingsDialog : Form
    {
        internal Settings Result;
        internal SettingsDialog(Settings current)
        {
            Text = Words.T("설정", "Settings"); ClientSize = new Size(600, 450); Font = Style.Font(10); BackColor = Style.Sidebar; ForeColor = Style.Ink; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var folder = new TextBox { Text = current.Folder, Bounds = new Rectangle(22, 48, 452, 28), AccessibleName = "저장 폴더 / Save folder" };
            var browse = Style.Button(Words.T("찾기", "Browse"), delegate { using (var d = new FolderBrowserDialog { SelectedPath = folder.Text }) if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath; }); browse.Location = new Point(486, 44);
            var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(22, 118, 140, 30), AccessibleName = "언어 / Language" }; language.Items.AddRange(new object[] { "한국어", "English" }); language.SelectedIndex = current.Language == "en" ? 1 : 0;
            var ctrl = new CheckBox { Text = "Ctrl", Checked = (current.Modifiers & 2) != 0, Bounds = new Rectangle(190, 118, 65, 30) };
            var shift = new CheckBox { Text = "Shift", Checked = (current.Modifiers & 4) != 0, Bounds = new Rectangle(260, 118, 65, 30) };
            var alt = new CheckBox { Text = "Alt", Checked = (current.Modifiers & 1) != 0, Bounds = new Rectangle(330, 118, 60, 30) };
            var key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(400, 118, 170, 30), DropDownWidth = 220, AccessibleName = "단축키 / Shortcut key" };
            foreach (var choice in ShortcutKeys.Choices()) { key.Items.Add(choice); if ((int)choice.Key == current.Hotkey) key.SelectedItem = choice; } if (key.SelectedIndex < 0) key.SelectedIndex = 2;
            var keyHint = new Label { Text = Words.T("숫자: F키 아래 줄  ·  키패드: 오른쪽 숫자판 (Num Lock 켜짐)", "Top row: below F keys · Numpad: right side (Num Lock on)"), Bounds = new Rectangle(190, 151, 390, 18), ForeColor = Style.Muted, Font = Style.Font(8) };
            var tray = new CheckBox { Text = Words.T("창을 닫으면 알림 영역에서 계속 실행", "Keep running in tray when window closes"), Checked = current.CloseToTray, Bounds = new Rectangle(22, 170, 550, 28) };
            var open = new CheckBox { Text = Words.T("캡처 후 편집기 열기", "Open editor after capture"), Checked = current.OpenAfterCapture, Bounds = new Rectangle(22, 206, 550, 28) };
            var ffmpeg = new TextBox { Text = current.FFmpegPath, Bounds = new Rectangle(22, 278, 452, 28), AccessibleName = "FFmpeg path" };
            var ffBrowse = Style.Button(Words.T("찾기", "Browse"), delegate { using (var d = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe" }) if (d.ShowDialog(this) == DialogResult.OK) ffmpeg.Text = d.FileName; }); ffBrowse.Location = new Point(486, 274);
            var note = new Label { Text = Words.T("비워 두면 tools 폴더와 PATH에서 자동으로 찾습니다. 언어는 재실행 시 적용됩니다.", "Blank: search tools folder and PATH. Language applies after restart."), Bounds = new Rectangle(22, 314, 555, 40), ForeColor = Color.DimGray, Font = Style.Font(9) };
            var automatic = new CheckBox { Text = Words.T("새 버전 자동 확인 (시작 시 · 이후 6시간마다)", "Check for updates automatically (startup and every 6 hours)"), Checked = current.AutoUpdate, Bounds = new Rectangle(22, 359, 555, 28) };
            var save = Style.Button(Words.T("저장", "Save"), delegate
            {
                try
                {
                    if (!ctrl.Checked && !shift.Checked && !alt.Checked) throw new ArgumentException(Words.T("단축키에 Ctrl, Shift, Alt 중 하나를 포함해 주세요.", "Include Ctrl, Shift or Alt in the shortcut."));
                    if (string.IsNullOrWhiteSpace(folder.Text)) throw new ArgumentException(Words.T("저장 폴더를 선택해 주세요.", "Choose a save folder."));
                    string full = Path.GetFullPath(folder.Text.Trim()); Directory.CreateDirectory(full);
                    string probe = Path.Combine(full, ".write-test-" + Guid.NewGuid().ToString("N")); File.WriteAllText(probe, ""); File.Delete(probe);
                    if (!string.IsNullOrWhiteSpace(ffmpeg.Text) && (!File.Exists(ffmpeg.Text) || !Path.GetFileName(ffmpeg.Text).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("ffmpeg.exe " + Words.T("경로를 확인해 주세요.", "path is invalid."));
                    Result = new Settings { Folder = full, Language = language.SelectedIndex == 1 ? "en" : "ko", Hotkey = (int)((ShortcutKeyChoice)key.SelectedItem).Key, Modifiers = (uint)((ctrl.Checked ? 2 : 0) | (shift.Checked ? 4 : 0) | (alt.Checked ? 1 : 0)), CloseToTray = tray.Checked, OpenAfterCapture = open.Checked, AutoUpdate = automatic.Checked, FFmpegPath = ffmpeg.Text.Trim() }; DialogResult = DialogResult.OK;
                }
                catch (Exception e) { MessageBox.Show(this, e.Message, Text); }
            }, true); save.Location = new Point(390, 403);
            var cancel = Style.Button(Words.T("취소", "Cancel"), delegate { DialogResult = DialogResult.Cancel; }); cancel.Location = new Point(484, 403); CancelButton = cancel;
            Controls.AddRange(new Control[] { Label(Words.T("저장 폴더", "Save folder"), 22, 18), folder, browse, Label(Words.T("언어", "Language"), 22, 88), Label(Words.T("캡처 전역 단축키", "Global capture shortcut"), 190, 88), language, ctrl, shift, alt, key, keyHint, tray, open, Label(Words.T("영상 기능용 FFmpeg (선택)", "FFmpeg for video (optional)"), 22, 248), ffmpeg, ffBrowse, note, save, cancel });
            Controls.Add(automatic);
        }
        private static Label Label(string text, int x, int y) { return new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Style.Ink }; }
    }
}
