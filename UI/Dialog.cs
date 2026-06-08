using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Arcanum.UI;

internal static class Dialog
{
    internal static async Task<bool> Confirm(Window owner, string message, string title = "Confirmar")
    {
        bool result = false;
        var w = Build(title, 380, 170);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text         = message,
            Foreground   = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 20),
        });

        var btns = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing             = 8,
        };
        var yes = new Button { Content = "Sim", Width = 80 };
        var no  = new Button { Content = "Não", Width = 80 };
        yes.Classes.Add("Primary");
        no.Classes.Add("Secondary");
        yes.Click += (_, _) => { result = true;  w.Close(); };
        no.Click  += (_, _) => { result = false; w.Close(); };
        btns.Children.Add(yes);
        btns.Children.Add(no);

        panel.Children.Add(btns);
        w.Content = panel;
        await w.ShowDialog(owner);
        return result;
    }

    internal static async Task Info(Window owner, string message, string title = "Atenção")
    {
        var w = Build(title, 380, 160);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text         = message,
            Foreground   = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 20),
        });
        var ok = new Button
        {
            Content             = "OK",
            Width               = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Classes.Add("Primary");
        ok.Click += (_, _) => w.Close();
        panel.Children.Add(ok);
        w.Content = panel;

        await w.ShowDialog(owner);
    }

    private static Window Build(string title, int width, int height) => new()
    {
        Title                   = title,
        Width                   = width,
        Height                  = height,
        CanResize               = false,
        WindowStartupLocation   = WindowStartupLocation.CenterOwner,
        Background              = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)),
    };
}
