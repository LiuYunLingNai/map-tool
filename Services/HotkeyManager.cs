using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BiliRemote.Services;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private readonly HwndSource source;
    private readonly Dictionary<int, string> registered = [];
    public event Action<string>? Pressed;

    public HotkeyManager(IntPtr windowHandle)
    {
        source = HwndSource.FromHwnd(windowHandle) ?? throw new InvalidOperationException("窗口未就绪。");
        source.AddHook(WindowProc);
    }

    public string? Enable(HotkeyConfiguration configuration)
    {
        if (configuration.Validate() is { } validationError) return validationError;
        Disable();
        var id = 0xB101;
        foreach (var (action, name) in HotkeyConfiguration.Actions)
        {
            var binding = configuration.Bindings[action];
            if (RegisterHotKey(source.Handle, id, binding.Modifiers | NoRepeat, (uint)binding.VirtualKey))
            {
                registered.Add(id++, action);
                continue;
            }
            var reason = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            Disable(); // Never leave a partially registered shortcut set.
            return $"{name}（{binding.Label}）注册失败，可能被其他程序占用。{reason}";
        }
        return null;
    }

    public void Disable()
    {
        foreach (var id in registered.Keys) UnregisterHotKey(source.Handle, id);
        registered.Clear();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        if (registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Disable();
        source.RemoveHook(WindowProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
