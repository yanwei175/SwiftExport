using SwiftExport.Contracts.SysEnums.StaticEnums;
using System;

namespace SwiftExport.Contracts.Common
{
    /// <summary>
    /// 为不进入产品主数据的临时非标需求生成稳定编号。
    /// </summary>
    public static class NonStandardSkuGenerator
    {
        private const int IdentifierLength = 12;

        public static string Create(Guid lineUid)
        {
            if (lineUid == Guid.Empty)
            {
                throw new ArgumentException("订单行 Uid 不能为空。", nameof(lineUid));
            }

            var identifier = lineUid.ToString("N").Substring(0, IdentifierLength).ToUpperInvariant();
            return DocumentTypeStaticsEnum.NonStandardPart + "-" + identifier;
        }

        public static bool IsGeneratedNonStandardSku(string sku)
        {
            var prefix = DocumentTypeStaticsEnum.NonStandardPart + "-";
            if (string.IsNullOrWhiteSpace(sku)
                || !sku.StartsWith(prefix, StringComparison.Ordinal)
                || sku.Length != prefix.Length + IdentifierLength)
            {
                return false;
            }

            for (var index = prefix.Length; index < sku.Length; index++)
            {
                var value = sku[index];
                if (!((value >= '0' && value <= '9') || (value >= 'A' && value <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
