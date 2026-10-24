using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Div;

// Avalonia has no message box, so here's a small one. No owner (from the tray): it shows on its own,
// on top. No cancelText: just the one button.
static class ConfirmDialog
{
    public static async Task<bool> Show(Window? owner, string title, string message, string confirmText, string? cancelText = "Cancel")
    {
        var dialog = new Window
        {
            Title = "<div>",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Topmost = owner is null,
        };

        bool confirmed = false;
        var confirm = new Button { Content = confirmText, Classes = { "primary", "action" }, IsCancel = cancelText is null };
        var cancel = new Button { Content = cancelText, Classes = { "action" }, IsCancel = true, IsVisible = cancelText is not null };
        confirm.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close(true);
        };
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

        if (owner is not null) return await dialog.ShowDialog<bool>(owner);
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.Show();
        dialog.Activate();
        await closed.Task;
        return confirmed;
    }
}
