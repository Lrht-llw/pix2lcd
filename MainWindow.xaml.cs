using System.Windows;

namespace pix2lcd
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private sealed record GuideStep(int TabIndex, string Title, string Body, string Tip);

        private static readonly GuideStep[] GuideSteps =
        {
            new(0, "文字取模：先跑通一次默认参数",
                "1. 在「文字」输入框写要取模的文字，左侧预览会实时更新。\n" +
                "2. 右侧「驱动预设」选择你的屏幕型号；不确定就保持「自定义」。\n" +
                "3. 点右下角「生成字模」，结果出现在下方输出区，可直接复制或保存。",
                "提示：取模方式或位序选错时，屏幕上会显示乱码；先确认驱动 IC 再微调。"),
            new(0, "复杂选项交给驱动预设",
                "取模方式、取模走向、点阵格式都由屏幕驱动 IC 决定：\n" +
                "SSD1306 / ST7565 用列行式 + 逆向，ST7920 用逐行式，TFT 屏用逐行式 + 顺向。\n" +
                "选中预设后这些选项会自动配好。",
                "「高级设置」里仍可手动微调走向和输出格式；改动后预设会回到「自定义」。"),
            new(1, "图片取模：拖进来就行",
                "1. 把图片拖入窗口，或点「打开图片…」。\n" +
                "2. 设置输出宽高（点「按原图」可还原），再选色深与扫描方式。\n" +
                "3. 点「生成数据」，可保存为 C 数组或 BIN。",
                "透明图片建议用 ARGB8888；其他色深会自动把透明区域合成到白底。"),
        };

        private int _guideIndex;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => App.ApplyDarkTitleBar(this, AppTheme.IsDark);
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!App.ScreenshotMode && !AppSettings.QuickStartSeen) ShowGuide(0);
        }

        // ---------------------------------------------------------------- 快速开始

        /// <summary>打开指定步骤的快速开始蒙层；供顶部「?」按钮与截图校验使用。</summary>
        internal void ShowGuide(int index)
        {
            index = Math.Clamp(index, 0, GuideSteps.Length - 1);
            _guideIndex = index;
            var step = GuideSteps[index];

            MainTabs.SelectedIndex = step.TabIndex;
            GuideStepLabel.Text = $"快速开始 · {index + 1} / {GuideSteps.Length}";
            GuideTitle.Text = step.Title;
            GuideBody.Text = step.Body;
            GuideTip.Text = step.Tip;
            GuideBack.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
            GuideNext.Content = index == GuideSteps.Length - 1 ? "开始使用" : "下一步";

            var dots = new string[GuideSteps.Length];
            for (int i = 0; i < dots.Length; i++) dots[i] = i == index ? "●" : "○";
            GuideDots.Text = string.Join(" ", dots);

            GuideLayer.Visibility = Visibility.Visible;
        }

        internal void CloseGuide(bool markSeen)
        {
            GuideLayer.Visibility = Visibility.Collapsed;
            if (markSeen) AppSettings.QuickStartSeen = true;
        }

        private void OnShowGuide(object sender, RoutedEventArgs e) => ShowGuide(0);

        private void OnGuideSkip(object sender, RoutedEventArgs e) => CloseGuide(true);

        private void OnGuideBack(object sender, RoutedEventArgs e)
        {
            if (_guideIndex > 0) ShowGuide(_guideIndex - 1);
        }

        private void OnGuideNext(object sender, RoutedEventArgs e)
        {
            if (_guideIndex < GuideSteps.Length - 1) ShowGuide(_guideIndex + 1);
            else CloseGuide(true);
        }
    }
}
