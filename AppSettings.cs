using System;
using System.Collections.Generic;
using System.IO;

namespace pix2lcd;

/// <summary>极简本地设置：只记录快速开始引导是否已看过。</summary>
public static class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pix2lcd");
    private static readonly string FilePath = Path.Combine(SettingsDir, "settings.ini");

    /// <summary>是否已跳过或完成快速开始（首次自动弹出后置为 true）。</summary>
    public static bool QuickStartSeen
    {
        get => Get("quickstart") == "1";
        set => Set("quickstart", value ? "1" : "0");
    }

    private static string Get(string key)
    {
        try
        {
            if (!File.Exists(FilePath)) return "";
            foreach (string line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim() == key) return line[(eq + 1)..].Trim();
            }
        }
        catch
        {
            // 读不到配置时按未看过处理。
        }
        return "";
    }

    private static void Set(string key, string value)
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsDir);
            var lines = File.Exists(FilePath) ? new List<string>(File.ReadAllLines(FilePath)) : new List<string>();
            int index = lines.FindIndex(l => l.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
            if (index >= 0) lines[index] = key + "=" + value;
            else lines.Add(key + "=" + value);
            File.WriteAllText(FilePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
        catch
        {
            // 写入失败不影响主流程。
        }
    }
}
