using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Myfirstrep.Models;
using SkiaSharp;

namespace Myfirstrep.Controls;

public sealed class ShapeGeometryDialog : Window
{
    private readonly ShapeType _shapeType;
    private readonly TextBox _xInput;
    private readonly TextBox _yInput;
    private readonly TextBox _widthInput;
    private readonly TextBox _heightInput;
    private readonly TextBox _radiusInput;

    public float X { get; private set; }
    public float Y { get; private set; }
    public float WidthValue { get; private set; }
    public float HeightValue { get; private set; }
    public float Radius { get; private set; }

    public ShapeGeometryDialog(ShapeModel shape)
    {
        _shapeType = shape.Type;
        Title = shape.Type == ShapeType.Rectangle ? "矩形精细调整" : "圆形精细调整";
        Width = 360;
        Height = shape.Type == ShapeType.Rectangle ? 260 : 230;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        InitializeValues(shape);

        var panel = new DockPanel { Margin = new Thickness(12) };
        var description = new TextBlock
        {
            Text = shape.Type == ShapeType.Rectangle
                ? "设置矩形左上角坐标、宽度和高度。"
                : "设置圆心坐标和半径。",
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(description, Dock.Top);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        DockPanel.SetDock(grid, Dock.Top);

        _xInput = AddRow(grid, "X：", X, 0);
        _yInput = AddRow(grid, "Y：", Y, 1);
        if (shape.Type == ShapeType.Rectangle)
        {
            _widthInput = AddRow(grid, "宽度：", WidthValue, 2);
            _heightInput = AddRow(grid, "高度：", HeightValue, 3);
            _radiusInput = new TextBox();
        }
        else
        {
            _radiusInput = AddRow(grid, "半径：", Radius, 2);
            _widthInput = new TextBox();
            _heightInput = new TextBox();
        }

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

    private void InitializeValues(ShapeModel shape)
    {
        if (shape.Type == ShapeType.Rectangle && shape.Points.Count >= 2)
        {
            X = Math.Min(shape.Points[0].X, shape.Points[1].X);
            Y = Math.Min(shape.Points[0].Y, shape.Points[1].Y);
            WidthValue = Math.Abs(shape.Points[1].X - shape.Points[0].X);
            HeightValue = Math.Abs(shape.Points[1].Y - shape.Points[0].Y);
        }
        else if (shape.Type == ShapeType.Circle && shape.Points.Count >= 2)
        {
            X = shape.Points[0].X;
            Y = shape.Points[0].Y;
            Radius = Distance(shape.Points[0], shape.Points[1]);
        }
    }

    private static TextBox AddRow(Grid grid, string labelText, float value, int row)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        var input = new TextBox { Text = Format(value), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        grid.Children.Add(label);
        grid.Children.Add(input);
        return input;
    }

    private void Confirm()
    {
        if (!TryParse(_xInput.Text, out var x) || !TryParse(_yInput.Text, out var y))
        {
            ShowInvalidMessage("坐标必须是有效数字。");
            return;
        }

        if (_shapeType == ShapeType.Rectangle)
        {
            if (!TryParse(_widthInput.Text, out var width) || width <= 0 ||
                !TryParse(_heightInput.Text, out var height) || height <= 0)
            {
                ShowInvalidMessage("宽度和高度必须是大于 0 的数字。");
                return;
            }

            X = x;
            Y = y;
            WidthValue = width;
            HeightValue = height;
        }
        else
        {
            if (!TryParse(_radiusInput.Text, out var radius) || radius <= 0)
            {
                ShowInvalidMessage("半径必须是大于 0 的数字。");
                return;
            }

            X = x;
            Y = y;
            Radius = radius;
        }

        DialogResult = true;
        Close();
    }

    private void ShowInvalidMessage(string message)
        => MessageBox.Show(this, message, "参数无效", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static bool TryParse(string text, out float value)
        => float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
           float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Format(float value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private static float Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
