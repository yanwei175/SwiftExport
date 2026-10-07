using System.Globalization;
using System.Text.RegularExpressions;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;

namespace SwiftExport.Application.Services.Systems.ExcelReadProfiles;

/// <summary>校验配置格式及读取边界，不读取 Excel、不维护客户端业务字段目录。</summary>
public static class ExcelReadProfilePolicy
{
    private static readonly Regex Identifier = new(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex FieldKey = new(@"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\z", RegexOptions.CultureInvariant);
    private static readonly Regex Cell = new(@"\A\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})\z", RegexOptions.CultureInvariant);

    /// <summary>判断单据分组是否为最多 40 字符的稳定 ASCII 标识。</summary>
    public static bool IsGroupValid(string? value) => value is not null && value.Trim().Length is > 0 and <= 40 && Identifier.IsMatch(value.Trim());

    /// <summary>校验名称与备注长度；名称去首尾空白后必须非空。</summary>
    public static ServiceErrorDto? ValidateName(string? name, string? remark)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return Error("配置名称必须为 1 至 100 个字符。", "ConfigName");
        if (remark?.Length > 500) return Error("备注不能超过 500 个字符。", "Remark");
        return null;
    }

    /// <summary>校验完整更新快照；只拒绝格式错误，缺少末行规则等结构缺项允许保存。</summary>
    public static ServiceErrorDto? ValidateUpdate(UpdateExcelReadProfileRequest request)
    {
        var error = ValidateName(request.ConfigName, request.Remark);
        if (error is not null) return error;
        if (request.ExpectedVersion <= 0) return Error("预期版本必须大于零。", "ExpectedVersion");
        if (request.EndMode is < 0 or > 2) return Error("结束方式只能是 0、1 或 2。", "EndMode");
        if (request.Fields is null || request.Fields.Count > 256) return Error("必须提交完整字段列表，且最多 256 个字段。", "Fields");
        if (!string.IsNullOrWhiteSpace(request.LastRowColumn)
            && (request.EndMode != 1 || !TryColumn(request.LastRowColumn, out _)))
            return Error("末行判断列必须为 A 至 XFD，且仅用于结束方式 1。", "LastRowColumn");
        if (!string.IsNullOrWhiteSpace(request.SummaryCellAddress)
            && (request.EndMode != 2 || !TryCell(request.SummaryCellAddress, out _, out _)))
            return Error("汇总位置必须为单个有效 A1 地址，且仅用于结束方式 2。", "SummaryCellAddress");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < request.Fields.Count; index++)
        {
            var field = request.Fields[index];
            var path = $"Fields[{index}]";
            if (field is null) return Error("字段映射不能为 null。", path);
            var key = field.TargetFieldKey?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 100 || !FieldKey.IsMatch(key))
                return Error("目标字段必须是最多 100 字符的点分隔 ASCII 标识。", path + ".TargetFieldKey");
            if (!keys.Add(key)) return Error("同一配置不能重复设置目标字段。", path + ".TargetFieldKey");
            if (!IsWorksheetValid(field.WorksheetName)) return Error("工作表名称无效。", path + ".WorksheetName");
            if (!TryCell(field.CellAddress, out _, out _)) return Error("必须指定单个有效 A1 单元格，不能使用范围、公式或外部引用。", path + ".CellAddress");
        }
        return null;
    }

    /// <summary>将单个 A1 地址规范化，限制在 Excel 的 XFD 列和 1048576 行内，拒绝多区域及外部引用。</summary>
    public static bool TryCell(string? value, out string normalized, out int row)
    {
        normalized = string.Empty;
        row = 0;
        if (value is null || value.Trim().Length > 12) return false;
        var match = Cell.Match(value.Trim());
        if (!match.Success || !TryColumn(match.Groups[1].Value, out var column)) return false;
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out row) || row > 1048576) return false;
        normalized = column + row.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>规范化列字母，拒绝超出 Excel 最大列的地址。</summary>
    public static bool TryColumn(string? value, out string normalized)
    {
        normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length is < 1 or > 3) return false;
        var number = 0;
        foreach (var character in normalized)
        {
            if (character is < 'A' or > 'Z') return false;
            number = number * 26 + character - 'A' + 1;
        }
        return number <= 16384;
    }

    /// <summary>计算保存配置的结构完整性；向下字段共用起始行和结束边界，不根据参考工作表名称决定完整性。</summary>
    public static void Evaluate(ExcelReadProfileDto profile)
    {
        profile.Issues.Clear();
        if (profile.Fields.Count == 0) Add(ExcelReadProfileErrorCodes.ExcelReadFieldsRequired, "Fields", "请至少指定一个字段的读取位置。");
        var downward = profile.Fields.Where(field => field.IsDownward).ToArray();
        if (downward.Length > 0)
        {
            TryCell(downward[0].CellAddress, out _, out var startRow);
            if (downward.Any(field => !TryCell(field.CellAddress, out _, out var row) || row != startRow))
                Add(ExcelReadProfileErrorCodes.ExcelReadStartRowMismatch, "Fields", "向下读取字段必须位于同一起始行。");
            if (profile.EndMode == 0) Add(ExcelReadProfileErrorCodes.ExcelReadEndRequired, "EndMode", "向下读取必须指定末行判断列或汇总行。");
            if (profile.EndMode == 1 && string.IsNullOrEmpty(profile.LastRowColumn))
                Add(ExcelReadProfileErrorCodes.ExcelReadColumnRequired, "LastRowColumn", "请指定明细工作表中的末行判断列。");
            if (profile.EndMode == 2)
            {
                if (!TryCell(profile.SummaryCellAddress, out _, out var summaryRow))
                    Add(ExcelReadProfileErrorCodes.ExcelReadSummaryRequired, "SummaryCellAddress", "请指定明细工作表中的汇总单元格。");
                else if (summaryRow <= startRow)
                    Add(ExcelReadProfileErrorCodes.ExcelReadSummaryBeforeStart, "SummaryCellAddress", "汇总行必须位于明细起始行之后。");
            }
        }
        profile.CanRead = profile.Issues.Count == 0;
        void Add(string code, string field, string message) => profile.Issues.Add(new ExcelReadProfileIssueDto { Code = code, Field = field, Message = message });
    }

    /// <summary>工作表名称可缺省；提供名称时校验格式，但不检查它是否为当前活动工作表。</summary>
    private static bool IsWorksheetValid(string? value) => string.IsNullOrWhiteSpace(value)
        || (value.Length <= 31 && value[0] != '\'' && value[^1] != '\''
        && !value.Any(character => char.IsControl(character) || "[]:*?/\\".Contains(character)));

    private static ServiceErrorDto Error(string message, string field) => new() { Code = ServiceErrorCodes.Validation, Message = message, Field = field };
}
