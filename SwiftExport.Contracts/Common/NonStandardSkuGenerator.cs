using System;

namespace SwiftExport.Contracts.Common
{
    /// <summary>
    /// 为不进入产品主数据的临时非标需求生成稳定编号。
    /// </summary>
    public static class NonStandardSkuGenerator
    {
        private const int IdentifierLength = 12;
        private const string Prefix = "NS-";

        /// <summary>由稳定行标识生成 NS 编号，拒绝空标识；重复调用得到相同结果。</summary>
        public static string Create(Guid lineUid)
        {
            if (lineUid == Guid.Empty)
            {
                throw new ArgumentException("订单行 Uid 不能为空。", nameof(lineUid));
            }

            var identifier = lineUid.ToString("N").Substring(0, IdentifierLength).ToUpperInvariant();
            return Prefix + identifier;
        }

        /// <summary>只验证生成编号的格式，不据此前缀推断单据或产品的非标业务类型。</summary>
        public static bool IsGeneratedNonStandardSku(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku)
                || !sku.StartsWith(Prefix, StringComparison.Ordinal)
                || sku.Length != Prefix.Length + IdentifierLength)
            {
                return false;
            }

            for (var index = Prefix.Length; index < sku.Length; index++)
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
