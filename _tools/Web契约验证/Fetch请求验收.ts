import {
  login, getMe, getDictionaryByCode, getProductWarehouseCandidates, saveProductBatch,
} from './generated/client.ts';
import type {
  SwiftExportContractsDTOsProductsProductDto as ProductDto,
  SwiftExportContractsSysEnumsStaticEnumsSaveOperation as SaveOperation,
  SwiftExportContractsWrappersPagedResponseBaseOfSwiftExportContractsDTOsProductsProductDto as ProductPage,
} from './generated/client.ts';
import { classifyProductSave, readProductPage } from './产品调用样例.ts';

/** 抛出可定位的契约验收失败，不依赖前端框架或额外测试库。 */
function check(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

// 编译时验证生成 DTO 允许业务 null、字符串枚举，日期仍是传输字符串。
const product: ProductDto = { uid: '49cdbca0-ef30-488d-9d05-2c25d2308ef7', unit: null,
  createdDate: '2026-10-07T01:02:03Z' };
const operation: SaveOperation = 'Create';
check(product.unit === null && operation === 'Create', '生成类型的 null/枚举契约不符。');
const page: ProductPage = {};
// @ts-expect-error 总页数是服务端计算投影，生成契约必须保持只读。
page.totalPages = 1;

const originalFetch = globalThis.fetch;
const requests: { url: string; options?: RequestInit }[] = [];
let status = 200;
let responseBody: unknown = {};
let networkFailure = false;
// 仅拦截本脚本的 Fetch；本测试不建立网络连接，真实 HTTP 已由 .NET 宿主验证。
globalThis.fetch = async (input, options) => {
  requests.push({ url: String(input), options });
  if (options?.signal?.aborted) throw new DOMException('验收取消', 'AbortError');
  if (networkFailure) throw new TypeError('验收网络故障');
  return new Response(JSON.stringify(responseBody), { status,
    headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': 'fetch-contract', 'Retry-After': '5' } });
};
try {
  responseBody = { access_token: 'contract-token' };
  const tokenResponse = await login({ userName: 'user', password: 'password' });
  check(tokenResponse.status === 200 && tokenResponse.data.access_token === 'contract-token', '裸登录 DTO 未按声明消费。');
  check(requests.at(-1)?.url === '/api/auth/login', '登录未使用同源路径。');
  check(new Headers(requests.at(-1)?.options?.headers).get('Content-Type') === 'application/json', '请求应选择 application/json。');

  responseBody = { userId: product.uid, permissions: ['Products.Product.View'] };
  const me = await getMe({ headers: { Authorization: 'Bearer contract-token' } });
  check(me.status === 200 && me.data.permissions?.[0] === 'Products.Product.View', '裸 me DTO 未按声明消费。');

  const abort = new AbortController();
  responseBody = { success: true, data: { items: [product], totalCount: 1 }, error: null };
  const result = await readProductPage({ keyword: 'SKU', categoryIds: [product.uid!], pageIndex: 2, pageSize: 30 }, 'contract-token', abort.signal);
  check(result.status === 200 && result.data.data?.items?.[0]?.unit === null, '分页包装/null 未正确保留。');
  const request = requests.at(-1)!;
  check(request.url === '/api/product/page' && request.options?.method === 'POST', '分页 HTTP 请求错误。');
  check(JSON.parse(String(request.options?.body)).pageIndex === 2, '分页请求正文错误。');
  check(new Headers(request.options?.headers).get('Authorization') === 'Bearer contract-token', 'Bearer 丢失。');
  check(request.options?.signal === abort.signal, 'AbortSignal 未透传。');

  await getDictionaryByCode('Unit / A');
  check(requests.at(-1)?.url === '/api/sysdictdata/by-code/Unit%20%2F%20A', '字典路径参数未编码。');
  await getProductWarehouseCandidates({ Keyword: '仓库 & A', PageIndex: 2, PageSize: 30 });
  const query = new URL(requests.at(-1)!.url, 'https://contract.invalid').searchParams;
  check(query.get('Keyword') === '仓库 & A' && query.get('PageIndex') === '2', 'GET 查询参数未正确编码。');

  const batch = { createItems: [{ clientKey: 'new-1', data: { sku: 'NEW', unit: null } }] };
  responseBody = { success: true, error: null, data: { totalCount: 1, allSucceeded: true,
    items: [{ clientKey: 'new-1', operation: 'Create', uid: product.uid, success: true, oldVersion: null, newVersion: 1 }] } };
  check(classifyProductSave(await saveProductBatch(batch)).kind === 'saved', '整批成功回执未确认。');
  check(JSON.parse(String(requests.at(-1)?.options?.body)).createItems[0].clientKey === 'new-1', '保存请求丢失 ClientKey。');

  responseBody = { success: true, error: null, data: { totalCount: 1, allSucceeded: false,
    items: [{ success: false, errorCode: 'CONCURRENCY_CONFLICT' }] } };
  check(classifyProductSave(await saveProductBatch(batch)).kind === 'unconfirmed', '原子接口不能仅凭 HTTP 200 接纳失败行。');

  for (const [httpStatus, code] of [[422, 'BUSINESS_BLOCKED'], [503, 'OUTCOME_UNKNOWN'], [503, 'SERVICE_UNAVAILABLE'], [429, 'RATE_LIMITED']] as const) {
    status = httpStatus;
    responseBody = { success: false, data: null, error: { code, field: 'UpdateItems[0].Version', message: '受控错误' } };
    const before = requests.length;
    const response = await saveProductBatch(batch);
    const classification = classifyProductSave(response);
    check(response.status === httpStatus && classification.code === code, 'HTTP 与机器错误码不匹配。');
    check(classification.kind === (code === 'OUTCOME_UNKNOWN' ? 'unknown' : 'rejected'), '结果未知分类错误。');
    check(classification.correlationId === 'fetch-contract' && requests.length === before + 1, '错误追踪丢失或进行了自动重试。');
  }

  abort.abort();
  let cancelled = false;
  try { await readProductPage({}, 'contract-token', abort.signal); }
  catch (error) { cancelled = error instanceof DOMException && error.name === 'AbortError'; }
  check(cancelled, '取消错误被吞掉。');
  networkFailure = true;
  const before = requests.length;
  let failed = false;
  try { await saveProductBatch(batch); }
  catch (error) { failed = error instanceof TypeError; }
  check(failed && requests.length === before + 1, '网络故障被吞掉或写入被自动重试。');
  console.log('Fetch 验收通过：裸认证 DTO、JSON/Bearer、查询编码、取消、批量回执、422/429/503 与网络错误。');
} finally {
  globalThis.fetch = originalFetch;
}
