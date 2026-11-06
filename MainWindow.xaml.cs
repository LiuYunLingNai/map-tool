using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using BiliRemote.Services;
using Microsoft.Web.WebView2.Core;

namespace BiliRemote;

public partial class MainWindow : Window
{
    private const string HomeUrl = "https://www.bilibili.com/";
    private readonly AppSettings settings;
    private readonly Dictionary<string, TaskCompletionSource<PlaybackResult>> pending = [];
    private readonly List<CoreWebView2Frame> frames = [];
    private readonly SemaphoreSlim commandLock = new(1, 1);
    private HotkeyManager? hotkeys;
    private WindowToolsMenu? toolsMenu;
    private bool initialized;
    private bool closing;
    private bool navigating;
    private string? hotkeyError;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MainWindow()
    {
        settings = App.SelfTest ? new() : AppSettings.Load();
        var seekSeconds = settings.SeekSeconds;
        InitializeComponent();
        settings.SeekSeconds = seekSeconds;
        StepBox.SelectedIndex = settings.SeekSeconds switch { 5 => 0, 30 => 2, _ => 1 };
        GlobalHotkeysBox.IsChecked = settings.GlobalHotkeys;
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTop;
        Topmost = settings.AlwaysOnTop;
        UpdateStepLabels();
        UpdateHotkeyLabels();
        UpdateToolbarVisibility();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        hotkeys = new HotkeyManager(new WindowInteropHelper(this).Handle);
        toolsMenu = new WindowToolsMenu(new WindowInteropHelper(this).Handle);
        toolsMenu.Selected += action => Dispatcher.BeginInvoke(new Action(() =>
        {
            switch (action)
            {
                case "toolbars": ToggleToolbars_Click(this, new RoutedEventArgs()); break;
                case "hotkeys": HotkeySettings_Click(this, new RoutedEventArgs()); break;
                case "topmost":
                    AlwaysOnTopBox.IsChecked = !settings.AlwaysOnTop;
                    AlwaysOnTop_Click(this, new RoutedEventArgs());
                    break;
            }
        }));
        toolsMenu.Update(settings.ToolbarsHidden, settings.AlwaysOnTop);
        hotkeys.Pressed += action => Dispatcher.BeginInvoke(new Action(async () => await ControlAsync(action)));
        ConfigureHotkeys();
        await InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingTitle.Text = "正在打开 B 站…";
        LoadingDetail.Text = "网页载入后，选择视频即可使用快捷键。";
        RetryButton.Visibility = Visibility.Collapsed;
        try
        {
            if (!initialized)
            {
                var profile = Path.Combine(App.SelfTest ? Path.Combine(AppContext.BaseDirectory, "self-test") : AppSettings.DataDirectory, "BrowserProfile");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                await Browser.EnsureCoreWebView2Async(environment);
                var core = Browser.CoreWebView2;
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BiliRemote.Browser.VideoController.js")!;
                using var reader = new StreamReader(stream);
                await core.AddScriptToExecuteOnDocumentCreatedAsync(await reader.ReadToEndAsync());
                core.WebMessageReceived += (_, args) => ReceiveResult(args);
                core.FrameCreated += (_, args) =>
                {
                    var frame = args.Frame;
                    frames.Add(frame);
                    frame.WebMessageReceived += (_, message) => ReceiveResult(message);
                    frame.Destroyed += (_, _) => frames.Remove(frame);
                };
                core.NavigationStarting += NavigationStarting;
                core.NavigationCompleted += NavigationCompleted;
                core.SourceChanged += (_, _) => AddressBox.Text = core.Source;
                core.HistoryChanged += (_, _) =>
                {
                    BackButton.IsEnabled = core.CanGoBack;
                    ForwardButton.IsEnabled = core.CanGoForward;
                };
                core.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    Navigate(args.Uri);
                };
                core.ProcessFailed += (_, args) => ShowError("浏览器进程退出", $"{args.ProcessFailedKind}。请关闭程序后重新打开。");
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.IsStatusBarEnabled = false;
                initialized = true;
            }
            if (App.SelfTest)
            {
                await RunSelfTestAsync();
                return;
            }
            Browser.CoreWebView2.Navigate(HomeUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("需要 Microsoft Edge WebView2", "未检测到 WebView2 运行时。请从 https://developer.microsoft.com/microsoft-edge/webview2 下载并安装 Evergreen Runtime，然后重试。");
        }
        catch (Exception ex)
        {
            ShowError("网页未能启动", ex.Message);
        }
    }

    private static bool AllowedAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return false;
        var host = uri.IdnHost;
        return host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("b23.tv", StringComparison.OrdinalIgnoreCase)
            || (App.SelfTest && host == "app-test.biliremote.local");
    }

    private void Navigate(string address)
    {
        if (!initialized) return;
        address = address.Trim();
        if (!address.Contains("://")) address = "https://" + address;
        if (!AllowedAddress(address))
        {
            SetStatus("请输入 bilibili.com 或 b23.tv 的网页地址。");
            return;
        }
        Browser.CoreWebView2.Navigate(address);
        Browser.Focus();
    }

    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!AllowedAddress(e.Uri))
        {
            e.Cancel = true;
            SetStatus("此程序仅打开 B 站和 b23.tv 链接。");
            return;
        }
        navigating = true;
        SetStatus("正在载入网页…");
    }

    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        navigating = false;
        if (e.IsSuccess)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            TransportPanel.IsEnabled = true;
            SetStatus("网页已打开。选择视频后即可使用快捷键。");
        }
        else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            ShowError("网页加载失败", $"{e.WebErrorStatus}。请检查网络后重试。");
        }
    }

    private void ReceiveResult(CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!AllowedAddress(args.Source)) return;
        try
        {
            var result = JsonSerializer.Deserialize<PlaybackResult>(args.WebMessageAsJson, JsonOptions);
            if (result is { Type: "bili-remote-result", RequestId: not null }
                && pending.TryGetValue(result.RequestId, out var completion)) completion.TrySetResult(result);
        }
        catch (JsonException) { /* Ignore unrelated messages from the website. */ }
    }

    private async Task<PlaybackResult> ExecuteInDocumentAsync(string action, Func<string, Task<string>> execute)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<PlaybackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            var command = JsonSerializer.Serialize(new { action, seconds = settings.SeekSeconds, requestId = id });
            await execute($"void window.__biliRemote?.execute({command});");
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(4));
        }
        finally { pending.Remove(id); }
    }

    internal async Task<PlaybackResult> SendCommandAsync(string action)
    {
        var result = await ExecuteInDocumentAsync(action, Browser.CoreWebView2.ExecuteScriptAsync);
        if (result.Code != "no-video") return result;
        foreach (var frame in frames.ToArray())
        {
            try
            {
                result = await ExecuteInDocumentAsync(action, frame.ExecuteScriptAsync);
                if (result.Code != "no-video") return result;
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
            { /* An iframe can disappear or have scripts disabled during navigation. */ }
        }
        return result;
    }

    private async Task ControlAsync(string action)
    {
        if (!initialized || closing) return;
        if (navigating) { SetStatus("网页正在加载，请稍后再试。"); return; }
        // Ignore repeated keystrokes while a command is still being processed.
        if (!await commandLock.WaitAsync(0)) return;
        try
        {
            var result = await SendCommandAsync(action);
            var time = result.Ok ? $" · {FormatTime(result.CurrentTime)} / {(result.Duration.HasValue ? FormatTime(result.Duration.Value) : "直播")}" : "";
            SetStatus(result.Message + time);
        }
        catch (TimeoutException) { SetStatus("播放器未响应，请等待网页加载完成，或刷新后重试。"); }
        catch (Exception ex) { if (!closing) SetStatus($"操作失败：{ex.Message}"); }
        finally { commandLock.Release(); }
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "00:00";
        var time = TimeSpan.FromSeconds(Math.Min(seconds, 315360000));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    private void SetStatus(string message)
    {
        if (closing) return;
        StatusText.Text = hotkeyError is null ? message : $"{message}  ·  全局快捷键不可用：{hotkeyError}";
    }

    private void ConfigureHotkeys()
    {
        if (hotkeys is null) return;
        hotkeyError = settings.GlobalHotkeys ? hotkeys.Enable(settings.Hotkeys) : null;
        if (!settings.GlobalHotkeys) hotkeys.Disable();
        if (hotkeyError is not null) GlobalHotkeysBox.IsChecked = false;
        GlobalHotkeysBox.IsChecked = settings.GlobalHotkeys && hotkeyError is null;
        UpdateHotkeyLabels();
    }

    private void UpdateHotkeyLabels()
    {
        var bindings = settings.Hotkeys.Bindings;
        ToggleButton.ToolTip = bindings["toggle"].Label;
        RewindButton.ToolTip = bindings["backward"].Label;
        SeekButton.ToolTip = bindings["forward"].Label;
        StopButton.ToolTip = bindings["stop"].Label + "：暂停并回到开头";
        ShortcutHint.Text = settings.GlobalHotkeys && hotkeyError is null
            ? string.Join("    ·    ", HotkeyConfiguration.Actions.Select(item => $"{bindings[item.Action].Label}：{item.Name}"))
            : "全局快捷键已关闭或被占用。可使用下方按钮，或网页自带的空格和方向键。";
    }

    private void HotkeySettings_Click(object sender, RoutedEventArgs e)
    {
        // Release the active shortcuts so recording them does not control the video.
        hotkeys?.Disable();
        var dialog = new HotkeySettingsWindow(settings.Hotkeys, candidate =>
        {
            if (settings.GlobalHotkeys && hotkeys?.Enable(candidate) is { } error) return error;
            settings.Hotkeys = candidate;
            return null;
        }) { Owner = this };
        bool saved;
        try { saved = dialog.ShowDialog() == true; }
        finally { ConfigureHotkeys(); }
        if (saved) SaveSettings("自定义快捷键已保存。");
        else SetStatus("快捷键设置未修改。");
    }

    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        settings.AlwaysOnTop = AlwaysOnTopBox.IsChecked == true;
        Topmost = settings.AlwaysOnTop;
        toolsMenu?.Update(settings.ToolbarsHidden, settings.AlwaysOnTop);
        SaveSettings(Topmost ? "窗口置顶已开启。" : "窗口置顶已关闭。");
    }

    internal void SetToolbarsHidden(bool hidden)
    {
        settings.ToolbarsHidden = hidden;
        UpdateToolbarVisibility();
    }

    private void UpdateToolbarVisibility()
    {
        NavigationToolbar.Visibility = PlaybackToolbar.Visibility = settings.ToolbarsHidden ? Visibility.Collapsed : Visibility.Visible;
        MinHeight = settings.ToolbarsHidden ? 240 : 480;
        toolsMenu?.Update(settings.ToolbarsHidden, settings.AlwaysOnTop);
    }

    private void ToggleToolbars_Click(object sender, RoutedEventArgs e)
    {
        SetToolbarsHidden(!settings.ToolbarsHidden);
        SaveSettings(settings.ToolbarsHidden ? "工具栏已收起。右键标题栏选择“显示工具栏”即可恢复。" : "工具栏已显示。");
    }

    private void SaveSettings(string message)
    {
        if (App.SelfTest) { SetStatus(message); return; }
        try { settings.Save(); SetStatus(message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SetStatus($"当前设置已生效，但保存失败：{ex.Message}"); }
    }

    private void ShowError(string title, string detail)
    {
        LoadingTitle.Text = title;
        LoadingDetail.Text = detail;
        LoadingPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
        TransportPanel.IsEnabled = false;
        SetStatus(title);
    }

    private void UpdateStepLabels()
    {
        if (RewindButton is null || SeekButton is null) return;
        RewindButton.Content = $"−{settings.SeekSeconds} 秒";
        SeekButton.Content = $"+{settings.SeekSeconds} 秒";
    }

    private void StepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StepBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var seconds))
        {
            settings.SeekSeconds = seconds;
            UpdateStepLabels();
        }
    }

    private void GlobalHotkeys_Click(object sender, RoutedEventArgs e)
    {
        settings.GlobalHotkeys = GlobalHotkeysBox.IsChecked == true;
        ConfigureHotkeys();
        SaveSettings(settings.GlobalHotkeys && hotkeyError is null ? "全局快捷键已开启，切换到其他应用后仍可控制视频。" : "可继续使用播放控制按钮。");
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Navigate(AddressBox.Text);
    }
    private void Navigate_Click(object sender, RoutedEventArgs e) => Navigate(AddressBox.Text);
    private void Home_Click(object sender, RoutedEventArgs e) => Navigate(HomeUrl);
    private void Back_Click(object sender, RoutedEventArgs e) { if (initialized && Browser.CoreWebView2.CanGoBack) Browser.CoreWebView2.GoBack(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (initialized && Browser.CoreWebView2.CanGoForward) Browser.CoreWebView2.GoForward(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) { if (initialized) Browser.CoreWebView2.Reload(); }
    private async void Retry_Click(object sender, RoutedEventArgs e) => await InitializeBrowserAsync();
    private async void Toggle_Click(object sender, RoutedEventArgs e) => await ControlAsync("toggle");
    private async void Stop_Click(object sender, RoutedEventArgs e) => await ControlAsync("stop");
    private async void Rewind_Click(object sender, RoutedEventArgs e) => await ControlAsync("backward");
    private async void Seek_Click(object sender, RoutedEventArgs e) => await ControlAsync("forward");

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        closing = true;
        hotkeys?.Dispose();
        toolsMenu?.Dispose();
        foreach (var completion in pending.Values) completion.TrySetCanceled();
        if (!App.SelfTest)
        {
            try { settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The browser can still be closed if settings cannot be saved. */ }
        }
        Browser.Dispose();
    }

    private async Task RunSelfTestAsync()
    {
        await IntegrationVerification.RunAsync(this, Browser.CoreWebView2, SendCommandAsync);
    }
}

public sealed class PlaybackResult
{
    public string? Type { get; set; }
    public string? RequestId { get; set; }
    public bool Ok { get; set; }
    public string? Code { get; set; }
    public string Message { get; set; } = "";
    public bool Paused { get; set; }
    public double CurrentTime { get; set; }
    public double? Duration { get; set; }
}
