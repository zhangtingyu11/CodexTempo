using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexTempo;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--reader-refresh-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(CodexUsageReader.RunRefreshSelfTestCode());
            return;
        }

        var dumpIndex = Array.FindIndex(e.Args, x =>
            x.Equals("--dump-usage", StringComparison.OrdinalIgnoreCase));
        if (dumpIndex >= 0 && dumpIndex + 1 < e.Args.Length)
        {
            try
            {
                using var reader = new CodexUsageReader();
                var snapshot = Task.Run(() => reader.ReadLatestAsync()).GetAwaiter().GetResult();
                File.WriteAllText(e.Args[dumpIndex + 1],
                    JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
                Environment.Exit(snapshot is null ? 1 : 0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(e.Args[dumpIndex + 1] + ".error.txt", ex.ToString());
                Environment.Exit(1);
            }
            return;
        }

        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(
                RecommendationEngine.RunSelfTest() &&
                CodexUsageReader.RunSelfTest() &&
                CodexTempo.MainWindow.RunDockingSelfTest()
                    ? 0
                    : 1);
            return;
        }

        base.OnStartup(e);
        var previewIndex = Array.FindIndex(e.Args, x =>
            x.Equals("--render-preview", StringComparison.OrdinalIgnoreCase));
        if (previewIndex >= 0 && previewIndex + 1 < e.Args.Length)
        {
            try
            {
                var preview = new MainWindow(previewMode: true);
                preview.Show();
                preview.PreparePreview();
                if (e.Args.Contains("--compact-horizontal", StringComparer.OrdinalIgnoreCase))
                    preview.PrepareHorizontalCompactPreview();
                else if (e.Args.Contains("--compact", StringComparer.OrdinalIgnoreCase))
                    preview.PrepareCompactPreview();
                var visual = (FrameworkElement)preview.Content;
                visual.UpdateLayout();
                preview.Dispatcher.Invoke(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Render);
                const double scale = 1.5;
                var bitmap = new RenderTargetBitmap(
                    (int)(preview.Width * scale),
                    (int)(preview.Height * scale),
                    96 * scale,
                    96 * scale,
                    PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(e.Args[previewIndex + 1]))
                {
                    encoder.Save(output);
                    output.Flush(true);
                }
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(e.Args[previewIndex + 1] + ".error.txt", ex.ToString());
                Shutdown(1);
            }
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
