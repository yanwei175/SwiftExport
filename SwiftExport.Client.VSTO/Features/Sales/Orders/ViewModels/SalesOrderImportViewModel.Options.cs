using System;
using System.Collections.Generic;
using System.Linq;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels
{
    public sealed partial class SalesOrderImportViewModel
    {
        private bool refreshingOptions;
        public IReadOnlyList<SysDicCurrencyDto> CurrencyOptions { get; private set; }
        public IReadOnlyList<SysDictDataDto> PaymentMethodOptions { get; private set; }
        public IReadOnlyList<SysDictDataDto> IncotermOptions { get; private set; }
        public IReadOnlyList<SysDictDataDto> ShippedByOptions { get; private set; }
        public IReadOnlyList<SysDictDataDto> UnitOptions { get; private set; }
        private string MissingOptionMessage { get; set; } = "";

        // 用页面自有选项承接失效值，ItemsSource 更新不删除用户输入或修改缓存对象。
        private void RefreshOptions()
        {
            if (refreshingOptions) return;
            refreshingOptions = true;
            try
            {
                var missing = new List<string>();
                var currency = Header.Currency;
                var currencies = ReferenceData.Currencies.ToList();
                if (!string.IsNullOrEmpty(currency) && !currencies.Any(item => item.CurrencyCode == currency))
                {
                    currencies.Add(new SysDicCurrencyDto { CurrencyCode = currency }); missing.Add("币种 " + currency);
                }
                currencies.Insert(0, new SysDicCurrencyDto { CurrencyCode = "", CurrencyNameZh = "（空）" });
                CurrencyOptions = currencies;
                PaymentMethodOptions = CreateOptions(DictCodes.PaymentMethod, new[] { Header.PaymentMethod }, missing);
                IncotermOptions = CreateOptions(DictCodes.Incoterm, new[] { Header.Incoterm }, missing);
                ShippedByOptions = CreateOptions(DictCodes.ShippedBy, new[] { Header.ShippedBy }, missing);
                UnitOptions = CreateOptions(DictCodes.Unit, Lines.Where(l => l.IsNonStandard).Select(l => l.UnitSnapshot), missing);
                if (Header.CustomerId != Guid.Empty && !ReferenceData.Customers.Any(c => c.Uid == Header.CustomerId)) missing.Add("当前客户");
                MissingOptionMessage = missing.Count == 0 ? "" : "以下值已不在可选基础资料中，请核对：" + string.Join("、", missing.Distinct());
                foreach (var name in new[] { nameof(CurrencyOptions), nameof(PaymentMethodOptions), nameof(IncotermOptions), nameof(ShippedByOptions), nameof(UnitOptions) }) OnPropertyChanged(name);
            }
            finally { refreshingOptions = false; }
        }
        private IReadOnlyList<SysDictDataDto> CreateOptions(string code, IEnumerable<string> selected, List<string> missing)
        {
            var values = ReferenceData.GetDictionaryValues(code).ToList();
            foreach (var value in selected.Where(s => !string.IsNullOrEmpty(s)).Distinct())
            {
                if (values.Any(item => item.DataNameEn == value)) continue;
                values.Add(new SysDictDataDto { DictCode = code, DataNameEn = value }); missing.Add(value);
            }
            values.Insert(0, new SysDictDataDto { DictCode = code, DataNameEn = "", DataNameZh = "（空）" });
            return values;
        }
    }
}
