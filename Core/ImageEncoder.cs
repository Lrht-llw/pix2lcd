using System;
using System.Collections.Generic;

namespace pix2lcd.Core;

/// <summary>多字节像素的字节顺序。</summary>
public enum ColorOrder
{
    /// <summary>R,G,B[,A]：内存低地址为红。</summary>
    Rgb,
    /// <summary>B,G,R[,A]：内存低地址为蓝（Windows DIB 顺序）。</summary>
    Bgr,
}

/// <summary>
/// 图片取模：把 <see cref="RgbaImage"/> 按目标色深与扫描方式编码成字节流。
/// </summary>
public static class ImageEncoder
{
    public const DotMatrixEncoder.ScanPattern HorizonScan = DotMatrixEncoder.ScanPattern.Horizontal;
    public const DotMatrixEncoder.ScanPattern VerticalScan = DotMatrixEncoder.ScanPattern.Vertical;
    public const DotMatrixEncoder.ScanPattern DataHorByteVer = DotMatrixEncoder.ScanPattern.RowColumn;
    public const DotMatrixEncoder.ScanPattern DataVerByteHor = DotMatrixEncoder.ScanPattern.ColumnRow;

    /// <summary>
    /// 生成按指定扫描方式排列的像素坐标序列。
    /// <paramref name="pixelsPerByte"/> = 8 / 每像素位数，小于 8bpp 时每行（列）要补齐到它的整数倍，
    /// 越界坐标用 (-1,-1) 表示，由 <see cref="Quantize"/> 当作背景。
    /// </summary>
    public static List<(int X, int Y)> ScanOrder(int width, int height, DotMatrixEncoder.ScanPattern pattern,
        int pixelsPerByte = 1)
    {
        pixelsPerByte = Math.Max(1, pixelsPerByte);
        var list = new List<(int, int)>(width * height + 16);
        int cb = DotMatrixEncoder.ColumnBlocks(width);
        int rb = DotMatrixEncoder.RowBlocks(height);
        switch (pattern)
        {
            case DotMatrixEncoder.ScanPattern.Horizontal:
                // 逐行：从左到右，从上到下；每行补齐到整字节
                {
                    int pad = (pixelsPerByte - width % pixelsPerByte) % pixelsPerByte;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++) list.Add((x, y));
                        for (int k = 0; k < pad; k++) list.Add((-1, -1));
                    }
                    break;
                }
            case DotMatrixEncoder.ScanPattern.Vertical:
                // 逐列：从上到下，从左到右；每列补齐到整字节
                {
                    int pad = (pixelsPerByte - height % pixelsPerByte) % pixelsPerByte;
                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++) list.Add((x, y));
                        for (int k = 0; k < pad; k++) list.Add((-1, -1));
                    }
                    break;
                }
            case DotMatrixEncoder.ScanPattern.RowColumn:
                // 行列式：先取第 0 列的 8 点，再第 1 列……（8 列一带，天然字节对齐）
                for (int bx = 0; bx < cb; bx++)
                    for (int y = 0; y < height; y++)
                        for (int i = 0; i < 8; i++)
                        {
                            int x = bx * 8 + i;
                            list.Add(x < width ? (x, y) : (-1, -1));
                        }
                break;
            case DotMatrixEncoder.ScanPattern.ColumnRow:
                // 列行式：先取第 0 行的 8 点，再第 1 行……（8 行一带，天然字节对齐）
                for (int by = 0; by < rb; by++)
                    for (int x = 0; x < width; x++)
                        for (int i = 0; i < 8; i++)
                        {
                            int y = by * 8 + i;
                            list.Add(y < height ? (x, y) : (-1, -1));
                        }
                break;
        }
        return list;
    }

    /// <summary>每个像素的位宽。</summary>
    public static int BitsPerPixel(ColorDepth depth) => depth switch
    {
        ColorDepth.Mono => 1,
        ColorDepth.Color4 => 2,
        ColorDepth.Color16 => 4,
        ColorDepth.Color256 => 8,
        ColorDepth.Rgb332 => 8,
        ColorDepth.Color4096 => 12,
        ColorDepth.Rgb565 => 16,
        ColorDepth.Rgb666 => 18,
        ColorDepth.Rgb888 => 24,
        ColorDepth.Argb8888 => 32,
        _ => 8,
    };

    /// <summary>把像素量化成目标色深下的数值（未打包）。越界坐标按背景（0）处理。</summary>
    public static int Quantize(RgbaImage img, int x, int y, ColorDepth depth, int monoThreshold, bool invertBits)
    {
        if ((uint)x >= (uint)img.Width || (uint)y >= (uint)img.Height) return 0;
        int i = (y * img.Width + x) * 4;
        byte b = img.Pixels[i], g = img.Pixels[i + 1], r = img.Pixels[i + 2];
        switch (depth)
        {
            case ColorDepth.Mono:
                {
                    int lum = (r * 299 + g * 587 + b * 114) / 1000;
                    bool lit = lum >= monoThreshold;
                    if (invertBits) lit = !lit;
                    return lit ? 1 : 0;
                }
            case ColorDepth.Color4:
                {
                    int v = Grayscale(r, g, b) * 4 / 256;
                    return invertBits ? 3 - v : v;
                }
            case ColorDepth.Color16:
                {
                    int v = Grayscale(r, g, b) * 16 / 256;
                    return invertBits ? 15 - v : v;
                }
            case ColorDepth.Color256:
                {
                    int v = Grayscale(r, g, b);
                    return invertBits ? 255 - v : v;
                }
            case ColorDepth.Rgb332:
                return ((r >> 5) << 5) | ((g >> 5) << 2) | (b >> 6);
            case ColorDepth.Color4096:
                return ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            case ColorDepth.Rgb565:
                return ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
            case ColorDepth.Rgb666:
                return ((r >> 2) << 12) | ((g >> 2) << 6) | (b >> 2);
            case ColorDepth.Rgb888:
                return (r << 16) | (g << 8) | b;
            case ColorDepth.Argb8888:
                return (255 << 24) | (r << 16) | (g << 8) | b;
            default:
                return 0;
        }
    }

    private static int Grayscale(int r, int g, int b) => (r * 299 + g * 587 + b * 114) / 1000;

    /// <summary>把量化结果还原成 RGB，用于界面预览（不经过字节打包）。</summary>
    public static RgbaImage PreviewQuantized(RgbaImage img, ColorDepth depth, int monoThreshold)
    {
        var dst = new byte[img.Pixels.Length];
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                int v = Quantize(img, x, y, depth, monoThreshold, false);
                byte r, g, b;
                switch (depth)
                {
                    case ColorDepth.Mono:
                        r = g = b = v != 0 ? (byte)0 : (byte)255;
                        break;
                    case ColorDepth.Color4:
                        r = g = b = (byte)(v * 255 / 3);
                        break;
                    case ColorDepth.Color16:
                        r = g = b = (byte)(v * 255 / 15);
                        break;
                    case ColorDepth.Color256:
                        r = g = b = (byte)v;
                        break;
                    case ColorDepth.Rgb332:
                        r = (byte)((v >> 5) * 255 / 7);
                        g = (byte)(((v >> 2) & 7) * 255 / 7);
                        b = (byte)((v & 3) * 255 / 3);
                        break;
                    case ColorDepth.Color4096:
                        r = (byte)(((v >> 8) & 0xF) * 17);
                        g = (byte)(((v >> 4) & 0xF) * 17);
                        b = (byte)((v & 0xF) * 17);
                        break;
                    case ColorDepth.Rgb565:
                        r = (byte)(((v >> 11) & 0x1F) * 255 / 31);
                        g = (byte)(((v >> 5) & 0x3F) * 255 / 63);
                        b = (byte)((v & 0x1F) * 255 / 31);
                        break;
                    case ColorDepth.Rgb666:
                        r = (byte)(((v >> 12) & 0x3F) * 255 / 63);
                        g = (byte)(((v >> 6) & 0x3F) * 255 / 63);
                        b = (byte)((v & 0x3F) * 255 / 63);
                        break;
                    default:
                        r = (byte)((v >> 16) & 0xFF);
                        g = (byte)((v >> 8) & 0xFF);
                        b = (byte)(v & 0xFF);
                        break;
                }
                int o = (y * img.Width + x) * 4;
                dst[o] = b;
                dst[o + 1] = g;
                dst[o + 2] = r;
                dst[o + 3] = 255;
            }
        }
        return new RgbaImage(img.Width, img.Height, dst);
    }

    /// <summary>
    /// 编码成字节流。
    /// </summary>
    /// <param name="img">源像素。</param>
    /// <param name="depth">目标色深。</param>
    /// <param name="pattern">扫描方式。</param>
    /// <param name="msbFirst">字节内/字节间的位序：true 时第一个像素放在最高位。</param>
    /// <param name="invertBits">位取反（单色=亮点为 1；灰度=反色）。</param>
    /// <param name="colorOrder">多字节像素的通道顺序。</param>
    /// <param name="monoThreshold">单色阈值 0..255。</param>
    public static byte[] Encode(RgbaImage img, ColorDepth depth, DotMatrixEncoder.ScanPattern pattern,
        bool msbFirst, bool invertBits, ColorOrder colorOrder, int monoThreshold)
    {
        int bppPre = BitsPerPixel(depth);
        int pixelsPerByte = bppPre <= 8 ? 8 / bppPre : 1;
        var order = ScanOrder(img.Width, img.Height, pattern, pixelsPerByte);
        int bpp = BitsPerPixel(depth);
        var outp = new List<byte>(order.Count * bpp / 8 + 8);

        if (bpp <= 8)
        {
            int acc = 0, accBits = 0;
            foreach (var (x, y) in order)
            {
                int v = Quantize(img, x, y, depth, monoThreshold, invertBits);
                v &= (1 << bpp) - 1;
                if (msbFirst)
                    acc = (acc << bpp) | v;
                else
                    acc |= v << accBits;
                accBits += bpp;
                if (accBits == 8)
                {
                    outp.Add((byte)acc);
                    acc = 0;
                    accBits = 0;
                }
            }
            if (accBits > 0)
            {
                if (msbFirst) acc <<= 8 - accBits;
                outp.Add((byte)acc);
            }
            return outp.ToArray();
        }

        int bytesPerPixel = bpp / 8;
        if (bpp == 12)
        {
            // 12 位：像素两两拼成 3 字节
            for (int k = 0; k < order.Count; k += 2)
            {
                var (x1, y1) = order[k];
                int v1 = Quantize(img, x1, y1, depth, monoThreshold, invertBits);
                int v2 = 0;
                if (k + 1 < order.Count)
                {
                    var (x2, y2) = order[k + 1];
                    v2 = Quantize(img, x2, y2, depth, monoThreshold, invertBits);
                }
                if (msbFirst)
                {
                    outp.Add((byte)(v1 >> 4));
                    outp.Add((byte)(((v1 & 0xF) << 4) | (v2 >> 8)));
                    outp.Add((byte)(v2 & 0xFF));
                }
                else
                {
                    outp.Add((byte)(v1 & 0xFF));
                    outp.Add((byte)(((v1 >> 8) << 4) | ((v2 & 0xF0) >> 4)));
                    outp.Add((byte)(v2 >> 4));
                }
            }
            return outp.ToArray();
        }

        foreach (var (x, y) in order)
        {
            int v = Quantize(img, x, y, depth, monoThreshold, invertBits);
            int r = (v >> 16) & 0xFF, g = (v >> 8) & 0xFF, b = v & 0xFF, a = (v >> 24) & 0xFF;
            switch (bytesPerPixel)
            {
                case 2:
                    if (msbFirst) { outp.Add((byte)(v >> 8)); outp.Add((byte)v); }
                    else { outp.Add((byte)v); outp.Add((byte)(v >> 8)); }
                    break;
                case 3:
                    if (colorOrder == ColorOrder.Rgb) { outp.Add((byte)r); outp.Add((byte)g); outp.Add((byte)b); }
                    else { outp.Add((byte)b); outp.Add((byte)g); outp.Add((byte)r); }
                    break;
                case 4:
                    if (colorOrder == ColorOrder.Rgb) { outp.Add((byte)r); outp.Add((byte)g); outp.Add((byte)b); outp.Add((byte)a); }
                    else { outp.Add((byte)b); outp.Add((byte)g); outp.Add((byte)r); outp.Add((byte)a); }
                    break;
            }
        }
        return outp.ToArray();
    }
}
