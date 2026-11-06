using System.Text.Json;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Documents;
using BiliRemote;
using BiliRemote.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var oldSettings = AppSettings.FromJson("{\"SeekSeconds\":30,\"GlobalHotkeys\":false}");
        Check(oldSettings.SeekSeconds == 30 && !oldSettings.GlobalHotkeys && !oldSettings.AlwaysOnTop
            && oldSettings.Hotkeys.Validate() is null, "older settings retain values and get default bindings");

        var settings = new AppSettings { AlwaysOnTop = true, ToolbarsHidden = true };
        settings.Hotkeys.Bindings["toggle"] = HotkeyBinding.Capture(Key.P, ModifierKeys.Control | ModifierKeys.Shift);
        var restored = AppSettings.FromJson(JsonSerializer.Serialize(settings));
        Check(restored.AlwaysOnTop && restored.ToolbarsHidden && restored.Hotkeys.Bindings["toggle"] == new HotkeyBinding(6, 0x50),
            "custom shortcuts, hidden toolbars and always-on-top survive a settings round trip");
        Check(restored.Hotkeys.Bindings["toggle"].Label == "Ctrl + Shift + P", "custom shortcut display matches modifiers");

        var duplicate = settings.Hotkeys.Clone();
        duplicate.Bindings["stop"] = duplicate.Bindings["toggle"];
        Check(duplicate.Validate()?.Contains("已用于其他操作") == true, "duplicate shortcuts rejected");
        Check(AppSettings.FromJson(JsonSerializer.Serialize(new AppSettings { Hotkeys = duplicate })).Hotkeys.Validate() is null,
            "invalid saved shortcuts recover to defaults");
        Check(AppSettings.FromJson("{\"Hotkeys\":null,\"AlwaysOnTop\":true}").AlwaysOnTop,
            "malformed hotkeys recover without losing topmost preference");
        Check(new HotkeyBinding(0, 0x41).Validate() is null && new HotkeyBinding(4, 0x41).Validate() is null
            && new HotkeyBinding(0, 0x20).Validate() is null && new HotkeyBinding(0, 0x25).Validate() is null
            && new HotkeyBinding(0, 0x09).Validate() is null && new HotkeyBinding(0, 0x1B).Validate() is null,
            "plain letter, shift-only, space, arrow, Tab and Escape accepted");
        Check(new HotkeyBinding(0, 0x77).Validate() is null, "standalone F8 accepted");
        Check(new HotkeyBinding(0, 0x7B).Validate() is null && new HotkeyBinding(1, 0x73).Validate() is null
            && new HotkeyBinding(3, 0x2E).Validate() is null, "system combinations deferred to Windows registration");
        Check(HotkeyBinding.Capture(Key.P, ModifierKeys.Windows) == new HotkeyBinding(8, 0x50)
            && new HotkeyBinding(8, 0x50).Label == "Win + P", "Windows modifiers preserved and labeled");
        Check(HotkeyBinding.ResolveKey(Key.ImeProcessed, Key.None, Key.A, Key.None) == Key.A
            && HotkeyBinding.ResolveKey(Key.System, Key.F4, Key.None, Key.None) == Key.F4
            && HotkeyBinding.ResolveKey(Key.DeadCharProcessed, Key.None, Key.None, Key.OemQuotes) == Key.OemQuotes,
            "IME, system and dead-character events resolve to actual keys");
        Check(new HotkeyBinding(0, 0xE5).Validate() is not null && new HotkeyBinding(0, 0xE7).Validate() is not null,
            "IME processed and text packet pseudo-keys rejected");
        var invalidIme = new AppSettings { AlwaysOnTop = true };
        invalidIme.Hotkeys.Bindings["toggle"] = new(0, 0xE5);
        var recoveredIme = AppSettings.FromJson(JsonSerializer.Serialize(invalidIme));
        Check(recoveredIme.Hotkeys.Bindings["toggle"] == new HotkeyBinding(3, 0x20) && recoveredIme.AlwaysOnTop,
            "previously saved ImeProcessed shortcut recovers to defaults");

        using var firstSource = new HwndSource(new HwndSourceParameters("Hotkey verification 1") { Width = 0, Height = 0 });
        using var secondSource = new HwndSource(new HwndSourceParameters("Hotkey verification 2") { Width = 0, Height = 0 });
        using var first = new HotkeyManager(firstSource.Handle);
        using var second = new HotkeyManager(secondSource.Handle);
        var configuration = new HotkeyConfiguration();
        string? registrationError = "No free test shortcut set.";
        foreach (var start in new[] { 0x36, 0x41, 0x47, 0x51, 0x70, 0x80 })
        {
            var index = 0;
            foreach (var (action, _) in HotkeyConfiguration.Actions) configuration.Bindings[action] = new(7, start + index++);
            registrationError = first.Enable(configuration);
            if (registrationError is null) break;
        }
        if (registrationError is not null) Console.WriteLine(registrationError);
        Check(registrationError is null, "custom keys registered with Windows");
        Check(second.Enable(configuration) is not null, "Windows reports another owner conflict");
        var partiallyConflicting = configuration.Clone();
        partiallyConflicting.Bindings["toggle"] = new(6, 0x39);
        Check(second.Enable(partiallyConflicting) is not null, "later binding conflict handled");
        first.Disable();
        Check(first.Enable(partiallyConflicting) is null, "failed registration releases earlier keys");
        first.Disable();
        Check(second.Enable(configuration) is null, "shortcuts released and available for registration again");
        second.Disable();
        var singleKeys = new HotkeyConfiguration();
        singleKeys.Bindings["toggle"] = new(0, 0x41);
        singleKeys.Bindings["backward"] = new(0, 0x25);
        singleKeys.Bindings["forward"] = new(0, 0x27);
        singleKeys.Bindings["stop"] = new(0, 0x53);
        Check(first.Enable(singleKeys) is null, "standalone letter and arrow keys registered with Windows");
        string? playbackAction = null;
        first.Pressed += action => playbackAction = action;
        SendMessage(firstSource.Handle, 0x0312, (IntPtr)0xB102, IntPtr.Zero);
        Check(playbackAction == "backward", "standalone binding dispatches correct playback action");
        first.Disable();

        var dialog = new HotkeySettingsWindow(settings.Hotkeys, _ => null);
        Check(!InputMethod.GetIsInputMethodEnabled((DependencyObject)dialog.FindName("ToggleKeyBox")),
            "shortcut recording fields disable IME composition");
        var content = (FrameworkElement)dialog.Content;
        dialog.Content = null;
        var surface = new Border { Background = Brushes.White, Resources = dialog.Resources, Child = content };
        TextElement.SetFontFamily(surface, dialog.FontFamily);
        TextElement.SetFontSize(surface, dialog.FontSize);
        TextElement.SetForeground(surface, dialog.Foreground);
        surface.Measure(new Size(560, double.PositiveInfinity));
        surface.Arrange(new Rect(0, 0, 560, surface.DesiredSize.Height));
        surface.UpdateLayout();
        var image = new RenderTargetBitmap(560, (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var output = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "settings-preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var file = File.Create(output)) encoder.Save(file);
        Check(image.PixelHeight > 350, "settings dialog renders with four fields and actions");
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Topmost = true };
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        Check((GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0, "always-on-top enabled in native window style");
        window.Topmost = false;
        Check((GetWindowLongPtr(handle, -20).ToInt64() & 8) == 0, "always-on-top disabled in native window style");
        window.Close();

        using var tools = new WindowToolsMenu(firstSource.Handle);
        var systemMenu = GetSystemMenu(firstSource.Handle, false);
        tools.Update(true, true);
        var label = new System.Text.StringBuilder(80);
        GetMenuString(systemMenu, 0x1E00, label, label.Capacity, 0);
        Check(label.ToString() == "显示工具栏", "native title-bar menu exposes toolbar recovery");
        string? selected = null;
        tools.Selected += action => selected = action;
        SendMessage(firstSource.Handle, 0x0112, (IntPtr)0x1E00, IntPtr.Zero);
        Check(selected == "toolbars", "native menu command reaches toolbar toggle handler");
        tools.Update(false, false);
        GetMenuString(systemMenu, 0x1E00, label, label.Capacity, 0);
        Check(label.ToString() == "收起工具栏", "native menu switches back to collapse action");
    }

    private static void Check(bool passed, string name)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) throw new InvalidOperationException(name);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool revert);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr menu, uint item, System.Text.StringBuilder label, int maxCount, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
