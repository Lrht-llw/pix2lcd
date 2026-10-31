using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using pix2lcd.Core;

namespace pix2lcd
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppTheme.Initialize();
            OutputEncoding.TryRegisterCodePages();

            if (e.Args.Length > 0 && e.Args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            {
                string outPath = e.Args.Length > 1 ? e.Args[1] : "selftest.txt";
                try
                {
                    string report = SelfTest.Run();
                    File.WriteAllText(outPath, report, new UTF8Encoding(true));
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(outPath, "selftest failed: " + ex, new UTF8Encoding(true)); } catch { }
                    Environment.Exit(1);
                }
                return;
            }

            if (e.Args.Length > 0 && e.Args[0].Equals("--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                RunScreenshot(e.Args.Length > 1 ? e.Args[1] : ".");
                return;
            }

            DispatcherUnhandledException += OnUnhandledException;
            base.OnStartup(e);
        }

        /// <summary>把两个模块各渲染一张 PNG，用于无人工介入的界面校验。</summary>
        private void RunScreenshot(string outDir)
        {
            Directory.CreateDirectory(outDir);
            string logPath = Path.Combine(outDir, "log.txt");
            var log = new StringBuilder();
            var win = new MainWindow();
            MainWindow = win;
            win.ContentRendered += (_, _) =>
            {
                win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        log.AppendLine($"window {win.ActualWidth}x{win.ActualHeight}");
                        win.MainTabs.SelectedIndex = 0;
                        win.UpdateLayout();
                        Save(win, Path.Combine(outDir, "ui_text.png"));
                        log.AppendLine("text ok");

                        win.TextPage.SetText("中A");
                        win.UpdateLayout();
                        log.AppendLine("settext ok");

                        win.ImagePage.LoadDemoImage(BuildDemoImage());
                        log.AppendLine("demo image ok");
                        win.MainTabs.SelectedIndex = 1;
                        win.UpdateLayout();
                        win.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                Save(win, Path.Combine(outDir, "ui_image.png"));
                                log.AppendLine("image ok");
                            }
                            catch (Exception ex) { log.AppendLine("image err: " + ex); }
                            try { File.WriteAllText(logPath, log.ToString()); } catch { }
                            Environment.Exit(0);
                        }), DispatcherPriority.ContextIdle);
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine("err: " + ex);
                        try { File.WriteAllText(logPath, log.ToString()); } catch { }
                        Environment.Exit(1);
                    }
                }), DispatcherPriority.ContextIdle);
            };
            win.Show();
        }

        private static void Save(Window win, string path)
        {
            int w = (int)Math.Max(1, win.ActualWidth);
            int h = (int)Math.Max(1, win.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(win);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private static BitmapSource BuildDemoImage()
        {
            const int w = 96, h = 64;
            var img = new RgbaImage(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    int r = x * 255 / (w - 1);
                    int g = y * 255 / (h - 1);
                    int b = 128;
                    if (x > w / 3 && x < w * 2 / 3 && y > h / 4 && y < h * 3 / 4)
                    {
                        r = 255; g = 255; b = 0;
                    }
                    img.Pixels[o] = (byte)b;
                    img.Pixels[o + 1] = (byte)g;
                    img.Pixels[o + 2] = (byte)r;
                    img.Pixels[o + 3] = 255;
                }
            }
            return img.ToBitmapSource();
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString(), "Pix2LCD — 未处理的异常",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        /// <summary>让系统标题栏跟随当前界面主题（Windows 10 1809+）。</summary>
        internal static void ApplyDarkTitleBar(Window window, bool dark)
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int enabled = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
            }
            catch
            {
                // 旧版系统不支持时保持系统默认标题栏。
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
