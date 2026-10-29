using System;
using System.Text;

namespace pix2lcd.Core;

/// <summary>输出格式预设。</summary>
public enum OutputStyle
{
    C51,
    A51,
    HexPlain,
    BinStyle,
    Custom,
}

/// <summary>
/// 字模数据的文本格式，字段划分：
/// 段前缀/后缀、注释前缀/后缀、数据前缀/后缀、行前缀/后缀/行尾缀。
/// </summary>
public sealed class OutputFormat
{
    public bool Hex = true;
    public int HexDigits = 2;
    public int PerLine = 16;

    public string SegmentPrefix = "";
    public string SegmentSuffix = "";

    public string DataPrefix = "0x";
    public string DataSuffix = ",";

    public string RowPrefix = "";
    public string RowSuffix = "";
    public string RowTerminator = "\r\n";

    public string CommentPrefix = "/*\"";
    public string CommentSuffix = "\",";

    /// <summary>是否用位操作化简连续 0x00 / 0xFF（“紧凑格式”只影响换行，这里不复刻）。</summary>
    public bool Compact;

    public string FormatNumber(int value)
    {
        if (Hex) return value.ToString("X" + Math.Max(1, HexDigits));
        return value.ToString();
    }

    public string FormatByte(int value) => DataPrefix + FormatNumber(value) + DataSuffix;

    /// <summary>把字节数组排成多行文本。</summary>
    public string Format(byte[] data)
    {
        var sb = new StringBuilder();
        if (SegmentPrefix.Length > 0) sb.Append(SegmentPrefix);
        int perLine = Math.Max(1, PerLine);
        for (int i = 0; i < data.Length; i++)
        {
            if (i % perLine == 0) sb.Append(RowPrefix);
            sb.Append(FormatByte(data[i]));
            if (i % perLine == perLine - 1 || i == data.Length - 1)
            {
                sb.Append(RowSuffix);
                sb.Append(RowTerminator);
            }
        }
        if (SegmentSuffix.Length > 0) sb.Append(SegmentSuffix);
        return sb.ToString();
    }

    /// <summary>一行注释文本。</summary>
    public string Comment(string text)
        => CommentPrefix + text.Replace("\r", " ").Replace("\n", " ") + CommentSuffix + RowTerminator;

    public static OutputFormat Preset(OutputStyle style) => style switch
    {
        OutputStyle.C51 => new OutputFormat
        {
            Hex = true,
            HexDigits = 2,
            PerLine = 16,
            DataPrefix = "0x",
            DataSuffix = ",",
            RowPrefix = "",
            RowSuffix = "",
            RowTerminator = "\r\n",
            CommentPrefix = "/*\"",
            CommentSuffix = "\",",
        },
        OutputStyle.A51 => new OutputFormat
        {
            Hex = true,
            HexDigits = 2,
            PerLine = 16,
            DataPrefix = "",
            DataSuffix = "H ",
            RowPrefix = "DB ",
            RowSuffix = ";",
            RowTerminator = "\r\n",
            CommentPrefix = "\"",
            CommentSuffix = "\",",
        },
        OutputStyle.HexPlain => new OutputFormat
        {
            Hex = true,
            HexDigits = 2,
            PerLine = 16,
            DataPrefix = "",
            DataSuffix = " ",
            RowPrefix = "",
            RowSuffix = "",
            RowTerminator = "\r\n",
            CommentPrefix = "// ",
            CommentSuffix = "",
        },
        OutputStyle.BinStyle => new OutputFormat
        {
            Hex = true,
            HexDigits = 2,
            PerLine = 16,
            DataPrefix = "",
            DataSuffix = "",
            RowPrefix = "",
            RowSuffix = "",
            RowTerminator = "\r\n",
            CommentPrefix = "# ",
            CommentSuffix = "",
        },
        _ => new OutputFormat(),
    };
}
