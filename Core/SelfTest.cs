using System;
using System.Collections.Generic;
using System.Text;

namespace pix2lcd.Core;

/// <summary>
/// 内置自检：用可手工验算的小样例校验四种取模方式的字节顺序，
/// 并验证“取模/还原”回环、字体光栅化与图片编码。通过 <c>pix2lcd.exe --selftest out.txt</c> 运行。
/// </summary>
public static class SelfTest
{
    private static readonly StringBuilder Log = new();
    private static int _fail;

    private static void Check(bool ok, string what)
    {
        Log.Append(ok ? "  [PASS] " : "  [FAIL] ").Append(what).Append("\r\n");
        if (!ok) _fail++;
    }

    private static void CheckBytes(byte[] actual, byte[] expected, string what)
    {
        bool ok = actual.Length == expected.Length;
        if (ok)
        {
            for (int i = 0; i < expected.Length; i++)
                if (actual[i] != expected[i]) { ok = false; break; }
        }
        Check(ok, $"{what}  (期望 {Hex(expected)} 实际 {Hex(actual)})");
    }

    private static string Hex(byte[] d)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < d.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(d[i].ToString("X2"));
        }
        return sb.ToString();
    }

    private static void DumpAscii(BitMatrix m, string title)
    {
        Log.Append("\r\n").Append(title).Append($"  ({m.Width}×{m.Height})\r\n");
        for (int y = 0; y < m.Height; y++)
        {
            var sb = new StringBuilder();
            for (int x = 0; x < m.Width; x++) sb.Append(m[x, y] ? '#' : '.');
            Log.Append("    ").Append(sb).Append("\r\n");
        }
    }

    private static void SetPixel(RgbaImage img, int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = (y * img.Width + x) * 4;
        img.Pixels[i] = b;
        img.Pixels[i + 1] = g;
        img.Pixels[i + 2] = r;
        img.Pixels[i + 3] = a;
    }

    public static string Run()
    {
        Log.Clear();
        _fail = 0;
        Log.Append("Pix2LCD 自检\r\n============\r\n");

        // ---------------------------------------------------------- 1. 逐列式
        Log.Append("\r\n1. 取模方式字节顺序（手工验算样例）\r\n");
        {
            // 8 列 × 16 行，只点亮 (0,0)、(0,8)、(7,15)
            var m = new BitMatrix(8, 16);
            m[0, 0] = true;
            m[0, 8] = true;
            m[7, 15] = true;

            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.Vertical, true, false),
                new byte[] { 0x80, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01 },
                "逐列式(顺向,阴码)");

            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.ColumnRow, true, false),
                new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0x01 },
                "列行式(顺向,阴码)");

            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.Horizontal, true, false),
                new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0x01 },
                "逐行式(顺向,阴码)");

            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.RowColumn, true, false),
                new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0x01 },
                "行列式(顺向,阴码)");
        }
        {
            // 16×16，只点亮 (0,0)、(8,0)，用于区分逐行式与行列式
            var m = new BitMatrix(16, 16);
            m[0, 0] = true;
            m[8, 0] = true;
            var exp1 = new byte[32];
            exp1[0] = 0x80;
            exp1[1] = 0x80;
            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.Horizontal, true, false),
                exp1, "逐行式 16×16 (0,0)&(8,0)");

            var exp2 = new byte[32];
            exp2[0] = 0x80;
            exp2[16] = 0x80;
            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.RowColumn, true, false),
                exp2, "行列式 16×16 (0,0)&(8,0)");
        }
        {
            // 逆向（低位在前）与阳码
            var m = new BitMatrix(8, 8);
            m[0, 0] = true;   // 第一个点
            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.Vertical, false, false),
                new byte[] { 0x01, 0, 0, 0, 0, 0, 0, 0 }, "逐列式(逆向,阴码) 首点 → bit0");
            CheckBytes(DotMatrixEncoder.Encode(m, DotMatrixEncoder.ScanPattern.Vertical, false, true),
                new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, "逐列式(逆向,阳码) 取反");
        }

        // ---------------------------------------------------------- 2. 回环
        Log.Append("\r\n2. 取模 → 还原 回环一致性\r\n");
        foreach (var pattern in Enum.GetValues<DotMatrixEncoder.ScanPattern>())
        {
            bool ok = true;
            foreach (int w in new[] { 5, 8, 12, 16, 17 })
            {
                foreach (int h in new[] { 7, 8, 16, 21 })
                {
                    var src = new BitMatrix(w, h);
                    var rnd = new Random(w * 100 + h);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            src[x, y] = rnd.Next(2) == 0;
                    foreach (bool msb in new[] { true, false })
                    {
                        foreach (bool inv in new[] { false, true })
                        {
                            var bytes = DotMatrixEncoder.Encode(src, pattern, msb, inv);
                            var back = BitMatrix.FromByteStream(bytes, w, h, pattern, msb, inv);
                            for (int y = 0; y < h && ok; y++)
                                for (int x = 0; x < w && ok; x++)
                                    if (back[x, y] != src[x, y]) ok = false;
                        }
                    }
                }
            }
            Check(ok, $"{pattern} 回环");
        }

        // ---------------------------------------------------------- 3. 字体
        Log.Append("\r\n3. GDI 字体光栅化\r\n");
        try
        {
            var font = new GdiFontRasterizer.FontSpec { Family = "宋体", PixelHeight = 16, CharSet = 134 };
            var cn = GdiFontRasterizer.RenderText("中", font, 16, 16);
            Check(!cn.IsEmpty, "渲染汉字“中”得到非空白点阵");
            DumpAscii(cn, "宋体 16px “中”");

            var en = GdiFontRasterizer.RenderText("A", font, 16, 16);
            Check(!en.IsEmpty, "渲染西文“A”得到非空白点阵");
            DumpAscii(en, "宋体 16px “A”");

            var line = GdiFontRasterizer.RenderText("字模ABC", font, 16, 16);
            Check(line.Width == 16 * 2 + 8 * 3, $"混合宽度 汉字16/西文8（实际 {line.Width}）");
            DumpAscii(line, "宋体 16px “字模ABC”");
        }
        catch (Exception ex)
        {
            Check(false, "字体光栅化异常: " + ex.Message);
        }

        // ---------------------------------------------------------- 4. 图片
        Log.Append("\r\n4. 图片编码\r\n");
        {
            var img = new RgbaImage(8, 8);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int o = (y * 8 + x) * 4;
                    byte v = (byte)((x + y) % 2 == 0 ? 255 : 0);
                    img.Pixels[o] = v;
                    img.Pixels[o + 1] = v;
                    img.Pixels[o + 2] = v;
                    img.Pixels[o + 3] = 255;
                }
            }
            var mono = ImageEncoder.Encode(img, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            Check(mono.Length == 8, $"8×8 单色水平扫描输出 8 字节（实际 {mono.Length}）");
            Check(mono[0] == 0xAA, $"棋盘首行 0xAA（实际 0x{mono[0]:X2}）");

            var rgb565 = ImageEncoder.Encode(img, ColorDepth.Rgb565, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            Check(rgb565.Length == 8 * 8 * 2, $"8×8 RGB565 输出 128 字节（实际 {rgb565.Length}）");
            Check(rgb565[0] == 0xFF && rgb565[1] == 0xFF, "白色像素编码为 0xFFFF");

            // 非 8 倍数宽度：每行/列必须补齐到整字节，否则后续行会错位
            var odd = new RgbaImage(13, 5);
            var h = ImageEncoder.Encode(odd, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            Check(h.Length == 5 * 2, $"13×5 单色水平扫描 = 5 行 × 2 字节（实际 {h.Length}）");
            var vert = ImageEncoder.Encode(odd, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.Vertical,
                true, false, ColorOrder.Rgb, 128);
            Check(vert.Length == 13 * 1, $"13×5 单色垂直扫描 = 13 列 × 1 字节（实际 {vert.Length}）");
            var rc = ImageEncoder.Encode(odd, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.RowColumn,
                true, false, ColorOrder.Rgb, 128);
            Check(rc.Length == 2 * 5, $"13×5 单色行列式 = 2 列块 × 5 行（实际 {rc.Length}）");
            var cr = ImageEncoder.Encode(odd, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.ColumnRow,
                true, false, ColorOrder.Rgb, 128);
            Check(cr.Length == 1 * 13, $"13×5 单色列行式 = 1 行块 × 13 列（实际 {cr.Length}）");
        }

        {
            // 低位在前：第一个像素进入 bit0
            var img = new RgbaImage(2, 2);
            SetPixel(img, 0, 0, 255, 255, 255);
            SetPixel(img, 1, 0, 255, 255, 255);
            var hLsb = ImageEncoder.Encode(img, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.Horizontal,
                false, false, ColorOrder.Rgb, 128);
            CheckBytes(hLsb, new byte[] { 0x03, 0x00 }, "单色低位在前 2×2 水平");
            var vLsb = ImageEncoder.Encode(img, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.Vertical,
                false, false, ColorOrder.Rgb, 128);
            CheckBytes(vLsb, new byte[] { 0x01, 0x01 }, "单色低位在前 2×2 垂直");
        }

        {
            // 2bpp / 4bpp 调色板打包
            var img = new RgbaImage(4, 1);
            SetPixel(img, 0, 0, 0, 0, 0);
            SetPixel(img, 1, 0, 85, 85, 85);
            SetPixel(img, 2, 0, 170, 170, 170);
            SetPixel(img, 3, 0, 255, 255, 255);
            var msb = ImageEncoder.Encode(img, ColorDepth.Color4, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            CheckBytes(msb, new byte[] { 0x1B }, "4 色高位在前 0,1,2,3");
            var lsb = ImageEncoder.Encode(img, ColorDepth.Color4, DotMatrixEncoder.ScanPattern.Horizontal,
                false, false, ColorOrder.Rgb, 128);
            CheckBytes(lsb, new byte[] { 0xE4 }, "4 色低位在前 0,1,2,3");

            var img16 = new RgbaImage(2, 1);
            SetPixel(img16, 0, 0, 255, 255, 255);
            SetPixel(img16, 1, 0, 0, 0, 0);
            var msb16 = ImageEncoder.Encode(img16, ColorDepth.Color16, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            CheckBytes(msb16, new byte[] { 0xF0 }, "16 色高位在前 15,0");
            var lsb16 = ImageEncoder.Encode(img16, ColorDepth.Color16, DotMatrixEncoder.ScanPattern.Horizontal,
                false, false, ColorOrder.Rgb, 128);
            CheckBytes(lsb16, new byte[] { 0x0F }, "16 色低位在前 15,0");
        }

        {
            // 12bpp：两个像素拼成 3 字节
            var img = new RgbaImage(2, 1);
            SetPixel(img, 0, 0, 0xA0, 0xB0, 0xC0); // RGB444 = 0xABC
            SetPixel(img, 1, 0, 0x10, 0x20, 0x30); // RGB444 = 0x123
            var msb = ImageEncoder.Encode(img, ColorDepth.Color4096, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            CheckBytes(msb, new byte[] { 0xAB, 0xC1, 0x23 }, "12 位高位在前 0xABC,0x123");
            var lsb = ImageEncoder.Encode(img, ColorDepth.Color4096, DotMatrixEncoder.ScanPattern.Horizontal,
                false, false, ColorOrder.Rgb, 128);
            CheckBytes(lsb, new byte[] { 0xBC, 0x3A, 0x12 }, "12 位低位在前 0xABC,0x123");
        }

        {
            // 多字节像素的字节序、通道顺序与 alpha
            var red = new RgbaImage(1, 1);
            SetPixel(red, 0, 0, 255, 0, 0);
            var msb = ImageEncoder.Encode(red, ColorDepth.Rgb565, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            CheckBytes(msb, new byte[] { 0xF8, 0x00 }, "RGB565 高位在前");
            var lsb = ImageEncoder.Encode(red, ColorDepth.Rgb565, DotMatrixEncoder.ScanPattern.Horizontal,
                false, false, ColorOrder.Rgb, 128);
            CheckBytes(lsb, new byte[] { 0x00, 0xF8 }, "RGB565 低位在前");

            var rgb = new RgbaImage(1, 1);
            SetPixel(rgb, 0, 0, 10, 20, 30);
            var rgbOrder = ImageEncoder.Encode(rgb, ColorDepth.Rgb888, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            CheckBytes(rgbOrder, new byte[] { 0x0A, 0x14, 0x1E }, "RGB888 通道顺序 R,G,B");
            var bgrOrder = ImageEncoder.Encode(rgb, ColorDepth.Rgb888, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Bgr, 128);
            CheckBytes(bgrOrder, new byte[] { 0x1E, 0x14, 0x0A }, "RGB888 通道顺序 B,G,R");

            var argbImg = new RgbaImage(1, 1);
            SetPixel(argbImg, 0, 0, 10, 20, 30, 100);
            var argb = ImageEncoder.Encode(argbImg, ColorDepth.Argb8888, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            CheckBytes(argb, new byte[] { 0x0A, 0x14, 0x1E, 0x64 }, "ARGB8888 保留 alpha");
        }

        {
            // 四种扫描方式的像素位置（9×9，宽高都不是 8 的倍数）
            var img = new RgbaImage(9, 9);
            SetPixel(img, 0, 0, 255, 255, 255);
            SetPixel(img, 8, 0, 255, 255, 255);
            byte[] expected;

            var h = ImageEncoder.Encode(img, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.Horizontal,
                true, false, ColorOrder.Rgb, 128);
            expected = new byte[18];
            expected[0] = 0x80; expected[1] = 0x80;
            CheckBytes(h, expected, "9×9 水平扫描像素位置");

            var v = ImageEncoder.Encode(img, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.Vertical,
                true, false, ColorOrder.Rgb, 128);
            expected = new byte[18];
            expected[0] = 0x80; expected[16] = 0x80;
            CheckBytes(v, expected, "9×9 垂直扫描像素位置");

            var rc = ImageEncoder.Encode(img, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.RowColumn,
                true, false, ColorOrder.Rgb, 128);
            expected = new byte[18];
            expected[0] = 0x80; expected[9] = 0x80;
            CheckBytes(rc, expected, "9×9 行列式扫描像素位置");

            var cr = ImageEncoder.Encode(img, ColorDepth.Mono, DotMatrixEncoder.ScanPattern.ColumnRow,
                true, false, ColorOrder.Rgb, 128);
            expected = new byte[18];
            expected[0] = 0x80; expected[8] = 0x80;
            CheckBytes(cr, expected, "9×9 列行式扫描像素位置");
        }

        {
            // 透明像素合成到白色背景
            var img = new RgbaImage(1, 1);
            SetPixel(img, 0, 0, 0, 0, 0, 0);
            var flat = img.FlattenOntoWhite();
            Check(flat.Pixels[0] == 255 && flat.Pixels[1] == 255 && flat.Pixels[2] == 255 && flat.Pixels[3] == 255,
                "透明像素合成到白色背景");
        }

        // ---------------------------------------------------------- 5. 输出格式
        Log.Append("\r\n5. 输出格式\r\n");
        {
            var bytes = new byte[] { 0x3C, 0x42, 0x00 };
            var c51 = OutputFormat.Preset(OutputStyle.C51);
            c51.PerLine = 2;
            Log.Append("  C51:\r\n").Append(Indent(c51.Format(bytes))).Append("\r\n");
            var a51 = OutputFormat.Preset(OutputStyle.A51);
            a51.PerLine = 2;
            Log.Append("  A51:\r\n").Append(Indent(a51.Format(bytes))).Append("\r\n");

            Check(OutputFormat.Preset(OutputStyle.C51).Comment("x").Contains("*/"), "C51 注释包含闭合的 */");
            Check(OutputFormat.Preset(OutputStyle.A51).Comment("x").TrimStart().StartsWith(";"), "A51 注释以分号开头");
        }

        // ---------------------------------------------------------- 6. 字库
        Log.Append("\r\n6. 字库生成\r\n");
        try
        {
            var font = new GdiFontRasterizer.FontSpec { Family = "宋体", PixelHeight = 16, CharSet = 134 };
            var chars = new[] { 'A', 'B', '中', '文' };
            var entries = FontLibrary.Build(chars, font, 16, 16,
                DotMatrixEncoder.ScanPattern.Vertical, true, false);
            Check(entries.Count == 4, $"字库条目数 = 4（实际 {entries.Count}）");
            bool same = true;
            foreach (var e in entries)
                if (e.Data.Length != 32) same = false;
            Check(same, "16×16 逐列式每字 32 字节（定长记录）");

            var src = FontLibrary.BuildCSource(entries, font, 16, 16,
                DotMatrixEncoder.ScanPattern.Vertical, true, false, OutputFormat.Preset(OutputStyle.C51));
            Check(src.Contains("ch_A[32]") && src.Contains("ch_4E2D[32]"), "C 源含 ch_A / ch_4E2D 符号");

            string idx = FontLibrary.BuildIndex(entries);
            Check(idx.StartsWith("A ~(0)") && idx.Contains("中 ~(64)"), "索引偏移按每字 32 字节递增");
        }
        catch (Exception ex)
        {
            Check(false, "字库生成异常: " + ex.Message);
        }

        Log.Append("\r\n结果: ").Append(_fail == 0 ? "全部通过" : $"{_fail} 项失败").Append("\r\n");
        return Log.ToString();
    }

    private static string Indent(string s)
        => "    " + s.Replace("\r\n", "\r\n    ").TrimEnd();
}
