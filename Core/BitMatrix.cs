using System;
using System.Collections.Generic;

namespace pix2lcd.Core;

/// <summary>
/// 二值点阵。约定 <c>true</c> 为“笔画/亮点”（前景），<c>false</c> 为背景。
/// 坐标原点在左上角，X 向右、Y 向下，与 GDI / 位图一致。
/// </summary>
public sealed class BitMatrix
{
    private bool[] _bits;

    public BitMatrix(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        _bits = new bool[width * height];
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    public bool this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
            return _bits[y * Width + x];
        }
        set
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            _bits[y * Width + x] = value;
        }
    }

    public bool[] Raw => _bits;

    public int LitCount
    {
        get
        {
            int n = 0;
            foreach (var b in _bits) if (b) n++;
            return n;
        }
    }

    public bool IsEmpty => LitCount == 0;

    public BitMatrix Clone()
    {
        var m = new BitMatrix(Width, Height);
        Array.Copy(_bits, m._bits, _bits.Length);
        return m;
    }

    public void Clear() => Array.Clear(_bits);

    /// <summary>左右翻转。</summary>
    public BitMatrix FlipHorizontal()
    {
        var m = new BitMatrix(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                m[Width - 1 - x, y] = this[x, y];
        return m;
    }

    /// <summary>上下翻转。</summary>
    public BitMatrix FlipVertical()
    {
        var m = new BitMatrix(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                m[x, Height - 1 - y] = this[x, y];
        return m;
    }

    /// <summary>逆时针旋转 90°（结果宽度 = 原高度）。</summary>
    public BitMatrix RotateLeft()
    {
        var m = new BitMatrix(Height, Width);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                m[y, Width - 1 - x] = this[x, y];
        return m;
    }

    /// <summary>顺时针旋转 90°。</summary>
    public BitMatrix RotateRight()
    {
        var m = new BitMatrix(Height, Width);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                m[Height - 1 - y, x] = this[x, y];
        return m;
    }

    /// <summary>最近邻缩放。</summary>
    public BitMatrix Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var m = new BitMatrix(width, height);
        for (int y = 0; y < height; y++)
        {
            int sy = (int)((long)y * Height / height);
            for (int x = 0; x < width; x++)
            {
                int sx = (int)((long)x * Width / width);
                m[x, y] = this[sx, sy];
            }
        }
        return m;
    }

    /// <summary>左上角对齐补边/裁剪到指定尺寸（用于“空白行列”调整）。</summary>
    public BitMatrix Canvas(int width, int height, int offsetX, int offsetY)
    {
        var m = new BitMatrix(width, height);
        for (int y = 0; y < Height; y++)
        {
            int dy = y + offsetY;
            if (dy < 0 || dy >= height) continue;
            for (int x = 0; x < Width; x++)
            {
                int dx = x + offsetX;
                if (dx < 0 || dx >= width) continue;
                m[dx, dy] = this[x, y];
            }
        }
        return m;
    }

    /// <summary>裁掉四周的空白（返回内容外接矩形）。图像全空时返回 1x1 空点阵。</summary>
    public BitMatrix Trim()
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (this[x, y])
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return new BitMatrix(1, 1);
        var m = new BitMatrix(maxX - minX + 1, maxY - minY + 1);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                m[x - minX, y - minY] = this[x, y];
        return m;
    }

    /// <summary>把若干点阵按给定的水平偏移量排成一行。</summary>
    public static BitMatrix ConcatHorizontal(IReadOnlyList<BitMatrix> items, int spacing, int height, int offsetY)
    {
        int total = 0;
        foreach (var it in items) total += it.Width + spacing;
        if (total <= 0) total = 1;
        var m = new BitMatrix(total, height);
        int cx = 0;
        foreach (var it in items)
        {
            for (int y = 0; y < it.Height && y + offsetY < height; y++)
            {
                int dy = y + offsetY;
                if (dy < 0) continue;
                for (int x = 0; x < it.Width && cx + x < m.Width; x++)
                    m[cx + x, dy] = it[x, y];
            }
            cx += it.Width + spacing;
        }
        return m;
    }

    /// <summary>
    /// 取模的逆过程：把字节流按指定的取模方式还原成位图，用于“取模顺序”预览与自检。
    /// </summary>
    public static BitMatrix FromByteStream(byte[] data, int width, int height, DotMatrixEncoder.ScanPattern pattern,
        bool msbFirst, bool inverted)
    {
        var m = new BitMatrix(width, height);
        int cb = DotMatrixEncoder.ColumnBlocks(width);
        int rb = DotMatrixEncoder.RowBlocks(height);
        int idx = 0;

        bool Bit(byte v, int i)
        {
            int bit = msbFirst ? 7 - i : i;
            bool on = ((v >> bit) & 1) != 0;
            return inverted ? !on : on;
        }

        for (int outer = 0; outer < (pattern is DotMatrixEncoder.ScanPattern.Vertical ? width
                                      : pattern is DotMatrixEncoder.ScanPattern.ColumnRow ? rb
                                      : pattern is DotMatrixEncoder.ScanPattern.Horizontal ? height : cb); outer++)
        {
            for (int inner = 0; inner < (pattern is DotMatrixEncoder.ScanPattern.Vertical ? rb
                                          : pattern is DotMatrixEncoder.ScanPattern.ColumnRow ? width
                                          : pattern is DotMatrixEncoder.ScanPattern.Horizontal ? cb : height); inner++)
            {
                if (idx >= data.Length) return m;
                byte v = data[idx++];
                bool vertical = pattern is DotMatrixEncoder.ScanPattern.Vertical
                                or DotMatrixEncoder.ScanPattern.ColumnRow;
                int x = vertical
                    ? (pattern == DotMatrixEncoder.ScanPattern.Vertical ? outer : inner)
                    : (pattern == DotMatrixEncoder.ScanPattern.RowColumn ? outer : inner) * 8;
                int y = vertical
                    ? (pattern == DotMatrixEncoder.ScanPattern.Vertical ? inner : outer) * 8
                    : (pattern == DotMatrixEncoder.ScanPattern.Horizontal ? outer : inner);
                for (int i = 0; i < 8; i++)
                {
                    bool on = Bit(v, i);
                    if (vertical) m[x, y + i] = on;
                    else m[x + i, y] = on;
                }
            }
        }
        return m;
    }
}
