using Mapster;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    public class SalesOrderLineModel : BaseLineModel, IDataErrorInfo
    { 
        #region 加载数据
        /// <summary>
        /// 中文注释：从 DTO 加载到行模型。
        /// 这里使用 程序内部修改Scope 包裹，确保“加载数据库数据”不会被识别成用户修改。
        /// 同时在加载结束后重置手工输入标记，避免后续数量/单价变化时自动计算失效。
        /// </summary>
        public override void LoadFromDto<SalesOrderLineDto>(SalesOrderLineDto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {

                dto.Adapt(this);//使用 Mapster 将 DTO 的属性值映射到 Model(this) 上
                if (this.Id == 0)
                { this.MarkAsAdded(); }

                // 根据调用方需要决定是否立即提交/清理脏数据
                if (!抑制清理脏数据)
                    CommitChanges();
            }

            // DTO 本身没有“手工覆盖金额/小计”的状态字段，
            // 加载完成后统一恢复为自动计算模式，避免打开单据后自动计算被锁死。
            重置计算标识();
        }
        #endregion



        /// <summary>
        /// 中文注释：重置所有“手工值”标记。
        /// 用于 DTO 加载完成后恢复自动计算行为。
        /// </summary>
        private void 重置计算标识()
        {
            _启动净重小计计算 = false;
            _启动毛重小计计算 = false;
            _启动体积小计计算 = false;
            _启动美金小计计算 = false;
            _启动人民币小计计算 = false;
            _启动已发货数量计算 = false;
        }

        #region 基础字段

        private string _mainImage = string.Empty;
        public string MainImage
        {
            get => _mainImage;
            set => SetProperty(ref _mainImage, value);
        }

        private string _单号;
        public string 单号
        {
            get => _单号;
            set => SetProperty(ref _单号, value);
        }

        private string _物料编码 = string.Empty;
        public string 物料编码
        {
            get => _物料编码;
            set => SetProperty(ref _物料编码, value);
        }

        private string _客户型号;
        public string 客户型号
        {
            get => _客户型号;
            set => SetProperty(ref _客户型号, value);
        }

        private string _描述 = string.Empty;
        public string 描述
        {
            get => _描述;
            set => SetProperty(ref _描述, value);
        }

        private string _颜色 = string.Empty;
        public string 颜色
        {
            get => _颜色;
            set => SetProperty(ref _颜色, value);
        }

        private string _配置 = string.Empty;
        public string 配置
        {
            get => _配置;
            set => SetProperty(ref _配置, value);
        }

        #endregion

        #region 计算相关字段

        private bool _启动净重小计计算;
        private bool _启动毛重小计计算;
        private bool _启动体积小计计算;
        private bool _启动美金小计计算;
        private bool _启动人民币小计计算;
        private bool _启动已发货数量计算;

        #region 数量 / 单价 / 金额（保留原样）

        private decimal _数量 = 0;
        public decimal 数量
        {
            get => _数量;
            set
            {
                if (SetProperty(ref _数量, value))
                    RecalculateAll();
            }
        }

        private decimal _美金单价;
        public decimal 美金单价
        {
            get => _美金单价;
            set
            {
                if (SetProperty(ref _美金单价, value))
                    RecalculateAll();
            }
        }

        private decimal _人民币单价;
        public decimal 人民币单价
        {
            get => _人民币单价;
            set
            {
                if (SetProperty(ref _人民币单价, value))
                    RecalculateAll();
            }
        }

        private decimal _美金小计;
        public decimal 美金小计
        {
            get => _美金小计;
            set
            {
                if (SetProperty(ref _美金小计, value) && !是否程序内部修改)
                    _启动美金小计计算 = true;
            }
        }

        private decimal _人民币小计;
        public decimal 人民币小计
        {
            get => _人民币小计;
            set
            {
                if (SetProperty(ref _人民币小计, value) && !是否程序内部修改)
                    _启动人民币小计计算 = true;
            }
        }

        #endregion

        #region 发货状态（新加字段，用于界面显示和逻辑判断）
        private decimal _已发货数量;
        public decimal 已发货数量
        {
            get => _已发货数量;
            set
            {
                if (SetProperty(ref _已发货数量, value))
                    RecalculateAll();
            }
        }

        private decimal _未发货数量;
        public decimal 未发货数量
        {
            get => _未发货数量;
            set => SetProperty(ref _未发货数量, value);
        }

        private bool _是否部分发货;
        public bool 是否部分发货
        {
            get => _是否部分发货;
            set => SetProperty(ref _是否部分发货, value);
        }

        private bool _是否全部发货;
        public bool 是否全部发货
        {
            get => _是否全部发货;
            set => SetProperty(ref _是否全部发货, value);
        }





        #endregion

        #region 包装运输信息（保留原样）

        private int _行号;
        public int 行号
        {
            get => _行号;
            set => SetProperty(ref _行号, value);
        }

        private string _备注 = string.Empty;
        public string 备注
        {
            get => _备注;
            set => SetProperty(ref _备注, value);
        }

        private decimal _每箱数量;
        public decimal 每箱数量
        {
            get => _每箱数量;
            set
            {
                if (SetProperty(ref _每箱数量, value))
                    RecalculateAll();
            }
        }

        private decimal _每箱净重;
        public decimal 每箱净重
        {
            get => _每箱净重;
            set
            {
                if (SetProperty(ref _每箱净重, value))
                    RecalculateAll();
            }
        }

        private decimal _每箱毛重;
        public decimal 每箱毛重
        {
            get => _每箱毛重;
            set
            {
                if (SetProperty(ref _每箱毛重, value))
                    RecalculateAll();
            }
        }

        private decimal _每箱体积;
        public decimal 每箱体积
        {
            get => _每箱体积;
            set
            {
                if (SetProperty(ref _每箱体积, value))
                    RecalculateAll();
            }
        }

        private decimal _箱数;
        public decimal 箱数
        {
            get => _箱数;
            private set => SetProperty(ref _箱数, value);
        }

        private decimal _净重小计;
        public decimal 净重小计
        {
            get => _净重小计;
            set
            {
                if (SetProperty(ref _净重小计, value) && !是否程序内部修改)
                    _启动净重小计计算 = true;
            }
        }

        private decimal _毛重小计;
        public decimal 毛重小计
        {
            get => _毛重小计;
            set
            {
                if (SetProperty(ref _毛重小计, value) && !是否程序内部修改)
                    _启动毛重小计计算 = true;
            }
        }

        private decimal _体积小计;
        public decimal 体积小计
        {
            get => _体积小计;
            set
            {
                if (SetProperty(ref _体积小计, value) && !是否程序内部修改)
                    _启动体积小计计算 = true;
            }
        }

        #endregion

        #region 计算逻辑（保留原样 + 自动抑制状态改变）

        private void RecalculateAll()
        {
            // 使用 程序内部修改Scope 控制程序计算不要触发数据修改状态改变，避免不必要的保存提示
            using (程序内部修改Scope())
            {
                CalculateCarton();
                CalculateNwSubTotal();
                CalculateGwSubTotal();
                CalculateCbmSubTotal();
                CalculateUsdAmount();
                CalculateRmbAmount();
                CalculateUnshipped();
            }
        }

        private void CalculateUnshipped()
        {
            if (!_启动已发货数量计算)
            {
                未发货数量 = 数量 - 已发货数量;
                是否全部发货 = 已发货数量 >= 数量;
                是否部分发货 = 已发货数量 > 0 && 已发货数量 < 数量;
            }
        }


        // 计算箱数（箱数） = 订单数量（数量） / 每箱数量（每箱数量），向上取整
        private void CalculateCarton()
        {
            if (每箱数量 <= 0)
            {
                箱数 = 0;
                return;
            }

            箱数 = Math.Ceiling(数量 / 每箱数量);
        }

        // 计算净重小计 = 箱数（箱数） * 每箱净重（每箱净重），保留两位小数，四舍五入
        private void CalculateNwSubTotal()
        {
            if (!_启动净重小计计算)
                净重小计 = Math.Round(箱数 * 每箱净重, 2, MidpointRounding.AwayFromZero);
        }

        // 计算毛重小计 = 箱数（箱数） * 每箱毛重（每箱毛重），保留两位小数，四舍五入
        private void CalculateGwSubTotal()
        {
            if (!_启动毛重小计计算)
                毛重小计 = Math.Round(箱数 * 每箱毛重, 2, MidpointRounding.AwayFromZero);
        }

        // 计算体积小计 = 箱数（箱数） * 每箱体积（每箱体积），保留三位小数，四舍五入
        private void CalculateCbmSubTotal()
        {
            if (!_启动体积小计计算)
                体积小计 = Math.Round(箱数 * 每箱体积, 3, MidpointRounding.AwayFromZero);
        }

        // 计算美金总金额 = 订单数量（数量） * 单位价格（美金单价），保留两位小数，四舍五入
        private void CalculateUsdAmount()
        {
            if (!_启动美金小计计算)
                美金小计 = Math.Round(数量 * 美金单价, 2, MidpointRounding.AwayFromZero);
        }

        // 计算人民币总金额 = 订单数量（数量） * 单位价格（人民币单价），保留两位小数，四舍五入
        private void CalculateRmbAmount()
        {
            if (!_启动人民币小计计算)
                人民币小计 = Math.Round(数量 * 人民币单价, 2, MidpointRounding.AwayFromZero);
        }
        #endregion


        #endregion

        #region IDataErrorInfo（保留原样）

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

        /// <summary>
        /// 字段级验证（WPF 绑定会自动调用）
        /// 这里写“单字段”的验证逻辑。
        /// </summary>
        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(物料编码) && string.IsNullOrWhiteSpace(物料编码))
                    return "产品 SKU 不能为空";

                if (columnName == nameof(数量) && 数量 <= 0)
                    return "订单数量必须大于 0";

                if (columnName == nameof(美金单价) && 美金单价 <= 0)
                    return "单位价格必须大于 0";

                return null;
            }
        }

        /// <summary>
        /// 行级验证（跨字段验证）
        /// 用于验证“多个字段组合是否有效”
        /// 例如：数量和单价必须同时有效、包装信息必须完整等。
        /// 保存时会自动调用 IsValid → ValidateRow。
        /// </summary>
        public override string ValidateRow()
        {
            //// 示例：数量和单价必须同时有效
            //if (数量 > 0 && 美金单价 > 0)
            return null;

            //return "数量和单价必须同时有效";
        }

        /// <summary>
        /// 异步验证（可选）
        /// 用于数据库验证，例如：SKU 是否存在、产品是否停产。
        /// 默认不做任何事，不会影响性能，也不会影响 UI。
        /// 保存时你可以手动调用：await line.ValidateAsync()
        /// </summary>
        public override async Task<string> ValidateAsync()
        {
            // 示例（伪代码）：
            // if (!await ProductService.ExistsAsync(物料编码))
            //     return "SKU 不存在于数据库";

            return null; // 默认无错误
        }

        #endregion
    }
}
