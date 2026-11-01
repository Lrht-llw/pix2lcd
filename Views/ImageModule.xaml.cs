using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using pix2lcd.Core;

namespace pix2lcd.Views;

/// <summary>图片取模面板。</summary>
public partial class ImageModule : UserControl
{
    private const int MaxPreviewSize = 512;
    private const int MaxOutputSize = 4096;

    private bool _loading = true;
    private readonly DispatcherTimer _reprocessTimer;
    private RgbaImage? _source;
    private RgbaImage? _processed;
    private byte[]? _encoded;
    private string _sourceName = "";
    private int _sourceW, _sourceH;

    private static readonly string[] DepthNames =
    {
        "单色 (1 bpp)", "4 色 (2 bpp)", "16 色 (4 bpp)", "256 色 (8 bpp)",
        "RGB332 (8 bpp)", "4096 色 (12 bpp)", "16 位色 RGB565", "18 位色 RGB666",
        "24 位色 RGB888", "32 位色 ARGB8888",
    };

    private static readonly ColorDepth[] DepthValues =
    {
        ColorDepth.Mono, ColorDepth.Color4, ColorDepth.Color16, ColorDepth.Color256,
        ColorDepth.Rgb332, ColorDepth.Color4096, ColorDepth.Rgb565, ColorDepth.Rgb666,
        ColorDepth.Rgb888, ColorDepth.Argb8888,
    };

    private static readonly string[] ScanNames =
    {
        "水平扫描 (Horizon Scan)",
        "垂直扫描 (Vertical Scan)",
        "行列式 (Data hor, Byte ver)",
        "列行式 (Data ver, Byte hor)",
    };

    private static readonly DotMatrixEncoder.ScanPattern[] ScanValues =
    {
        DotMatrixEncoder.ScanPattern.Horizontal,
        DotMatrixEncoder.ScanPattern.Vertical,
        DotMatrixEncoder.ScanPattern.RowColumn,
        DotMatrixEncoder.ScanPattern.ColumnRow,
    };

    public ImageModule()
    {
        InitializeComponent();
        _reprocessTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _reprocessTimer.Tick += (_, _) =>
        {
            _reprocessTimer.Stop();
            Reprocess();
            if (outputBox.Text.Length > 0) Generate();
        };
        depthBox.ItemsSource = DepthNames;
        depthBox.SelectedIndex = 0;
        scanBox.ItemsSource = ScanNames;
        scanBox.SelectedIndex = 0;
        _loading = false;
        AllowDrop = true;
        Drop += OnDrop;
        UpdateLabels();
    }

    private ColorDepth CurrentDepth => DepthValues[Math.Clamp(depthBox.SelectedIndex, 0, DepthValues.Length - 1)];
    private DotMatrixEncoder.ScanPattern CurrentScan => ScanValues[Math.Clamp(scanBox.SelectedIndex, 0, ScanValues.Length - 1)];
    private bool MsbFirst => msbBox.IsChecked == true;

    private int OutWidth => ParseSize(outWidthBox.Text, 128);
    private int OutHeight => ParseSize(outHeightBox.Text, 64);

    private static int ParseSize(string? s, int fallback)
        => int.TryParse((s ?? "").Trim(), out int v) && v > 0 ? Math.Min(v, MaxOutputSize) : fallback;

    /// <summary>按最长边等比缩放到上限内，避免宽高各自截断导致比例失真。</summary>
    private static (int Width, int Height) FitWithin(int width, int height, int max)
    {
        int longest = Math.Max(width, height);
        if (longest <= max) return (width, height);
        double scale = (double)max / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    // ---------------------------------------------------------------- 载入

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图像文件 (*.bmp;*.png;*.jpg;*.jpeg;*.gif;*.tif;*.tiff)|*.bmp;*.png;*.jpg;*.jpeg;*.gif;*.tif;*.tiff|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        LoadImage(dlg.FileName);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            LoadImage(files[0]);
    }

    private void LoadImage(string path)
    {
        try
        {
            _source = RgbaImage.FromFile(path);
            _sourceName = System.IO.Path.GetFileName(path);
            _sourceW = _source.Width;
            _sourceH = _source.Height;
            fileLabel.Text = $"{_sourceName}  ({_sourceW}×{_sourceH})";
            sourceImage.Source = _source.ToBitmapSource();

            _loading = true;
            var (w, h) = FitWithin(_sourceW, _sourceH, MaxPreviewSize);
            outWidthBox.Text = w.ToString();
            outHeightBox.Text = h.ToString();
            _loading = false;

            _reprocessTimer.Stop();
            Reprocess();
        }
        catch (Exception ex)
        {
            statusLabel.Text = "加载失败：" + ex.Message;
        }
    }

    /// <summary>供自动化截图与测试使用：直接载入内存位图。</summary>
    public void LoadDemoImage(System.Windows.Media.Imaging.BitmapSource src)
    {
        _source = RgbaImage.FromBitmapSource(src);
        _sourceName = "demo";
        _sourceW = _source.Width;
        _sourceH = _source.Height;
        fileLabel.Text = $"demo  ({_sourceW}×{_sourceH})";
        sourceImage.Source = src;
        _loading = true;
        var (w, h) = FitWithin(_sourceW, _sourceH, MaxPreviewSize);
        outWidthBox.Text = w.ToString();
        outHeightBox.Text = h.ToString();
        _loading = false;
        _reprocessTimer.Stop();
        Reprocess();
        Generate();
    }

    // ---------------------------------------------------------------- 处理

    private void Reprocess()
    {
        if (_source is null) return;
        int w = OutWidth, h = OutHeight;
        var img = _source.Resize(w, h);
        if (CurrentDepth != ColorDepth.Argb8888) img = img.FlattenOntoWhite();
        img = img.Adjust(brightSlider.Value, contrastSlider.Value);
        if (invertBox.IsChecked == true) img = img.Invert();
        if (mirrorHBox.IsChecked == true) img = img.FlipHorizontal();
        if (mirrorVBox.IsChecked == true) img = img.FlipVertical();
        _processed = img;

        var preview = ImageEncoder.PreviewQuantized(img, CurrentDepth, (int)thresholdSlider.Value);
        previewImage.Source = preview.ToBitmapSource();
        previewLabel.Text = $"取模后的量化效果  ({w}×{h}, {DepthNames[depthBox.SelectedIndex]})";
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        thresholdLabel.Text = ((int)thresholdSlider.Value).ToString(CultureInfo.InvariantCulture);
        brightLabel.Text = ((int)brightSlider.Value).ToString(CultureInfo.InvariantCulture);
        contrastLabel.Text = ((int)contrastSlider.Value).ToString(CultureInfo.InvariantCulture);
    }

    private void OnUseOriginalSize(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        _loading = true;
        var (w, h) = FitWithin(_sourceW, _sourceH, MaxOutputSize);
        outWidthBox.Text = w.ToString();
        outHeightBox.Text = h.ToString();
        _loading = false;
        _reprocessTimer.Stop();
        Reprocess();
    }

    // ---------------------------------------------------------------- 生成

    private void Generate()
    {
        if (_processed is null) return;
        var data = ImageEncoder.Encode(_processed, CurrentDepth, CurrentScan, MsbFirst,
            false, ColorOrder.Rgb, (int)thresholdSlider.Value);
        _encoded = data;

        int bpp = ImageEncoder.BitsPerPixel(CurrentDepth);
        var sb = new StringBuilder();
        if (commentBox.IsChecked == true)
        {
            sb.Append("/*\r\n");
            sb.Append($" * 图像: {_sourceName}  {_sourceW}x{_sourceH}\r\n");
            sb.Append($" * 输出: {_processed.Width}x{_processed.Height}  色深: {DepthNames[depthBox.SelectedIndex]}\r\n");
            sb.Append($" * 扫描: {ScanNames[scanBox.SelectedIndex]}  位序: {(MsbFirst ? "高位在前" : "低位在前")}\r\n");
            sb.Append($" * 数据长度: {data.Length} 字节\r\n");
            sb.Append(" */\r\n");
        }
        sb.Append($"const unsigned char gImage_{_processed.Width}x{_processed.Height}[{data.Length}] = {{\r\n");
        var fmt = OutputFormat.Preset(OutputStyle.C51);
        fmt.PerLine = 16;
        sb.Append(fmt.Format(data));
        sb.Append("};\r\n");
        outputBox.Text = sb.ToString();

        byteLabel.Text = $"{data.Length} 字节  ({bpp} bpp)";
        statusLabel.Text = $"已生成 {data.Length} 字节";
    }

    // ---------------------------------------------------------------- 事件

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        ScheduleReprocess();
    }

    private void OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ScheduleReprocess();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        ScheduleReprocess();
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateLabels();
        ScheduleReprocess();
    }

    /// <summary>合并连续的滑动/输入事件，避免每次变化都全量重算并编码。</summary>
    private void ScheduleReprocess()
    {
        _reprocessTimer.Stop();
        _reprocessTimer.Start();
    }

    /// <summary>如果有挂起的重算，立即执行，保证“生成/保存”使用最新参数。</summary>
    private bool FlushReprocess()
    {
        if (!_reprocessTimer.IsEnabled) return false;
        _reprocessTimer.Stop();
        Reprocess();
        return true;
    }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        FlushReprocess();
        Generate();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (FlushReprocess()) Generate();
        if (outputBox.Text.Length == 0) return;
        try
        {
            Clipboard.SetText(outputBox.Text);
            statusLabel.Text = "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "复制失败：" + ex.Message;
        }
    }

    private void OnSaveC(object sender, RoutedEventArgs e)
    {
        bool flushed = FlushReprocess();
        if (flushed || outputBox.Text.Length == 0) Generate();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "C 源文件 (*.c)|*.c|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = "image_data.c",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            System.IO.File.WriteAllText(dlg.FileName, outputBox.Text, OutputEncoding.Text);
            statusLabel.Text = "已保存：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            statusLabel.Text = "保存失败：" + ex.Message;
        }
    }

    private void OnSaveBin(object sender, RoutedEventArgs e)
    {
        bool flushed = FlushReprocess();
        if (flushed || _encoded is null) Generate();
        if (_encoded is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "二进制文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            FileName = "image_data.bin",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            System.IO.File.WriteAllBytes(dlg.FileName, _encoded);
            statusLabel.Text = "已保存：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            statusLabel.Text = "保存失败：" + ex.Message;
        }
    }
}
