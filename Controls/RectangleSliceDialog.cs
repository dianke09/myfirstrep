using System.Windows;
using System.Windows.Controls;
using Myfirstrep.Models;

namespace Myfirstrep.Controls;

public sealed class RectangleSliceDialog : Window
{
    private readonly TextBox _columnsInput;
    private readonly TextBox _rowsInput;

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    public RectangleSliceDialog(RectangleSliceOptions? current)
    {
        Title = "矩形切片";
        Width = 360;
        Height = 220;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Columns = Math.Max(1, current?.Columns ?? 1);
        Rows = Math.Max(1, current?.Rows ?? 1);

        var panel = new DockPanel { Margin = new Thickness(12) };
        var description = new TextBlock
        {
            Text = "设置切片列数和行数。最后一列/最后一行会使用总宽/总高减去前面切片后的剩余值。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(description, Dock.Top);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        DockPanel.SetDock(grid, Dock.Top);

        var columnsLabel = new TextBlock { Text = "列数：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        _columnsInput = new TextBox { Text = Columns.ToString(), Margin = new Thickness(0, 0, 0, 8) };
        var rowsLabel = new TextBlock { Text = "行数：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _rowsInput = new TextBox { Text = Rows.ToString() };

        Grid.SetRow(columnsLabel, 0);
        Grid.SetColumn(columnsLabel, 0);
        Grid.SetRow(_columnsInput, 0);
        Grid.SetColumn(_columnsInput, 1);
        Grid.SetRow(rowsLabel, 1);
        Grid.SetColumn(rowsLabel, 0);
        Grid.SetRow(_rowsInput, 1);
        Grid.SetColumn(_rowsInput, 1);

        grid.Children.Add(columnsLabel);
        grid.Children.Add(_columnsInput);
        grid.Children.Add(rowsLabel);
        grid.Children.Add(_rowsInput);

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
        if (!int.TryParse(_columnsInput.Text.Trim(), out var columns) || columns < 1 ||
            !int.TryParse(_rowsInput.Text.Trim(), out var rows) || rows < 1)
        {
            MessageBox.Show(this, "列数和行数必须是大于 0 的整数。", "切片参数无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Columns = columns;
        Rows = rows;
        DialogResult = true;
        Close();
    }
}
