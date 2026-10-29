using System;

namespace pix2lcd.Core;

/// <summary>
/// 文字/图形点阵的取模（位打包）器。
/// </summary>
public static class DotMatrixEncoder
{
    /// <summary>
    /// 取模方式：逐列式 / 逐行式 / 列行式 / 行列式。
    /// </summary>
    public enum ScanPattern
    {
        /// <summary>逐列式：从第一列开始向下，每 8 个点成一个字节，一列取完接下一列。</summary>
        Vertical = 0,

        /// <summary>逐行式：从第一行开始向右，每 8 个点成一个字节，一行取完接下一行。</summary>
        Horizontal = 1,

        /// <summary>列行式：先取第 1 列的 8 点，再第 2 列的 8 点……；全部列取完后再取下一 8 行带。</summary>
        ColumnRow = 2,

        /// <summary>行列式：先取第 1 行的 8 点，再第 2 行的 8 点……；全部行取完后再取下一 8 列带。</summary>
        RowColumn = 3,
    }

    public static int ColumnBlocks(int width) => (width + 7) / 8;
    public static int RowBlocks(int height) => (height + 7) / 8;

    /// <summary>按扫描方式计算输出字节数。</summary>
    public static int ByteCount(BitMatrix m, ScanPattern pattern) => pattern switch
    {
        ScanPattern.Vertical => m.Width * RowBlocks(m.Height),
        ScanPattern.ColumnRow => RowBlocks(m.Height) * m.Width,
        ScanPattern.Horizontal => m.Height * ColumnBlocks(m.Width),
        ScanPattern.RowColumn => ColumnBlocks(m.Width) * m.Height,
        _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
    };

    /// <summary>
    /// 取模。
    /// </summary>
    /// <param name="m">二值点阵，true 表示笔画（前景）。</param>
    /// <param name="pattern">取模方式。</param>
    /// <param name="msbFirst">
    /// true = 顺向/高位在前（第一个点放在 bit7）；false = 逆向/低位在前（第一个点放在 bit0）。
    /// 界面中该选项名为“取模走向”。
    /// </param>
    /// <param name="inverted">
    /// true = 阳码（亮点为 0，背景为 1）；false = 阴码（亮点为 1，背景为 0）。
    /// 越界补齐的点按“背景”处理：阴码补 0、阳码补 1。
    /// </param>
    public static byte[] Encode(BitMatrix m, ScanPattern pattern, bool msbFirst, bool inverted)
    {
        int w = m.Width, h = m.Height;
        int cb = ColumnBlocks(w), rb = RowBlocks(h);
        var result = new byte[ByteCount(m, pattern)];
        int n = 0;

        // 取一个字节：沿着“字节内点的走向”读取 bitCount 个点。
        byte PackVertical(int x, int blockRow)
        {
            int v = 0;
            for (int i = 0; i < 8; i++)
            {
                int y = blockRow * 8 + i;
                bool fg = y < h && m[x, y];
                bool on = inverted ? !fg : fg;
                if (on) v |= msbFirst ? 1 << (7 - i) : 1 << i;
            }
            return (byte)v;
        }

        byte PackHorizontal(int y, int blockCol)
        {
            int v = 0;
            for (int i = 0; i < 8; i++)
            {
                int x = blockCol * 8 + i;
                bool fg = x < w && m[x, y];
                bool on = inverted ? !fg : fg;
                if (on) v |= msbFirst ? 1 << (7 - i) : 1 << i;
            }
            return (byte)v;
        }

        switch (pattern)
        {
            case ScanPattern.Vertical:
                // 外层：列 x；内层：行块。每列自上而下连续取 8 点。
                for (int x = 0; x < w; x++)
                    for (int by = 0; by < rb; by++)
                        result[n++] = PackVertical(x, by);
                break;

            case ScanPattern.ColumnRow:
                // 外层：行块；内层：列。先取完所有列的同一 8 行带。
                for (int by = 0; by < rb; by++)
                    for (int x = 0; x < w; x++)
                        result[n++] = PackVertical(x, by);
                break;

            case ScanPattern.Horizontal:
                // 外层：行 y；内层：列块。每行自左向右连续取 8 点。
                for (int y = 0; y < h; y++)
                    for (int bx = 0; bx < cb; bx++)
                        result[n++] = PackHorizontal(y, bx);
                break;

            case ScanPattern.RowColumn:
                // 外层：列块；内层：行。先取完所有行的同一 8 列带。
                for (int bx = 0; bx < cb; bx++)
                    for (int y = 0; y < h; y++)
                        result[n++] = PackHorizontal(y, bx);
                break;
        }

        return result;
    }
}
