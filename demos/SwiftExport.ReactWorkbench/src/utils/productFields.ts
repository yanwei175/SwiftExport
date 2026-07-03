import type { ProductDto } from "../types";

export const productUid = (row: ProductDto) => row.uid ?? row.Uid ?? "";
export const productVersion = (row: ProductDto) => row.version ?? row.Version ?? 0;
export const productSku = (row: ProductDto) => row.sku ?? row.Sku ?? "";
export const productMasterSku = (row: ProductDto) => row.masterSku ?? row.MasterSku ?? "";
export const productDescriptionEn = (row: ProductDto) => row.descriptionEn ?? row.DescriptionEn ?? "";
export const productDescriptionZh = (row: ProductDto) => row.descriptionZh ?? row.DescriptionZh ?? "";
export const productFinish = (row: ProductDto) => row.finish ?? row.Finish ?? "";
export const productMaterialType = (row: ProductDto) => row.materialType ?? row.MaterialType ?? "";
export const productSupplyType = (row: ProductDto) => row.supplyType ?? row.SupplyType ?? "";
export const productBomVersion = (row: ProductDto) => row.bomVersion ?? row.BomVersion ?? 0;
export const productBomNo = (row: ProductDto) => row.bomNo ?? row.BomNo ?? "";
export const productNote = (row: ProductDto) => row.note ?? row.Note ?? "";
export const productCnyPrice = (row: ProductDto) => row.cnyPrice ?? row.CNYPrice ?? 0;
export const productUsdPrice = (row: ProductDto) => row.usdPrice ?? row.USDPrice ?? 0;
export const productPcsOfCarton = (row: ProductDto) => row.pcsOfCarton ?? row.PcsOfCarton ?? null;
export const productNwOfCarton = (row: ProductDto) => row.nwOfCarton ?? row.NwOfCarton ?? null;
export const productGwOfCarton = (row: ProductDto) => row.gwOfCarton ?? row.GwOfCarton ?? null;
export const productCbmOfCarton = (row: ProductDto) => row.cbmOfCarton ?? row.CbmOfCarton ?? null;
export const productCategoryName = (row: ProductDto) => row.categoryName ?? row.CategoryName ?? "";
export const productSupplierName = (row: ProductDto) => row.supplierName ?? row.SupplierName ?? "";
export const productDefaultCustomerCode = (row: ProductDto) =>
  row.defaultCustomerCode ?? row.DefaultCustomerCode ?? "";
export const productDefaultCustomerSku = (row: ProductDto) =>
  row.defaultCustomerSku ?? row.DefaultCustomerSku ?? "";
export const productDefaultSupplierCode = (row: ProductDto) =>
  row.defaultSupplierCode ?? row.DefaultSupplierCode ?? "";
export const productDefaultSupplierSku = (row: ProductDto) =>
  row.defaultSupplierSku ?? row.DefaultSupplierSku ?? "";
export const productMainImage = (row: ProductDto) => row.mainImage ?? row.MainImage ?? "";
