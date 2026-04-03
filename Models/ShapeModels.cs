using SkiaSharp;

namespace Myfirstrep.Models;

public enum ShapeType
{
    Rectangle,
    Circle,
    Polygon
}

public sealed class ShapeModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ShapeType Type { get; set; }
    public List<SKPoint> Points { get; set; } = new();
    public float StrokeWidth { get; set; } = 2f;
    public string StrokeColor { get; set; } = "#FF00FF00";
    public string FillColor { get; set; } = "#5500FF00";
    public bool IsFillTransparent { get; set; } = true;

    public SKColor GetStrokeColor() => SKColor.Parse(StrokeColor);
    public SKColor GetFillColor() => SKColor.Parse(FillColor);

    public SKPath ToPath()
    {
        var path = new SKPath();
        switch (Type)
        {
            case ShapeType.Rectangle:
                if (Points.Count >= 2)
                {
                    var r = SKRect.Create(
                        Math.Min(Points[0].X, Points[1].X),
                        Math.Min(Points[0].Y, Points[1].Y),
                        Math.Abs(Points[1].X - Points[0].X),
                        Math.Abs(Points[1].Y - Points[0].Y));
                    path.AddRect(r);
                }
                break;
            case ShapeType.Circle:
                if (Points.Count >= 2)
                {
                    var center = Points[0];
                    var radius = Distance(center, Points[1]);
                    path.AddCircle(center.X, center.Y, radius);
                }
                break;
            case ShapeType.Polygon:
                if (Points.Count >= 3)
                {
                    path.MoveTo(Points[0]);
                    for (var i = 1; i < Points.Count; i++)
                    {
                        path.LineTo(Points[i]);
                    }
                    path.Close();
                }
                break;
        }

        return path;
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

public sealed class CanvasState
{
    public string? ImageBase64 { get; set; }
    public List<ShapeModel> Shapes { get; set; } = new();
}
