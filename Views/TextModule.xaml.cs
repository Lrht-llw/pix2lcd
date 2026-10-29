using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pix2lcd.Core;

namespace pix2lcd.Views;

/// <summary>文字取模面板。</summary>
public partial class TextModule : UserControl
{
    private bool _loading = true;
    private BitMatrix? _matrix;

    public TextModule()
    {
        InitializeComponent();
        InitializeOptions();
        Rebuild();
        Generate();
    }

    private DotMatrixEncoder.ScanPattern CurrentPattern =>
        modeHorizontal.IsChecked == true ? DotMatrixEncoder.ScanPattern.Horizontal
        : modeColumnRow.IsChecked == true ? DotMatrixEncoder.ScanPattern.ColumnRow
        : modeRowColumn.IsChecked == true ? DotMatrixEncoder.ScanPattern.RowColumn
        : DotMatrixEncoder.ScanPattern.Vertical;

    private bool MsbFirst => msbFirst.IsChecked == true;
    private bool InvertedYang => yangCode.IsChecked == true;

    private void InitializeOptions()
    {
        _loading = true;
        try
        {
            var sizes = new List<string>();
            for (int i = 3; i <= 200; i++) sizes.Add(i.ToString());
            widthBox.ItemsSource = sizes;
            heightBox.ItemsSource = sizes;
            dotWidthBox.ItemsSource = sizes;
            dotHeightBox.ItemsSource = sizes;
            widthBox.Text = "16";
            heightBox.Text = "16";
            dotWidthBox.Text = "16";
            dotHeightBox.Text = "16";

            var fonts = new List<string>();
            foreach (var f in Fonts.SystemFontFamilies)
                fonts.Add(f.Source);
            fonts.Sort(StringComparer.CurrentCultureIgnoreCase);
            foreach (var f in fonts) fontBox.Items.Add(f);

            int prefer = fonts.FindIndex(n =>
                n.Equals("宋体", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("SimSun", StringComparison.OrdinalIgnoreCase));
            if (prefer < 0) prefer = fonts.FindIndex(n => n.Contains("宋", StringComparison.Ordinal));
            fontBox.SelectedIndex = prefer >= 0 ? prefer : 0;

            styleBox.ItemsSource = new[] { "C51", "A51", "十六进制", "二进制" };
            styleBox.SelectedIndex = 0;
            ApplyStylePreset();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyStylePreset()
    {
        var fmt = OutputFormat.Preset(StyleFromBox());
        dataPrefixBox.Text = fmt.DataPrefix;
        dataSuffixBox.Text = fmt.DataSuffix;
        perLineBox.Text = fmt.PerLine.ToString();
    }

    private OutputStyle StyleFromBox() => styleBox.SelectedIndex switch
    {
        1 => OutputStyle.A51,
        2 => OutputStyle.HexPlain,
        3 => OutputStyle.BinStyle,
        _ => OutputStyle.C51,
    };

    private OutputFormat BuildFormat()
    {
        var fmt = OutputFormat.Preset(StyleFromBox());
        fmt.Hex = hexMode.IsChecked == true;
        fmt.PerLine = ParseInt(perLineBox.Text, 16);
        fmt.DataPrefix = dataPrefixBox.Text ?? "";
        fmt.DataSuffix = dataSuffixBox.Text ?? "";
        return fmt;
    }

    private static int ParseInt(string? s, int fallback)
        => int.TryParse((s ?? "").Trim(), out int v) && v > 0 ? v : fallback;

    // ---------------------------------------------------------------- 渲染

    /// <summary>按当前字体参数重新渲染文字为点阵。</summary>
    private void Rebuild()
    {
        if (_loading) return;
        var font = new GdiFontRasterizer.FontSpec
        {
            Family = fontBox.SelectedItem as string ?? "宋体",
            PixelHeight = ParseInt(heightBox.Text, 16),
            Bold = boldBtn.IsChecked == true,
            Italic = italicBtn.IsChecked == true,
            Underline = underlineBtn.IsChecked == true,
            CharSet = 134,
            LooseThreshold = true,
        };
        int cellW = ParseInt(widthBox.Text, ParseInt(heightBox.Text, 16));
        int cellH = font.PixelHeight;
        if (scaleBox.IsChecked == true) cellW = cellH;

        string text = (textBox.Text ?? "").Replace("\r", "").Replace("\n", "");
        var m = GdiFontRasterizer.RenderText(text, font, cellW, cellH);

        if (lockSizeBox.IsChecked == true)
        {
            int dw = ParseInt(dotWidthBox.Text, cellW);
            int dh = ParseInt(dotHeightBox.Text, cellH);
            m = m.Canvas(dw, dh, (dw - m.Width) / 2, (dh - m.Height) / 2);
        }

        _matrix = m;
        _loading = true;
        zoomSlider.Value = Math.Clamp(420 / Math.Max(1, m.Width), 1, 16);
        _loading = false;
        UpdateHelp();
        UpdatePreview();
        if (outputBox.Text.Length > 0) Generate();
    }

    private void UpdatePreview()
    {
        if (_matrix is null) return;
        int zoom = (int)Math.Max(1, zoomSlider.Value);
        previewImage.Source = BitmapRenderer.Render(_matrix, zoom, Colors.Black, Colors.White, true);
        orderImage.Source = BitmapRenderer.RenderScanOrder(_matrix, CurrentPattern, zoom);
        int bytes = DotMatrixEncoder.ByteCount(_matrix, CurrentPattern);
        sizeLabel.Text = $"{_matrix.Width} × {_matrix.Height}    共 {bytes} 字节";
    }

    private void UpdateHelp()
    {
        bool msb = MsbFirst;
        string first = msb ? "第一个点作为最高位" : "第一个点作为最低位";
        helpText.Text = CurrentPattern switch
        {
            DotMatrixEncoder.ScanPattern.Vertical =>
                $"逐列式：从第一列开始向下，每取 8 个点作为一个字节，如果最后不足 8 个点就补满 8 位。\r\n取模顺序是从{(msb ? "高" : "低")}到{(msb ? "低" : "高")}，即{first}。",
            DotMatrixEncoder.ScanPattern.Horizontal =>
                $"逐行式：从第一行开始向右，每取 8 个点作为一个字节，如果最后不足 8 个点就补满 8 位。\r\n取模顺序是从{(msb ? "高" : "低")}到{(msb ? "低" : "高")}，即{first}。",
            DotMatrixEncoder.ScanPattern.ColumnRow =>
                $"列行式：从第一列开始向下取 8 个点作为一个字节，然后从第二列开始向下取 8 个点作为第二个字节……依此类推。\r\n如果最后不足 8 个点就补满 8 位。取模顺序是从{(msb ? "高" : "低")}到{(msb ? "低" : "高")}，即{first}。",
            _ =>
                $"行列式：从第一行开始向右取 8 个点作为一个字节，然后从第二行开始向右取 8 个点作为第二个字节……依此类推。\r\n如果最后不足 8 个点就补满 8 位。取模顺序是从{(msb ? "高" : "低")}到{(msb ? "低" : "高")}，即{first}。",
        };
    }

    // ---------------------------------------------------------------- 生成

    private void Generate()
    {
        if (_matrix is null) return;
        var fmt = BuildFormat();
        var data = DotMatrixEncoder.Encode(_matrix, CurrentPattern, MsbFirst, InvertedYang);
        var sb = new StringBuilder();

        string text = (textBox.Text ?? "").Replace("\r", "").Replace("\n", "");
        if (commentBox.IsChecked == true)
        {
            string fontName = fontBox.SelectedItem as string ?? "宋体";
            string name = string.IsNullOrEmpty(text) ? "图形" : text;
            sb.Append(fmt.Comment($"{name}  {fontName} {_matrix.Width}x{_matrix.Height}"));
        }
        if (indexBox.IsChecked == true)
        {
            sb.Append(fmt.Comment("索引: 字符 起始字节偏移"));
            int offset = 0;
            foreach (char ch in text)
            {
                sb.Append(fmt.Comment($"{ch} ~({offset})"));
                offset += DotMatrixEncoder.ByteCount(_matrix, CurrentPattern) / Math.Max(1, text.Length);
            }
        }
        sb.Append(fmt.Format(data));
        outputBox.Text = sb.ToString();
        statusLabel.Text = $"{data.Length} 字节 / {CurrentPattern} / {(MsbFirst ? "顺向" : "逆向")} / {(InvertedYang ? "阳码" : "阴码")}";
    }

    /// <summary>供自动化截图与测试使用。</summary>
    public void SetText(string text)
    {
        textBox.Text = text ?? "";
    }

    // ---------------------------------------------------------------- 事件
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ReferenceEquals(sender, styleBox))
        {
            ApplyStylePreset();
            if (outputBox.Text.Length > 0) Generate();
            return;
        }
        if (ReferenceEquals(sender, fontBox) || ReferenceEquals(sender, widthBox) ||
            ReferenceEquals(sender, heightBox))
        {
            if (scaleBox.IsChecked == true && ReferenceEquals(sender, heightBox))
            {
                _loading = true;
                widthBox.Text = heightBox.Text;
                _loading = false;
            }
            Rebuild();
            return;
        }
        if (ReferenceEquals(sender, dotWidthBox) || ReferenceEquals(sender, dotHeightBox))
        {
            if (lockSizeBox.IsChecked == true) Rebuild();
            return;
        }
        if (_matrix is not null)
        {
            UpdateHelp();
            if (outputBox.Text.Length > 0) Generate();
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ReferenceEquals(sender, widthBox) || ReferenceEquals(sender, heightBox) ||
            ReferenceEquals(sender, dotWidthBox) || ReferenceEquals(sender, dotHeightBox))
            Rebuild();
    }

    private void OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ReferenceEquals(sender, scaleBox) || ReferenceEquals(sender, boldBtn) ||
            ReferenceEquals(sender, italicBtn) || ReferenceEquals(sender, underlineBtn) ||
            ReferenceEquals(sender, lockSizeBox))
        {
            Rebuild();
            return;
        }
        if (_matrix is not null)
        {
            UpdateHelp();
            UpdatePreview();
            if (outputBox.Text.Length > 0) Generate();
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (ReferenceEquals(sender, textBox))
        {
            Rebuild();
            return;
        }
        if (_matrix is not null && outputBox.Text.Length > 0) Generate();
    }

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _matrix is null) return;
        UpdatePreview();
    }

    private void OnRotateLeft(object sender, RoutedEventArgs e)
    {
        if (_matrix is null) return;
        _matrix = _matrix.RotateLeft();
        AfterManualTransform();
    }

    private void OnRotateRight(object sender, RoutedEventArgs e)
    {
        if (_matrix is null) return;
        _matrix = _matrix.RotateRight();
        AfterManualTransform();
    }

    private void OnFlipH(object sender, RoutedEventArgs e)
    {
        if (_matrix is null) return;
        _matrix = _matrix.FlipHorizontal();
        AfterManualTransform();
    }

    private void OnFlipV(object sender, RoutedEventArgs e)
    {
        if (_matrix is null) return;
        _matrix = _matrix.FlipVertical();
        AfterManualTransform();
    }

    private void OnTrim(object sender, RoutedEventArgs e)
    {
        if (_matrix is null) return;
        _matrix = _matrix.Trim();
        AfterManualTransform();
    }

    private void OnReset(object sender, RoutedEventArgs e) => Rebuild();

    private void AfterManualTransform()
    {
        UpdatePreview();
        if (outputBox.Text.Length > 0) Generate();
    }

    private void OnGenerate(object sender, RoutedEventArgs e) => Generate();

    /// <summary>打开字库生成对话框（批量字符 → 字模文本 / 二进制字库 + 索引）。</summary>
    private void OnLibrary(object sender, RoutedEventArgs e)
    {
        var dlg = new LibraryDialog(BuildOptions(), DefaultChars()) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private LibraryOptions BuildOptions()
    {
        int cw = ParseInt(widthBox.Text, 16);
        int ch = ParseInt(heightBox.Text, 16);
        if (scaleBox.IsChecked == true) cw = ch;
        var opt = new LibraryOptions
        {
            FontFamily = fontBox.SelectedItem as string ?? "宋体",
            CellWidth = cw,
            CellHeight = ch,
            Bold = boldBtn.IsChecked == true,
            Italic = italicBtn.IsChecked == true,
            Underline = underlineBtn.IsChecked == true,
            Pattern = CurrentPattern,
            MsbFirst = MsbFirst,
            Yang = InvertedYang,
            Format = BuildFormat(),
        };
        if (lockSizeBox.IsChecked == true)
        {
            opt.DotWidth = ParseInt(dotWidthBox.Text, cw);
            opt.DotHeight = ParseInt(dotHeightBox.Text, ch);
        }
        var probe = new BitMatrix(opt.DotWidth > 0 ? opt.DotWidth : cw, opt.DotHeight > 0 ? opt.DotHeight : ch);
        opt.BytesPerChar = DotMatrixEncoder.ByteCount(probe, CurrentPattern);
        return opt;
    }

    private static string DefaultChars()
    {
        var sb = new StringBuilder();
        for (char c = (char)0x20; c <= (char)0x7E; c++) sb.Append(c);
        sb.Append("\r\n");
        sb.Append("的一是在不了有和人这中大为上个国我以要他时来用们生到作地于出就分对成会可主发年动同工也能下过子说产种面而方后多定行学法所民得经十三之进着等部度家电力里如水化高自二理起小物现实加量都两体制机当使点从业本去把性好应开它合还因由其些然前外天政四日那社义事平形相全表间样与关各重新线内数正心反你明看原又么利比或但质气第向道命此变条只没结解问意建月公无系军很情者最立代想已通并提直题程展五果料象员革位入常文总次品式活设及管特件长求老头基资边流路级少图山统接知较将组见计别她手角期根论运农指几九区强放决西被干做必战先回则任取据处队南给色光门即保治北造百规热领七海口东导器压志世金增争济阶油思术极交受联什认六共权收证改清己美再采转更单风切打白教速花带安场身车例真务具万每目至达走积示议声报斗完类八离华名确才科张信马节话米整空元况今集温传土许步群广石记需段研界拉林律叫且究观越织装影算低持音众书布复容儿须际商非验连断深难近矿千周委素技备半办青省列习响约支般史感劳便团往酸历市克何除消构府称太准精值号率族维划选标写存候毛亲快效斯院查江型眼王按格养易置派层片始却专状育厂京识适属圆包火住调满县局照参红细引听该铁价严龙飞");
        return sb.ToString();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        outputBox.Text = "";
        statusLabel.Text = "";
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
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

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (outputBox.Text.Length == 0) Generate();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|C 源文件 (*.c)|*.c|所有文件 (*.*)|*.*",
            FileName = "字模数据.txt",
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
}
