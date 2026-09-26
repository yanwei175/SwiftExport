using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <remarks>来源表：SysDicCurrency、SysDicCurrencyExchange。</remarks>
    public class SysDicCurrencyDetailDto
    {

        //public string CurrencyCode;
        //public string CurrencyNameEn;
        //public string CurrencyNameZh;
        //public string CurrencySymbol;
        //public int Precision;
        //public decimal? RateToBase;
        //public bool IsBase;
        //public bool IsSystem;
        //public bool IsEnabled;
        //public string Remark;
        
        public SysDicCurrencyDto Currency;

        public List<SysDicCurrencyExchangeDto> CurrencyRates;


    }
}
