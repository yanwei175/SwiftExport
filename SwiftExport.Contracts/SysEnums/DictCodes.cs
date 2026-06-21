using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums
{
    public static class DictCodes
    {
        //=======动态类型枚举定义========

        // 这里定义系统中使用的字典类型代码，确保在整个系统中使用一致的代码来引用这些字典类型
        // 例如： DictCode,DictName, IsSystem, Remark
        //左边是枚举的名称,枚举名称可以是中文,方便写代码，右边是数据库中存储的字符串值
        #region 产品类
        //public const string SupplyType = "SupplyType";
        #endregion

        #region 单据相关
        public const string Currency = "Currency"; //货币
        public const string CustomerType = "CustomerType"; //客户类型
        public const string Incoterm = "Incoterm"; //国际贸易术语
        public const string ProductColor = "ProductColor"; //产品颜色
        public const string ProductType = "ProductType"; //产品类型
        public const string PaymentMethod = "PaymentMethod"; //支付方式
        public const string IncotermSortRemark = "IncotermSortRemark"; //
        public const string IncotermLocation = "IncotermLocation"; //
        public const string ProductCategory = "ProductCategory"; //
        #endregion

        public static List<string> DictCodeList => new List<string>
        {
            "Currency","CustomerType","Incoterm","ProductColor","ProductType","PaymentMethod","IncotermSortRemark","IncotermLocation","ProductCategory"
        };
    }




}
