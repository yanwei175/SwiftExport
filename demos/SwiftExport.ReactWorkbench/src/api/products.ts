import type { PagedResultDto, ProductDto, ServiceResult } from "../types";

export interface ProductSearchRequest {
  Keyword?: string;
  PageIndex: number;
  PageSize: number;
  CategoryName?: string;
  MaterialType?: string;
}

const unwrapServiceResult = <T>(result: ServiceResult<T>): T => {
  const success = result.success ?? result.Success;
  const message = result.message ?? result.Message ?? "API 请求失败";
  if (!success) throw new Error(message);
  return (result.data ?? result.Data) as T;
};

export const getPagedItems = <T>(page: PagedResultDto<T>) => page.items ?? page.Items ?? [];
export const getTotalCount = <T>(page: PagedResultDto<T>) => page.totalCount ?? page.TotalCount ?? 0;

export async function searchProducts(request: ProductSearchRequest) {
  const response = await fetch("/api/Product/search", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(request)
  });

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}：${response.statusText}`);
  }

  const result = (await response.json()) as ServiceResult<PagedResultDto<ProductDto>>;
  return unwrapServiceResult(result);
}

export async function getProductById(uid: string) {
  const response = await fetch(`/api/Product/${uid}`);
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}：${response.statusText}`);
  }

  const result = (await response.json()) as ServiceResult<ProductDto>;
  return unwrapServiceResult(result);
}
