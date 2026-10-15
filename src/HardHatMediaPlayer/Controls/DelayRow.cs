using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace HardHatMediaPlayer.Controls;

/// <summary>
/// A labelled delay slider for the subtitle and audio menus: −5 s to +5 s in 50 ms steps, with the value and a reset.
/// Bound two-way to a millisecond property on the player.
/// </summary>
public sealed class DelayRow : StackPanel
{
    public DelayRow(object source, string label, string valuePath, string textPath, long max, long step, string keys)
    {
        Margin = new Thickness(10, 4, 10, 6);
        Width = 260;

        var header = new DockPanel();
        var value = new TextBlock { Foreground = (Brush)Application.Current.Resources["AccentBrush"], FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        value.SetBinding(TextBlock.TextProperty, new Binding(textPath) { Source = source });
        DockPanel.SetDock(value, Dock.Right);
        header.Children.Add(value);
        header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(header);

        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var reset = new Button
        {
            Content = "Reset",
            Style = (Style)Application.Current.Resources["IconButton"],
            Width = double.NaN,
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(10, 0, 0, 0),
            ToolTip = "Back to 0",
        };
        AutomationProperties.SetName(reset, "Reset " + label.ToLowerInvariant());
        DockPanel.SetDock(reset, Dock.Right);
        row.Children.Add(reset);

        var slider = new Slider
        {
            Style = (Style)Application.Current.Resources["MediaSlider"],
            Minimum = -max,
            Maximum = max,
            SmallChange = step,
            LargeChange = step * 10,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Stylus.SetIsPressAndHoldEnabled(slider, false);
        AutomationProperties.SetName(slider, label);
        slider.SetBinding(RangeBase.ValueProperty, new Binding(valuePath) { Source = source, Mode = BindingMode.TwoWay });
        // The wheel nudges by one step, like the keyboard shortcuts.
        slider.MouseWheel += (_, e) =>
        {
            slider.Value += e.Delta > 0 ? step : -step;
            e.Handled = true;
        };
        reset.Click += (_, _) => slider.Value = 0;
        row.Children.Add(slider);
        Children.Add(row);

        Children.Add(new TextBlock
        {
            Text = keys,
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            Margin = new Thickness(0, 6, 0, 0),
        });
    }
}
