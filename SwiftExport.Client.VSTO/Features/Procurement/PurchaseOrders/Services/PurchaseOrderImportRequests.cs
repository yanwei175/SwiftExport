using System;
using System.Collections.Generic;
using System.Linq;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Services
{
    /// <summary>复制检查或保存意图；只新建无来源草稿，不发送来源、状态、审计和包装要求。</summary>
    internal static class PurchaseOrderImportRequests
    {
        internal static CheckPurchaseOrderImportRequest Check(PurchaseOrderImportHeader h, Guid supplier, IEnumerable<PurchaseOrderImportLine> lines) =>
            new CheckPurchaseOrderImportRequest { CustomerCode=h.CustomerCode?.Trim(), SupplierId=supplier,
                Header=new PurchaseOrderImportHeaderInput { BoNo = h.BoNo,
                CustomerPoNo = h.CustomerPoNo,
                SalesPerson = h.SalesPerson,
                PurchasePerson = h.PurchasePerson,
                InletHoseType = h.InletHoseType,
                AeratorType = h.AeratorType,
                PalletType = h.PalletType,
                BoxType = h.BoxType,
                LabelType = h.LabelType,
                InstructionManualType = h.InstructionManualType,
                CartonType = h.CartonType,
                LogoType = h.LogoType,
                Remark = h.Remark,
                DiscountRemark = h.DiscountRemark,
                OrderDate = h.OrderDate,
                ReplyDate = h.ReplyDate,
                DiscountRate = h.DiscountRate,
                DiscountAmount = h.DiscountAmount },
                Lines=lines.Select(l=>new PurchaseOrderImportLineInput { ClientLineId=l.Uid, IsNonStandard=l.IsNonStandard, SelectedCustomerProductId=l.SelectedCustomerProductId, CustomerSku=l.CustomerSku, LinkSku=l.OriginalLinkSku, AcceptLinkSkuMismatch=l.AcceptLinkSkuMismatch, Unit=l.Unit, FinishZh=l.FinishZh, FeaturesZh=l.FeaturesZh, Quantity=l.Quantity.Value, UnitPrice=l.UnitPrice, TaxPrice=l.TaxPrice, DeliverySupplierId=l.IsNonStandard ? null : l.DeliverySupplierId, PlannedDeliveryDate=l.PlannedDeliveryDate, SupplierSku=l.SupplierSku, DescriptionZh=l.DescriptionZh, SubNote=l.SubNote }).ToList() };
        internal static CreateIndependentPurchaseOrderCommand Create(PurchaseOrderImportHeader h, Guid customer, Guid supplier, IEnumerable<PurchaseOrderImportLine> lines) =>
            new CreateIndependentPurchaseOrderCommand { CommandId=Guid.NewGuid(), CustomerId=customer, SupplierId=supplier,
                BoNo = h.BoNo,
                CustomerPoNo = h.CustomerPoNo,
                SalesPerson = h.SalesPerson,
                PurchasePerson = h.PurchasePerson,
                InletHoseType = h.InletHoseType,
                AeratorType = h.AeratorType,
                PalletType = h.PalletType,
                BoxType = h.BoxType,
                LabelType = h.LabelType,
                InstructionManualType = h.InstructionManualType,
                CartonType = h.CartonType,
                LogoType = h.LogoType,
                Remark = h.Remark,
                DiscountRemark = h.DiscountRemark,
                OrderDate = h.OrderDate.Value,
                ReplyDate = h.ReplyDate,
                DiscountRate = h.DiscountRate,
                DiscountAmount = h.DiscountAmount,
                Lines=lines.Select(l=>new CreateIndependentPurchaseOrderLineCommand { ProductId=l.IsNonStandard ? Guid.Empty : l.ProductId.Value, IsNonStandard=l.IsNonStandard, SelectedCustomerProductId=l.SelectedCustomerProductId, CustomerSku=l.CustomerSku, LinkSku=l.OriginalLinkSku, AcceptLinkSkuMismatch=l.AcceptLinkSkuMismatch, Unit=l.Unit, FinishZh=l.FinishZh, FeaturesZh=l.FeaturesZh, Quantity=l.Quantity.Value, UnitPrice=l.UnitPrice, TaxPrice=l.TaxPrice, DeliverySupplierId=l.IsNonStandard ? null : l.DeliverySupplierId, PlannedDeliveryDate=l.PlannedDeliveryDate, SupplierSku=l.SupplierSku, DescriptionZh=l.DescriptionZh, SubNote=l.SubNote }).ToList() };
    }
}
