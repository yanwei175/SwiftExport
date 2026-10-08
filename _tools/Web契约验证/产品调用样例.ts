import { getProductPage } from './generated/client.ts';
import type {
  SwiftExportContractsDTOsProductsRequestsProductSearchRequest as ProductSearchRequest,
  saveProductBatchResponse,
} from './generated/client.ts';

/** 使用同源路径和调用方提供的令牌/取消信号读取产品；不保存令牌或引入状态框架。 */
export function readProductPage(request: ProductSearchRequest, accessToken: string, signal?: AbortSignal) {
  return getProductPage(request, { headers: { Authorization: `Bearer ${accessToken}` }, signal });
}

/** 按实际 Product 原子保存契约分类；只有完整整批成功回执才能接纳草稿，未知结果由调用方核实。 */
export function classifyProductSave(response: saveProductBatchResponse) {
  const correlationId = response.headers.get('X-Correlation-ID');
  if (response.status === 200) {
    const batch = response.data.data;
    const items = batch?.items;
    if (response.data.success === true && batch?.allSucceeded === true && items?.length
      && batch.totalCount === items.length && items.every(item => item.success === true)) {
      return { kind: 'saved' as const, items, correlationId };
    }
    // HTTP 200 的行失败不改判为 409；对于 Product 原子接口，这种不完整回执需要核实。
    return { kind: 'unconfirmed' as const, correlationId };
  }
  const error = response.data.error;
  if (response.status === 503 && error?.code === 'OUTCOME_UNKNOWN') {
    return { kind: 'unknown' as const, code: error.code, correlationId };
  }
  return { kind: 'rejected' as const, status: response.status,
    code: error?.code, field: error?.field, message: error?.message, correlationId };
}
