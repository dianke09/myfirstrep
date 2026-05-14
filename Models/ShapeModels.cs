using System.Text.Json.Serialization;
using SkiaSharp;

namespace Myfirstrep.Models;

public enum ShapeType
{
    Rectangle,
    RotatedRectangle,
    Circle,
    Polygon,
    CrossPoint
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$shape")]
[JsonDerivedType(typeof(RectangleShapeModel), typeDiscriminator: "rectangle")]
[JsonDerivedType(typeof(CircleShapeModel), typeDiscriminator: "circle")]
[JsonDerivedType(typeof(PolygonShapeModel), typeDiscriminator: "polygon")]
public abstract class ShapeModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public abstract ShapeType Type { get; }
    public List<SKPoint> Points { get; set; } = new();
    public float StrokeWidth { get; set; } = 2f;
    public string StrokeColor { get; set; } = "#FF00FF00";
    public string FillColor { get; set; } = "#5500FF00";
    public bool IsFillTransparent { get; set; } = true;
    public string RegionName { get; set; } = "未分配";
    public bool IsInteractive { get; set; } = true;
    public RectangleSliceOptions? RectangleSlice { get; set; }
    public List<ShapeModel> SubSlicedRectangles { get; set; } = new();

    public SKColor GetStrokeColor() => SKColor.Parse(StrokeColor);
    public SKColor GetFillColor() => SKColor.Parse(FillColor);
    public abstract SKPath ToPath();
}

public sealed class RectangleShapeModel : ShapeModel
{
    public override ShapeType Type => ShapeType.Rectangle;

    public override SKPath ToPath()
    {
        var path = new SKPath();
        if (Points.Count >= 2)
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
            case ShapeType.RotatedRectangle:
                if (Points.Count >= 4)
                {
                    path.MoveTo(Points[0]);
                    path.LineTo(Points[1]);
                    path.LineTo(Points[2]);
                    path.LineTo(Points[3]);
                    path.Close();
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
            case ShapeType.CrossPoint:
                if (Points.Count >= 1)
                {
                    var p = Points[0];
                    var size = 8f;
                    path.MoveTo(p.X - size, p.Y);
                    path.LineTo(p.X + size, p.Y);
                    path.MoveTo(p.X, p.Y - size);
                    path.LineTo(p.X, p.Y + size);
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

public sealed class RectangleSliceOptions
{
    public float RowHeight { get; set; } = 10f;
}

public sealed class CanvasState
{
    public string? ImagePath { get; set; }
    public List<ShapeModel> Shapes { get; set; } = new();
}
