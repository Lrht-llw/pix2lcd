using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using pix2lcd.Core;

namespace pix2lcd.Views;

/// <summary>字库生成对话框：把一批字符按当前取模设置批量生成字模文本 / 二进制字库 + 索引文件。</summary>
public sealed class LibraryDialog : Window
{
    private readonly TextBox _chars;
    private readonly CheckBox _filterDup;
    private readonly CheckBox _filterSpace;
    private readonly RadioButton _textMode;
    private readonly RadioButton _binMode;
    private readonly TextBox _pathBox;
    private readonly TextBlock _status;
    private readonly TextBlock _info;
    private readonly LibraryOptions _options;

    public LibraryDialog(LibraryOptions options, string defaultChars)
    {
        _options = options;
        Title = "生成字库";
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8));

        var root = new Grid { Margin = new Thickness(10) };
        for (int i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        _info = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(_info, 0);
        root.Children.Add(_info);

        var charLabel = new TextBlock { Text = "字符集（每行一个或直接连写，可粘贴）:", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(charLabel, 1);
        root.Children.Add(charLabel);

        _chars = new TextBox
        {
            Text = defaultChars,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, 宋体"),
            Height = 220,
        };
        Grid.SetRow(_chars, 2);
        root.Children.Add(_chars);

        var opts = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        _filterDup = new CheckBox { Content = "过滤重复字符", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        _filterSpace = new CheckBox { Content = "过滤空格/控制符", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        _textMode = new RadioButton { Content = "文本字模（C 数组）", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        _binMode = new RadioButton { Content = "二进制字库（.fon）", VerticalAlignment = VerticalAlignment.Center };
        opts.Children.Add(_filterDup);
        opts.Children.Add(_filterSpace);
        opts.Children.Add(_textMode);
        opts.Children.Add(_binMode);
        Grid.SetRow(opts, 3);
        root.Children.Add(opts);

        var mid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pathLabel = new TextBlock { Text = "输出文件:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        Grid.SetColumn(pathLabel, 0);
        mid.Children.Add(pathLabel);
        _pathBox = new TextBox { Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_pathBox, 1);
        mid.Children.Add(_pathBox);
        var browse = new Button { Content = "浏览…", Width = 70, Height = 26, Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        Grid.SetColumn(browse, 2);
        mid.Children.Add(browse);
        Grid.SetRow(mid, 4);
        root.Children.Add(mid);

        _status = new TextBlock { Text = "就绪", Margin = new Thickness(0, 4, 0, 4), Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x33)) };
        Grid.SetRow(_status, 5);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "开始生成", Width = 110, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DoGenerate();
        var cancel = new Button { Content = "取消", Width = 80, Height = 30 };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 6);
        root.Children.Add(buttons);

        UpdateInfo();
    }

    private void UpdateInfo()
    {
        _info.Text = $"字体 {_options.FontFamily}  {_options.CellWidth}×{_options.CellHeight}  " +
                     $"取模 {FontLibrary.PatternName(_options.Pattern)}  {(_options.MsbFirst ? "顺向" : "逆向")}  {(_options.Yang ? "阳码" : "阴码")}  " +
                     $"每字 {_options.BytesPerChar} 字节";
    }

    private static string PatternName(DotMatrixEncoder.ScanPattern p) => p switch
    {
        DotMatrixEncoder.ScanPattern.Horizontal => "逐行式",
        DotMatrixEncoder.ScanPattern.ColumnRow => "列行式",
        DotMatrixEncoder.ScanPattern.RowColumn => "行列式",
        _ => "逐列式",
    };

    private void Browse()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = _binMode.IsChecked == true
                ? "二进制字库 (*.fon)|*.fon|所有文件 (*.*)|*.*"
                : "C 源文件 (*.c)|*.c|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = _binMode.IsChecked == true ? "font.fon" : "font.c",
        };
        if (dlg.ShowDialog() == true) _pathBox.Text = dlg.FileName;
    }

    private void DoGenerate()
    {
        try
        {
            var chars = CollectChars();
            if (chars.Count == 0)
            {
                _status.Text = "没有有效字符。";
                return;
            }
            string path = _pathBox.Text?.Trim() ?? "";
            if (path.Length == 0)
            {
                _status.Text = "请先选择输出文件。";
                return;
            }

            var font = new GdiFontRasterizer.FontSpec
            {
                Family = _options.FontFamily,
                PixelHeight = _options.CellHeight,
                Bold = _options.Bold,
                Italic = _options.Italic,
                Underline = _options.Underline,
                CharSet = 134,
                LooseThreshold = true,
            };

            // 字库要求定长记录：每个字符都渲染到同样的 cellWidth × cellHeight 格子（不按半宽处理）
            var entries = FontLibrary.Build(chars, font, _options.CellWidth, _options.CellHeight,
                _options.Pattern, _options.MsbFirst, _options.Yang, _options.DotWidth, _options.DotHeight);
            int perChar = entries.Count > 0 ? entries[0].Data.Length : 0;

            if (_binMode.IsChecked == true)
            {
                FontLibrary.WriteBinary(path, entries);
                string idxPath = Path.ChangeExtension(path, ".txt");
                FontLibrary.WriteIndex(idxPath, entries);
                _status.Text = $"已生成二进制字库 {path}（{entries.Count} 字 × {perChar} 字节 = {entries.Count * perChar} 字节），索引 {idxPath}";
            }
            else
            {
                string src = FontLibrary.BuildCSource(entries, font, _options.CellWidth, _options.CellHeight,
                    _options.Pattern, _options.MsbFirst, _options.Yang, _options.Format);
                File.WriteAllText(path, src, OutputEncoding.Text);
                string idxPath = Path.ChangeExtension(path, ".idx.txt");
                FontLibrary.WriteIndex(idxPath, entries);
                _status.Text = $"已生成文本字模 {path}（{entries.Count} 字 × {perChar} 字节），索引 {idxPath}";
            }
        }
        catch (Exception ex)
        {
            _status.Text = "生成失败：" + ex.Message;
        }
    }

    private List<char> CollectChars()
    {
        var text = _chars.Text ?? "";
        var seen = new HashSet<char>();
        var list = new List<char>();
        foreach (char ch in text)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t') continue;
            if (_filterSpace.IsChecked == true && (ch < 0x20)) continue;
            if (_filterDup.IsChecked == true && !seen.Add(ch)) continue;
            list.Add(ch);
        }
        return list;
    }
}

/// <summary>字库生成所需的当前设置快照。</summary>
public sealed class LibraryOptions
{
    public string FontFamily = "宋体";
    public int CellWidth = 16;
    public int CellHeight = 16;
    public int DotWidth;
    public int DotHeight;
    public bool Bold;
    public bool Italic;
    public bool Underline;
    public DotMatrixEncoder.ScanPattern Pattern = DotMatrixEncoder.ScanPattern.Vertical;
    public bool MsbFirst = true;
    public bool Yang;
    public OutputFormat Format = OutputFormat.Preset(OutputStyle.C51);
    public int BytesPerChar;
}
