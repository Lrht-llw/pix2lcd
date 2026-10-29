using System;
using System.Text;

namespace pix2lcd.Core;

/// <summary>输出文本编码：优先 GBK（兼容 Keil / 老编辑器），不可用时退回 UTF-8。</summary>
public static class OutputEncoding
{
    private static readonly Encoding Enc = Probe();

    public static Encoding Text => Enc;

    private static Encoding Probe()
    {
        TryRegisterCodePages();
        try { return Encoding.GetEncoding(936); }
        catch { return new UTF8Encoding(true); }
    }

    /// <summary>尽力注册代码页提供程序（存在 System.Text.Encoding.CodePages 时）。</summary>
    public static void TryRegisterCodePages()
    {
        try
        {
            var t = Type.GetType("System.Text.Encoding.CodePages.CodePagesEncodingProvider, System.Text.Encoding.CodePages");
            if (t is null) return;
            var prop = t.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop?.GetValue(null) is EncodingProvider provider)
                Encoding.RegisterProvider(provider);
        }
        catch
        {
            // 忽略：编码回退到 UTF-8
        }
    }
}
