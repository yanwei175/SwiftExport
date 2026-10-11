namespace SwiftExportClient.Services.Files.Excel;

/// <summary>调用方自定义字段键与单个 A1 地址的关联，不解释业务属性路径。</summary>
/// <param name="FieldKey">结果中的稳定字段键。</param>
/// <param name="CellAddress">单格 A1 地址，可含绝对标记，不接受范围或外部引用。</param>
/// <param name="IsDownward">是否从该单格逐行向下读取明细。</param>
public sealed record ExcelLayoutField(string FieldKey, string CellAddress, bool IsDownward = false);
