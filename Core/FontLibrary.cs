using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pix2lcd.Core;

/// <summary>
/// 字库生成：把一批字符按统一点阵尺寸取模，输出文本字模（C 数组）、二进制字库和索引文件。
/// 纯逻辑与界面分离，便于自检。
/// </summary>
public static class FontLibrary
{
    public sealed class Entry
    {
        public char Char;
        public byte[] Data = Array.Empty<byte>();
    }

    /// <summary>统一渲染并取模。每个字符都占用 cellWidth × cellHeight 的等宽格子。</summary>
    public static List<Entry> Build(IEnumerable<char> chars, GdiFontRasterizer.FontSpec font,
        int cellWidth, int cellHeight, DotMatrixEncoder.ScanPattern pattern, bool msbFirst, bool yang,
        int dotWidth = 0, int dotHeight = 0)
    {
        var list = new List<Entry>();
        foreach (char ch in chars)
        {
            var m = GdiFontRasterizer.RenderChar(ch, font, cellWidth, cellHeight);
            if (dotWidth > 0 && dotHeight > 0)
                m = m.Canvas(dotWidth, dotHeight, (dotWidth - m.Width) / 2, (dotHeight - m.Height) / 2);
            list.Add(new Entry
            {
                Char = ch,
                Data = DotMatrixEncoder.Encode(m, pattern, msbFirst, yang),
            });
        }
        return list;
    }

    /// <summary>二进制字库：定长记录按字符顺序拼接，偏移 = 序号 × 每字字节数。</summary>
    public static int WriteBinary(string path, IReadOnlyList<Entry> entries)
    {
        using var fs = File.Create(path);
        foreach (var e in entries) fs.Write(e.Data, 0, e.Data.Length);
        return entries.Count;
    }

    /// <summary>索引文件：<c>&lt;字符&gt; ~(&lt;字节偏移&gt;)</c>。</summary>
    public static void WriteIndex(string path, IReadOnlyList<Entry> entries)
    {
        var sb = new StringBuilder();
        int offset = 0;
        foreach (var e in entries)
        {
            sb.Append(e.Char).Append(" ~(").Append(offset).Append(")\r\n");
            offset += e.Data.Length;
        }
        File.WriteAllText(path, sb.ToString(), OutputEncoding.Text);
    }

    /// <summary>文本字模：每个字符一个 <c>const unsigned char</c> 数组。</summary>
    public static string BuildCSource(IReadOnlyList<Entry> entries, GdiFontRasterizer.FontSpec font,
        int cellWidth, int cellHeight, DotMatrixEncoder.ScanPattern pattern, bool msbFirst, bool yang,
        OutputFormat format)
    {
        var sb = new StringBuilder();
        sb.Append("/*\r\n * 由 pix2lcd 生成的字模库\r\n");
        sb.Append($" * 字体: {font.Family}  {cellWidth}x{cellHeight}  ");
        sb.Append($"取模: {PatternName(pattern)}  {(msbFirst ? "顺向" : "逆向")}  {(yang ? "阳码" : "阴码")}\r\n");
        sb.Append($" * 字符数: {entries.Count}\r\n */\r\n\r\n");
        foreach (var e in entries)
        {
            sb.Append($"/* \"{e.Char}\"  0x{(int)e.Char:X4} */\r\n");
            sb.Append($"const unsigned char {SymbolName(e.Char)}[{e.Data.Length}] = {{\r\n");
            sb.Append(format.Format(e.Data));
            sb.Append("};\r\n\r\n");
        }
        return sb.ToString();
    }

    public static string SymbolName(char ch)
    {
        if (ch == ' ') return "ch_space";
        if ((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
            return "ch_" + ch;
        return "ch_" + ((int)ch).ToString("X4");
    }

    public static string PatternName(DotMatrixEncoder.ScanPattern p) => p switch
    {
        DotMatrixEncoder.ScanPattern.Horizontal => "逐行式",
        DotMatrixEncoder.ScanPattern.ColumnRow => "列行式",
        DotMatrixEncoder.ScanPattern.RowColumn => "行列式",
        _ => "逐列式",
    };
}
