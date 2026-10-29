using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace pix2lcd.Core;

/// <summary>图片取模的目标色深。</summary>
public enum ColorDepth
{
    Mono,
    Color4,
    Color16,
    Color256,
    Rgb332,
    Color4096,
    Rgb565,
    Rgb666,
    Rgb888,
    Argb8888,
}

/// <summary>32 位 BGRA 位图，图片取模的统一中间格式。</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; } // BGRA, 行主序

    public RgbaImage(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height * 4];
        if (Pixels.Length != width * height * 4) throw new ArgumentException("pixel buffer size mismatch");
    }

    public static RgbaImage FromFile(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return FromBitmapSource(decoder.Frames[0]);
    }

    public static RgbaImage FromBitmapSource(BitmapSource src)
    {
        BitmapSource bgra = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var buf = new byte[w * h * 4];
        bgra.CopyPixels(buf, w * 4, 0);
        return new RgbaImage(w, h, buf);
    }

    public BitmapSource ToBitmapSource()
    {
        var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Width * 4);
        bmp.Freeze();
        return bmp;
    }

    public RgbaImage Clone() => new(Width, Height, (byte[])Pixels.Clone());

    /// <summary>双线性缩放。</summary>
    public RgbaImage Resize(int width, int height)
    {
        if (width == Width && height == Height) return Clone();
        var dst = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            double sy = (y + 0.5) * Height / height - 0.5;
            int y0 = (int)Math.Floor(sy);
            double fy = sy - y0;
            int y1 = Math.Clamp(y0 + 1, 0, Height - 1);
            y0 = Math.Clamp(y0, 0, Height - 1);
            for (int x = 0; x < width; x++)
            {
                double sx = (x + 0.5) * Width / width - 0.5;
                int x0 = (int)Math.Floor(sx);
                double fx = sx - x0;
                int x1 = Math.Clamp(x0 + 1, 0, Width - 1);
                x0 = Math.Clamp(x0, 0, Width - 1);
                int i00 = (y0 * Width + x0) * 4, i10 = (y0 * Width + x1) * 4;
                int i01 = (y1 * Width + x0) * 4, i11 = (y1 * Width + x1) * 4;
                int o = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double v = Pixels[i00 + c] * (1 - fx) * (1 - fy)
                             + Pixels[i10 + c] * fx * (1 - fy)
                             + Pixels[i01 + c] * (1 - fx) * fy
                             + Pixels[i11 + c] * fx * fy;
                    dst[o + c] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
                }
            }
        }
        return new RgbaImage(width, height, dst);
    }

    /// <summary>亮度/对比度调整。brightness/contrast 取 -100..100。</summary>
    public RgbaImage Adjust(double brightness, double contrast)
    {
        var dst = (byte[])Pixels.Clone();
        double b = brightness * 255.0 / 100.0;
        double c = (contrast + 100.0) / 100.0;
        c *= c;
        for (int i = 0; i < dst.Length; i += 4)
        {
            for (int k = 0; k < 3; k++)
            {
                double v = dst[i + k] + b;
                v = (v - 128) * c + 128;
                dst[i + k] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
            }
        }
        return new RgbaImage(Width, Height, dst);
    }

    public RgbaImage Invert()
    {
        var dst = (byte[])Pixels.Clone();
        for (int i = 0; i < dst.Length; i += 4)
        {
            dst[i] = (byte)(255 - dst[i]);
            dst[i + 1] = (byte)(255 - dst[i + 1]);
            dst[i + 2] = (byte)(255 - dst[i + 2]);
        }
        return new RgbaImage(Width, Height, dst);
    }

    public RgbaImage FlipHorizontal()
    {
        var dst = new byte[Pixels.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Array.Copy(Pixels, (y * Width + (Width - 1 - x)) * 4, dst, (y * Width + x) * 4, 4);
        return new RgbaImage(Width, Height, dst);
    }

    public RgbaImage FlipVertical()
    {
        var dst = new byte[Pixels.Length];
        for (int y = 0; y < Height; y++)
            Array.Copy(Pixels, ((Height - 1 - y) * Width) * 4, dst, (y * Width) * 4, Width * 4);
        return new RgbaImage(Width, Height, dst);
    }

    public byte Lum(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return (byte)((Pixels[i + 2] * 299 + Pixels[i + 1] * 587 + Pixels[i] * 114) / 1000);
    }

    /// <summary>转二值点阵：亮 = true。用于单色取模的预览。</summary>
    public BitMatrix ToMono(int threshold)
    {
        var m = new BitMatrix(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                m[x, y] = Lum(x, y) >= threshold;
        return m;
    }
}
