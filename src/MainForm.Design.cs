using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ParkCapture
{
    internal sealed partial class MainForm
    {
        private Label detail, toolHint, zoomLabel, widthLabel, fontLabel;
        private SoftButton copyButton, exportButton, applyButton, deleteMarkButton, undoButton, redoButton, moreTools, emptyCapture, emptyImport, customColor;
        private FlowLayoutPanel styleOptions, emptyActions;
        private NumericUpDown strokeInput, fontInput;
        private NumericUpDown imageWidthInput, imageHeightInput;
        private FlowLayoutPanel imageSizeOptions;
        private CheckBox keepAspect;
        private Document sizeInputDocument;
        private Size sizeInputSource;
        private bool syncingSize;
        private TextBox search;
        private readonly List<SoftButton> filterButtons = new List<SoftButton>(), swatches = new List<SoftButton>();
        private bool updatingStyle;

        private SoftButton ActionButton(string text, string glyph, EventHandler action, ButtonTone tone = ButtonTone.Normal)
        {
            var button = new SoftButton { Text = text, Glyph = glyph, Tone = tone, AccessibleName = text.Length > 0 ? text : glyph };
            button.Click += action; return button;
        }
        private static TableLayoutPanel Grid(int rows)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Margin = Padding.Empty, Padding = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table;
        }
        private static Label Caption(string text, float size = 9, bool bold = false)
        { return new Label { Text = text, Font = Style.Font(size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = Style.Ink, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty, Dock = DockStyle.Fill }; }
        private void BuildUI()
        {
            var root = Grid(3); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); Controls.Add(root);
            root.Controls.Add(BuildBrandFooter(), 0, 2);
            var header = new Surface { Dock = DockStyle.Fill, BackColor = Color.White, BottomLine = true, Margin = Padding.Empty, Padding = new Padding(18, 10, 18, 10) };
            var headerGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 248)); headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var brand = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var mark = new BrandMark { Location = new Point(0, 10) }; brand.Controls.Add(mark);
            var name = Caption(Words.T("박주임 캡처", "Park Capture"), 14, true); name.Dock = DockStyle.None; name.Bounds = new Rectangle(46, 6, 192, 42); brand.Controls.Add(name); headerGrid.Controls.Add(brand, 0, 0);
            var documentHeading = Grid(2); documentHeading.Padding = new Padding(10, 4, 8, 4); documentHeading.RowStyles.Add(new RowStyle(SizeType.Percent, 54)); documentHeading.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            title = Caption(Words.T("캡처를 시작해 보세요", "Ready when you are"), 10, true);
            detail = Caption(Words.T("화면을 담고, 필요한 설명만 더하세요.", "Capture a screen, then make it clear."), 8.5f); detail.ForeColor = Style.Muted;
            documentHeading.Controls.Add(title, 0, 0); documentHeading.Controls.Add(detail, 0, 1); headerGrid.Controls.Add(documentHeading, 1, 0);
            var headerActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty };
            copyButton = ActionButton(Words.T("복사", "Copy"), "copy", delegate { CopyImage(); }); tips.SetToolTip(copyButton, Words.T("이미지를 클립보드에 복사 · Ctrl+C", "Copy image to clipboard · Ctrl+C"));
            exportButton = ActionButton(Words.T("이미지 저장", "Save image"), "save", delegate { if (selectedVideo != null) EditVideo(); else ExportImage(); }, ButtonTone.Primary);
            tips.SetToolTip(exportButton, Words.T("편집한 이미지를 PNG 또는 JPG로 저장", "Save edited image as PNG or JPEG"));
            var more = ActionButton("", "more", delegate { }, ButtonTone.Quiet); tips.SetToolTip(more, Words.T("파일과 편집 메뉴", "File and edit menu")); more.AccessibleName = Words.T("파일 메뉴", "File menu");
            var fileMenu = new ContextMenuStrip();
            fileMenu.Items.Add(Words.T("이미지 가져오기…", "Import image…"), null, delegate { ImportImage(); });
            fileMenu.Items.Add(Words.T("클립보드에서 가져오기  Ctrl+V", "Paste image  Ctrl+V"), null, delegate { PasteImage(); });
            fileMenu.Items.Add(Words.T("선택한 파일 위치 열기", "Show selected file in folder"), null, delegate { ShowFile(); });
            fileMenu.Items.Add(new ToolStripSeparator());
            fileMenu.Items.Add(Words.T("휴지통으로 이동…", "Move to Recycle Bin…"), null, delegate { RecycleSelected(); });
            fileMenu.Items.Add(Words.T("단축키 안내", "Keyboard shortcuts"), null, delegate { ShowShortcuts(); });
            fileMenu.Items.Add(Words.T("업데이트 확인…", "Check for updates…"), null, async delegate { await CheckForUpdates(true); });
            more.Click += delegate { fileMenu.Show(more, new Point(0, more.Height)); };
            headerActions.Controls.AddRange(new Control[] { copyButton, exportButton, more }); headerGrid.Controls.Add(headerActions, 2, 0); header.Controls.Add(headerGrid); root.Controls.Add(header, 0, 0);

            var split = new SplitContainer { Size = new Size(1300, 700), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 266, Panel1MinSize = 250, SplitterWidth = 1, IsSplitterFixed = true, Margin = Padding.Empty, BackColor = Style.Line };
            root.Controls.Add(split, 0, 1); split.Panel1.BackColor = Style.Sidebar; split.Panel2.BackColor = Style.Paper;
            BuildSidebar(split.Panel1);
            var workspace = Grid(4); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 94)); workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); split.Panel2.Controls.Add(workspace);
            var toolSurface = new Surface { Dock = DockStyle.Fill, BackColor = Color.White, BottomLine = true, Margin = Padding.Empty };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 5, 10, 4), Margin = Padding.Empty, WrapContents = true };
            bool sizingTools = false;
            tools.Layout += delegate
            {
                if (sizingTools || !tools.Visible) return;
                int bottom = tools.Padding.Top;
                foreach (Control child in tools.Controls) if (child.Visible) bottom = Math.Max(bottom, child.Bottom + child.Margin.Bottom);
                int height = Math.Max(74, bottom + tools.Padding.Bottom + 1);
                if (Math.Abs(workspace.RowStyles[0].Height - height) < 1) return;
                sizingTools = true; workspace.RowStyles[0].Height = height; sizingTools = false;
            };
            Shown += delegate { tools.PerformLayout(); };
            AddTool(tools, Tool.Select, "선택", "Select", "select", "V", "요소를 클릭해 선택한 뒤 이동하거나 삭제합니다.", "Click an element to select, move or delete it.");
            AddTool(tools, Tool.Crop, "자르기", "Crop", "crop", "C", "남길 영역을 선택하고 적용을 누릅니다.", "Choose the area to keep, then apply.");
            AddTool(tools, Tool.Resize, "크기 조절", "Resize", "resize", "S", "이미지 프레임을 드래그하거나 가로·세로 픽셀을 입력합니다.", "Drag the image frame or enter its pixel dimensions.");
            Separator(tools);
            AddTool(tools, Tool.Number, "번호", "Number", "number", "N", "클릭할 때마다 순서대로 번호를 붙입니다.", "Click to add sequential numbered markers.");
            AddTool(tools, Tool.Text, "글자", "Text", "text", "T", "이미지 위에 글자를 넣습니다.", "Add text to your image.");
            AddTool(tools, Tool.TextBox, "텍스트 상자", "Text box", "textBox", "X", "영역을 드래그하고 여러 줄의 글을 입력합니다.", "Drag a text box and enter multiple lines.");
            AddTool(tools, Tool.Arrow, "화살표", "Arrow", "arrow", "A", "드래그해서 가리킬 곳을 표시합니다.", "Drag an arrow to point something out.");
            AddTool(tools, Tool.Bubble, "말풍선", "Callout", "bubble", "B", "설명을 담은 말풍선을 그립니다.", "Draw a callout with a short explanation.");
            Separator(tools);
            AddTool(tools, Tool.Rectangle, "사각형", "Box", "rectangle", "R", "드래그해서 사각형을 그립니다.", "Drag to draw a rectangle.");
            AddTool(tools, Tool.Ellipse, "원", "Ellipse", "ellipse", "O", "드래그해서 원을 그립니다.", "Drag to draw an ellipse.");
            AddTool(tools, Tool.Mosaic, "가리기", "Mosaic", "mosaic", "M", "개인정보 등 가릴 부분을 모자이크 처리합니다.", "Pixelate the region you want to hide.");
            moreTools = ActionButton(Words.T("더 보기", "More"), "more", delegate { }, ButtonTone.Tool); moreTools.Font = Style.Font(8.5f); moreTools.MinimumSize = new Size(60, 60); moreTools.Margin = new Padding(0, 0, 0, 0);
            var extraMenu = new ContextMenuStrip();
            foreach (var band in new[] { Tool.CutHorizontal, Tool.CutVertical })
            {
                Tool selected = band; string caption = band == Tool.CutHorizontal ? Words.T("가로 띠 삭제", "Remove horizontal band") : Words.T("세로 띠 삭제", "Remove vertical band");
                var hidden = Style.Button(caption, delegate { ChooseTool(selected); }); hidden.Visible = false; toolButtons.Add(band, hidden); tools.Controls.Add(hidden);
                extraMenu.Items.Add(caption, null, delegate { ChooseTool(selected); });
            }
            moreTools.Click += delegate { extraMenu.Show(moreTools, new Point(0, moreTools.Height)); }; tips.SetToolTip(moreTools, Words.T("이미지 중간의 가로·세로 띠를 삭제합니다.", "Remove a horizontal or vertical band from the image.")); tools.Controls.Add(moreTools);
            toolSurface.Controls.Add(tools); workspace.Controls.Add(toolSurface, 0, 0);
            workspace.Controls.Add(BuildContextBar(), 0, 1);
            editor = new Editor { Dock = DockStyle.Fill, AllowDrop = true, Margin = Padding.Empty };
            editor.Changed += delegate { saveTimer.Stop(); saveTimer.Start(); UpdateStatus(); };
            editor.ViewChanged += delegate { UpdateStatus(); };
            editor.DragEnter += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            editor.DragDrop += delegate(object sender, DragEventArgs e) { var paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null && paths.Length > 0) ImportPath(paths[0]); };
            workspace.Controls.Add(editor, 0, 2);
            emptyActions = new FlowLayoutPanel { BackColor = Style.Paper, Size = new Size(380, 48), WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            emptyCapture = ActionButton(Words.T("새 캡처", "New capture"), "plus", async delegate { if (selectedVideo != null) EditVideo(); else await StartCapture(false); }, ButtonTone.Primary);
            emptyImport = ActionButton(Words.T("이미지 가져오기", "Import image"), "import", delegate { ImportImage(); }); emptyActions.Controls.AddRange(new Control[] { emptyCapture, emptyImport }); editor.Controls.Add(emptyActions);
            editor.Resize += delegate { PositionEmptyActions(); }; PositionEmptyActions();
            workspace.Controls.Add(BuildFooter(), 0, 3); ChooseTool(Tool.Select); UpdateStatus();
        }

        private void BuildSidebar(Control parent)
        {
            var sidebar = Grid(7); sidebar.BackColor = Style.Sidebar; sidebar.Padding = new Padding(14, 14, 14, 8);
            foreach (float height in new[] { 54, 40, 48, 38, 44 }) sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); parent.Controls.Add(sidebar);
            var capture = ActionButton(Words.T("새 캡처", "New capture"), "plus", async delegate { await StartCapture(false); }, ButtonTone.Primary);
            capture.AutoSize = false; capture.Dock = DockStyle.Fill; capture.Margin = new Padding(2, 0, 2, 10); capture.Font = Style.Font(10); tips.SetToolTip(capture, HotkeyText(settings)); sidebar.Controls.Add(capture, 0, 0);
            var quick = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var record = ActionButton(Words.T("화면 녹화", "Record"), "video", async delegate { await StartCapture(true); }, ButtonTone.Quiet); record.AutoSize = false; record.Dock = DockStyle.Fill;
            var import = ActionButton(Words.T("가져오기", "Import"), "import", delegate { ImportImage(); }, ButtonTone.Quiet); import.AutoSize = false; import.Dock = DockStyle.Fill;
            tips.SetToolTip(record, Words.T("영역을 선택해 화면을 녹화합니다.", "Choose a region to record.")); tips.SetToolTip(import, Words.T("저장된 이미지를 편집합니다. Ctrl+O", "Edit an existing image. Ctrl+O"));
            quick.Controls.Add(record, 0, 0); quick.Controls.Add(import, 1, 0); sidebar.Controls.Add(quick, 0, 1);
            count = Caption(Words.T("보관함", "Library"), 10, true); count.Padding = new Padding(6, 12, 0, 0); sidebar.Controls.Add(count, 0, 2);
            filter = new ComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
            filter.Items.AddRange(new object[] { Words.T("전체", "All"), Words.T("이미지", "Images"), Words.T("영상", "Videos") }); filter.SelectedIndex = 0;
            filter.SelectedIndexChanged += delegate
            {
                for (int i = 0; i < filterButtons.Count; i++) filterButtons[i].Selected = i == filter.SelectedIndex;
                if (!loading) { SaveCurrent(); LoadLibrary(doc == null ? selectedVideo : doc.PathName); }
            };
            var segmentSurface = new Surface { Dock = DockStyle.Fill, BackColor = Style.Hover, Radius = 9, Padding = new Padding(2), Margin = new Padding(2, 1, 2, 3) };
            var segments = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Style.Hover };
            for (int i = 0; i < 3; i++)
            {
                segments.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f)); int value = i;
                var b = ActionButton((string)filter.Items[i], "", delegate { filter.SelectedIndex = value; }, ButtonTone.Segment); b.AutoSize = false; b.Dock = DockStyle.Fill; b.MinimumSize = new Size(30, 24); b.Margin = Padding.Empty; b.Selected = i == 0; filterButtons.Add(b); segments.Controls.Add(b, i, 0);
            }
            segmentSurface.Controls.Add(segments); sidebar.Controls.Add(segmentSurface, 0, 3); parent.Controls.Add(filter);
            var searchSurface = new Surface { Dock = DockStyle.Fill, BackColor = Color.White, Radius = 8, Margin = new Padding(2, 7, 2, 8), Padding = new Padding(30, 4, 6, 2) };
            searchSurface.Paint += delegate(object sender, PaintEventArgs e) { Glyphs.Draw(e.Graphics, "search", new RectangleF(8, 6, 15, 15), Style.Muted); };
            search = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Style.Font(9), AccessibleName = Words.T("캡처 검색 · 파일명 또는 날짜", "Search captures by filename or date") };
            search.HandleCreated += delegate { Native.SendMessage(search.Handle, 0x1501, new IntPtr(1), Words.T("캡처 검색", "Search captures")); };
            search.TextChanged += delegate { if (!loading) LoadLibrary(doc == null ? selectedVideo : doc.PathName); };
            tips.SetToolTip(search, Words.T("파일명이나 날짜를 입력하세요. 예: 2026, 10.15", "Search filename or date, e.g. 2026, 10.15")); searchSurface.Controls.Add(search); sidebar.Controls.Add(searchSurface, 0, 4);
            thumbnails = new ImageList { ImageSize = new Size(206, 114), ColorDepth = ColorDepth.Depth32Bit };
            library = new CaptureList { Dock = DockStyle.Fill, LargeImageList = thumbnails, MultiSelect = true, HideSelection = false, Margin = Padding.Empty, AccessibleName = Words.T("캡처 보관함", "Capture library") };
            library.SelectedIndexChanged += delegate
            {
                if (loading || library.SelectedItems.Count == 0) return;
                var item = library.FocusedItem != null && library.FocusedItem.Selected ? library.FocusedItem : library.SelectedItems[0];
                OpenFile((string)item.Tag);
                if (library.SelectedItems.Count > 1) SetStatus(Words.T(library.SelectedItems.Count + "개 선택됨 · Delete: 휴지통으로 이동 · 더블클릭: 크게 보기", library.SelectedItems.Count + " selected · Delete: Recycle Bin · Double-click: preview"));
            };
            library.DoubleClick += delegate { if (selectedVideo != null) EditVideo(); else ShowPreview(); };
            var context = new ContextMenuStrip();
            context.Items.Add(Words.T("이미지 복사", "Copy image"), null, delegate { CopyImage(); });
            context.Items.Add(Words.T("이미지 저장…", "Save image…"), null, delegate { if (selectedVideo != null) EditVideo(); else ExportImage(); });
            context.Items.Add(Words.T("파일 위치 열기", "Show in folder"), null, delegate { ShowFile(); });
            context.Items.Add(new ToolStripSeparator()); context.Items.Add(Words.T("휴지통으로 이동…", "Move to Recycle Bin…"), null, delegate { RecycleSelected(); }); library.ContextMenuStrip = context; sidebar.Controls.Add(library, 0, 5);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty };
            footer.Controls.Add(ActionButton(Words.T("저장 폴더", "Folder"), "folder", delegate { Directory.CreateDirectory(settings.Folder); Process.Start("explorer.exe", Quote(settings.Folder)); }, ButtonTone.Quiet));
            var settingsButton = ActionButton(Words.T("설정", "Settings"), "settings", delegate { ShowSettings(); }, ButtonTone.Quiet); footer.Controls.Add(settingsButton); sidebar.Controls.Add(footer, 0, 6);
        }
        private Control BuildContextBar()
        {
            var surface = new Surface { Dock = DockStyle.Fill, BackColor = Color.White, BottomLine = true, Margin = Padding.Empty, Padding = new Padding(18, 2, 14, 3) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolHint = Caption("", 8.5f); toolHint.ForeColor = Style.Muted; layout.Controls.Add(toolHint, 0, 0);
            imageSizeOptions = BuildImageSizeOptions(); layout.Controls.Add(imageSizeOptions, 1, 0);
            styleOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0) };
            foreach (Color c in new[] { Style.Red, Style.Blue, Color.FromArgb(48, 179, 94), Color.FromArgb(255, 159, 10), Style.Ink })
            {
                Color color = c; var swatch = new SoftButton { Tone = ButtonTone.Swatch, SwatchColor = c, AutoSize = false, Size = new Size(28, 28), MinimumSize = new Size(28, 28), Margin = new Padding(1, 0, 1, 0), AccessibleName = Words.T("주석 색상 ", "Annotation color ") + ColorTranslator.ToHtml(c) };
                swatch.Click += delegate { editor.MarkColor = color; editor.ApplyStyle(); UpdateStatus(); }; swatches.Add(swatch); styleOptions.Controls.Add(swatch);
            }
            customColor = ActionButton("", "more", delegate { using (var d = new ColorDialog { Color = editor.MarkColor, FullOpen = true }) if (d.ShowDialog(this) == DialogResult.OK) { editor.MarkColor = d.Color; editor.ApplyStyle(); UpdateStatus(); } }, ButtonTone.Quiet); customColor.AutoSize = false; customColor.Size = new Size(28, 28); customColor.MinimumSize = new Size(28, 28); customColor.Margin = new Padding(0); styleOptions.Controls.Add(customColor); tips.SetToolTip(customColor, Words.T("다른 색상 선택", "Choose another color"));
            widthLabel = new Label { Text = Words.T("굵기", "Stroke"), AutoSize = true, Margin = new Padding(10, 7, 5, 0), ForeColor = Style.Muted, Font = Style.Font(8.5f) };
            strokeInput = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 3, Width = 44, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 0), AccessibleName = Words.T("선 굵기 또는 모자이크 강도", "Line width or mosaic strength") };
            strokeInput.ValueChanged += delegate { if (!updatingStyle) { editor.LineWidth = (float)strokeInput.Value; editor.ApplyStyle(); } };
            fontLabel = new Label { Text = Words.T("크기", "Size"), AutoSize = true, Margin = new Padding(10, 7, 5, 0), ForeColor = Style.Muted, Font = Style.Font(8.5f) };
            fontInput = new NumericUpDown { Minimum = 8, Maximum = 512, Value = 24, Width = 48, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 0), AccessibleName = Words.T("글자 크기", "Text size") };
            fontInput.ValueChanged += delegate { if (!updatingStyle) { editor.TextSize = (float)fontInput.Value; editor.ApplyStyle(); } };
            styleOptions.Controls.AddRange(new Control[] { widthLabel, strokeInput, fontLabel, fontInput }); layout.Controls.Add(styleOptions, 2, 0);
            applyButton = ActionButton(Words.T("자르기 적용", "Apply crop"), "check", delegate { ApplyEditorAction(); }, ButtonTone.Primary); applyButton.MinimumSize = new Size(80, 32); applyButton.Margin = new Padding(8, 2, 0, 0); layout.Controls.Add(applyButton, 3, 0);
            deleteMarkButton = ActionButton(Words.T("요소 삭제", "Delete element"), "trash", delegate { editor.DeleteSelected(); });
            deleteMarkButton.Margin = new Padding(12, 2, 0, 0); layout.Controls.Add(deleteMarkButton, 4, 0);
            tips.SetToolTip(deleteMarkButton, Words.T("선택한 요소만 삭제 · Delete · Ctrl+Z로 복구", "Delete the selected element · Delete · Ctrl+Z to restore"));
            var rows = Grid(2); rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rows.Controls.Add(layout, 0, 0); rows.Controls.Add(BuildAnnotationOptions(), 0, 1); surface.Controls.Add(rows); return surface;
        }
        private Control BuildFooter()
        {
            var surface = new Surface { Dock = DockStyle.Fill, BackColor = Color.White, TopLine = true, Margin = Padding.Empty, Padding = new Padding(12, 4, 12, 4) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var history = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            undoButton = ActionButton("", "undo", delegate { editor.Undo(); }, ButtonTone.Quiet); redoButton = ActionButton("", "redo", delegate { editor.Redo(); }, ButtonTone.Quiet);
            undoButton.AccessibleName = Words.T("실행 취소", "Undo"); redoButton.AccessibleName = Words.T("다시 실행", "Redo"); tips.SetToolTip(undoButton, Words.T("실행 취소 · Ctrl+Z", "Undo · Ctrl+Z")); tips.SetToolTip(redoButton, Words.T("다시 실행 · Ctrl+Y", "Redo · Ctrl+Y"));
            foreach (var b in new[] { undoButton, redoButton }) { b.MinimumSize = new Size(32, 32); b.Margin = new Padding(1); history.Controls.Add(b); }
            layout.Controls.Add(history, 0, 0);
            status = Caption("", 8.5f); status.Padding = new Padding(10, 0, 0, 0); status.ForeColor = Style.Muted; layout.Controls.Add(status, 1, 0);
            var zoom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            var minus = ActionButton("", "minus", delegate { editor.ZoomBy(1 / 1.2f); }, ButtonTone.Quiet); var plus = ActionButton("", "plus", delegate { editor.ZoomBy(1.2f); }, ButtonTone.Quiet);
            minus.AccessibleName = Words.T("축소", "Zoom out"); plus.AccessibleName = Words.T("확대", "Zoom in");
            zoomLabel = new Label { Text = "100%", Width = 48, Height = 34, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9), ForeColor = Style.Muted, Margin = Padding.Empty };
            var fit = ActionButton(Words.T("맞춤", "Fit"), "fit", delegate { editor.Fit(); }, ButtonTone.Quiet);
            foreach (var b in new[] { minus, plus, fit }) { b.MinimumSize = new Size(30, 32); b.Margin = new Padding(1); }
            zoom.Controls.AddRange(new Control[] { minus, zoomLabel, plus, fit }); layout.Controls.Add(zoom, 2, 0); surface.Controls.Add(layout); return surface;
        }
        private void AddTool(FlowLayoutPanel panel, Tool tool, string ko, string en, string glyph, string shortcut, string koHint, string enHint)
        {
            var button = ActionButton(Words.T(ko, en), glyph, delegate { ChooseTool(tool); }, ButtonTone.Tool); button.Font = Style.Font(8.5f); button.MinimumSize = new Size(60, 60); button.Margin = new Padding(0, 0, 0, 0);
            tips.SetToolTip(button, Words.T(koHint, enHint) + "\n" + Words.T("단축키: ", "Shortcut: ") + shortcut); toolButtons.Add(tool, button); panel.Controls.Add(button);
        }
        private static void Separator(FlowLayoutPanel panel)
        { panel.Controls.Add(new Panel { Width = 1, Height = 30, BackColor = Style.Line, Margin = new Padding(5, 14, 5, 0) }); }
        private void ChooseTool(Tool tool)
        {
            if (tool == Tool.Resize) sizeInputDocument = null;
            if (editor != null) editor.SetTool(tool);
            foreach (var entry in toolButtons) { var b = entry.Value as SoftButton; if (b != null) b.Selected = entry.Key == tool; }
            if (moreTools != null) moreTools.Selected = tool == Tool.CutHorizontal || tool == Tool.CutVertical;
            UpdateStatus();
        }
        private void SetStatus(string text) { if (status != null) status.Text = text; }
        private void UpdateStatus()
        {
            if (status == null || editor == null) return;
            bool hasImage = doc != null, hasVideo = selectedVideo != null;
            copyButton.Enabled = hasImage; exportButton.Enabled = hasImage || hasVideo;
            exportButton.Text = hasVideo ? Words.T("영상 내보내기", "Export video") : Words.T("이미지 저장", "Save image");
            undoButton.Enabled = hasImage && doc.CanUndo; redoButton.Enabled = hasImage && doc.CanRedo;
            foreach (var entry in toolButtons) { entry.Value.Enabled = hasImage; var button = entry.Value as SoftButton; if (button != null) button.Selected = entry.Key == editor.Tool; } moreTools.Enabled = hasImage;
            moreTools.Selected = editor.Tool == Tool.CutHorizontal || editor.Tool == Tool.CutVertical;
            editor.VideoMode = hasVideo;
            emptyActions.Visible = !hasImage;
            emptyCapture.Text = hasVideo ? Words.T("영상 재생 · 편집", "Play and edit") : Words.T("새 캡처", "New capture"); emptyCapture.Glyph = hasVideo ? "play" : "plus"; emptyImport.Visible = !hasVideo;
            PositionEmptyActions();
            zoomLabel.Text = hasImage ? Math.Round(editor.Zoom * 100) + "%" : "—";
            if (!hasImage)
            {
                if (!hasVideo) title.Text = Words.T("캡처를 시작해 보세요", "Ready when you are");
                styleOptions.Visible = false; imageSizeOptions.Visible = false; applyButton.Visible = false; deleteMarkButton.Visible = false; sizeInputDocument = null;
                UpdateAnnotationOptions(null, Tool.Select, false);
                detail.Text = hasVideo ? Words.T("MP4 · 원본 해상도", "MP4 · Original resolution") : Words.T("화면을 담고, 필요한 설명만 더하세요.", "Capture a screen, then make it clear.");
                toolHint.Text = hasVideo ? Words.T("영상을 재생하거나 필요한 구간만 내보낼 수 있어요.", "Play your recording or export a shorter clip.") : Words.T("이미지를 선택하면 편집 도구를 사용할 수 있어요.", "Select an image to start editing.");
                SetStatus(hasVideo ? Words.T("원본 영상은 그대로 보관됩니다.", "Your original recording is preserved.") : HotkeyText(settings) + Words.T("  ·  언제든 새 캡처", "  ·  Capture from any app")); editor.Invalidate(); return;
            }
            Tool current = editor.Tool;
            Mark selected = editor.SelectedMark;
            deleteMarkButton.Visible = current == Tool.Select || selected != null; deleteMarkButton.Enabled = selected != null;
            Tool styled = selected == null ? current : selected.Kind;
            bool area = current == Tool.Crop || current == Tool.CutHorizontal || current == Tool.CutVertical;
            bool resizing = current == Tool.Resize;
            bool styling = selected != null || (current != Tool.Select && !area && !resizing);
            imageSizeOptions.Visible = resizing; imageSizeOptions.Enabled = !editor.IsFrameResizing; SynchronizeSizeInputs();
            styleOptions.Visible = styling; applyButton.Visible = area || resizing;
            applyButton.Enabled = resizing ? !editor.IsFrameResizing && (imageWidthInput.Value != doc.Image.Width || imageHeightInput.Value != doc.Image.Height) : editor.HasAreaSelection;
            applyButton.Text = resizing ? Words.T("크기 적용", "Apply size") : current == Tool.Crop ? Words.T("자르기 적용", "Apply crop") : Words.T("선택한 띠 삭제", "Remove band");
            bool text = styled == Tool.Text || styled == Tool.TextBox || styled == Tool.Number || styled == Tool.Bubble;
            fontLabel.Visible = fontInput.Visible = text;
            widthLabel.Visible = strokeInput.Visible = styled != Tool.Text && styled != Tool.Number;
            widthLabel.Text = styled == Tool.Mosaic ? Words.T("강도", "Block") : Words.T("굵기", "Stroke");
            customColor.Visible = styled != Tool.Mosaic;
            foreach (var swatch in swatches) { swatch.Visible = styled != Tool.Mosaic; swatch.Selected = swatch.SwatchColor.ToArgb() == editor.MarkColor.ToArgb(); }
            if (selected != null)
            {
                updatingStyle = true;
                strokeInput.Value = Math.Max(strokeInput.Minimum, Math.Min(strokeInput.Maximum, (decimal)selected.Width)); fontInput.Value = Math.Max(fontInput.Minimum, Math.Min(fontInput.Maximum, (decimal)selected.FontSize));
                updatingStyle = false;
            }
            toolHint.Text = ToolInstruction(current, selected != null);
            UpdateAnnotationOptions(selected, styled, true);
            if (editor.IsFrameResizing) toolHint.Text = editor.ResizePreviewSize.Width + " × " + editor.ResizePreviewSize.Height + Words.T(" px · 놓으면 적용, Esc 취소", " px · Release to apply, Esc cancels");
            detail.Text = doc.Dirty ? Words.T("변경 내용 저장 중…", "Saving changes…") : Words.T("자동 저장됨 · 원본 보관", "Saved automatically · Original preserved");
            SetStatus(doc.Image.Width + " × " + doc.Image.Height + Words.T(" px   ·   휠로 확대 · 가운데 버튼으로 이동", " px   ·   Scroll to zoom · Middle-drag to pan"));
        }
        private static string ToolInstruction(Tool tool, bool selected)
        {
            if (selected) return Words.T("선택됨 · 드래그 이동 · 손잡이로 크기 조절", "Selected · Drag to move · Resize with handles");
            switch (tool)
            {
                case Tool.Select: return Words.T("요소를 클릭해 크기·색상·내용을 수정하세요.", "Click an element to edit its size, color or content.");
                case Tool.Resize: return Words.T("프레임을 끌거나 픽셀을 입력하세요. Alt: 자유 비율", "Drag the frame or enter pixels. Alt: free aspect ratio");
                case Tool.Crop: return Words.T("남길 영역을 드래그한 뒤 적용 또는 Enter를 누르세요.", "Drag the area to keep, then Apply or Enter.");
                case Tool.CutHorizontal: return Words.T("없앨 가로 띠를 드래그한 뒤 Enter를 누르세요.", "Drag the horizontal band to remove, then Enter.");
                case Tool.CutVertical: return Words.T("없앨 세로 띠를 드래그한 뒤 Enter를 누르세요.", "Drag the vertical band to remove, then Enter.");
                case Tool.Number: return Words.T("클릭하면 1, 2, 3 순서대로 번호가 생겨요.", "Click to place markers numbered 1, 2, 3…");
                case Tool.Text: return Words.T("글자를 넣을 곳을 클릭하세요.", "Click where you want to add text.");
                case Tool.TextBox: return Words.T("상자를 드래그하고 글을 입력하세요. 기존 요소는 클릭해 수정", "Drag a text box, then enter text. Click existing elements to edit.");
                case Tool.Mosaic: return Words.T("가릴 부분을 드래그하세요. 강도로 모자이크 크기를 조절해요.", "Drag over the area to hide. Block controls pixel size.");
                case Tool.Bubble: return Words.T("말풍선을 드래그한 뒤 내용을 입력하세요.", "Drag a callout, then enter its text.");
                default: return Words.T("드래그해서 그리세요. Shift를 누르면 모양이 고정돼요.", "Drag to draw. Hold Shift to constrain the shape.");
            }
        }
        private void PositionEmptyActions()
        {
            if (emptyActions == null || editor == null) return;
            int width = emptyCapture.GetPreferredSize(Size.Empty).Width + emptyCapture.Margin.Horizontal;
            if (selectedVideo == null) width += emptyImport.GetPreferredSize(Size.Empty).Width + emptyImport.Margin.Horizontal;
            emptyActions.Width = Math.Max(180, width + 4); emptyActions.Location = new Point((editor.Width - emptyActions.Width) / 2, editor.Height / 2 + 78);
        }
        private void PasteImage()
        {
            try { if (Clipboard.ContainsImage() && SaveCurrent()) using (var image = Clipboard.GetImage()) using (var b = new Bitmap(image)) AddImage(b, true); else SetStatus(Words.T("복사된 이미지가 없어요. 먼저 이미지를 복사해 주세요.", "No image on the clipboard. Copy an image first.")); }
            catch (Exception e) { Program.Report(e); }
        }
        private void ShowShortcuts()
        {
            MessageBox.Show(this, HotkeyText(settings) + Words.T("  새 캡처\n\nV 선택   C 자르기   S 크기 조절\nN 번호   T 글자   X 텍스트 상자\nA 화살표   B 말풍선   R 사각형   O 원   M 가리기\n\nCtrl+C 이미지 복사   Ctrl+V 이미지 가져오기\nCtrl+Z 실행 취소   Ctrl+Y 다시 실행\nCtrl+0 화면 맞춤   Enter 자르기 적용\nF2 / Enter 내용 수정   Delete / Backspace 요소 삭제\nEsc 선택 해제\n\n마우스 휠: 확대·축소\n가운데 버튼 드래그: 화면 이동", "  New capture\n\nV Select   C Crop   S Resize\nN Number   T Text   X Text box\nA Arrow   B Callout   R Box   O Ellipse   M Mosaic\n\nCtrl+C Copy image   Ctrl+V Paste image\nCtrl+Z Undo   Ctrl+Y Redo\nCtrl+0 Fit   Enter Apply crop\nF2 / Enter Edit text   Delete / Backspace Delete element\nEsc Deselect\n\nScroll: zoom\nMiddle-drag: pan"), Words.T("단축키 안내", "Keyboard shortcuts"));
        }
        private void SavePreview(string path)
        { using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); Storage.SavePng(bitmap, path); } }
    }
}
