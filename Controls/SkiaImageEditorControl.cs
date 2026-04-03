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
    Circle,
    Polygon
}

public sealed class SkiaImageEditorControl : UserControl
{
    private sealed class EditHandle
    {
        public int Index { get; set; }
    }
    private readonly SKElement _surface;
    private readonly List<ShapeModel> _shapes = new();
    private readonly List<SKPoint> _polygonBuffer = new();
    private SKBitmap? _bitmap;
    private ShapeModel? _selected;
    private ShapeModel? _hovered;
    private ShapeModel? _drawing;
    private EditorTool _tool = EditorTool.Select;
    private bool _isCanvasPanning;
    private bool _isShapeDragging;
    private Point _lastMouse;
    private SKPoint _lastWorld;
    private SKPoint _pan = new(0, 0);
    private float _zoom = 1f;
    private SKPoint? _polygonHoverPoint;
    private EditHandle? _activeHandle;

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

            if (_polygonHoverPoint is SKPoint hover)
            {
                canvas.DrawLine(_polygonBuffer[^1], hover, p);
            }
        }

        if (_selected is not null)
        {
            DrawEditHandles(canvas, _selected);
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

        var pos = ToSurfacePoint(e.GetPosition(_surface));
        var worldX = (pos.X - _pan.X) / old;
        var worldY = (pos.Y - _pan.Y) / old;
        _pan = new SKPoint(pos.X - worldX * _zoom, pos.Y - worldY * _zoom);
        Redraw();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _surface.Focus();
        _lastMouse = e.GetPosition(_surface);
        var p = ToWorld(_lastMouse);
        _lastWorld = p;

        if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount > 1)
        {
            FitImageToViewport();
            return;
        }

        if (e.ChangedButton == MouseButton.Right && _selected is not null)
        {
            ContextMenu!.IsOpen = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.LeftButton == MouseButtonState.Pressed)
        {
            _isCanvasPanning = true;
            return;
        }

        if (_tool == EditorTool.Select)
        {
            _selected = HitTest(p);
            if (_selected is not null && e.LeftButton == MouseButtonState.Pressed)
            {
                _activeHandle = HitTestHandle(_selected, p);
                if (_selected.Type == ShapeType.Polygon && _activeHandle is null)
                {
                    var edgeIndex = HitPolygonEdge(_selected, p);
                    if (edgeIndex >= 0)
                    {
                        _selected.Points.Insert(edgeIndex + 1, p);
                        _activeHandle = new EditHandle { Index = edgeIndex + 1 };
                    }
                }

                _isShapeDragging = true;
            }
            Redraw();
            return;
        }

        if (_tool == EditorTool.Rectangle || _tool == EditorTool.Circle)
        {
            _drawing = new ShapeModel
            {
                Type = _tool == EditorTool.Rectangle ? ShapeType.Rectangle : ShapeType.Circle,
                Points = new List<SKPoint> { p, p }
            };
            return;
        }

        if (_tool == EditorTool.Polygon && e.LeftButton == MouseButtonState.Pressed)
        {
            var canCloseByDoubleClick = e.ClickCount > 1 && _polygonBuffer.Count >= 3 &&
                                        Distance(p, _polygonBuffer[^1]) < 8f / _zoom;
            var canCloseByManualConnect = _polygonBuffer.Count >= 3 && Distance(p, _polygonBuffer[0]) < 8f / _zoom;

            if (canCloseByDoubleClick || canCloseByManualConnect)
            {
                _shapes.Add(new ShapeModel { Type = ShapeType.Polygon, Points = new List<SKPoint>(_polygonBuffer) });
                _polygonBuffer.Clear();
                _polygonHoverPoint = null;
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

        if (_isCanvasPanning)
        {
            var now = ToSurfacePoint(pos);
            var before = ToSurfacePoint(_lastMouse);
            var dx = now.X - before.X;
            var dy = now.Y - before.Y;
            _pan = new SKPoint(_pan.X + dx, _pan.Y + dy);
            _lastMouse = pos;
            Redraw();
            return;
        }

        if (_isShapeDragging && _selected is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var dx = p.X - _lastWorld.X;
            var dy = p.Y - _lastWorld.Y;
            if (_activeHandle is not null)
            {
                ResizeShapeByHandle(_selected, _activeHandle, p);
            }
            else
            {
                TranslateShape(_selected, dx, dy);
            }
            _lastWorld = p;
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

        if (_tool == EditorTool.Polygon)
        {
            _polygonHoverPoint = p;
            Redraw();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isCanvasPanning = false;
        _isShapeDragging = false;
        _activeHandle = null;
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
            _polygonHoverPoint = null;
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
            var fallback = ToSurfacePoint(p);
            return new SKPoint(fallback.X, fallback.Y);
        }

        var surface = ToSurfacePoint(p);
        return screenToWorld.MapPoint(surface);
    }

    private SKMatrix CreateWorldToScreenMatrix()
        => SKMatrix.CreateScaleTranslation(_zoom, _zoom, _pan.X, _pan.Y);

    private SKPoint ToSurfacePoint(Point p)
    {
        var width = _surface.ActualWidth <= 0 ? 1.0 : _surface.ActualWidth;
        var height = _surface.ActualHeight <= 0 ? 1.0 : _surface.ActualHeight;
        var scaleX = (float)(_surface.CanvasSize.Width / width);
        var scaleY = (float)(_surface.CanvasSize.Height / height);
        return new SKPoint((float)p.X * scaleX, (float)p.Y * scaleY);
    }

    private void TranslateShape(ShapeModel shape, float dx, float dy)
    {
        for (var i = 0; i < shape.Points.Count; i++)
        {
            shape.Points[i] = new SKPoint(shape.Points[i].X + dx, shape.Points[i].Y + dy);
        }
    }

    private void DrawEditHandles(SKCanvas canvas, ShapeModel shape)
    {
        using var paint = new SKPaint { Color = SKColors.Cyan, Style = SKPaintStyle.Fill, IsAntialias = true };
        foreach (var pt in GetHandlePoints(shape))
        {
            canvas.DrawCircle(pt, 5 / _zoom, paint);
        }
    }

    private List<SKPoint> GetHandlePoints(ShapeModel shape)
    {
        if (shape.Type == ShapeType.Rectangle && shape.Points.Count >= 2)
        {
            var minX = Math.Min(shape.Points[0].X, shape.Points[1].X);
            var maxX = Math.Max(shape.Points[0].X, shape.Points[1].X);
            var minY = Math.Min(shape.Points[0].Y, shape.Points[1].Y);
            var maxY = Math.Max(shape.Points[0].Y, shape.Points[1].Y);
            return new List<SKPoint>
            {
                new(minX, minY), new((minX + maxX) / 2f, minY), new(maxX, minY),
                new(maxX, (minY + maxY) / 2f), new(maxX, maxY), new((minX + maxX) / 2f, maxY),
                new(minX, maxY), new(minX, (minY + maxY) / 2f)
            };
        }

        if (shape.Type == ShapeType.Circle && shape.Points.Count >= 2)
        {
            return new List<SKPoint> { shape.Points[0], shape.Points[1] };
        }

        return new List<SKPoint>(shape.Points);
    }

    private EditHandle? HitTestHandle(ShapeModel shape, SKPoint p)
    {
        var handles = GetHandlePoints(shape);
        for (var i = 0; i < handles.Count; i++)
        {
            if (Distance(handles[i], p) <= 8f / _zoom)
            {
                return new EditHandle { Index = i };
            }
        }

        return null;
    }

    private void ResizeShapeByHandle(ShapeModel shape, EditHandle handle, SKPoint p)
    {
        if (shape.Type == ShapeType.Circle && shape.Points.Count >= 2)
        {
            if (handle.Index == 0) shape.Points[0] = p;
            else shape.Points[1] = p;
            return;
        }

        if (shape.Type == ShapeType.Polygon)
        {
            if (handle.Index >= 0 && handle.Index < shape.Points.Count)
            {
                shape.Points[handle.Index] = p;
            }
            return;
        }

        if (shape.Type == ShapeType.Rectangle && shape.Points.Count >= 2)
        {
            var minX = Math.Min(shape.Points[0].X, shape.Points[1].X);
            var maxX = Math.Max(shape.Points[0].X, shape.Points[1].X);
            var minY = Math.Min(shape.Points[0].Y, shape.Points[1].Y);
            var maxY = Math.Max(shape.Points[0].Y, shape.Points[1].Y);

            switch (handle.Index)
            {
                case 0: minX = p.X; minY = p.Y; break;
                case 1: minY = p.Y; break;
                case 2: maxX = p.X; minY = p.Y; break;
                case 3: maxX = p.X; break;
                case 4: maxX = p.X; maxY = p.Y; break;
                case 5: maxY = p.Y; break;
                case 6: minX = p.X; maxY = p.Y; break;
                case 7: minX = p.X; break;
            }

            shape.Points[0] = new SKPoint(minX, minY);
            shape.Points[1] = new SKPoint(maxX, maxY);
        }
    }

    private int HitPolygonEdge(ShapeModel shape, SKPoint p)
    {
        if (shape.Type != ShapeType.Polygon || shape.Points.Count < 2) return -1;
        for (var i = 0; i < shape.Points.Count; i++)
        {
            var a = shape.Points[i];
            var b = shape.Points[(i + 1) % shape.Points.Count];
            if (DistancePointToSegment(p, a, b) <= 6f / _zoom &&
                Distance(p, a) > 8f / _zoom && Distance(p, b) > 8f / _zoom)
            {
                return i;
            }
        }
        return -1;
    }

    private static float DistancePointToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var len2 = abx * abx + aby * aby;
        if (len2 <= float.Epsilon) return Distance(p, a);
        var t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0f, 1f);
        var proj = new SKPoint(a.X + t * abx, a.Y + t * aby);
        return Distance(p, proj);
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void FitImageToViewport()
    {
        if (_bitmap is null || _surface.CanvasSize.Width <= 0 || _surface.CanvasSize.Height <= 0)
        {
            return;
        }

        var viewportW = _surface.CanvasSize.Width;
        var viewportH = _surface.CanvasSize.Height;
        var sx = viewportW / (float)_bitmap.Width;
        var sy = viewportH / (float)_bitmap.Height;
        _zoom = Math.Clamp(Math.Min(sx, sy), 0.01f, 30f);

        var drawW = _bitmap.Width * _zoom;
        var drawH = _bitmap.Height * _zoom;
        _pan = new SKPoint((viewportW - drawW) / 2f, (viewportH - drawH) / 2f);
        Redraw();
    }

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
