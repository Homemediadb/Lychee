using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Lychee.Gui;

// WinUI only lets a control set its own cursor, so it's set from the root
static class ClickCursor
{
    static readonly InputCursor Hand = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicProperties, typeof(UIElement))]
    [SuppressMessage("Security", "S3011", Justification = "ProtectedCursor is the only way to set another control's cursor")]
    static readonly PropertyInfo CursorProperty = typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void Attach(UIElement root) =>
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);

    static void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is not (ButtonBase or ToggleSwitch or SelectorBarItem or TabViewItem or ComboBox)) continue;
            var cursor = d is Control { IsEnabled: false } ? null : Hand;
            if (!ReferenceEquals(CursorProperty.GetValue(d), cursor)) CursorProperty.SetValue(d, cursor);
            return;
        }
    }
}
