using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace BiliRemote;

// Optional --self-test exercises the real Chromium media engine without a B站 login or network.
internal static class IntegrationVerification
{
    public static async Task RunAsync(Window window, CoreWebView2 core, Func<string, Task<PlaybackResult>> command)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "self-test");
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        var exitCode = 0;
        try
        {
            WriteWave(Path.Combine(directory, "sample.wav"));
            File.WriteAllText(Path.Combine(directory, "video.html"), """
                <!doctype html><html lang="zh-CN"><meta charset="utf-8">
                <title>BiliRemote 本地播放验证</title>
                <body style="font-family: sans-serif; padding: 24px">
                <h1>本地播放验证</h1><div class="bpx-player-container">
                <video id="video" src="sample.wav" controls muted preload="auto" style="width:640px;height:360px"></video>
                </div></body></html>
                """, Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "frame.html"), """
                <!doctype html><html><meta charset="utf-8"><title>iframe 验证</title>
                <iframe src="video.html" width="760" height="520"></iframe></html>
                """, Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "empty.html"), "<!doctype html><html><title>空页面验证</title>没有视频</html>");
            core.SetVirtualHostNameToFolderMapping("app-test.biliremote.local", directory, CoreWebView2HostResourceAccessKind.Allow);

            await LoadAsync(core, "https://app-test.biliremote.local/video.html");
            await WaitForMediaAsync(core, "document.querySelector('video')");
            if (window is MainWindow player) VerifyWindowLayout(player, directory, report);
            var result = await command("toggle");
            Require(result.Ok && !result.Paused, "native video play", result, report);
            result = await command("toggle");
            Require(result.Ok && result.Paused, "native video pause", result, report);
            result = await command("forward");
            Require(result.Ok && result.CurrentTime >= 10 && result.CurrentTime < 12, "seek forward 10 seconds", result, report);
            result = await command("backward");
            Require(result.Ok && result.CurrentTime < 2, "seek backward 10 seconds", result, report);
            await command("forward");
            result = await command("stop");
            Require(result.Ok && result.Paused && result.CurrentTime < 0.1, "stop pauses and rewinds", result, report);

            await LoadAsync(core, "https://app-test.biliremote.local/frame.html");
            await WaitForMediaAsync(core, "document.querySelector('iframe')?.contentDocument?.querySelector('video')");
            result = await command("forward");
            Require(result.Ok && result.CurrentTime >= 10, "iframe media command and result bridge", result, report);
            result = await command("stop");
            Require(result.Ok && result.Paused && result.CurrentTime < 0.1, "iframe stop", result, report);

            await LoadAsync(core, "https://app-test.biliremote.local/empty.html");
            result = await command("toggle");
            Require(!result.Ok && result.Code == "no-video", "empty page returns actionable feedback", result, report);
        }
        catch (Exception ex)
        {
            report.Add("FAIL: " + ex);
            exitCode = 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(directory, "results.txt"), report);
            Application.Current.Shutdown(exitCode);
        }
    }

    private static void VerifyWindowLayout(MainWindow player, string directory, List<string> report)
    {
        foreach (var width in new[] { 1280, 375 })
        {
            player.SetToolbarsHidden(false);
            player.Width = width;
            player.Height = 840;
            player.UpdateLayout();
            CaptureLayout(player, Path.Combine(directory, $"toolbars-{width}.png"));
            if (!player.NavigationToolbar.IsVisible || !player.PlaybackToolbar.IsVisible || player.Browser.ActualHeight < 100)
                throw new InvalidOperationException($"Expanded layout unusable at width {width}");
            report.Add($"PASS: visible toolbars and usable browser at width {width}");
        }
        player.SetToolbarsHidden(true);
        player.Width = 320;
        player.Height = 240;
        player.UpdateLayout();
        CaptureLayout(player, Path.Combine(directory, "compact-320.png"));
        if (player.NavigationToolbar.Visibility != Visibility.Collapsed || player.PlaybackToolbar.Visibility != Visibility.Collapsed
            || player.MinWidth != 320 || player.MinHeight != 240 || player.Browser.ActualHeight < 180)
            throw new InvalidOperationException("Compact mode did not free the client area at 320x240.");
        report.Add("PASS: compact mode hides both toolbars and supports 320x240 window");
        player.SetToolbarsHidden(false);
        player.Width = 1280;
        player.Height = 840;
        player.UpdateLayout();
        if (!player.NavigationToolbar.IsVisible || !player.PlaybackToolbar.IsVisible)
            throw new InvalidOperationException("Toolbars failed to restore.");
        report.Add("PASS: toolbars restore after compact mode without replacing browser");
    }

    private static void CaptureLayout(Window window, string path)
    {
        // WPF renders the native toolbars; the WebView2 HWND area is deliberately blank.
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void Require(bool passed, string label, PlaybackResult result, List<string> report)
    {
        report.Add($"{(passed ? "PASS" : "FAIL")}: {label}: {JsonSerializer.Serialize(result)}");
        if (!passed) throw new InvalidOperationException(label);
    }

    private static async Task LoadAsync(CoreWebView2 core, string url)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess) completion.TrySetResult();
            else completion.TrySetException(new IOException($"Navigation failed: {args.WebErrorStatus}"));
        }
        core.NavigationCompleted += Complete;
        try
        {
            core.Navigate(url);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { core.NavigationCompleted -= Complete; }
    }

    private static async Task WaitForMediaAsync(CoreWebView2 core, string selector)
    {
        for (var i = 0; i < 100; i++)
        {
            var ready = await core.ExecuteScriptAsync($"(() => {{ const v = {selector}; return !!v && v.readyState >= 2 && v.seekable.length > 0; }})()");
            if (ready == "true") return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Local media did not become seekable.");
    }

    private static void WriteWave(string path)
    {
        const int rate = 8000, sampleCount = rate * 90;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + sampleCount * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(sampleCount * 2);
        for (var i = 0; i < sampleCount; i++) writer.Write((short)0);
    }
}
