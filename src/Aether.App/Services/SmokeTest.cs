using Aether.App.Views;
using Aether.Presentation.Infrastructure;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Aether.App.Services;

/// <summary>
/// CI verification: navigates to every page, waits for it to load, captures a screenshot, opens the launcher,
/// command palette and a widget, then exits with a non-zero code if anything failed.
/// </summary>
public static class SmokeTest
{
    public static async Task RunAsync(MainWindow window, string reportPath)
    {
        var lines = new List<string>();
        var failures = 0;
        var shots = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "screenshots");
        Directory.CreateDirectory(shots);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1480, 940));
        await Task.Delay(2500);
        var nav = App.Get<INavigationService>();
        foreach (var item in NavigationCatalog.All)
        {
            try
            {
                nav.Navigate(item.Key);
                await Task.Delay(1800);
                var error = window.CurrentPageError();
                if (error is not null) { failures++; lines.Add($"FAIL {item.Key}: {error}"); }
                else lines.Add($"OK   {item.Key}");
                await CaptureAsync(window, Path.Combine(shots, $"{(int)item.Key:00}-{item.Key}.png"));
            }
            catch (Exception ex) { failures++; lines.Add($"FAIL {item.Key}: {ex}"); }
        }
        try
        {
            nav.Navigate(PageKey.Overview);
            await Task.Delay(800);
            App.Get<Aether.Presentation.ViewModels.CommandPaletteViewModel>().Open();
            App.Get<Aether.Presentation.ViewModels.CommandPaletteViewModel>().Query = "theme";
            await Task.Delay(1000);
            await CaptureAsync(window, Path.Combine(shots, "20-CommandPalette.png"));
            App.Get<Aether.Presentation.ViewModels.CommandPaletteViewModel>().Close();
            lines.Add("OK   CommandPalette");
            App.Get<Aether.WidgetEngine.WidgetManager>().AddWidget("clock");
            App.Get<Aether.WidgetEngine.WidgetManager>().AddWidget("system");
            await App.Get<WidgetHost>().StartAsync();
            await Task.Delay(2500);
            lines.Add($"OK   Widgets ({App.Get<WidgetHost>().WindowCount} windows)");
            await App.Get<WidgetHost>().StopAsync();
            App.Get<LauncherHost>().Toggle();
            await Task.Delay(1500);
            lines.Add("OK   Launcher");
            App.Get<LauncherHost>().Hide();
        }
        catch (Exception ex) { failures++; lines.Add($"FAIL extras: {ex}"); }
        lines.Add(failures == 0 ? "SMOKE TEST PASSED" : $"SMOKE TEST FAILED ({failures})");
        await File.WriteAllLinesAsync(reportPath, lines);
        await App.ExitAppAsync(failures == 0 ? 0 : 1);
    }

    public static async Task CaptureAsync(MainWindow window, string path)
    {
        if (window.Content is not Microsoft.UI.Xaml.UIElement root) return;
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(root);
        var pixels = await rtb.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync();
        stream.Seek(0);
        await using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }
}
