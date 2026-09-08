namespace SwiftExportClient.Features.QualityControl.AfterSales.Models;

/// <summary>售后筛选下拉项，将稳定值与中文显示文本分开。</summary>
public sealed record AfterSalesFilterOption<T>(T Value, string Text);
