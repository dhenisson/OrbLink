using System.Windows.Input;

namespace AMS.Transfer.App.Services;

public static class PanelShortcuts
{
    public static bool ShouldPasteImage(Key key, ModifierKeys modifiers, bool isActive, bool childOpen) =>
        key == Key.V && modifiers == ModifierKeys.Control && isActive && !childOpen;
}
