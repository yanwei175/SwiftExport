# SwiftExport WPF DataGrid 图片单元格草稿

## 当前项目事实

图片转换器已存在：

- `MateWpfThemes/MateWpfThemes.FW/Converters/ImgPathToImgConverter.cs`

基础样式中已有图片相关资源：

- `MateWpfThemes/MateWpfThemes.FW/Styles/Base/Controls/Image.xaml`
- `MateWpfThemes/MateWpfThemes.FW/Styles/Base/Controls/DataGrid.xaml`

多个页面已有局部资源：

- `ImgPathToImgConverter`
- `ZeroPaddingCell`
- `ImageCellTemplate`

## 适用范围

用于产品、销售、采购、箱单等 DataGrid 图片列。

## SwiftExport 规则

- 优先复用现有 `ImgPathToImgConverter`，不要为单个页面复制转换器。
- DataGrid 图片单元格使用零内边距 CellStyle，避免图片被默认 Padding 挤压。
- 缩略图使用 `Stretch="Uniform"` 或项目已有模板。
- 大图预览优先使用 ToolTip 或现有弹窗方案。
- 图片加载应避免锁定文件；如果需要优化转换器，优先在 MateWpfThemes 统一处理。

## 修改检查

- 是否已有全局/局部 `ImageCellTemplate` 可复用。
- 图片字段名是否为当前模块真实字段，例如 `MainImage`。
- 是否影响 DataGrid 行高、虚拟化、滚动性能。
- 是否在 DevApp 和 VSTO 宿主都能解析资源。
