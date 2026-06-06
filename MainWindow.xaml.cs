using System.Windows;
using Myfirstrep.Controls;
using Myfirstrep.Models;
using SkiaSharp;

namespace Myfirstrep;

public partial class MainWindow : Window
{
    private readonly SkiaImageEditorControl _editor = new();

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.Children.Add(_editor);
    }

    private void LoadImage_Click(object sender, RoutedEventArgs e) => _editor.ShowLoadImageDialog();
    private void Select_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.Select);
    private void Pan_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.Pan);
    private void Rectangle_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.Rectangle);
    private void RotatedRectangle_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.RotatedRectangle);
    private void Circle_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.Circle);
    private void Polygon_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.Polygon);
    private void Line_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.Line);
    private void Corner_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.Corner);
    private void CrossPoint_Click(object sender, RoutedEventArgs e) => _editor.SetTool(EditorTool.CrossPoint);
    private void SaveState_Click(object sender, RoutedEventArgs e) => _editor.ShowSaveStateDialog();
    private void LoadState_Click(object sender, RoutedEventArgs e) => _editor.ShowLoadStateDialog();

    private void AddDemoShape_Click(object sender, RoutedEventArgs e)
    {
        _editor.AddShape(new PolygonShapeModel
        {
            StrokeWidth = 3,
            StrokeColor = "#FFFF0000",
            FillColor = "#55FF0000",
            IsFillTransparent = false,
            Points = new List<SKPoint>
            {
                new(50, 50),
                new(220, 70),
                new(240, 180),
                new(90, 200)
            }
        });
    }
}
