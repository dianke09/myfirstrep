using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Myfirstrep.Models;
using OpenCvSharp;
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
    private string? _imageFilePath;
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
    private readonly ContextMenu _draftPolygonMenu;
    public SKColor PolygonEdgeColor { get; set; } = SKColors.Orange;
    public SKColor PolygonVertexColor { get; set; } = SKColors.DeepSkyBlue;
    public float PolygonVertexRadius { get; set; } = 4f;

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

        var cancelDraftMenu = new MenuItem { Header = "取消当前多边形绘制" };
        cancelDraftMenu.Click += (_, _) =>
        {
            _polygonBuffer.Clear();
            _polygonHoverPoint = null;
            Redraw();
        };
        _draftPolygonMenu = new ContextMenu();
        _draftPolygonMenu.Items.Add(cancelDraftMenu);

        Content = _surface;
    }

    public void SetTool(EditorTool tool)
    {
        _tool = tool;
        _polygonBuffer.Clear();
    }

    public void LoadImage(string path)
    {
        _bitmap = LoadBitmapWithDepthSupport(path);
        _imageFilePath = path;
        Redraw();
    }

    public void AddShape(ShapeModel shape)
    {
        _shapes.Add(shape);
        Redraw();
    }

    public void SaveState(string filePath)
    {
        var stateDir = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
        string? imagePath = null;
        if (!string.IsNullOrWhiteSpace(_imageFilePath) && File.Exists(_imageFilePath))
        {
            var fileName = Path.GetFileName(_imageFilePath);
            var targetPath = Path.Combine(stateDir, fileName);
            if (!string.Equals(Path.GetFullPath(_imageFilePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(_imageFilePath, targetPath, overwrite: true);
            }
            imagePath = fileName;
        }

        var state = new CanvasState
        {
            ImagePath = imagePath,
            Shapes = _shapes
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(state, jsonOptions), new UTF8Encoding(false));
    }

    public void LoadState(string filePath)
    {
        var state = JsonSerializer.Deserialize<CanvasState>(File.ReadAllText(filePath));
        _shapes.Clear();
        _bitmap = null;
        _imageFilePath = null;

        if (!string.IsNullOrWhiteSpace(state?.ImagePath))
        {
            var stateDir = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
            var path = Path.IsPathRooted(state.ImagePath) ? state.ImagePath : Path.Combine(stateDir, state.ImagePath);
            if (File.Exists(path))
            {
                LoadImage(path);
            }
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
            DrawRegionLabel(canvas, shape);
        }

        if (_drawing is not null)
        {
            DrawShape(canvas, _drawing, isSelected: false, isHovered: false);
        }

        if (_tool == EditorTool.Polygon && _polygonBuffer.Count > 0)
        {
            using var p = new SKPaint { Color = PolygonEdgeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2 / _zoom };
            for (var i = 0; i < _polygonBuffer.Count - 1; i++)
            {
                canvas.DrawLine(_polygonBuffer[i], _polygonBuffer[i + 1], p);
            }

            if (_polygonHoverPoint is SKPoint hover)
            {
                canvas.DrawLine(_polygonBuffer[^1], hover, p);
            }
        }
        if (_tool == EditorTool.Polygon && _polygonBuffer.Count > 0)
        {
            using var v = new SKPaint { Color = PolygonVertexColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            foreach (var point in _polygonBuffer)
            {
                canvas.DrawCircle(point, PolygonVertexRadius / _zoom, v);
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

    private void DrawRegionLabel(SKCanvas canvas, ShapeModel shape)
    {
        var text = string.IsNullOrWhiteSpace(shape.RegionName) ? "未分配" : shape.RegionName;
        var labelRect = GetRegionLabelRect(shape, text);
        if (labelRect is null) return;

        using var bg = new SKPaint { Color = SKColors.Black.WithAlpha(130), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 14f / _zoom,
            Typeface = SKTypeface.FromFamilyName("Microsoft YaHei")
        };
        var padding = 4f / _zoom;
        canvas.DrawRoundRect(labelRect.Value, 3f / _zoom, 3f / _zoom, bg);
        canvas.DrawText(text, labelRect.Value.Left + padding, labelRect.Value.Bottom - 6f / _zoom, textPaint);
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

        if (_tool == EditorTool.Polygon && e.ChangedButton == MouseButton.Right && _polygonBuffer.Count > 0)
        {
            _draftPolygonMenu.IsOpen = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            var shapeAtLabel = HitTestRegionLabel(p);
            if (shapeAtLabel is not null)
            {
                var dialog = new RegionEditDialog(shapeAtLabel.RegionName) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.RegionName))
                {
                    shapeAtLabel.RegionName = dialog.RegionName;
                    Redraw();
                }
                return;
            }
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
            if (_polygonBuffer.Count > 0 && IsOnPolygonVertexPreview(p))
            {
                Cursor = Cursors.Hand;
            }
            else if (_selected is not null && HitTestHandle(_selected, p) is not null)
            {
                Cursor = Cursors.Hand;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
            Redraw();
        }
        else if (_selected is not null && HitTestHandle(_selected, p) is not null)
        {
            Cursor = Cursors.Hand;
        }
        else
        {
            Cursor = Cursors.Arrow;
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

    private bool IsOnPolygonVertexPreview(SKPoint p)
    {
        foreach (var point in _polygonBuffer)
        {
            if (Distance(point, p) <= 8f / _zoom)
            {
                return true;
            }
        }

        return false;
    }

    private ShapeModel? HitTestRegionLabel(SKPoint p)
    {
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            var text = string.IsNullOrWhiteSpace(_shapes[i].RegionName) ? "未分配" : _shapes[i].RegionName;
            var rect = GetRegionLabelRect(_shapes[i], text);
            if (rect is SKRect r && r.Contains(p))
            {
                return _shapes[i];
            }
        }

        return null;
    }

    private SKRect? GetRegionLabelRect(ShapeModel shape, string text)
    {
        using var path = shape.ToPath();
        var bounds = path.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        using var textPaint = new SKPaint
        {
            TextSize = 14f / _zoom,
            Typeface = SKTypeface.FromFamilyName("Microsoft YaHei")
        };
        var textWidth = textPaint.MeasureText(text);
        var padding = 4f / _zoom;
        var labelHeight = 20f / _zoom;
        return new SKRect(bounds.Left, bounds.Top - labelHeight - 2f / _zoom, bounds.Left + textWidth + padding * 2, bounds.Top - 2f / _zoom);
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
        var d = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (d.ShowDialog() == true)
        {
            LoadImage(d.FileName);
        }
    }

    private static SKBitmap LoadBitmapWithDepthSupport(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".tif" or ".tiff")
        {
            try
            {
                return LoadTiffWithOpenCv(path);
            }
            catch
            {
                // Fallback for unexpected TIFF variants.
            }
        }

        using var stream = File.OpenRead(path);
        return SKBitmap.Decode(stream);
    }

    private static SKBitmap LoadTiffWithOpenCv(string path)
    {
        using var mat = Cv2.ImRead(path, ImreadModes.Unchanged);
        if (mat.Empty())
        {
            throw new InvalidOperationException("无法读取TIFF图像。");
        }

        Mat display;
        if (mat.Type().Depth == MatType.CV_16U)
        {
            display = new Mat();
            Cv2.Normalize(mat, display, 0, 255, NormTypes.MinMax, MatType.CV_8U);
        }
        else if (mat.Type().Depth == MatType.CV_8U)
        {
            display = mat.Clone();
        }
        else
        {
            display = new Mat();
            mat.ConvertTo(display, MatType.CV_8U);
        }

        using (display)
        {
            using var bgra = new Mat();
            if (display.Channels() == 1)
            {
                Cv2.CvtColor(display, bgra, ColorConversionCodes.GRAY2BGRA);
            }
            else if (display.Channels() == 3)
            {
                Cv2.CvtColor(display, bgra, ColorConversionCodes.BGR2BGRA);
            }
            else
            {
                display.CopyTo(bgra);
            }

            var bitmap = new SKBitmap(bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var totalBytes = (int)(bgra.Total() * bgra.ElemSize());
            var buffer = new byte[totalBytes];
            Marshal.Copy(bgra.Data, buffer, 0, totalBytes);
            Marshal.Copy(buffer, 0, bitmap.GetPixels(), totalBytes);
            return bitmap;
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
