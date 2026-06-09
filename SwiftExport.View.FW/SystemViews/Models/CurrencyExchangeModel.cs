using Mapster;
using SwiftExport.View.FW.Mvvm;
using System;
using System.ComponentModel;

namespace SwiftExport.View.FW.SystemViews.Models
{
    /// <summary>
    /// 汇率行模型
    /// 继承 BaseLineModel，支持脏数据追踪与字段级校验
    /// </summary>
    public class CurrencyExchangeModel : BaseLineModel
    {


        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private string _fromCurrencyCode;
        public string FromCurrencyCode
        {
            get => _fromCurrencyCode;
            set => SetProperty(ref _fromCurrencyCode, value);
        }

        private string _toCurrencyCode;
        public string ToCurrencyCode
        {
            get => _toCurrencyCode;
            set => SetProperty(ref _toCurrencyCode, value);
        }

        private int _precision = 6;
        public int Precision
        {
            get => _precision;
            set => SetProperty(ref _precision, value);
        }

        private decimal? _rate;
        public decimal? Rate
        {
            get => _rate;
            set => SetProperty(ref _rate, value);
        }

        private string _remark;
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        public override string Error => string.Empty;

        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(FromCurrencyCode) && string.IsNullOrWhiteSpace(FromCurrencyCode))
                    return "源货币代码不能为空";

                if (columnName == nameof(ToCurrencyCode) && string.IsNullOrWhiteSpace(ToCurrencyCode))
                    return "目标货币代码不能为空";

                if (columnName == nameof(ToCurrencyCode) && string.Equals(FromCurrencyCode, ToCurrencyCode, StringComparison.OrdinalIgnoreCase))
                    return "源货币与目标货币不能相同";

                if (columnName == nameof(Precision) && (Precision < 0 || Precision > 6))
                    return "精度范围为 0 ~ 6";

                if (columnName == nameof(Rate) && (Rate == null || Rate <= 0))
                    return "汇率必须大于 0";

                return string.Empty;
            }
        }
    }
}
