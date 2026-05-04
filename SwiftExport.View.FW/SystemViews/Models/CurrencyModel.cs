using Mapster;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;

namespace SwiftExport.View.FW.SystemViews.Models
{
    /// <summary>
    /// 货币行模型
    /// 继承 BaseLineModel，自动获得：状态机、脏数据追踪、撤销/提交、IDataErrorInfo 验证
    /// </summary>
    public class CurrencyModel : BaseLineModel, IBaseModel, IDataErrorInfo
    {
        /// <summary>
        /// 从 DTO 加载数据到 Model（Mapster 映射）
        /// 抑制清理脏数据 = true 时：映射完不立即 CommitChanges（用于 UI 新增行场景）
        /// </summary>
        public override void LoadFromDto<TDto>(TDto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {
                // Mapster 将 DTO 属性值映射到 this
                dto.Adapt(this);

                if (this.Id == 0)
                {
                    this.MarkAsAdded(); // 新行标记为 Added 状态
                }

                // 根据调用方决定是否立即清理脏数据
                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        // ============================================================
        // 字段属性区域
        // ============================================================

        #region 货币标识

        private string _currencyCode;
        /// <summary>货币代码，如 USD / CNY / EUR</summary>
        public string CurrencyCode
        {
            get => _currencyCode;
            set => SetProperty(ref _currencyCode, value);
        }

        private string _currencyNameEn;
        /// <summary>货币英文名称</summary>
        public string CurrencyNameEn
        {
            get => _currencyNameEn;
            set => SetProperty(ref _currencyNameEn, value);
        }

        private string _currencyNameZh;
        /// <summary>货币中文名称</summary>
        public string CurrencyNameZh
        {
            get => _currencyNameZh;
            set => SetProperty(ref _currencyNameZh, value);
        }

        private string _currencySymbol;
        /// <summary>货币符号，如 $、¥、€</summary>
        public string CurrencySymbol
        {
            get => _currencySymbol;
            set => SetProperty(ref _currencySymbol, value);
        }

        #endregion

        #region 精度与汇率

        private int _precision = 2;
        /// <summary>小数位精度，默认 2</summary>
        public int Precision
        {
            get => _precision;
            set => SetProperty(ref _precision, value);
        }

        private decimal? _rateToBase;
        /// <summary>对本位币的汇率（本位币自身 = 1）</summary>
        public decimal? RateToBase
        {
            get => _rateToBase;
            set
            {
                //将值按照_precision格式化为小数位，避免用户输入过多小数位导致的脏数据
                if (value == null)
                {
                    SetProperty(ref _rateToBase, null);
                }
                else
                {
                    decimal roundedValue = Math.Round(value.Value, Precision);
                    // 消除多余的后缀 0，例如 6.000 -> 6
                    roundedValue = roundedValue / 1.000000000000000000000000000000000m;
                    SetProperty(ref _rateToBase, roundedValue);
                }
            }
        }

        #endregion

        #region 状态标志

        private bool _isBase;
        /// <summary>是否本位币（全局唯一，UI 应互斥选择）</summary>
        public bool IsBase
        {
            get => _isBase;
            set => SetProperty(ref _isBase, value);
        }

        private bool _isSystem;
        /// <summary>是否系统内置货币（内置货币不允许删除）</summary>
        public bool IsSystem
        {
            get => _isSystem;
            set => SetProperty(ref _isSystem, value);
        }

        private bool _isEnabled = true;
        /// <summary>是否启用</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        #endregion

        #region 备注

        private string _remark;
        /// <summary>备注信息</summary>
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        #endregion

        // ============================================================
        // IDataErrorInfo 字段级验证
        // ============================================================

        /// <summary>行级错误（IDataErrorInfo.Error）</summary>
        public override string Error => string.Empty;

        /// <summary>
        /// 字段级验证（DataGrid 单元格实时验证）
        /// </summary>
        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(CurrencyCode) && string.IsNullOrWhiteSpace(CurrencyCode))
                    return "货币代码不能为空";

                if (columnName == nameof(CurrencyNameZh) && string.IsNullOrWhiteSpace(CurrencyNameZh))
                    return "货币中文名称不能为空";

                if (columnName == nameof(Precision) && (Precision < 0 || Precision > 6))
                    return "精度范围为 0 ~ 6";

                if (columnName == nameof(RateToBase) && !IsBase && (RateToBase == null || RateToBase <= 0))
                    return "非本位币必须填写大于 0 的汇率";

                return string.Empty;
            }
        }
    }
}
