using System.Text.Json.Serialization;
using System.Windows.Input;

namespace BiliRemote.Services;

public sealed record HotkeyBinding(uint Modifiers, int VirtualKey)
{
    [JsonIgnore]
    public string Label
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & 2) != 0) parts.Add("Ctrl");
            if ((Modifiers & 1) != 0) parts.Add("Alt");
            if ((Modifiers & 4) != 0) parts.Add("Shift");
            if ((Modifiers & 8) != 0) parts.Add("Win");
            parts.Add(VirtualKey switch
            {
                0x20 => "空格", 0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓",
                0x0D => "Enter", 0x1B => "Esc", 0x21 => "PageUp", 0x22 => "PageDown",
                0x24 => "Home", 0x23 => "End", 0x2D => "Insert", 0x2E => "Delete",
                >= 0x30 and <= 0x39 => ((char)VirtualKey).ToString(),
                _ => KeyInterop.KeyFromVirtualKey(VirtualKey).ToString()
            });
            return string.Join(" + ", parts);
        }
    }

    public string? Validate()
    {
        if ((Modifiers & ~15u) != 0) return "快捷键包含无法识别的修饰键。";
        var key = KeyInterop.KeyFromVirtualKey(VirtualKey);
        if (VirtualKey is < 1 or > 254 || key is Key.None or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return "请按下一个有效按键，不能只使用修饰键。";
        return null;
    }

    public static HotkeyBinding Capture(Key key, ModifierKeys modifiers)
    {
        uint nativeModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) nativeModifiers |= 1;
        if (modifiers.HasFlag(ModifierKeys.Control)) nativeModifiers |= 2;
        if (modifiers.HasFlag(ModifierKeys.Shift)) nativeModifiers |= 4;
        if (modifiers.HasFlag(ModifierKeys.Windows)) nativeModifiers |= 8;
        return new(nativeModifiers, KeyInterop.VirtualKeyFromKey(key));
    }
}

public sealed class HotkeyConfiguration
{
    public static readonly (string Action, string Name)[] Actions =
    [
        ("toggle", "播放 / 暂停"), ("backward", "后退"), ("forward", "前进"), ("stop", "停止并回到开头")
    ];

    public Dictionary<string, HotkeyBinding> Bindings { get; set; } = Defaults();

    private static Dictionary<string, HotkeyBinding> Defaults() => new()
    {
        ["toggle"] = new(3, 0x20), ["backward"] = new(3, 0x25),
        ["forward"] = new(3, 0x27), ["stop"] = new(3, 0x53)
    };

    public HotkeyConfiguration Clone() => new() { Bindings = new(Bindings) };

    public string? Validate()
    {
        if (Bindings is null) return "快捷键设置为空。";
        var seen = new HashSet<HotkeyBinding>();
        foreach (var (action, name) in Actions)
        {
            if (!Bindings.TryGetValue(action, out var binding) || binding is null) return $"请设置“{name}”的快捷键。";
            if (binding.Validate() is { } error) return $"{name}：{error}";
            if (!seen.Add(binding)) return $"{name}：{binding.Label} 已用于其他操作，请选择不同的快捷键。";
        }
        return null;
    }
}
