import { readFile, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

// 只选真实依赖的首批端点，递归保留其引用组件；不改字段、类型或响应正文。
const doc = JSON.parse(await readFile('.artifacts/OpenAPI完整.json', 'utf8'));
const selected = [
  ['/api/auth/login', 'post', 'login'],
  ['/api/auth/me', 'get', 'getMe'],
  ['/api/product/page', 'post', 'getProductPage'],
  ['/api/product/batch-save', 'post', 'saveProductBatch'],
  ['/api/productcategory/all', 'get', 'getProductCategories'],
  ['/api/sysdictdata/by-code/{dictCode}', 'get', 'getDictionaryByCode'],
  ['/api/productcolor/all', 'get', 'getProductColors'],
  ['/api/supplier/page', 'post', 'getSupplierPage'],
  ['/api/product/default-warehouse-candidates', 'get', 'getProductWarehouseCandidates'],
];
const paths = {};
for (const [path, method, id] of selected) {
  const operation = doc.paths[path]?.[method];
  assert.ok(operation, `缺少 ${method} ${path}`);
  assert.equal(operation.operationId, id);
  assert.ok(operation.responses['200'].content['application/json']);
  assert.ok(operation.responses['401'].content['application/json']);
  if (id === 'login') assert.deepEqual(operation.security, []);
  else assert.deepEqual(operation.security, [{ Bearer: [] }]);
  if (method === 'post') assert.equal(operation.requestBody.required, true);
  paths[path] = { [method]: operation };
}

// 用实际引用闭包裁剪全量文档，组件身份仍与生产导出相同。
const schemas = {};
function collect(value) {
  if (value === null || typeof value !== 'object') return;
  if (typeof value.$ref === 'string') {
    assert.ok(value.$ref.startsWith('#/components/schemas/'), `未处理引用 ${value.$ref}`);
    const name = value.$ref.slice('#/components/schemas/'.length);
    assert.match(name, /^[a-zA-Z0-9._-]+$/);
    if (!(name in schemas)) {
      assert.ok(doc.components.schemas[name], `悬空引用 ${name}`);
      schemas[name] = doc.components.schemas[name];
      collect(schemas[name]);
    }
  }
  for (const child of Object.values(value)) collect(child);
}
collect(paths);
const product = schemas['SwiftExport.Contracts.DTOs.Products.ProductDto'];
assert.equal(product.properties.uid.format, 'uuid');
assert.equal(product.properties.createdDate.format, 'date-time');
assert.equal(product.properties.unit.nullable, true);
assert.ok(!product.required?.includes('unit'));
const saveOperation = schemas['SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation'];
assert.equal(saveOperation.type, 'string');
assert.deepEqual(saveOperation.enum, ['Create', 'Update', 'Delete']);
const wrapper = schemas['SwiftExport.Contracts.Wrappers.ServiceResultOfSwiftExport.Contracts.DTOs.Products.Responses.ProductBatchSaveResponse'];
assert.equal(wrapper.properties.error.nullable, true);
assert.equal(wrapper.properties.data.nullable, true);
assert.equal(doc.components.securitySchemes.Bearer.scheme, 'bearer');
await writeFile('openapi.json', JSON.stringify({
  openapi: doc.openapi, info: doc.info, paths,
  components: { schemas, securitySchemes: { Bearer: doc.components.securitySchemes.Bearer } },
}, null, 2) + '\n');
console.log(`已核对 ${selected.length} 个 HTTP 端点、${Object.keys(schemas).length} 个引用组件；写入 openapi.json。`);
