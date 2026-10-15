using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HardHatMediaPlayer.ViewModels;

namespace HardHatMediaPlayer.Controls;

public static class MenuBuilder
{
    public static ContextMenu Show(IEnumerable<MenuAction> actions, UIElement target, PlacementMode placement = PlacementMode.Bottom)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = placement };
        foreach (var a in actions) menu.Items.Add(Build(a));
        if (menu.Items.Count > 0) menu.IsOpen = true;
        return menu;
    }

    private static object Build(MenuAction action)
    {
        if (action.IsSeparator) return new Separator();

        // Escape underscores so names like "The_Wand_S01E05" aren't read as access keys.
        var item = new MenuItem
        {
            Header = action.Label.Replace("_", "__"),
            IsChecked = action.IsChecked,
            InputGestureText = action.Gesture ?? "",
        };
        if (action.Children != null)
        {
            foreach (var child in action.Children) item.Items.Add(Build(child));
        }
        else if (action.Run != null)
        {
            var run = action.Run;
            item.Click += (_, _) => run();
        }
        return item;
    }
}
