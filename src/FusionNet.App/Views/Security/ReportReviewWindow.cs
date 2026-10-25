using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FusionNet.App.Localization;

namespace FusionNet.App.Views.Security;

internal static class ReportReviewWindow
{
    internal static bool Approve(string title,string content,string action)=>Create(title,content,action).ShowDialog()==true;

    internal static Window Create(string title, string content, string action)
    {
        var application = Application.Current ?? throw new InvalidOperationException("Application is unavailable.");
        var owner=application.MainWindow;
        var window = new Window { Title = title, Width = 780, Height = 560, MinWidth = 400, MinHeight = 300,FlowDirection=Loc.Instance.FlowDirection,
            Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)application.FindResource("Brush.Surface"), Foreground = (Brush)application.FindResource("Brush.Text") };
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text=title, Style=(Style)application.FindResource("Text.Body"),
            TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,12) });
        var text = new TextBox { Text = content, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight, FontFamily = new FontFamily("Consolas"), AcceptsReturn = true };
        text.Foreground = (Brush)application.FindResource("Brush.Text");
        text.Background = (Brush)application.FindResource("Brush.SurfaceAlt");
        text.BorderBrush = (Brush)application.FindResource("Brush.ControlBorder");
        text.FocusVisualStyle=(Style)application.FindResource("FocusRing");
        text.Padding = new Thickness(10);
        Grid.SetRow(text, 1); grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var accept = new Button { Content = action, Style=(Style)application.FindResource("Btn.Primary"), IsDefault = false };
        accept.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = Loc.T("Common.Cancel"), Style=(Style)application.FindResource("Btn.Ghost"), Margin = new Thickness(12, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(accept); buttons.Children.Add(cancel); Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
        window.Content = grid;
        return window;
    }
}
