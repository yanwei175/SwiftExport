export type LineState = "clean" | "created" | "updated" | "deleted";

export interface ProductLookupItem {
  uid: string;
  sku: string;
  customerSku: string;
  name: string;
  color: string;
  unit: string;
  defaultPrice: number;
  stockQty: number;
  imageUrl: string;
  supplier: string;
}

export interface OrderLine {
  id: string;
  version: number;
  productUid?: string;
  sku: string;
  customerSku: string;
  productName: string;
  productImageUrl: string;
  color: string;
  unit: string;
  qty: number;
  unitPrice: number;
  amount: number;
  deliveryDate: string;
  remark: string;
  lineState: LineState;
  errors: string[];
}

export interface GridContext {
  updateLineProduct: (lineId: string, product: ProductLookupItem) => void;
}

export interface ServiceResult<T> {
  success?: boolean;
  Success?: boolean;
  message?: string;
  Message?: string;
  data?: T;
  Data?: T;
}

export interface PagedResultDto<T> {
  items?: T[];
  Items?: T[];
  totalCount?: number;
  TotalCount?: number;
  pageIndex?: number;
  PageIndex?: number;
  pageSize?: number;
  PageSize?: number;
}

export interface ProductDto {
  uid?: string;
  Uid?: string;
  version?: number;
  Version?: number;
  masterSku?: string;
  MasterSku?: string;
  sku?: string;
  Sku?: string;
  descriptionEn?: string;
  DescriptionEn?: string;
  descriptionZh?: string;
  DescriptionZh?: string;
  finish?: string;
  Finish?: string;
  materialType?: string;
  MaterialType?: string;
  supplyType?: string;
  SupplyType?: string;
  bomVersion?: number;
  BomVersion?: number;
  bomNo?: string;
  BomNo?: string;
  note?: string;
  Note?: string;
  linkSku?: string;
  LinkSku?: string;
  cnyPrice?: number;
  CNYPrice?: number;
  usdPrice?: number;
  USDPrice?: number;
  pcsOfCarton?: number | null;
  PcsOfCarton?: number | null;
  nwOfCarton?: number | null;
  NwOfCarton?: number | null;
  gwOfCarton?: number | null;
  GwOfCarton?: number | null;
  cbmOfCarton?: number | null;
  CbmOfCarton?: number | null;
  categoryId?: string;
  CategoryId?: string;
  colorId?: string;
  ColorId?: string;
  categoryName?: string;
  CategoryName?: string;
  supplierName?: string;
  SupplierName?: string;
  defaultCustomerCode?: string;
  DefaultCustomerCode?: string;
  defaultCustomerSku?: string;
  DefaultCustomerSku?: string;
  defaultSupplierCode?: string;
  DefaultSupplierCode?: string;
  defaultSupplierSku?: string;
  DefaultSupplierSku?: string;
  mainImage?: string;
  MainImage?: string;
}
