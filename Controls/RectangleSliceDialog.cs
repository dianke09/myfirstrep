using System.Windows;
using System.Windows.Controls;
using Myfirstrep.Models;

namespace Myfirstrep.Controls;

public sealed class RectangleSliceDialog : Window
{
    private readonly TextBox _rowHeightInput;

    public float RowHeight { get; private set; }

    public RectangleSliceDialog(RectangleSliceOptions? current)
    {
        Title = "矩形切片";
        Width = 360;
        Height = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        RowHeight = Math.Max(1f, current?.RowHeight ?? 10f);

        var panel = new DockPanel { Margin = new Thickness(12) };
        var description = new TextBlock
        {
            Text = "设置切片行高。总高使用原 Rectangle 的高度，最后一行会使用总高减去前面切片后的剩余高度。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(description, Dock.Top);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        DockPanel.SetDock(grid, Dock.Top);

        var rowHeightLabel = new TextBlock { Text = "行高：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _rowHeightInput = new TextBox { Text = RowHeight.ToString("0.###") };

        Grid.SetRow(rowHeightLabel, 0);
        Grid.SetColumn(rowHeightLabel, 0);
        Grid.SetRow(_rowHeightInput, 0);
        Grid.SetColumn(_rowHeightInput, 1);

        grid.Children.Add(rowHeightLabel);
        grid.Children.Add(_rowHeightInput);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var ok = new Button { Content = "确定", Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Confirm();

        var cancel = new Button { Content = "取消", Width = 70 };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        panel.Children.Add(description);
        panel.Children.Add(grid);
        panel.Children.Add(buttons);

        Content = panel;
    }

    private void Confirm()
    {
        if (!float.TryParse(_rowHeightInput.Text.Trim(), out var rowHeight) || rowHeight <= 0)
        {
            MessageBox.Show(this, "切片行高必须是大于 0 的数字。", "切片参数无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RowHeight = rowHeight;
        DialogResult = true;
        Close();
    }
}
