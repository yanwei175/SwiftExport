using System;
using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.QualityControl.FlowManagement
{
    /// <summary>返回本次测试功能行、历史字典名称及版本；流量单位由压力列确定。</summary>
    public sealed class FlowLineDto : BaseDto
    {
        public Guid HeaderId { get; set; }
        public string FunctionNameSnapshot { get; set; }
        public string LimiterSpecificationNameSnapshot { get; set; }
        public Guid FunctionId { get; set; }
        public Guid LimiterSpecificationId { get; set; }
        /// <summary>1 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow1BarLMin { get; set; }
        /// <summary>2 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow2BarLMin { get; set; }
        /// <summary>3 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow3BarLMin { get; set; }
        /// <summary>4 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow4BarLMin { get; set; }
        /// <summary>5 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow5BarLMin { get; set; }
        /// <summary>6 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow6BarLMin { get; set; }
        /// <summary>7 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow7BarLMin { get; set; }
        /// <summary>8 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
        public decimal? Flow8BarLMin { get; set; }
        /// <summary>20 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
        public decimal? Flow20PsiGpm { get; set; }
        /// <summary>40 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
        public decimal? Flow40PsiGpm { get; set; }
        /// <summary>60 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
        public decimal? Flow60PsiGpm { get; set; }
        /// <summary>80 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
        public decimal? Flow80PsiGpm { get; set; }
        /// <summary>100 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
        public decimal? Flow100PsiGpm { get; set; }
        public int SortNo { get; set; }
        public string Remark { get; set; }
    }
}

