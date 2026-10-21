using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowWrangler;

// Avalonia has no message box, so here's a small one.
static class ConfirmDialog
{
    public static async Task<bool> Show(Window owner, string title, string message, string confirmText)
    {
        var dialog = new Window
        {
            Title = "Window Wrangler",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var confirm = new Button { Content = confirmText, Classes = { "primary", "action" } };
        var cancel = new Button { Content = "Cancel", Classes = { "action" }, IsCancel = true };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        dialog.Content = new StackPanel
        {
            Margin = new(24),
            Spacing = 12,
            MaxWidth = 420,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new(0, 8, 0, 0),
                    Children = { confirm, cancel },
                },
            },
        };

        return await dialog.ShowDialog<bool>(owner);
    }
}
