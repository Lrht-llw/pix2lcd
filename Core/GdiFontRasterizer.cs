using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace pix2lcd.Core;

/// <summary>
/// 用 GDI 把文字光栅化成二值点阵：
/// CreateFontIndirectA + TextOutA 绘制，再用 CreateDIBSection 直接读回像素。
/// </summary>
public static class GdiFontRasterizer
{
    private const int GWL_STYLE = -16;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFontW(int nHeight, int nWidth, int nEscapement, int nOrientation,
        int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet,
        uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, uint rop);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string lpString, int c);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint crColor);

    [DllImport("gdi32.dll")]
    private static extern uint SetBkColor(IntPtr hdc, uint crColor);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    private const uint WHITENESS = 0x00FF0062;
    private const int TRANSPARENT = 1;
    private const int LOGPIXELSY = 90;

    /// <summary>字体描述。</summary>
    public sealed class FontSpec
    {
        public string Family = "宋体";
        public int PixelHeight = 16;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikeout;
        /// <summary>GB2312 字符集为 134，西文 ANSI 为 0。</summary>
        public int CharSet = 134;
        /// <summary>为 true 时使用“非纯白即亮点”的宽松判定。</summary>
        public bool LooseThreshold = true;
    }

    /// <summary>把单个字符渲染成宽 × 高的二值点阵。</summary>
    public static BitMatrix RenderChar(char ch, FontSpec font, int cellWidth, int cellHeight)
    {
        var m = new BitMatrix(Math.Max(1, cellWidth), Math.Max(1, cellHeight));
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        IntPtr hfont = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            var bi = new BITMAPINFO();
            bi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bi.bmiHeader.biWidth = m.Width;
            bi.bmiHeader.biHeight = -m.Height; // top-down
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = 0;
            dib = CreateDIBSection(memDc, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return m;
            old = SelectObject(memDc, dib);

            PatBlt(memDc, 0, 0, m.Width, m.Height, WHITENESS);
            SetBkMode(memDc, TRANSPARENT);
            SetBkColor(memDc, 0x00FFFFFF);
            SetTextColor(memDc, 0x00000000);

            int weight = font.Bold ? 700 : 400;
            hfont = CreateFontW(-Math.Max(1, font.PixelHeight), 0, 0, 0, weight,
                font.Italic ? 1u : 0u, font.Underline ? 1u : 0u, font.Strikeout ? 1u : 0u,
                (uint)Math.Max(0, font.CharSet), 0, 0, 0, 0, font.Family);
            if (hfont == IntPtr.Zero) return m;
            var oldFont = SelectObject(memDc, hfont);

            var s = ch.ToString();
            TextOutW(memDc, 0, 0, s, 1);

            var buf = new byte[m.Width * m.Height * 4];
            Marshal.Copy(bits, buf, 0, buf.Length);
            int n = 0;
            for (int y = 0; y < m.Height; y++)
            {
                for (int x = 0; x < m.Width; x++)
                {
                    byte b = buf[n], g = buf[n + 1], r = buf[n + 2];
                    n += 4;
                    bool lit;
                    if (font.LooseThreshold)
                        lit = !(r == 255 && g == 255 && b == 255);
                    else
                        lit = (r + g + b) / 3 < 128;
                    m[x, y] = lit;
                }
            }
        }
        finally
        {
            if (hfont != IntPtr.Zero) DeleteObject(hfont);
            if (old != IntPtr.Zero) SelectObject(memDc, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
        return m;
    }

    /// <summary>
    /// 把整段文字渲染成一行点阵。汉字占满 <paramref name="cellWidth"/>，
    /// 西文与数字占一半宽（英文和数字的宽度为汉字的一半）。
    /// </summary>
    public static BitMatrix RenderText(string text, FontSpec font, int cellWidth, int cellHeight, int charSpace = 0)
    {
        cellWidth = Math.Max(1, cellWidth);
        cellHeight = Math.Max(1, cellHeight);
        if (string.IsNullOrEmpty(text)) return new BitMatrix(cellWidth, cellHeight);

        var glyphs = new List<BitMatrix>();
        foreach (char ch in text)
        {
            bool half = ch < 0x80;
            int w = half ? Math.Max(1, cellWidth / 2) : cellWidth;
            glyphs.Add(RenderChar(ch, font, w, cellHeight));
        }

        int total = 0;
        foreach (var g in glyphs) total += g.Width + charSpace;
        total = Math.Max(1, total - charSpace);
        var line = new BitMatrix(total, cellHeight);
        int cx = 0;
        foreach (var g in glyphs)
        {
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                    if (g[x, y]) line[cx + x, y] = true;
            cx += g.Width + charSpace;
        }
        return line;
    }
}
