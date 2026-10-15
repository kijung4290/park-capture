using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed class VideoProcess : IDisposable
    {
        internal readonly Process Process;
        private readonly StringBuilder errors = new StringBuilder();
        internal VideoProcess(string executable, string arguments, bool input = false)
        {
            Process = new Process { StartInfo = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = input, RedirectStandardOutput = true, StandardErrorEncoding = Encoding.UTF8 } };
            Process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data == null) return; lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 32000) errors.Remove(0, errors.Length - 24000); } };
            Process.OutputDataReceived += delegate { };
            Process.Start(); Process.BeginErrorReadLine(); Process.BeginOutputReadLine();
        }
        internal string Log { get { lock (errors) return errors.ToString(); } }
        internal async Task<int> Wait(CancellationToken token)
        {
            using (token.Register(delegate { try { if (!Process.HasExited) Process.Kill(); } catch { } }))
            {
                await Task.Run(delegate { Process.WaitForExit(); }); token.ThrowIfCancellationRequested(); return Process.ExitCode;
            }
        }
        internal async Task StopRecording()
        {
            if (!Process.HasExited) { try { Process.StandardInput.WriteLine("q"); Process.StandardInput.Flush(); } catch (IOException) { } }
            Task wait = Task.Run(delegate { Process.WaitForExit(); });
            if (await Task.WhenAny(wait, Task.Delay(12000)) != wait) { try { Process.Kill(); } catch { } await wait; throw new IOException(Words.T("녹화 종료 시간이 초과되었습니다. 임시 녹화 파일을 확인해 주세요.", "Recording shutdown timed out. Check the temporary segments.")); }
            await wait;
            if (Process.ExitCode != 0) throw new IOException(Log);
        }
        public void Dispose() { try { if (!Process.HasExited) Process.Kill(); } catch { } Process.Dispose(); }
    }

    internal static class Video
    {
        internal static string MissingMessage { get { return Words.T("영상 기능에는 FFmpeg가 필요합니다. ffmpeg.exe를 프로젝트의 tools 폴더에 넣거나, 설정에서 이미 설치된 ffmpeg.exe를 지정해 주세요. 이미지 캡처·편집은 FFmpeg 없이 사용할 수 있습니다.", "Video requires FFmpeg. Place ffmpeg.exe in the project's tools folder or choose an installed ffmpeg.exe in Settings. Image capture and editing work without FFmpeg."); } }
        internal static string FindFFmpeg(Settings settings)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(settings.FFmpegPath)) candidates.Add(settings.FFmpegPath);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "tools", "ffmpeg.exe")); candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "tools", "ffmpeg.exe")));
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) if (!string.IsNullOrWhiteSpace(dir)) try { candidates.Add(Path.Combine(dir.Trim('"'), "ffmpeg.exe")); } catch { }
            return candidates.FirstOrDefault(File.Exists);
        }
        internal static string N(double number) { return number.ToString("0.######", CultureInfo.InvariantCulture); }
        internal static VideoProcess StartRecording(string ffmpeg, Rectangle region, string part)
        {
            string arguments = "-hide_banner -loglevel warning -y -f gdigrab -framerate 30 -draw_mouse 1 -offset_x " + region.X + " -offset_y " + region.Y + " -video_size " + region.Width + "x" + region.Height + " -i desktop -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" -an -c:v libx264 -preset ultrafast -crf 20 -pix_fmt yuv420p " + MainForm.Quote(part);
            return new VideoProcess(ffmpeg, arguments, true);
        }
        internal static async Task Run(string ffmpeg, string arguments, CancellationToken token)
        {
            using (var p = new VideoProcess(ffmpeg, "-hide_banner -loglevel warning " + arguments))
            { int code = await p.Wait(token); if (code != 0) throw new IOException(Words.T("영상 처리 실패:\n", "Video processing failed:\n") + p.Log); }
        }
        internal static async Task<double> Duration(string ffmpeg, string path)
        {
            using (var p = new VideoProcess(ffmpeg, "-hide_banner -i " + MainForm.Quote(path)))
            {
                await p.Wait(CancellationToken.None);
                var match = Regex.Match(p.Log, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
                if (!match.Success) throw new IOException(Words.T("영상 길이를 읽을 수 없습니다.", "Could not read video duration."));
                return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            }
        }
        internal static async Task Export(string ffmpeg, string input, string output, double start, double end, double speed, bool gif, int fps, CancellationToken token)
        {
            if (start < 0 || end <= start || speed <= 0) throw new ArgumentException("Invalid video range.");
            if (Path.GetFullPath(input).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(Words.T("원본과 다른 파일 이름을 선택해 주세요.", "Choose a different output filename."));
            string temp = Path.Combine(Path.GetDirectoryName(output), ".parkcapture-" + Guid.NewGuid().ToString("N") + (gif ? ".gif" : ".mp4"));
            string args = "-y -ss " + N(start) + " -i " + MainForm.Quote(input) + " -t " + N((end - start) / speed) + " ";
            if (gif) args += "-filter_complex " + MainForm.Quote("[0:v]setpts=(PTS-STARTPTS)/" + N(speed) + ",fps=" + fps + ",split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a") + " -loop 0 ";
            else args += "-vf " + MainForm.Quote("setpts=(PTS-STARTPTS)/" + N(speed) + ",pad=ceil(iw/2)*2:ceil(ih/2)*2") + " -an -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart ";
            try
            {
                await Run(ffmpeg, args + MainForm.Quote(temp), token); token.ThrowIfCancellationRequested();
                if (File.Exists(output)) File.Replace(temp, output, null); else File.Move(temp, output);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    internal sealed class RecordingForm : Form
    {
        private readonly string ffmpeg, folder, work;
        private readonly Rectangle region;
        private readonly List<string> parts = new List<string>();
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
        private readonly Label label;
        private readonly Button pause, stop;
        private VideoProcess process;
        private string part;
        private bool paused, busy, finished;
        internal string OutputPath;
        internal RecordingForm(string executable, Rectangle area, string outputFolder)
        {
            ffmpeg = executable; region = area; folder = outputFolder;
            work = Path.Combine(Program.DataDir, "recordings", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            Text = Words.T("박주임 캡처 · 녹화", "Park Capture · Recording"); ClientSize = new Size(448, 82); FormBorderStyle = FormBorderStyle.FixedToolWindow; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Style.Ink;
            var screen = Screen.FromRectangle(area).WorkingArea; Location = new Point(Math.Max(screen.Left, screen.Right - Width - 24), screen.Top + 24);
            label = new Label { ForeColor = Color.White, Bounds = new Rectangle(14, 14, 216, 56), Font = Style.Font(10), Text = Words.T("녹화 준비 중…", "Preparing…") };
            pause = Style.Button(Words.T("일시정지", "Pause"), async delegate { await TogglePause(); }); pause.Location = new Point(236, 22); pause.Enabled = false;
            stop = Style.Button(Words.T("녹화 마침", "Finish"), async delegate { await Finish(); }, true); stop.Location = new Point(336, 22); stop.Enabled = false;
            Controls.AddRange(new Control[] { label, pause, stop });
            Shown += async delegate
            {
                try { Native.SetWindowDisplayAffinity(Handle, 0x11); await StartPart(); pause.Enabled = true; stop.Enabled = true; timer.Start(); }
                catch (Exception e) { Fail(e); }
            };
            timer.Tick += delegate
            {
                label.Text = (paused ? "Ⅱ " + Words.T("일시정지", "Paused") : "● " + Words.T("녹화 중", "Recording")) + "  " + elapsed.Elapsed.ToString(@"mm\:ss") + "\n" + region.Width + " × " + region.Height + " px · 30 fps";
                if (!busy && !paused && process != null && process.Process.HasExited) Fail(new IOException(process.Log));
            };
            FormClosing += async delegate(object sender, FormClosingEventArgs e) { if (!finished) { e.Cancel = true; if (!busy) await Finish(); } };
        }
        private async Task StartPart()
        {
            part = Path.Combine(work, "part-" + parts.Count.ToString("D4") + ".mp4");
            process = Video.StartRecording(ffmpeg, region, part); elapsed.Start(); await Task.Delay(650);
            if (process.Process.HasExited) throw new IOException(process.Log);
            paused = false;
        }
        private async Task EndPart()
        {
            elapsed.Stop(); if (process == null) return;
            try { await process.StopRecording(); if (File.Exists(part) && new FileInfo(part).Length > 0) parts.Add(part); }
            finally { process.Dispose(); process = null; }
        }
        private async Task TogglePause()
        {
            if (busy || finished) return; busy = true; pause.Enabled = false; stop.Enabled = false;
            try
            {
                if (paused) { await StartPart(); pause.Text = Words.T("일시정지", "Pause"); }
                else { await EndPart(); paused = true; pause.Text = Words.T("다시 시작", "Resume"); }
            }
            catch (Exception e) { Fail(e); }
            finally { busy = false; if (!finished) { pause.Enabled = true; stop.Enabled = true; } }
        }
        private async Task Finish()
        {
            if (busy || finished) return; busy = true; timer.Stop(); pause.Enabled = false; stop.Enabled = false;
            label.Text = Words.T("녹화 파일 저장 중…", "Saving recording…");
            string partial = null;
            try
            {
                await EndPart(); if (parts.Count == 0) throw new IOException(Words.T("저장할 녹화 구간이 없습니다.", "No recording segments were produced."));
                string output = Storage.NewPath(folder, ".mp4");
                partial = output + ".partial";
                if (parts.Count == 1) File.Copy(parts[0], partial, false);
                else
                {
                    string list = Path.Combine(work, "segments.txt");
                    File.WriteAllLines(list, parts.Select(p => "file '" + Path.GetFileName(p) + "'"), new UTF8Encoding(false));
                    await Video.Run(ffmpeg, "-y -f concat -safe 1 -i " + MainForm.Quote(list) + " -c copy -movflags +faststart -f mp4 " + MainForm.Quote(partial), CancellationToken.None);
                }
                File.Move(partial, output); partial = null;
                OutputPath = output; finished = true;
                try { foreach (string p in Directory.GetFiles(work)) File.Delete(p); Directory.Delete(work); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                Close();
            }
            catch (Exception e) { Fail(e); }
            finally { busy = false; if (partial != null && File.Exists(partial)) try { File.Delete(partial); } catch { } }
        }
        private void Fail(Exception e)
        {
            timer.Stop(); elapsed.Stop(); if (process != null) { process.Dispose(); process = null; }
            MessageBox.Show(this, e.Message + "\n\n" + Words.T("임시 구간 보관 위치: ", "Temporary segments: ") + work, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            finished = true; Close();
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); if (process != null) process.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class VideoDialog : Form
    {
        private readonly string ffmpeg, path;
        private readonly NumericUpDown start, end;
        private readonly ComboBox speed, format, fps;
        private readonly Button export, cancel;
        private readonly Label state;
        private readonly ProgressBar progress;
        private CancellationTokenSource cancellation;
        private bool running;
        internal VideoDialog(string executable, string videoPath)
        {
            ffmpeg = executable; path = videoPath;
            Text = Words.T("영상 재생 / 구간 내보내기", "Video playback / export"); Font = Style.Font(10); BackColor = Style.Sidebar; ForeColor = Style.Ink; ClientSize = new Size(560, 390); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var file = new Label { Text = Path.GetFileName(path), AutoEllipsis = true, Bounds = new Rectangle(22, 18, 510, 28), ForeColor = Style.Ink };
            var play = Style.Button(Words.T("▶ 기본 플레이어로 재생", "▶ Play in default player"), delegate { try { Process.Start(path); } catch (Exception e) { Program.Report(e); } }); play.Location = new Point(22, 54);
            start = new NumericUpDown { DecimalPlaces = 2, Increment = .1m, Maximum = 604800, Bounds = new Rectangle(22, 140, 130, 28), AccessibleName = "시작 초 / Start seconds" };
            end = new NumericUpDown { DecimalPlaces = 2, Increment = .1m, Maximum = 604800, Bounds = new Rectangle(174, 140, 130, 28), AccessibleName = "끝 초 / End seconds" };
            speed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(328, 140, 100, 28), AccessibleName = "재생 속도 / Speed" }; speed.Items.AddRange(new object[] { "0.5", "1", "1.5", "2", "4" }); speed.SelectedIndex = 1;
            format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(22, 214, 130, 28), AccessibleName = "출력 형식 / Output format" }; format.Items.AddRange(new object[] { "MP4", "GIF" }); format.SelectedIndex = 0;
            fps = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(174, 214, 130, 28), AccessibleName = "GIF FPS" }; fps.Items.AddRange(new object[] { "15", "30", "45" }); fps.SelectedIndex = 1;
            state = new Label { Bounds = new Rectangle(22, 260, 510, 42), ForeColor = Color.DimGray, Text = Words.T("영상 길이를 확인하고 있습니다…", "Reading video duration…") };
            progress = new ProgressBar { Bounds = new Rectangle(22, 310, 510, 5), Visible = false, Style = ProgressBarStyle.Marquee };
            export = Style.Button(Words.T("내보내기", "Export"), async delegate { await Export(); }, true); export.Location = new Point(320, 336); export.Enabled = false;
            cancel = Style.Button(Words.T("닫기", "Close"), delegate { if (running) cancellation.Cancel(); else Close(); }); cancel.Location = new Point(440, 336);
            Controls.AddRange(new Control[] { file, play, L(Words.T("시작 (초)", "Start (seconds)"), 22, 112), start, L(Words.T("끝 (초)", "End (seconds)"), 174, 112), end, L(Words.T("배속", "Speed"), 328, 112), speed, L(Words.T("형식", "Format"), 22, 186), format, L("GIF fps", 174, 186), fps, state, progress, export, cancel });
            Shown += async delegate { try { double duration = await Video.Duration(ffmpeg, path); if (IsDisposed) return; end.Maximum = start.Maximum = (decimal)Math.Min(604800, duration); end.Value = end.Maximum; export.Enabled = true; state.Text = Words.T("원본 해상도로 저장합니다. 오디오는 포함하지 않습니다.", "Exports at original resolution, without audio."); } catch (Exception e) { if (!IsDisposed) state.Text = e.Message; } };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; cancellation.Cancel(); } };
        }
        private static Label L(string text, int x, int y) { return new Label { Text = text, Location = new Point(x, y), AutoSize = true }; }
        private async Task Export()
        {
            if (end.Value <= start.Value) { state.Text = Words.T("끝은 시작보다 뒤여야 합니다.", "End must be after start."); return; }
            bool gif = format.SelectedIndex == 1;
            using (var save = new SaveFileDialog { Filter = gif ? "GIF|*.gif" : "MP4|*.mp4", DefaultExt = gif ? "gif" : "mp4", FileName = Path.GetFileNameWithoutExtension(path) + "_edited" + (gif ? ".gif" : ".mp4"), OverwritePrompt = true })
            {
                if (save.ShowDialog(this) != DialogResult.OK) return;
                cancellation = new CancellationTokenSource(); running = true; export.Enabled = false; progress.Visible = true; cancel.Text = Words.T("취소", "Cancel"); state.Text = Words.T("내보내는 중…", "Exporting…");
                try { await Video.Export(ffmpeg, path, save.FileName, (double)start.Value, (double)end.Value, double.Parse(speed.Text, CultureInfo.InvariantCulture), gif, int.Parse(fps.Text), cancellation.Token); state.Text = Words.T("저장 완료: ", "Saved: ") + save.FileName; }
                catch (OperationCanceledException) { state.Text = Words.T("내보내기를 취소했습니다.", "Export canceled."); }
                catch (Exception e) { state.Text = e.Message; }
                finally { running = false; cancellation.Dispose(); cancellation = null; export.Enabled = true; progress.Visible = false; cancel.Text = Words.T("닫기", "Close"); }
            }
        }
    }
}
