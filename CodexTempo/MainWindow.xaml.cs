using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CodexTempo;

public partial class MainWindow : Window
{
    private readonly CodexUsageProvider _reader = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _positionSaveTimer;
    private bool _refreshing;
    private bool _isDark;
    private bool _isPinned = true;
    private bool _isDocked;
    private bool _allowClose;
    private bool _closePromptOpen;
    private readonly bool _previewMode;
    private DockEdge _dockEdge;
    private readonly Forms.NotifyIcon _trayIcon;
    private PaceTone _currentTone = PaceTone.Waiting;
    private const double FullWidth = 420;
    private const double FullHeight = 340;
    private const double HorizontalCompactWidth = 244;
    private const double HorizontalCompactHeight = 58;
    private const double VerticalCompactWidth = 150;
    private const double VerticalCompactHeight = 112;
    private const double EdgeTouchSlop = 6;
    private const double UndockDistance = 48;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public MainWindow(bool previewMode = false)
    {
        _previewMode = previewMode;
        InitializeComponent();
        _isDark = ThemeService.Apply(
            Resources,
            Environment.GetCommandLineArgs().Contains("--dark-preview", StringComparer.OrdinalIgnoreCase)
                ? true
                : null);
        _trayIcon = previewMode ? new Forms.NotifyIcon() : CreateTrayIcon();
        if (!previewMode) RestorePosition();
        _positionSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _positionSaveTimer.Tick += (_, _) =>
        {
            _positionSaveTimer.Stop();
            if (!_previewMode && !_isDocked && WindowState == WindowState.Normal)
                WindowPlacementStore.Save(Left, Top);
        };
        LocationChanged += (_, _) =>
        {
            if (_isDocked) return;
            _positionSaveTimer.Stop();
            _positionSaveTimer.Start();
        };
        if (!previewMode) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        if (!previewMode)
        {
            Loaded += async (_, _) =>
            {
                await RefreshAsync();
                _timer.Start();
            };
        }
        SourceInitialized += (_, _) => ApplyPinnedState();
        Deactivated += (_, _) =>
        {
            if (_isPinned)
                Dispatcher.BeginInvoke(ApplyPinnedState, DispatcherPriority.ApplicationIdle);
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Show();
                DockToNearestEdge();
                ApplyPinnedState();
            }
        };
        Closing += OnClosing;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await _reader.ReadLatestAsync();
            if (snapshot is null)
            {
                ShowUnavailable();
                return;
            }

            var now = DateTimeOffset.Now;
            var advice = RecommendationEngine.Recommend(snapshot, now);
            AdviceTitle.Text = advice.Title;
            AdviceDetail.Text = advice.Detail;
            AdviceDetail.ToolTip = "今日已用量根据昨天最后一条额度快照与当前快照的差值估算；不是官方逐日账单。";
            RateLabel.Text = advice.RateLabel;
            RatePill.ToolTip = BuildPaceTooltip(advice);
            ApplyTone(advice.Tone);
            UpdateLimit(snapshot.FiveHour, FivePercent, FiveProgress, FiveReset, now);
            UpdateLimit(snapshot.Week, WeekPercent, WeekProgress, WeekReset, now);

            var age = now - snapshot.CapturedAt;
            var isLive = snapshot.SourceFile == CodexAppServerClient.SourceName;
            var isCachedLive = snapshot.SourceFile == CodexUsageProvider.CachedSourceName;
            var fresh = isLive || age < TimeSpan.FromMinutes(3);
            StatusDot.Fill = Brush(isCachedLive ? "#FF9F0A" : fresh ? "#34C759" : "#FF9F0A");
            SyncLabel.Text = isLive
                ? $"实时查询 · {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}"
                : isCachedLive
                ? $"连接波动 · 保留 {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}"
                : fresh
                ? $"额度更新 · {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}"
                : $"最后额度 · {snapshot.CapturedAt.ToLocalTime():MM-dd HH:mm}";
            FooterLabel.Text = "每 10 秒查询实时额度";
        }
        catch
        {
            SyncLabel.Text = "暂时无法读取 · 将自动重试";
            StatusDot.Fill = Brush("#FF3B30");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void UpdateLimit(
        LimitWindow? limit,
        TextBlock percent,
        System.Windows.Controls.ProgressBar progress,
        TextBlock reset,
        DateTimeOffset now)
    {
        if (limit is null)
        {
            percent.Text = "--%";
            progress.Value = 0;
            reset.Text = "暂无快照";
            return;
        }
        percent.Text = $"{limit.RemainingPercent:0}%";
        progress.Value = limit.RemainingPercent;
        reset.Text = $"{RecommendationEngine.FormatDuration(limit.TimeRemaining(now))} 后重置";
    }

    private void ApplyTone(PaceTone tone)
    {
        _currentTone = tone;
        var (pill, text) = (_isDark, tone) switch
        {
            (true, PaceTone.Encourage) => ("#20382A", "#66D487"),
            (true, PaceTone.Caution) => ("#3B3120", "#FFB340"),
            (true, PaceTone.Urgent) => ("#3D2524", "#FF6961"),
            (true, PaceTone.Waiting) => ("#343438", "#A3A3AA"),
            (true, _) => ("#20334A", "#64A8FF"),
            (false, PaceTone.Encourage) => ("#E8F7ED", "#248A3D"),
            (false, PaceTone.Caution) => ("#FFF4DF", "#9A6700"),
            (false, PaceTone.Urgent) => ("#FFF0ED", "#C9342B"),
            (false, PaceTone.Waiting) => ("#EBEBF0", "#6E6E73"),
            _ => ("#E7F1FF", "#0066CC")
        };
        RatePill.Background = Brush(pill);
        RateLabel.Foreground = Brush(text);
        FiveProgress.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        WeekProgress.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
    }

    private void ShowUnavailable()
    {
        AdviceTitle.Text = "等待首次额度快照";
        AdviceDetail.Text = "在 Codex 中发一条消息，小组件就会自动更新";
        RateLabel.Text = "本地待命";
        ApplyTone(PaceTone.Waiting);
        SyncLabel.Text = "未找到最近的 session 数据";
        StatusDot.Fill = Brush(_isDark ? "#77777E" : "#8E8E93");
    }

    public void PreparePreview()
    {
        var now = DateTimeOffset.Now;
        var sample = new UsageSnapshot(
            new LimitWindow(36, 300, now.AddHours(3.4)),
            new LimitWindow(42, 10080, now.AddDays(3.8)),
            now,
            "preview.jsonl");
        var advice = RecommendationEngine.Recommend(sample, now);
        AdviceTitle.Text = advice.Title;
        AdviceDetail.Text = advice.Detail;
        AdviceDetail.ToolTip = "今日已用量根据昨天最后一条额度快照与当前快照的差值估算；不是官方逐日账单。";
        RateLabel.Text = advice.RateLabel;
        RatePill.ToolTip = BuildPaceTooltip(advice);
        ApplyTone(advice.Tone);
        UpdateLimit(sample.FiveHour, FivePercent, FiveProgress, FiveReset, now);
        UpdateLimit(sample.Week, WeekPercent, WeekProgress, WeekReset, now);
        SyncLabel.Text = $"实时查询 · {DateTime.Now:HH:mm:ss}";
        FooterLabel.Text = "每 10 秒查询实时额度";
    }

    public void PrepareCompactPreview() => DockToEdge(DockEdge.Right);

    public void PrepareHorizontalCompactPreview() => DockToEdge(DockEdge.Bottom);

    private static SolidColorBrush Brush(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private static string BuildPaceTooltip(PaceAdvice advice)
    {
        var delta = (advice.RateMultiplier - 1) * 100;
        var action = delta switch
        {
            > 4 => $"比均匀速度多用约 {delta:0}%",
            < -4 => $"比均匀速度少用约 {Math.Abs(delta):0}%",
            _ => "保持均匀速度"
        };
        return $"1.0× 表示刚好在周额度重置前用完。\n当前建议：{action}。";
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            FindAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
            return;

        if (_isDocked && e.ClickCount >= 2)
        {
            ExpandFromDock();
            e.Handled = true;
            return;
        }

        DragMove();
        EvaluateEdgeDock();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        ApplyPinnedState();
    }

    private void ApplyPinnedState()
    {
        Topmost = _isPinned;
        PinButton.Content = _isPinned ? "\uE840" : "\uE77A";
        PinButton.Foreground = _isPinned
            ? (System.Windows.Media.Brush)FindResource("Accent")
            : (System.Windows.Media.Brush)FindResource("Muted");
        PinButton.ToolTip = _isPinned ? "已置顶，点击取消" : "未置顶，点击保持在最前";

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(
                handle,
                _isPinned ? HwndTopmost : HwndNotTopmost,
                0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示完整面板", null, (_, _) => ShowFromTray());
        menu.Items.Add("切换置顶", null, (_, _) =>
        {
            _isPinned = !_isPinned;
            ApplyPinnedState();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            RequestExit();
        });

        var icon = new Forms.NotifyIcon
        {
            Text = "Codex Tempo",
            Icon = CreateTrayLeafIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private static Drawing.Icon CreateTrayLeafIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                using var associated = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (associated is not null) return (Drawing.Icon)associated.Clone();
            }
        }
        catch
        {
        }

        using var bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Drawing.Color.Transparent);
            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 0, 122, 255));
            using var leaf = new Drawing.SolidBrush(Drawing.Color.White);
            using var vein = new Drawing.Pen(Drawing.Color.FromArgb(255, 0, 98, 214), 2.2f)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round
            };
            graphics.FillEllipse(background, 1, 1, 30, 30);
            graphics.FillEllipse(leaf, 8, 5, 16, 22);
            graphics.DrawLine(vein, 9, 23, 23, 8);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void ShowFromTray()
    {
        if (_isDocked)
            ExpandFromDock();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        ApplyPinnedState();
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => DockToNearestEdge();

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_isDocked && FindAncestor<System.Windows.Controls.Button>(
                e.OriginalSource as DependencyObject) is null)
        {
            ExpandFromDock();
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => PromptForCloseChoice();

    private void PromptForCloseChoice()
    {
        if (_closePromptOpen) return;

        _closePromptOpen = true;
        try
        {
            var dialog = new CloseChoiceDialog
            {
                Owner = this,
                Topmost = _isPinned
            };
            dialog.ShowDialog();

            switch (dialog.Choice)
            {
                case CloseChoice.HideToTray:
                    HideToTray();
                    break;
                case CloseChoice.Exit:
                    RequestExit();
                    break;
            }
        }
        finally
        {
            _closePromptOpen = false;
        }
    }

    private void HideToTray()
    {
        Hide();
    }

    private void RequestExit()
    {
        _allowClose = true;
        Close();
    }

    private void RestorePosition()
    {
        if (WindowPlacementStore.Load() is { } saved && IsVisiblePosition(saved.Left, saved.Top))
        {
            Left = saved.Left;
            Top = saved.Top;
            return;
        }

        var area = SystemParameters.WorkArea;
        var instanceOffset = Math.Clamp(
            Process.GetProcessesByName("CodexTempo").Length - 1,
            0,
            5) * 28;
        Left = Math.Max(area.Left, area.Right - Width - 24 - instanceOffset);
        Top = Math.Max(area.Top, area.Bottom - Height - 24 - instanceOffset);
    }

    private static bool IsVisiblePosition(double left, double top)
    {
        const double visibleEdge = 48;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        return left + visibleEdge >= virtualLeft && left <= virtualRight - visibleEdge &&
               top + visibleEdge >= virtualTop && top <= virtualBottom - visibleEdge;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _isDark = ThemeService.Apply(Resources);
            ApplyTone(_currentTone);
            ApplyPinnedState();
        });
    }

    private void EvaluateEdgeDock()
    {
        var geometry = GetWindowGeometry();
        if (geometry is null) return;

        var (_, rect, work, dpi) = geometry.Value;
        var horizontalSlop = (int)Math.Ceiling(EdgeTouchSlop * dpi.DpiScaleX);
        var verticalSlop = (int)Math.Ceiling(EdgeTouchSlop * dpi.DpiScaleY);
        var touched = FindTouchedEdge(rect, work, horizontalSlop, verticalSlop);

        if (touched is not null)
        {
            DockToEdge(touched.Value);
            return;
        }

        var undockX = (int)Math.Ceiling(UndockDistance * dpi.DpiScaleX);
        var undockY = (int)Math.Ceiling(UndockDistance * dpi.DpiScaleY);
        if (_isDocked &&
            rect.Left - work.Left > undockX &&
            work.Right - rect.Right > undockX &&
            rect.Top - work.Top > undockY &&
            work.Bottom - rect.Bottom > undockY)
            ExpandFromDock();
    }

    private static DockEdge? FindTouchedEdge(
        NativeRect rect,
        NativeRect work,
        int horizontalSlop,
        int verticalSlop)
    {
        var matches = new[]
        {
            (Edge: DockEdge.Left, Gap: rect.Left - work.Left, Slop: horizontalSlop),
            (Edge: DockEdge.Right, Gap: work.Right - rect.Right, Slop: horizontalSlop),
            (Edge: DockEdge.Top, Gap: rect.Top - work.Top, Slop: verticalSlop),
            (Edge: DockEdge.Bottom, Gap: work.Bottom - rect.Bottom, Slop: verticalSlop)
        }
        .Where(item => item.Gap <= item.Slop)
        .OrderBy(item => item.Gap)
        .ToArray();

        return matches.Length == 0 ? null : matches[0].Edge;
    }

    public static bool RunDockingSelfTest()
    {
        var primary = new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        var secondary = new NativeRect { Left = -1920, Top = 167, Right = 0, Bottom = 1199 };
        return FindTouchedEdge(
                   new NativeRect { Left = 0, Top = 220, Right = 372, Bottom = 498 },
                   primary, 6, 6) == DockEdge.Left
               && FindTouchedEdge(
                   new NativeRect { Left = 1800, Top = 220, Right = 2010, Bottom = 498 },
                   primary, 6, 6) == DockEdge.Right
               && FindTouchedEdge(
                   new NativeRect { Left = 500, Top = 800, Right = 872, Bottom = 1040 },
                   primary, 6, 6) == DockEdge.Bottom
               && FindTouchedEdge(
                   new NativeRect { Left = -1922, Top = 300, Right = -1550, Bottom = 578 },
                   secondary, 6, 6) == DockEdge.Left
               && FindTouchedEdge(
                   new NativeRect { Left = 100, Top = 100, Right = 472, Bottom = 378 },
                   primary, 6, 6) is null;
    }

    private void DockToNearestEdge()
    {
        var geometry = GetWindowGeometry();
        if (geometry is null) return;

        var (_, rect, work, _) = geometry.Value;
        var centerX = (rect.Left + rect.Right) / 2;
        var centerY = (rect.Top + rect.Bottom) / 2;
        var nearest = new[]
        {
            (Edge: DockEdge.Left, Distance: Math.Abs(centerX - work.Left)),
            (Edge: DockEdge.Right, Distance: Math.Abs(work.Right - centerX)),
            (Edge: DockEdge.Top, Distance: Math.Abs(centerY - work.Top)),
            (Edge: DockEdge.Bottom, Distance: Math.Abs(work.Bottom - centerY))
        }.MinBy(item => item.Distance);
        DockToEdge(nearest.Edge);
    }

    private void DockToEdge(DockEdge edge)
    {
        _isDocked = true;
        _dockEdge = edge;
        var isVertical = edge is DockEdge.Left or DockEdge.Right;
        HeaderRow.Height = new GridLength(0);
        AdviceRow.Height = new GridLength(0);
        LimitsRow.Height = new GridLength(1, GridUnitType.Star);
        FooterRow.Height = new GridLength(0);
        LimitsPanel.Visibility = isVertical ? Visibility.Collapsed : Visibility.Visible;
        VerticalLimitsPanel.Visibility = isVertical ? Visibility.Visible : Visibility.Collapsed;
        FiveProgress.Visibility = Visibility.Collapsed;
        WeekProgress.Visibility = Visibility.Collapsed;
        FiveCard.Padding = new Thickness(0);
        WeekCard.Padding = new Thickness(0);
        FiveCard.Background = System.Windows.Media.Brushes.Transparent;
        WeekCard.Background = System.Windows.Media.Brushes.Transparent;
        FiveCard.BorderThickness = new Thickness(0);
        WeekCard.BorderThickness = new Thickness(0);
        CompactDivider.Visibility = isVertical ? Visibility.Collapsed : Visibility.Visible;
        QuotaGap.Width = new GridLength(12);
        FiveLimitHeader.Height = double.NaN;
        WeekLimitHeader.Height = double.NaN;
        FiveTitle.Text = "5 小时";
        WeekTitle.Text = "本周";
        FiveTitle.VerticalAlignment = VerticalAlignment.Center;
        WeekTitle.VerticalAlignment = VerticalAlignment.Center;
        FivePercent.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        WeekPercent.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        FivePercent.VerticalAlignment = VerticalAlignment.Center;
        WeekPercent.VerticalAlignment = VerticalAlignment.Center;
        FivePercent.FontSize = 16;
        WeekPercent.FontSize = 16;
        FivePercent.Foreground = (System.Windows.Media.Brush)FindResource("Ink");
        WeekPercent.Foreground = (System.Windows.Media.Brush)FindResource("Ink");
        FiveLimitHeader.Margin = new Thickness(0, 0, 0, 2);
        WeekLimitHeader.Margin = new Thickness(0, 0, 0, 2);
        FiveReset.Margin = new Thickness(0);
        WeekReset.Margin = new Thickness(0);
        FiveLimitStack.VerticalAlignment = VerticalAlignment.Center;
        WeekLimitStack.VerticalAlignment = VerticalAlignment.Center;
        Width = isVertical ? VerticalCompactWidth : HorizontalCompactWidth;
        Height = isVertical ? VerticalCompactHeight : HorizontalCompactHeight;
        WidgetShell.Effect = null;
        WidgetShell.Margin = new Thickness(0);
        WidgetShell.CornerRadius = new CornerRadius(12);
        LimitsPanel.Margin = new Thickness(10, 5, 10, 4);
        WidgetShell.ToolTip = "拖离屏幕边缘或双击，恢复完整面板";

        UpdateLayout();
        var geometry = GetWindowGeometry();
        if (geometry is null) return;

        var (handle, rect, work, dpi) = geometry.Value;
        var width = (int)Math.Ceiling(Width * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(Height * dpi.DpiScaleY);
        var x = edge switch
        {
            DockEdge.Left => work.Left,
            DockEdge.Right => work.Right - width,
            _ => Math.Clamp(rect.Left, work.Left, work.Right - width)
        };
        var y = edge switch
        {
            DockEdge.Top => work.Top,
            DockEdge.Bottom => work.Bottom - height,
            _ => Math.Clamp(rect.Top, work.Top, work.Bottom - height)
        };
        SetWindowPos(
            handle,
            _isPinned ? HwndTopmost : HwndNotTopmost,
            x, y, width, height,
            SwpNoActivate);
    }

    private void ExpandFromDock()
    {
        if (!_isDocked) return;

        var previousEdge = _dockEdge;
        _isDocked = false;
        HeaderRow.Height = new GridLength(54);
        AdviceRow.Height = new GridLength(76);
        LimitsRow.Height = new GridLength(142);
        FooterRow.Height = new GridLength(1, GridUnitType.Star);
        Width = FullWidth;
        Height = FullHeight;
        WidgetShell.Effect =
            (System.Windows.Media.Effects.Effect)FindResource("SoftShadow");
        WidgetShell.Margin = new Thickness(12);
        WidgetShell.CornerRadius = new CornerRadius(18);
        LimitsPanel.Visibility = Visibility.Visible;
        VerticalLimitsPanel.Visibility = Visibility.Collapsed;
        FiveProgress.Visibility = Visibility.Visible;
        WeekProgress.Visibility = Visibility.Visible;
        FiveCard.Padding = new Thickness(15, 13, 15, 13);
        WeekCard.Padding = new Thickness(15, 13, 15, 13);
        FiveCard.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        WeekCard.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        FiveCard.BorderThickness = new Thickness(1);
        WeekCard.BorderThickness = new Thickness(1);
        CompactDivider.Visibility = Visibility.Collapsed;
        FiveLimitHeader.Height = 55;
        WeekLimitHeader.Height = 55;
        FiveLimitHeader.Margin = new Thickness(0, 0, 0, 7);
        WeekLimitHeader.Margin = new Thickness(0, 0, 0, 7);
        FiveReset.Margin = new Thickness(0, 8, 0, 0);
        WeekReset.Margin = new Thickness(0, 8, 0, 0);
        FiveTitle.Text = "5 小时额度";
        WeekTitle.Text = "每周额度";
        FiveTitle.VerticalAlignment = VerticalAlignment.Top;
        WeekTitle.VerticalAlignment = VerticalAlignment.Top;
        FivePercent.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        WeekPercent.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        FivePercent.VerticalAlignment = VerticalAlignment.Bottom;
        WeekPercent.VerticalAlignment = VerticalAlignment.Bottom;
        FivePercent.FontSize = 33;
        WeekPercent.FontSize = 33;
        FivePercent.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        WeekPercent.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        FiveLimitStack.VerticalAlignment = VerticalAlignment.Stretch;
        WeekLimitStack.VerticalAlignment = VerticalAlignment.Stretch;
        LimitsPanel.Margin = new Thickness(20, 8, 20, 0);
        WidgetShell.ToolTip = null;

        UpdateLayout();
        var geometry = GetWindowGeometry();
        if (geometry is null) return;

        var (handle, rect, work, dpi) = geometry.Value;
        var width = (int)Math.Ceiling(Width * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(Height * dpi.DpiScaleY);
        var insetX = (int)Math.Ceiling(12 * dpi.DpiScaleX);
        var insetY = (int)Math.Ceiling(12 * dpi.DpiScaleY);
        var x = previousEdge switch
        {
            DockEdge.Left => work.Left + insetX,
            DockEdge.Right => work.Right - width - insetX,
            _ => Math.Clamp(rect.Left, work.Left, work.Right - width)
        };
        var y = previousEdge switch
        {
            DockEdge.Top => work.Top + insetY,
            DockEdge.Bottom => work.Bottom - height - insetY,
            _ => Math.Clamp(rect.Top, work.Top, work.Bottom - height)
        };
        SetWindowPos(
            handle,
            _isPinned ? HwndTopmost : HwndNotTopmost,
            x, y, width, height,
            SwpNoActivate);
    }

    private (IntPtr Handle, NativeRect Rect, NativeRect Work, DpiScale Dpi)? GetWindowGeometry()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            return null;

        const uint monitorDefaultToNearest = 2;
        var monitor = MonitorFromWindow(handle, monitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return null;

        return (handle, rect, info.WorkArea, VisualTreeHelper.GetDpi(this));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke((Action)PromptForCloseChoice);
            return;
        }

        _timer.Stop();
        _positionSaveTimer.Stop();
        if (!_previewMode && !_isDocked) WindowPlacementStore.Save(Left, Top);
        if (!_previewMode) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _reader.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private enum DockEdge
    {
        Left,
        Right,
        Top,
        Bottom
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
