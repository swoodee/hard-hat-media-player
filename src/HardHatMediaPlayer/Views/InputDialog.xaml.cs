using System.Windows;
using System.Windows.Input;

namespace HardHatMediaPlayer.Views;

/// <summary>Small dark dialog used for text prompts and confirmations.</summary>
public partial class InputDialog : Window
{
    private InputDialog(Window? owner, string title, string okText)
    {
        InitializeComponent();
        Owner = owner;
        TitleText.Text = title;
        OkButton.Content = okText;
    }

    public static string? Prompt(Window? owner, string title, string hint, string initial, string okText)
    {
        var dialog = new InputDialog(owner, title, okText);
        dialog.Input.Visibility = Visibility.Visible;
        dialog.Input.Tag = hint;
        dialog.Input.Text = initial;
        dialog.Loaded += (_, _) =>
        {
            dialog.Input.Focus();
            dialog.Input.SelectAll();
        };
        return dialog.ShowDialog() == true ? dialog.Input.Text : null;
    }

    public static bool Confirm(Window? owner, string title, string message, string okText)
    {
        var dialog = new InputDialog(owner, title, okText);
        dialog.MessageText.Text = message;
        dialog.MessageText.Visibility = Visibility.Visible;
        if (okText == "OK") dialog.CancelButton.Visibility = Visibility.Collapsed;
        return dialog.ShowDialog() == true;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
