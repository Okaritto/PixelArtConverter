using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PixelArtConverter;

public sealed class MainForm : Form
{
    readonly NumericUpDown columns = Number(5, 1, 32), rows = Number(4, 1, 32);
    readonly NumericUpDown width = Number(32, 4, 128), height = Number(32, 4, 128);
    readonly NumericUpDown alpha = Number(48, 0, 255), step = Number(24, 1, 255), tolerance = Number(24, 0, 255);
    readonly NumericUpDown marginLeft = Number(0, 0, 100000), marginTop = Number(0, 0, 100000);
    readonly NumericUpDown marginRight = Number(0, 0, 100000), marginBottom = Number(0, 0, 100000), padding = Number(1, 0, 63);
    readonly ComboBox centerMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly CheckBox fitEach = new() { Text = "各機体を個別に拡大縮小", AutoSize = true };
    readonly CheckBox overview = new() { Text = "元画像全体と分割線を表示", Checked = true, AutoSize = true };
    readonly TextBox columnWidths = new() { Width = 240, PlaceholderText = "空欄＝均等分割" }, rowHeights = new() { Width = 240, PlaceholderText = "空欄＝均等分割" };
    List<Rectangle> currentCells = new();
    readonly TrackBar alphaSlider = new() { Minimum = 0, Maximum = 255, Value = 48, Width = 180, TickFrequency = 32 };
    readonly TrackBar stepSlider = new() { Minimum = 1, Maximum = 255, Value = 24, Width = 180, TickFrequency = 32 };
    readonly ComboBox background = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 215 };
    readonly PixelView sourceView = new(), actualView = new() { ActualSize = true }, zoomView = new();
    readonly Label status = new() { AutoSize = true }, selection = new() { AutoSize = true, Padding = new Padding(12) };
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 250 };
    Bitmap? source;
    List<Bitmap> sprites = new();
    int selected;
    Color key = Color.Magenta;
    bool updating;

    static NumericUpDown Number(int value, int min, int max) => new() { Value = value, Minimum = min, Maximum = max, Width = 65 };
    public MainForm()
    {
        Text = "Pixel Art Converter 1.3"; Width = 1220; Height = 860; MinimumSize = new Size(1060, 780);
        Font = new Font("Yu Gothic UI", 10);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        var files = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        AddButton(files, "画像を開く", Open); AddButton(files, "シートPNG保存", SaveSheet); AddButton(files, "個別PNG保存", SaveIndividual);
        root.Controls.Add(files, 0, 0);
        var settings = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        void Field(string name, Control control) { settings.Controls.Add(new Label { Text = name, AutoSize = true, Margin = new Padding(8, 8, 2, 0) }); settings.Controls.Add(control); }
        Field("列", columns); Field("行", rows); Field("幅", width); Field("高さ", height);
        Field("Alpha", alpha); settings.Controls.Add(alphaSlider); Field("Color Step", step); settings.Controls.Add(stepSlider);
        settings.SetFlowBreak(stepSlider, true);
        background.Items.AddRange(new object[] { "透明画像をそのまま使用", "左上の色を透明化", "指定色を透明化" }); background.SelectedIndex = 0;
        Field("背景", background); AddButton(settings, "背景色を選ぶ", () => { using var d = new ColorDialog { Color = key }; if (d.ShowDialog() == DialogResult.OK) { key = d.Color; background.SelectedIndex = 2; Schedule(); } });
        Field("色の許容差", tolerance);
        settings.Controls.Add(new Label { Text = "Alpha: 小さいほど輪郭を残す ／ Color Step: 大きいほど減色", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
        settings.SetFlowBreak(settings.Controls[settings.Controls.Count - 1], true);
        Field("除外する入力余白(px) 左", marginLeft); Field("上", marginTop); Field("右", marginRight); Field("下", marginBottom);
        Field("出力余白(px)", padding); settings.Controls.Add(fitEach);
        centerMode.Items.AddRange(new object[] { "輪郭の中心を揃える", "機体の重心を揃える" }); centerMode.SelectedIndex = 0;
        Field("中心合わせ", centerMode);
        settings.SetFlowBreak(centerMode, true);
        Field("列の幅(px)", columnWidths); Field("行の高さ(px)", rowHeights);
        AddButton(settings, "分割設定をリセット", ResetLayout);
        settings.SetFlowBreak(settings.Controls[settings.Controls.Count - 1], true);
        settings.Controls.Add(overview);
        settings.Controls.Add(new Label { Text = "各マスの幅・高さをカンマ区切り。余白の後から計算。最後のマスは省略可。", AutoSize = true, Margin = new Padding(8) });
        root.Controls.Add(settings, 0, 1);
        var views = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        for (int i = 0; i < 3; i++) views.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        views.RowStyles.Add(new RowStyle(SizeType.AutoSize)); views.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        string[] labels = { "元画像（選択セル）", "出力・実寸", "出力・整数倍拡大" };
        PixelView[] panels = { sourceView, actualView, zoomView };
        for (int i = 0; i < 3; i++) { views.Controls.Add(new Label { Text = labels[i], AutoSize = true }, i, 0); panels[i].Dock = DockStyle.Fill; views.Controls.Add(panels[i], i, 1); }
        root.Controls.Add(views, 0, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        AddButton(footer, "◀", () => { if (debounce.Enabled && !Rebuild()) return; if (sprites.Count > 0) { selected = (selected + sprites.Count - 1) % sprites.Count; ShowSprite(); } });
        footer.Controls.Add(selection);
        AddButton(footer, "▶", () => { if (debounce.Enabled && !Rebuild()) return; if (sprites.Count > 0) { selected = (selected + 1) % sprites.Count; ShowSprite(); } }); footer.Controls.Add(status);
        root.Controls.Add(footer, 0, 3);
        foreach (var n in new[] { columns, rows, width, height, alpha, step, tolerance, marginLeft, marginTop, marginRight, marginBottom, padding }) n.ValueChanged += (_, _) => Schedule();
        centerMode.SelectedIndexChanged += (_, _) => Schedule();
        fitEach.CheckedChanged += (_, _) => Schedule();
        columnWidths.TextChanged += (_, _) => Schedule(); rowHeights.TextChanged += (_, _) => Schedule();
        overview.CheckedChanged += (_, _) => ShowSprite();
        background.SelectedIndexChanged += (_, _) => Schedule();
        alpha.ValueChanged += (_, _) => alphaSlider.Value = (int)alpha.Value;
        step.ValueChanged += (_, _) => stepSlider.Value = (int)step.Value;
        alphaSlider.ValueChanged += (_, _) => alpha.Value = alphaSlider.Value;
        stepSlider.ValueChanged += (_, _) => step.Value = stepSlider.Value;
        debounce.Tick += (_, _) => { debounce.Stop(); Rebuild(); };
    }
    static void AddButton(FlowLayoutPanel panel, string text, Action action)
    { var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4) }; b.Click += (_, _) => action(); panel.Controls.Add(b); }
    void Schedule() { if (source == null || updating) return; debounce.Stop(); debounce.Start(); }
    void ResetLayout()
    {
        bool previous = updating; updating = true;
        try { marginLeft.Value = marginTop.Value = marginRight.Value = marginBottom.Value = 0; columnWidths.Clear(); rowHeights.Clear(); }
        finally { updating = previous; }
        Schedule();
    }
    void Open()
    {
        using var dialog = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべて|*.*" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            using var image = Image.FromFile(dialog.FileName);
            if ((long)image.Width * image.Height > 16_000_000) throw new ArgumentException("画像は1600万ピクセル以下にしてください。");
            var next = new Bitmap(image);
            sourceView.Image = null; actualView.Image = null; zoomView.Image = null;
            foreach (var sprite in sprites) sprite.Dispose(); sprites.Clear(); currentCells.Clear();
            source?.Dispose(); source = next; sourceView.Image = source; sourceView.RegionOfImage = null; sourceView.Cells = null;
            selected = 0; ResetLayout(); Rebuild(); sourceView.Invalidate(); actualView.Invalidate(); zoomView.Invalidate();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "読み込みエラー"); }
    }
    bool Rebuild()
    {
        if (source == null) return false;
        debounce.Stop(); updating = true; Cursor = Cursors.WaitCursor;
        try
        {
            var settings = new Settings((int)columns.Value, (int)rows.Value, (int)width.Value, (int)height.Value,
                (int)alpha.Value, (int)step.Value, background.SelectedIndex, key, (int)tolerance.Value,
                (int)marginLeft.Value, (int)marginTop.Value, (int)marginRight.Value, (int)marginBottom.Value,
                fitEach.Checked, (int)padding.Value, columnWidths.Text, rowHeights.Text, centerMode.SelectedIndex == 1);
            if ((long)settings.Columns * settings.Rows * settings.Width * settings.Height > 4_000_000)
                throw new ArgumentException("出力の合計を400万ピクセル以下にしてください。");
            var nextCells = SpriteLayout.GetCells(source.Width, source.Height, settings);
            var next = Converter.Convert(source, settings);
            actualView.Image = null; zoomView.Image = null;
            foreach (var image in sprites) image.Dispose(); sprites = next; currentCells = nextCells;
            selected = Math.Min(selected, sprites.Count - 1); ShowSprite();
            status.Text = $"元 {source.Width}×{source.Height} → シート {settings.Width * settings.Columns}×{settings.Height * settings.Rows}";
            return true;
        }
        catch (Exception ex) { status.Text = ex.Message; return false; }
        finally { updating = false; Cursor = Cursors.Default; }
    }
    void ShowSprite()
    {
        if (source == null || sprites.Count == 0) return;
        sourceView.Image = source;
        sourceView.RegionOfImage = overview.Checked ? null : currentCells[selected];
        sourceView.Cells = overview.Checked ? currentCells : null;
        sourceView.SelectedCell = selected;
        actualView.Image = sprites[selected]; zoomView.Image = sprites[selected];
        selection.Text = $"Sprite {selected + 1} / {sprites.Count}";
        sourceView.Invalidate(); actualView.Invalidate(); zoomView.Invalidate();
    }
    void SaveSheet()
    {
        if (!Rebuild()) return;
        using var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = "spritesheet.png" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            using var sheet = new Bitmap((int)columns.Value * (int)width.Value, (int)rows.Value * (int)height.Value, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(sheet))
            { g.CompositingMode = CompositingMode.SourceCopy; for (int i = 0; i < sprites.Count; i++) g.DrawImageUnscaled(sprites[i], i % (int)columns.Value * (int)width.Value, i / (int)columns.Value * (int)height.Value); }
            sheet.Save(d.FileName, ImageFormat.Png); status.Text = "シートを保存しました。";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存エラー"); }
    }
    void SaveIndividual()
    {
        if (!Rebuild()) return;
        using var d = new FolderBrowserDialog { Description = "保存先を選択（新しいサブフォルダーを作成します）", UseDescriptionForTitle = true };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            string folder = Path.Combine(d.SelectedPath, "sprites_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(folder);
            for (int i = 0; i < sprites.Count; i++) sprites[i].Save(Path.Combine(folder, $"sprite_{i + 1:D3}.png"), ImageFormat.Png);
            status.Text = $"{sprites.Count}枚を保存しました。";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存エラー"); }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { debounce.Dispose(); source?.Dispose(); foreach (var b in sprites) b.Dispose(); } base.Dispose(disposing); }
}

public sealed class PixelView : Control
{
    public Image? Image { get; set; }
    public Rectangle? RegionOfImage { get; set; }
    public IReadOnlyList<Rectangle>? Cells { get; set; }
    public int SelectedCell { get; set; }
    public bool ActualSize { get; set; }
    public PixelView() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (int y = 0; y < Height; y += 12)
        for (int x = 0; x < Width; x += 12)
            e.Graphics.FillRectangle(((x / 12 + y / 12) % 2 == 0) ? Brushes.LightGray : Brushes.WhiteSmoke, x, y, 12, 12);
        if (Image == null) return;
        Rectangle src = RegionOfImage ?? new Rectangle(0, 0, Image.Width, Image.Height);
        double scale = ActualSize ? 1 : Math.Min((double)Width / src.Width, (double)Height / src.Height);
        if (!ActualSize && scale >= 1) scale = Math.Floor(scale);
        int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        int ox = (Width - w) / 2, oy = (Height - h) / 2;
        e.Graphics.DrawImage(Image, new Rectangle(ox, oy, w, h), src, GraphicsUnit.Pixel);
        if (Cells != null)
        {
            using var line = new Pen(Color.LimeGreen, 1);
            using var highlight = new Pen(Color.OrangeRed, 3);
            float sx = (float)w / src.Width, sy = (float)h / src.Height;
            for (int i = 0; i < Cells.Count; i++)
            {
                var cell = Cells[i];
                var r = new RectangleF(ox + (cell.Left - src.Left) * sx, oy + (cell.Top - src.Top) * sy, cell.Width * sx, cell.Height * sy);
                e.Graphics.DrawRectangle(i == SelectedCell ? highlight : line, r.X, r.Y, r.Width, r.Height);
                e.Graphics.DrawString((i + 1).ToString(), Font, Brushes.Yellow, r.X + 2, r.Y + 2);
            }
        }
    }
}
