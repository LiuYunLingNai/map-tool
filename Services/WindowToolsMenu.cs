using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BiliRemote.Services;

// Native title-bar menu remains reachable even when WebView2 occupies the whole client area.
public sealed class WindowToolsMenu : IDisposable
{
    private const uint ToggleToolbars = 0x1E00, EditHotkeys = 0x1E10, ToggleTopmost = 0x1E20;
    private readonly HwndSource source;
    private readonly IntPtr menu;
    public event Action<string>? Selected;

    public WindowToolsMenu(IntPtr handle)
    {
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("窗口未就绪。");
        menu = GetSystemMenu(handle, false);
        AppendMenu(menu, 0x0800, 0, "");
        AppendMenu(menu, 0, ToggleToolbars, "收起工具栏");
        AppendMenu(menu, 0, EditHotkeys, "自定义快捷键…");
        AppendMenu(menu, 0, ToggleTopmost, "窗口置顶");
        source.AddHook(WindowProc);
    }

    public void Update(bool hidden, bool topmost)
    {
        ModifyMenu(menu, ToggleToolbars, 0, ToggleToolbars, hidden ? "显示工具栏" : "收起工具栏");
        CheckMenuItem(menu, ToggleTopmost, topmost ? 0x0008u : 0);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0112) return IntPtr.Zero;
        var action = ((uint)wParam.ToInt64() & 0xFFF0) switch
        {
            ToggleToolbars => "toolbars", EditHotkeys => "hotkeys", ToggleTopmost => "topmost", _ => null
        };
        if (action is null) return IntPtr.Zero;
        handled = true;
        Selected?.Invoke(action);
        return IntPtr.Zero;
    }

    public void Dispose() => source.RemoveHook(WindowProc);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool revert);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint item, string label);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ModifyMenu(IntPtr menu, uint position, uint flags, uint item, string label);
    [DllImport("user32.dll")]
    private static extern uint CheckMenuItem(IntPtr menu, uint item, uint flags);
}
