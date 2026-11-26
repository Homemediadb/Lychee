using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lychee.Gui;

public sealed partial class MainWindow
{
    const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control,
        Shift = VirtualKeyModifiers.Shift,
        None = VirtualKeyModifiers.None;

    void AddShortcuts()
    {
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        IgnoreStrayEnter();

        OnScan(VirtualKey.B, None, v => v.ToggleView());
        OnScan(VirtualKey.S, Ctrl, v => v.Save());
        OnScan(VirtualKey.C, Ctrl | Shift, v => v.Copy());

        Add(VirtualKey.O, Ctrl, PickFiles);
        Add(VirtualKey.W, Ctrl, () =>
        {
            if (Tabs.SelectedItem is TabViewItem tab)
                CloseTab(tab);
        });
        Add(VirtualKey.Tab, Ctrl, () => CycleTab(1));
        Add(VirtualKey.Tab, Ctrl | Shift, () => CycleTab(-1));
        for (int i = 0; i < 9; i++)
        {
            int index = i;
            Add(VirtualKey.Number1 + i, Ctrl, () =>
            {
                if (index < Tabs.TabItems.Count)
                    Tabs.SelectedIndex = index;
            });
        }
    }

    // An Enter that closes a file dialog repeats over its button: only one pressed here counts
    void IgnoreStrayEnter()
    {
        bool down = false;
        Root.AddHandler(
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(
                (_, e) =>
                {
                    if (e.Key != VirtualKey.Enter)
                        return;
                    if (e.KeyStatus.WasKeyDown && !down)
                        e.Handled = true;
                    else
                        down = true;
                }
            ),
            handledEventsToo: true
        );
        Root.AddHandler(
            UIElement.PreviewKeyUpEvent,
            new KeyEventHandler(
                (_, e) =>
                {
                    if (e.Key != VirtualKey.Enter)
                        return;
                    if (!down)
                        e.Handled = true;
                    down = false;
                }
            ),
            handledEventsToo: true
        );
    }

    void OnScan(VirtualKey key, VirtualKeyModifiers modifiers, Action<ScanView> action) => Add(key, modifiers, () =>
        {
            if (Active is { } view)
                action(view);
        });

    void Add(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            if (modifiers == None && FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or ComboBox)
                return;
            action();
            e.Handled = true;
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    void CycleTab(int step)
    {
        int count = Tabs.TabItems.Count;
        if (count > 0)
            Tabs.SelectedIndex = ((Math.Max(0, Tabs.SelectedIndex) + step) % count + count) % count;
    }
}
