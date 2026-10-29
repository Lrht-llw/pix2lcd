using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace pix2lcd.Core;

/// <summary>把点阵/像素图渲染成 WPF 位图，供预览使用。</summary>
public static class BitmapRenderer
{
    /// <summary>点阵预览：每个点放大为 scale×scale 像素，可选网格线。</summary>
    public static BitmapSource Render(BitMatrix m, int scale, Color foreground, Color background, bool grid = false)
    {
        scale = Math.Max(1, scale);
        int w = m.Width * scale, h = m.Height * scale;
        var buf = new byte[w * h * 4];
        byte fb = foreground.B, fg = foreground.G, fr = foreground.R;
        byte bb = background.B, bg = background.G, br = background.R;
        for (int y = 0; y < h; y++)
        {
            int sy = y / scale;
            for (int x = 0; x < w; x++)
            {
                int sx = x / scale;
                bool on = m[sx, sy];
                int o = (y * w + x) * 4;
                buf[o + 3] = 255;
                if (on)
                {
                    buf[o] = fb;
                    buf[o + 1] = fg;
                    buf[o + 2] = fr;
                }
                else
                {
                    buf[o] = bb;
                    buf[o + 1] = bg;
                    buf[o + 2] = br;
                }
                if (grid && !on && (x % scale == 0 || y % scale == 0))
                {
                    buf[o] = (byte)(bb * 0.85);
                    buf[o + 1] = (byte)(bg * 0.85);
                    buf[o + 2] = (byte)(br * 0.85);
                }
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// 取模走线示意：用序号渐变着色，直观展示“哪个点先被取到”。
    /// </summary>
    public static BitmapSource RenderScanOrder(BitMatrix m, DotMatrixEncoder.ScanPattern pattern, int scale)
    {
        scale = Math.Max(1, scale);
        int w = m.Width * scale, h = m.Height * scale;
        var order = new int[m.Width * m.Height];
        Array.Fill(order, -1);
        int n = 0;
        switch (pattern)
        {
            case DotMatrixEncoder.ScanPattern.Vertical:
                for (int x = 0; x < m.Width; x++)
                    for (int y = 0; y < m.Height; y++)
                        order[y * m.Width + x] = n++;
                break;
            case DotMatrixEncoder.ScanPattern.Horizontal:
                for (int y = 0; y < m.Height; y++)
                    for (int x = 0; x < m.Width; x++)
                        order[y * m.Width + x] = n++;
                break;
            case DotMatrixEncoder.ScanPattern.ColumnRow:
                for (int by = 0; by * 8 < m.Height; by++)
                    for (int x = 0; x < m.Width; x++)
                        for (int i = 0; i < 8 && by * 8 + i < m.Height; i++)
                            order[(by * 8 + i) * m.Width + x] = n++;
                break;
            case DotMatrixEncoder.ScanPattern.RowColumn:
                for (int bx = 0; bx * 8 < m.Width; bx++)
                    for (int y = 0; y < m.Height; y++)
                        for (int i = 0; i < 8 && bx * 8 + i < m.Width; i++)
                            order[y * m.Width + bx * 8 + i] = n++;
                break;
        }
        int total = Math.Max(1, n);
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int sy = y / scale;
            for (int x = 0; x < w; x++)
            {
                int sx = x / scale;
                int k = order[sy * m.Width + sx];
                int o = (y * w + x) * 4;
                buf[o + 3] = 255;
                if (k < 0)
                {
                    buf[o] = buf[o + 1] = buf[o + 2] = 240;
                }
                else
                {
                    double t = (double)k / total;
                    // 蓝 -> 青 -> 绿 -> 黄 -> 红 的渐变
                    byte r = (byte)(255 * Math.Clamp(1.5 - Math.Abs(t * 4 - 3) * 0.8, 0, 1));
                    byte g = (byte)(255 * Math.Clamp(1.5 - Math.Abs(t * 4 - 2) * 0.8, 0, 1));
                    byte b = (byte)(255 * Math.Clamp(1.5 - Math.Abs(t * 4 - 1) * 0.8, 0, 1));
                    if (!m[sx, sy])
                    {
                        r = (byte)(r * 0.45 + 150);
                        g = (byte)(g * 0.45 + 150);
                        b = (byte)(b * 0.45 + 150);
                    }
                    buf[o] = b;
                    buf[o + 1] = g;
                    buf[o + 2] = r;
                }
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
        bmp.Freeze();
        return bmp;
    }

    public static BitmapSource ToBitmapSource(RgbaImage img) => img.ToBitmapSource();
}
