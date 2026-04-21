using SwiftExport.Core.Entities.Systems;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.ReadModels.Systems
{
    public class SysDicCurrencyDetail
    {
        public required SysDicCurrency Currency { get; set; }
        public required List<SysDicCurrencyExchange> CurrencyRates { get; set; }
    }
}
