using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HardHatMediaPlayer.ViewModels;

namespace HardHatMediaPlayer.Controls;

public static class MenuBuilder
{
    /// <param name="footer">Extra content under the items, such as a slider; clicking it doesn't close the menu.</param>
    public static ContextMenu Show(IEnumerable<MenuAction> actions, UIElement target, PlacementMode placement = PlacementMode.Bottom, UIElement? footer = null)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = placement };
        foreach (var a in actions) menu.Items.Add(Build(a));
        if (footer != null)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            // Hosted in a plain item that neither highlights nor closes the menu when clicked.
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            menu.Items.Add(new MenuItem
            {
                Header = footer,
                StaysOpenOnClick = true,
                Focusable = false,
                Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter },
            });
        }
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
            // Checkable so screen readers (and UI Automation) report the tick.
            IsCheckable = action.IsChecked,
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
