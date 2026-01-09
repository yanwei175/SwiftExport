using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities
{
    public class ProductMaster : BaseEntity
    {
        /// <summary>
        /// 主型号
        /// </summary>
        public string MasterSku { get; set; }// 必须唯一 (Unique)

        // === 模具属性 (作为新增 SKU 时的默认值) ===
        public string Description { get; set; }//描述
        public string Features { get; set; }//默认配置
        public string ProductClass { get; set; }//分类
        public string ProductType { get; set; } //类型
        public string ProductTag { get; set; }//标签属性用来归类

    }
}
