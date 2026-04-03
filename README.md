# WPF + .NET 6 Skia 图像显示/标注控件

该示例实现了基于 **SkiaSharp (SkiaWindow/Skia Surface 思路)** 的图像显示与几何图形标注控件，满足：

1. 加载显示图像。
2. 支持手动画矩形、圆形、多边形；也支持通过代码 `AddShape` 传入坐标直接显示；支持线宽、颜色、透明填充。
3. 支持滚轮以当前光标为中心缩放，并支持 `Ctrl + 鼠标左键拖拽` 平移整个画布。
4. 支持保存/加载当前图片与几何图形（JSON，图片以 Base64 PNG 存储）。
5. 控件核心为 `SkiaImageEditorControl`，使用 `SKElement`（Skia WPF 视图）。
6. 全部绘制由 Skia 完成，无 GDI+。
7. 单击可选中单个图形，选中后仅用鼠标左键即可拖拽移动图形（光标需落在图形内），右键弹出选中图形操作菜单（删除）。
8. 鼠标移入/移出图形时带透明蒙层填充高亮。
9. 双击画布可让图片自适应控件窗口。
10. 多边形绘制时显示已绘制边与“当前鼠标位置”的正在绘制边；可在最后一点双击闭合，或点击首点手动闭合。
11. 图形编辑：圆形可拖拽半径点调半径；矩形可拖拽 4 边/4 顶点调尺寸；多边形可拖拽顶点调形状，并可点击边上非顶点位置新增顶点。

## 主要文件

- `Controls/SkiaImageEditorControl.cs`：控件核心，含渲染、交互、命中测试、保存加载。
- `Models/ShapeModels.cs`：图形模型和状态模型。
- `MainWindow.xaml` + `MainWindow.xaml.cs`：演示窗口与工具栏。

## 运行

```bash
dotnet restore
dotnet run
```

> 注意：这是 `net6.0-windows` WPF 项目，请在 Windows 环境运行。
