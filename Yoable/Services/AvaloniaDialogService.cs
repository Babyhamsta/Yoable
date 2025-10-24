using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Yoable.Services;

/// <summary>
/// Avalonia implementation of dialog service
/// </summary>
public class AvaloniaDialogService : IDialogService
{
    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = GetMainWindow();

        var dialog = new Window
        {
            Title = title,
            Width = 450,
            MinHeight = 150,
            MaxHeight = 600,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = window != null && window.IsVisible
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
            CanResize = false
        };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 410
        });

        var button = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 0),
            MinWidth = 80,
            Padding = new Thickness(16, 8)
        };
        button.Click += (s, e) => dialog.Close();
        panel.Children.Add(button);

        dialog.Content = panel;

        // Try to show as modal dialog with owner, fall back to non-modal if owner is invalid
        if (window != null && window.IsVisible)
        {
            try
            {
                await dialog.ShowDialog(window);
            }
            catch (InvalidOperationException)
            {
                // Window closed during transition, show non-modal instead
                dialog.Show();
                await Task.CompletedTask;
            }
        }
        else
        {
            // No valid parent window, show non-modal
            dialog.Show();
            await Task.CompletedTask;
        }
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        await ShowMessageAsync(title, message);
    }

    public async Task ShowWarningAsync(string title, string message)
    {
        await ShowMessageAsync(title, message);
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var result = await ShowYesNoCancelAsync(title, message);
        return result == DialogResult.Yes;
    }

    public async Task<DialogResult> ShowYesNoCancelAsync(string title, string message)
    {
        var window = GetMainWindow();

        var dialog = new Window
        {
            Title = title,
            Width = 450,
            MinHeight = 180,
            MaxHeight = 600,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = window != null && window.IsVisible
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
            CanResize = false
        };

        var result = DialogResult.Cancel;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 410,
            Margin = new Thickness(0, 0, 0, 20)
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        var yesButton = new Button
        {
            Content = "Yes",
            MinWidth = 80,
            Margin = new Thickness(5),
            Padding = new Thickness(16, 8)
        };
        yesButton.Click += (s, e) => { result = DialogResult.Yes; dialog.Close(); };

        var noButton = new Button
        {
            Content = "No",
            MinWidth = 80,
            Margin = new Thickness(5),
            Padding = new Thickness(16, 8)
        };
        noButton.Click += (s, e) => { result = DialogResult.No; dialog.Close(); };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Margin = new Thickness(5),
            Padding = new Thickness(16, 8)
        };
        cancelButton.Click += (s, e) => { result = DialogResult.Cancel; dialog.Close(); };

        buttonPanel.Children.Add(yesButton);
        buttonPanel.Children.Add(noButton);
        buttonPanel.Children.Add(cancelButton);

        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        // Try to show as modal dialog with owner, fall back to non-modal if owner is invalid
        if (window != null && window.IsVisible)
        {
            try
            {
                await dialog.ShowDialog(window);
            }
            catch (InvalidOperationException)
            {
                // Window closed during transition, show non-modal instead
                dialog.Show();
                await Task.CompletedTask;
            }
        }
        else
        {
            // No valid parent window, show non-modal and return cancel
            dialog.Show();
            await Task.CompletedTask;
        }

        return result;
    }
}
