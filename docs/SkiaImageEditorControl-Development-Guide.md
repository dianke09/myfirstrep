# SkiaImageEditorControl 开发文档

## 1. 文档目的

本文档统一整理 `SkiaImageEditorControl` 的产品需求、数据结构、交互规则、公共 API、持久化格式、切片语义和验收标准，作为后续开发、联调、测试和代码评审的基线。

控件面向 WPF 桌面应用，使用 SkiaSharp 完成图片和标注图形的渲染，不依赖 GDI+。当前示例项目目标框架为 `net6.0-windows`。

## 2. 范围与组成

### 2.1 核心文件

| 文件 | 职责 |
| --- | --- |
| `Controls/SkiaImageEditorControl.cs` | 画布渲染、鼠标/键盘交互、命中测试、图形编辑、切片、保存与加载 |
| `Models/ShapeModels.cs` | 图形类型、图形数据、孔洞轮廓、切片配置和画布状态 |
| `Controls/ShapeGeometryDialog.cs` | Rectangle/Circle 数值化位置和尺寸编辑 |
| `Controls/RectangleSliceDialog.cs` | 水平切片行高配置 |
| `Controls/RegionEditDialog.cs` | `RegionName` 编辑 |
| `MainWindow.xaml(.cs)` | 控件集成和工具栏示例 |

### 2.2 坐标系

- `ShapeModel.Points`、孔洞、切片结果均使用图片/世界坐标。
- `_zoom` 和 `_pan` 只参与世界坐标到屏幕坐标的显示变换，不应直接改写图形坐标。
- 画布平移、缩放不会改变图形模型；图形平移和编辑会改变模型坐标。
- 线宽、编辑手柄和命中容差应结合缩放比例处理，使屏幕交互尺寸基本稳定。

## 3. 功能需求总览

### 3.1 图片显示

1. 支持从文件加载普通位图。
2. 支持 `.tif`/`.tiff` 16 位灰度深度图，并归一化为可显示的 8 位灰度图。
3. 鼠标滚轮以当前光标位置为中心缩放，缩放范围为 `0.1`～`30`。
4. 双击画布将图片自适应到当前控件视口。
5. 使用 Pan 工具左键拖动画布；任意工具下按住 `Ctrl` 再左键拖动也可平移画布。

### 3.2 图形类型

| `ShapeType` | 数据约定 | 绘制与编辑要求 |
| --- | --- | --- |
| `Rectangle` | `Points[0]`、`Points[1]` 为对角点 | 绘制、整体平移、8 个边/角手柄缩放、数值化设置 X/Y/宽/高、水平切片 |
| `RotatedRectangle` | `Points[0..3]` 按轮廓顺序排列 | 绘制、整体平移、角点缩放时始终保持相邻边垂直、旋转控制点旋转 |
| `Circle` | `Points[0]` 为圆心，`Points[1]` 为半径点 | 绘制、整体平移、圆心/半径点编辑、数值化设置圆心和半径 |
| `Polygon` | `Points` 为闭合外轮廓顶点 | 多点绘制、顶点拖动、边上新增顶点、整体平移、水平切片 |
| `PolygonWithHoles` | `Points` 为主外轮廓；`PolygonHoles` 为其他轮廓 | 偶奇填充、外轮廓/孔洞顶点编辑、孔洞独立平移、整体平移、水平切片 |
| `Line` | `Points[0]`、`Points[1]` 为端点 | 绘制、整体平移、端点编辑；按 `Shift` 时正交吸附为水平线或垂直线 |
| `Corner` | `CornerLines` 中包含两条 `Line` | 显示两条线全部端点的包围框；线可独立平移/编辑，空白包围框区域拖动整体 |
| `CrossPoint` | `Points[0]` 为单点 | 以十字形式显示、命中、选择和整体平移 |

### 3.3 通用图形能力

每个图形应支持以下通用数据和行为：

- 唯一 `Id`。
- 边线宽度 `StrokeWidth`、边线颜色 `StrokeColor`、填充颜色 `FillColor`。
- `IsFillTransparent` 控制是否常态显示填充；Hover 时使用半透明高亮。
- `RegionName` 显示在图形包围框上方，右键标签可编辑；字体使用 Microsoft YaHei。
- `IsInteractive` 控制单个图形是否允许 Hover、选择、平移、删除、缩放、旋转和菜单操作。
- 控件级 `AreShapesInteractive` 统一控制全部图形是否可交互；关闭时清除当前选择、Hover 和编辑状态。
- 可通过方向键移动选中图形：方向键每次移动 1 个世界坐标单位，`Shift + 方向键` 每次移动 10 个单位。
- 复制/粘贴支持右键菜单和 `Ctrl+C`/`Ctrl+V`；复制必须深拷贝嵌套孔洞、Corner 子线和切片数据，并为新图形生成新 ID。
- 每次粘贴相对复制源偏移 `(10, 10)`，连续粘贴继续递增偏移，并自动选中新图形。

## 4. 工具和交互状态

### 4.1 `EditorTool`

当前工具枚举包括：

- `Select`
- `Pan`
- `Rectangle`
- `RotatedRectangle`
- `Circle`
- `Polygon`
- `Line`
- `Corner`
- `CrossPoint`

通过 `SetTool(EditorTool tool)` 切换工具。切换工具时应取消未完成的多边形草稿点。

### 4.2 选择和 Hover

- Select 工具下单击图形进行选择。
- 仅可命中同时满足 `AreShapesInteractive == true` 和 `shape.IsInteractive == true` 的图形。
- Hover 图形时显示半透明蒙层；非交互图形不得触发 Hover。
- 编辑手柄仅对当前选中且可交互的图形显示。
- 直线使用点到线段距离进行命中；CrossPoint 使用以单点为中心的容差范围命中。

### 4.3 多边形绘制

- 左键逐点添加顶点。
- 绘制过程中显示已完成边、顶点以及末点到当前鼠标位置的预览边。
- 点击首点或在最后一点双击完成闭合，至少需要 3 个顶点。
- 草稿未闭合时右键显示“取消当前多边形绘制”。
- Select 模式下可拖动已有顶点；点击边上且避开端点的位置可插入新顶点。

### 4.4 旋转矩形

- 初次绘制生成 4 个矩形顶点。
- 选中后显示独立旋转控制点，控制点应在命中普通形状之前参与命中，避免点击控制点时选择结果变为 `null`。
- 拖动旋转控制点时应围绕矩形中心旋转全部 4 个顶点。
- 拖动任意角点时以对角点为锚点，沿当前局部坐标轴更新宽高；结果必须保持 4 个角均为直角，不得退化为自由多边形编辑。

### 4.5 Line 正交约束

- 新建 Line 时按住 `Shift`，根据水平位移和垂直位移的较大者吸附为水平或垂直。
- Select 模式拖动 Line 端点时按住 `Shift`，以另一端点为锚点执行相同正交吸附。

### 4.6 Corner

- Corner 由两个 `ShapeType.Line` 子图形组成，存放于 `CornerLines`。
- Corner 的 Bound 为两条 Line 所有端点的最小轴对齐包围框，并须实时绘制。
- 命中某条 Line 后，仅移动/编辑该 Line，Corner Bound 随端点实时重算。
- 在 Bound 内但未命中任一 Line 时，拖动整个 Corner，两条 Line 同步移动。
- Corner 整体复制、粘贴、方向键移动或程序化平移时必须同步处理两条子线。

## 5. PolygonWithHoles 规范

### 5.1 轮廓模型

```csharp
public class PolygonContour
{
    public bool IsHole { get; set; }
    public List<SKPoint> Points { get; set; } = new();
}
```

- `ShapeModel.Points` 是主外轮廓。
- `ShapeModel.PolygonHoles` 保存附加轮廓。
- `IsHole == true` 表示应从填充区域中扣除的孔洞。
- `IsHole == false` 可表示与主外轮廓不相交、但仍属于同一 `PolygonWithHoles` 的额外实体轮廓。
- 路径使用 `SKPathFillType.EvenOdd` 渲染全部轮廓。

### 5.2 编辑约束

- 外轮廓可整体移动和逐顶点编辑。
- 每个孔洞可独立整体拖动，也可独立拖动顶点。
- 孔洞移动或顶点编辑后，孔洞所有边必须保持在主外轮廓内部或边界上，不允许越过外轮廓。
- 移动整个 `PolygonWithHoles` 时，主外轮廓和全部附加轮廓同步移动。
- 有切片配置时，移动孔洞、编辑孔洞顶点或编辑外轮廓后必须立即重建切片结果。

## 6. 切片功能规范

### 6.1 适用范围

切片菜单仅对以下图形开放：

- `Rectangle`
- `Polygon`
- `PolygonWithHoles`

切片配置使用 `RectangleSliceOptions.RowHeight`。总高度取原图形包围框高度，不通过行列数配置；最后一行高度为总高度减去前面完整行高后的剩余值。

### 6.2 结果模型

- 切片结果存放在父图形的 `SubSlicedRectangles`。
- 切片子图形必须 `IsInteractive = false`，不可独立 Hover、选择、移动、缩放或删除。
- 子图形继承父图形的边线、填充和 `RegionName`。
- 子图形使用虚线轮廓绘制，父图形保持原有实线状态。
- 父图形移动时切片子图形同步移动；父图形尺寸、顶点或孔洞改变时重建切片。
- 画布平移和缩放是统一显示变换，父图形和切片结果天然同步。

### 6.3 Rectangle 和 Polygon

- Rectangle 按包围框从上到下生成矩形条带。
- Polygon 先按行高生成水平带，再将外轮廓裁剪到每个水平带内。

### 6.4 PolygonWithHoles

每一行切片必须表达以下集合运算：

```text
切片区域 =（主外轮廓 ∩ 当前水平带）- Σ（孔洞轮廓 ∩ 当前水平带）
```

验收语义：

1. 每个水平带只生成一个 `ShapeType.PolygonWithHoles` 父级切片对象。
2. 主外轮廓和每个 `IsHole == true` 的孔洞均裁剪到相同的 `[top, bottom]` 水平带。
3. 裁剪后的孔洞仍标记为 `IsHole = true`，通过偶奇填充从该切片中扣除。
4. 一个水平带被孔洞分成两个或多个互不相交的红色实体区域时，这些区域在数据语义上仍属于同一个 `PolygonWithHoles` 切片，而不是多个独立可操作 Shape。
5. 切片不得生成孔洞内部的填充区域，也不得生成超出外轮廓或当前水平带的区域。
6. 不应为了布尔差集引入可见的内部辅助边；虚线仅表示该切片最终区域的真实边界。

## 7. 精细调整

右键菜单“精细调整”仅用于：

- Rectangle：设置左上角 `X`、`Y`、宽度和高度，宽高必须大于 0。
- Circle：设置圆心 `X`、`Y` 和半径，半径必须大于 0。

数值解析同时接受当前区域格式和 invariant 格式。Rectangle 修改后如存在切片配置，应立即重建切片。

## 8. 右键菜单与快捷键

### 8.1 图形菜单

| 操作 | 显示条件 | 行为 |
| --- | --- | --- |
| 复制 | 当前选中图形可交互 | 深拷贝当前图形 |
| 粘贴 | 已存在复制缓存，且全局允许交互 | 新增偏移后的副本并选中 |
| 切片 | Rectangle/Polygon/PolygonWithHoles 且可交互 | 打开行高配置窗口并重建切片 |
| 精细调整 | Rectangle/Circle 且可交互 | 打开数值化几何编辑窗口 |
| 删除 | 当前选中图形可交互 | 删除整个图形及其从属数据 |

在空白区域右键时，如果复制缓存非空，应允许打开仅包含可用“粘贴”操作的菜单。

### 8.2 键盘

| 快捷键 | 行为 |
| --- | --- |
| `Ctrl+C` | 复制选中图形 |
| `Ctrl+V` | 粘贴图形 |
| `←/→/↑/↓` | 选中图形移动 1 单位 |
| `Shift + ←/→/↑/↓` | 选中图形移动 10 单位 |
| `Enter` | Polygon 草稿顶点不少于 3 个时完成绘制 |

## 9. 公共 API

### 9.1 控件 API

```csharp
public void SetTool(EditorTool tool);
public void LoadImage(string path);
public void AddShape(ShapeModel shape);
public void SaveState(string filePath);
public void LoadState(string filePath);
public void ShowLoadImageDialog();
public void ShowSaveStateDialog();
public void ShowLoadStateDialog();
```

### 9.2 控件属性

```csharp
public bool AreShapesInteractive { get; set; }
public SKColor PolygonEdgeColor { get; set; }
public SKColor PolygonVertexColor { get; set; }
public float PolygonVertexRadius { get; set; }
```

### 9.3 程序化添加示例

```csharp
editor.AddShape(new ShapeModel
{
    Type = ShapeType.PolygonWithHoles,
    RegionName = "检测区域 A",
    StrokeColor = "#FFFF0000",
    FillColor = "#55FF0000",
    IsFillTransparent = false,
    Points = new List<SKPoint>
    {
        new(50, 50), new(300, 50), new(300, 400), new(50, 400)
    },
    PolygonHoles = new List<PolygonContour>
    {
        new()
        {
            IsHole = true,
            Points = new List<SKPoint>
            {
                new(100, 100), new(250, 100), new(250, 350), new(100, 350)
            }
        }
    }
});
```

## 10. 保存与加载

- `CanvasState` 保存图片路径和全部 Shape。
- 保存状态时，如果图片存在，将图片复制到 JSON 文件同目录，并在 JSON 中写入相对文件名。
- JSON 使用 UTF-8，无 BOM，并允许中文直接写入。
- 加载状态时，以 JSON 所在目录解析相对图片路径。
- 必须持久化图形类型、点、样式、区域名、交互标志、孔洞、Corner 子线、切片配置及切片结果。
- 加载旧状态时应允许新增属性缺失，并使用模型默认值。

## 11. 渲染顺序

建议保持以下顺序，以避免交互提示被图形覆盖：

1. 清空背景。
2. 应用画布缩放和平移。
3. 绘制图片。
4. 绘制全部 Shape 的填充、切片虚线和实线轮廓。
5. 绘制 Hover/选中效果。
6. 绘制编辑手柄、旋转控制点和 Corner Bound。
7. 绘制 `RegionName` 标签。
8. 绘制当前未完成的 Polygon 草稿。

## 12. 验收测试清单

### 12.1 图片与画布

- [ ] 普通图片和 16 位 TIFF 均可加载。
- [ ] 光标中心缩放位置稳定，缩放上下限有效。
- [ ] Pan 工具无需 `Ctrl` 即可拖动画布。
- [ ] 任意工具下 `Ctrl + 左键拖动` 可平移画布。
- [ ] 双击后图片完整适配视口。

### 12.2 通用交互

- [ ] 每种 Shape 均可选择、Hover、平移和方向键移动。
- [ ] `IsInteractive = false` 的 Shape 不响应任何形状操作。
- [ ] `AreShapesInteractive = false` 时全部 Shape 不响应操作，但画布平移/缩放仍可用。
- [ ] 复制粘贴后模型完全独立，编辑副本不影响原图形。
- [ ] 保存再加载后图片、样式、几何、孔洞、切片和 RegionName 保持一致。

### 12.3 图形专项

- [ ] RotatedRectangle 角点编辑后相邻边仍垂直，旋转控制点始终可命中。
- [ ] Line 绘制和端点编辑时 `Shift` 正交约束有效。
- [ ] Corner 单线操作与整体操作可正确区分，Bound 实时更新。
- [ ] Polygon 可闭合、拖顶点、边上插点和取消草稿。
- [ ] PolygonWithHoles 孔洞不能拖出外轮廓，孔洞顶点也不能越界。

### 12.4 切片专项

- [ ] 行高大于总高时只生成一行。
- [ ] 总高不能整除行高时，最后一行使用剩余高度。
- [ ] Rectangle、Polygon、PolygonWithHoles 均显示切片菜单。
- [ ] 切片边界为虚线，原图形边界保持原样。
- [ ] 父图形平移/编辑后切片同步更新。
- [ ] PolygonWithHoles 每行切片都为一个 `PolygonWithHoles`。
- [ ] 穿过孔洞的切片不包含孔洞内部区域。
- [ ] 被孔洞分隔的多个红色区域仍属于同一个切片 Shape。
- [ ] 移动孔洞或拖动孔洞顶点后，所有切片立即重建且几何正确。

## 13. 已知边界与后续建议

- 当前水平裁剪采用逐边裁剪方法；复杂自交多边形、相互重叠孔洞或孔洞接触外轮廓时，应补充明确的数据合法性校验和专项测试。
- `PolygonHoles` 名称兼容历史设计，但它实际可承载 `IsHole = false` 的附加实体轮廓；后续可考虑重命名为 `Contours`，并提供 JSON 迁移策略。
- 建议将几何算法从控件类拆分为独立服务，并为水平裁剪、孔洞约束、旋转矩形和命中测试增加单元测试。
- 建议公开只读 Shapes 集合、选择变化事件、图形变化事件和撤销/重做接口，便于业务系统集成。
