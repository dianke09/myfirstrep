using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Myfirstrep.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Myfirstrep.Controls;

public enum EditorTool
{
    Select,
    Pan,
    Rectangle,
    Ellipse,
    Polygon
}

public sealed class SkiaImageEditorControl : UserControl
{
    private readonly SKElement _surface;
    private readonly List<ShapeModel> _shapes = new();
    private readonly List<SKPoint> _polygonBuffer = new();
    private SKBitmap? _bitmap;
    private ShapeModel? _selected;
    private ShapeModel? _hovered;
    private ShapeModel? _drawing;
    private EditorTool _tool = EditorTool.Select;
    private bool _isDragging;
    private Point _lastMouse;
    private SKPoint _pan = new(0, 0);
    private float _zoom = 1f;

    public SkiaImageEditorControl()
    {
        _surface = new SKElement();
        _surface.PaintSurface += OnPaintSurface;
        _surface.MouseWheel += OnMouseWheel;
        _surface.MouseDown += OnMouseDown;
        _surface.MouseMove += OnMouseMove;
        _surface.MouseUp += OnMouseUp;
        _surface.Focusable = true;
        _surface.KeyDown += OnKeyDown;

        var deleteMenu = new MenuItem { Header = "删除选中图形" };
        deleteMenu.Click += (_, _) =>
        {
            if (_selected is null) return;
            _shapes.Remove(_selected);
            _selected = null;
            Redraw();
        };

        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(deleteMenu);

        Content = _surface;
    }

    public void SetTool(EditorTool tool)
    {
        _tool = tool;
        _polygonBuffer.Clear();
    }

    public void LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        _bitmap = SKBitmap.Decode(stream);
        Redraw();
    }

    public void AddShape(ShapeModel shape)
    {
        _shapes.Add(shape);
        Redraw();
    }

    public void SaveState(string filePath)
    {
        string? base64 = null;
        if (_bitmap is not null)
        {
            using var image = SKImage.FromBitmap(_bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            base64 = Convert.ToBase64String(data.ToArray());
        }

        var state = new CanvasState
        {
            ImageBase64 = base64,
            Shapes = _shapes
        };

        File.WriteAllText(filePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void LoadState(string filePath)
    {
        var state = JsonSerializer.Deserialize<CanvasState>(File.ReadAllText(filePath));
        _shapes.Clear();

        if (!string.IsNullOrWhiteSpace(state?.ImageBase64))
        {
            var bytes = Convert.FromBase64String(state.ImageBase64);
            using var data = SKData.CreateCopy(bytes);
            _bitmap = SKBitmap.Decode(data);
        }

        if (state?.Shapes is { Count: > 0 })
        {
            _shapes.AddRange(state.Shapes);
        }

        Redraw();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        canvas.SetMatrix(CreateWorldToScreenMatrix());

        if (_bitmap is not null)
        {
            canvas.DrawBitmap(_bitmap, 0, 0);
        }

        foreach (var shape in _shapes)
        {
            DrawShape(canvas, shape, shape == _selected, shape == _hovered);
        }

        if (_drawing is not null)
        {
            DrawShape(canvas, _drawing, isSelected: false, isHovered: false);
        }

        if (_tool == EditorTool.Polygon && _polygonBuffer.Count > 1)
        {
            using var p = new SKPaint { Color = SKColors.Orange, Style = SKPaintStyle.Stroke, StrokeWidth = 2 / _zoom };
            for (var i = 0; i < _polygonBuffer.Count - 1; i++)
            {
                canvas.DrawLine(_polygonBuffer[i], _polygonBuffer[i + 1], p);
            }
        }
    }

    private static void DrawShape(SKCanvas canvas, ShapeModel shape, bool isSelected, bool isHovered)
    {
        using var path = shape.ToPath();
        using var stroke = new SKPaint
        {
            Color = isSelected ? SKColors.Yellow : shape.GetStrokeColor(),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = shape.StrokeWidth,
            IsAntialias = true
        };

        if (!shape.IsFillTransparent || isHovered)
        {
            var c = shape.GetFillColor();
            if (isHovered)
            {
                c = c.WithAlpha(140);
            }

            using var fill = new SKPaint
            {
                Color = c,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawPath(path, fill);
        }

        canvas.DrawPath(path, stroke);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var old = _zoom;
        _zoom = e.Delta > 0 ? _zoom * 1.1f : _zoom / 1.1f;
        _zoom = Math.Clamp(_zoom, 0.1f, 30f);

        var pos = e.GetPosition(_surface);
        var worldX = (float)((pos.X - _pan.X) / old);
        var worldY = (float)((pos.Y - _pan.Y) / old);
        _pan = new SKPoint((float)pos.X - worldX * _zoom, (float)pos.Y - worldY * _zoom);
        Redraw();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _surface.Focus();
        _lastMouse = e.GetPosition(_surface);
        var p = ToWorld(_lastMouse);

        if (e.ChangedButton == MouseButton.Right && _selected is not null)
        {
            ContextMenu!.IsOpen = true;
            return;
        }

        if (_tool == EditorTool.Pan || e.MiddleButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            return;
        }

        if (_tool == EditorTool.Select)
        {
            _selected = HitTest(p);
            Redraw();
            return;
        }

        if (_tool == EditorTool.Rectangle || _tool == EditorTool.Ellipse)
        {
            _drawing = new ShapeModel
            {
                Type = _tool == EditorTool.Rectangle ? ShapeType.Rectangle : ShapeType.Ellipse,
                Points = new List<SKPoint> { p, p }
            };
            return;
        }

        if (_tool == EditorTool.Polygon && e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount > 1 && _polygonBuffer.Count >= 3)
            {
                _shapes.Add(new ShapeModel { Type = ShapeType.Polygon, Points = new List<SKPoint>(_polygonBuffer) });
                _polygonBuffer.Clear();
            }
            else
            {
                _polygonBuffer.Add(p);
            }
            Redraw();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_surface);
        var p = ToWorld(pos);

        if (_isDragging)
        {
            var dx = (float)(pos.X - _lastMouse.X);
            var dy = (float)(pos.Y - _lastMouse.Y);
            _pan = new SKPoint(_pan.X + dx, _pan.Y + dy);
            _lastMouse = pos;
            Redraw();
            return;
        }

        if (_drawing is not null)
        {
            _drawing.Points[1] = p;
            Redraw();
            return;
        }

        var hit = HitTest(p);
        if (hit?.Id != _hovered?.Id)
        {
            _hovered = hit;
            Redraw();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        if (_drawing is not null)
        {
            _shapes.Add(_drawing);
            _drawing = null;
            Redraw();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _tool == EditorTool.Polygon && _polygonBuffer.Count >= 3)
        {
            _shapes.Add(new ShapeModel { Type = ShapeType.Polygon, Points = new List<SKPoint>(_polygonBuffer) });
            _polygonBuffer.Clear();
            Redraw();
        }
    }

    private ShapeModel? HitTest(SKPoint p)
    {
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            using var path = _shapes[i].ToPath();
            if (path.Contains(p.X, p.Y))
            {
                return _shapes[i];
            }
        }

        return null;
    }

    private SKPoint ToWorld(Point p)
    {
        var worldToScreen = CreateWorldToScreenMatrix();
        if (!worldToScreen.TryInvert(out var screenToWorld))
        {
            return new SKPoint((float)p.X, (float)p.Y);
        }

        return screenToWorld.MapPoint(new SKPoint((float)p.X, (float)p.Y));
    }

    private SKMatrix CreateWorldToScreenMatrix()
        => SKMatrix.CreateScaleTranslation(_zoom, _zoom, _pan.X, _pan.Y);

    private void Redraw() => _surface.InvalidateVisual();

    public void ShowLoadImageDialog()
    {
        var d = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" };
        if (d.ShowDialog() == true)
        {
            LoadImage(d.FileName);
        }
    }

    public void ShowSaveStateDialog()
    {
        var d = new SaveFileDialog { Filter = "State Json|*.json" };
        if (d.ShowDialog() == true)
        {
            SaveState(d.FileName);
        }
    }

    public void ShowLoadStateDialog()
    {
        var d = new OpenFileDialog { Filter = "State Json|*.json" };
        if (d.ShowDialog() == true)
        {
            LoadState(d.FileName);
        }
    }
}
