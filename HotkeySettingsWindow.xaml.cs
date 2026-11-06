using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BiliRemote.Services;

namespace BiliRemote;

public partial class HotkeySettingsWindow : Window
{
    private HotkeyConfiguration draft;
    private readonly Func<HotkeyConfiguration, string?> apply;

    public HotkeySettingsWindow(HotkeyConfiguration current, Func<HotkeyConfiguration, string?> apply)
    {
        draft = current.Clone();
        this.apply = apply;
        InitializeComponent();
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        foreach (var box in new[] { ToggleKeyBox, BackwardKeyBox, ForwardKeyBox, StopKeyBox })
            box.Text = draft.Bindings[(string)box.Tag].Label;
    }

    private void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = HotkeyBinding.ResolveKey(e.Key, e.SystemKey, e.ImeProcessedKey, e.DeadCharProcessedKey);
        e.Handled = true;
        if (e.IsRepeat || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var binding = HotkeyBinding.Capture(key, Keyboard.Modifiers);
        if (binding.Validate() is { } error)
        {
            FeedbackText.Text = error;
            return;
        }
        var box = (TextBox)sender;
        draft.Bindings[(string)box.Tag] = binding;
        box.Text = binding.Label;
        FeedbackText.Text = draft.Validate() ?? "按键已记录，点击“保存”应用。";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        draft = new();
        RefreshLabels();
        FeedbackText.Text = "已填入默认快捷键，点击“保存”应用。";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if ((draft.Validate() ?? apply(draft.Clone())) is { } error)
        {
            FeedbackText.Text = error;
            return;
        }
        DialogResult = true;
    }
}
