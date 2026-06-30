# WPF Image Cell Guide

## 适用场景

用于产品、销售、采购、箱单等 DataGrid 图片列，以及图片缩略图、ToolTip 预览和图片路径转换。

## SwiftExport 事实

图片转换器位于：

- `MateWpfThemes/MateWpfThemes.FW/Converters/ImgPathToImgConverter.cs`

主题库已有图片和 DataGrid 相关资源：

- `MateWpfThemes/MateWpfThemes.FW/Styles/Base/Controls/Image.xaml`
- `MateWpfThemes/MateWpfThemes.FW/Styles/Base/Controls/DataGrid.xaml`

多个页面已有局部 `ImgPathToImgConverter`、`ZeroPaddingCell`、`ImageCellTemplate`。

## SwiftExport 规则

- 优先复用 `ImgPathToImgConverter`，不要为单个页面复制图片路径转换器。
- 图片单元格优先使用零内边距 CellStyle，避免图片被默认 Padding 挤压。
- 缩略图优先使用 `Stretch="Uniform"` 或当前模块已有模板。
- 大图预览优先用 ToolTip 或项目已有 DialogHost 弹窗方案。
- 图片字段名以当前模块真实 DTO / Model 为准，例如 `MainImage`。
- 如需优化缓存、解码尺寸、文件锁释放，应在 MateWpfThemes 统一处理。

## 修改前检查

- 当前页面是否已有 `ImageCellTemplate` 可复用。
- 图片路径是否可能为空、无效或来自网络/本地磁盘。
- 是否影响 DataGrid 行高、虚拟化、滚动性能。
- DevApp 和 VSTO 宿主是否都能解析相关资源。

## 禁止事项

- 禁止每个页面复制一份图片转换器。
- 禁止在 ViewModel 中加载 `BitmapImage` 作为常规做法。
- 禁止为单个图片列修改全局 DataGrid 样式而不评估影响。
