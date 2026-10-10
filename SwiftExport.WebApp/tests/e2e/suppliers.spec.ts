import { expect, test } from "@playwright/test";
import type { Locator, Page, Route } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { authenticationCalls, mockBrowserSession } from "./会话模拟";
import type {
  SwiftExportContractsDTOsSuppliersSupplierDto as Supplier,
  SwiftExportContractsDTOsSuppliersRequestsSupplierBatchSaveRequest as SupplierBatch,
  SwiftExportContractsDTOsSuppliersRequestsSupplierSearchRequest as SupplierSearch,
  SwiftExportContractsWrappersSaveItemResult as SaveItemResult,
} from "../../app/shared/api/generated/client";

const supplierPermissions = [
  "Suppliers.Supplier.View",
  "Suppliers.Supplier.Create",
  "Suppliers.Supplier.Edit",
  "Suppliers.Supplier.Delete",
];
const pagePath = "/api/supplier/page";
const savePath = "/api/supplier/batch-save";
type SaveMode =
  | "success"
  | "business-unknown"
  | "outer-failure"
  | "server-failure"
  | "rate-limited"
  | "forbidden"
  | "response-lost"
  | "incomplete"
  | "duplicate"
  | "wrong-version"
  | "partial";

/** 供应商测试只持有浏览器内快照，精确模拟两个已审核端点，不访问业务库。 */
interface SupplierFixture {
  session: Awaited<ReturnType<typeof mockBrowserSession>>;
  rows: Supplier[];
  reads: SupplierSearch[];
  writes: SupplierBatch[];
  readStatus: number;
  mode: SaveMode;
  writeGate?: Promise<void>;
  nextUid: number;
}
const observations = new WeakMap<Page, SupplierFixture>();

/** 使用稳定合法 Guid，创建身份由模拟服务端另外产生。 */
function uid(index: number): string {
  return `87654321-1234-4321-8765-${index.toString(16).padStart(12, "0")}`;
}

/** 仅定位当前 Activity 展示；隐藏后控制器与原始输入仍由原页签持有。 */
function pane(page: Page): Locator {
  return page.locator('[data-testid="supplier-management"]:visible');
}

/** 统一字段定位保留行号语义，不使用页码序号作为提交身份。 */
function field(page: Page, row: number, name: string): Locator {
  return pane(page).getByLabel(`供应商${row}${name}`, { exact: true });
}

/** 测试数据含完整可写字段与审计元数据，便于发现页面误清空非当前编辑字段。 */
function supplier(index: number): Supplier {
  return {
    uid: uid(index),
    version: index === 1 ? 7 : index === 2 ? 9 : 1,
    supplierCode: `SUP-${index.toString().padStart(3, "0")}`,
    supplierName: `供应商样本${index}`,
    docRelateNo: `关联-${index}`,
    zheKou: 0.95,
    zheKouBeiZhu: "保留折扣说明",
    taxRate: 0.13,
    defaultLeadTime: 4,
    contactPerson: `联系人${index}`,
    contactEmail: `contact${index}@example.test`,
    contactPhone: `020-0000${index}`,
    address: `测试地址${index}`,
    currency: "USD",
    paymentTerms: "月结30天",
    isDeleted: false,
    createdBy: "原创建人",
    createdDate: "2026-10-01T08:00:00Z",
    updatedBy: "原修改人",
    updatedDate: "2026-10-02T08:00:00Z",
  };
}

/** 通过既有 Cookie 登录协议进入真实 Workspace，供应商请求必须携带正确媒体类型与 Bearer。 */
async function initialize(
  page: Page,
  permissions = supplierPermissions,
  rowCount = 2,
  discount = 0.95,
): Promise<SupplierFixture> {
  const session = await mockBrowserSession(page, {
    permissions: [...permissions],
  });
  const fixture: SupplierFixture = {
    session,
    rows: Array.from({ length: rowCount }, (_, index) => ({
      ...supplier(index + 1),
      zheKou: discount,
    })),
    reads: [],
    writes: [],
    readStatus: 200,
    mode: "success",
    nextUid: 100,
  };
  observations.set(page, fixture);
  await page.route(
    (url) =>
      url.origin === "https://localhost:4173" && url.pathname === pagePath,
    async (route) => {
      const request = route.request();
      expect(request.method()).toBe("POST");
      expect(request.headers().authorization).toBe("Bearer " + session.token);
      expect(request.headers()["content-type"]).toBe("application/json");
      const body = request.postDataJSON() as SupplierSearch;
      fixture.reads.push(structuredClone(body));
      expect(body.pageIndex).toBeGreaterThanOrEqual(1);
      expect(body.pageSize).toBeGreaterThan(0);
      const keyword = body.keyword?.trim() ?? "";
      const filtered = fixture.rows.filter(
        (row) =>
          !keyword ||
          row.supplierCode?.includes(keyword) ||
          row.supplierName?.includes(keyword),
      );
      const start = (body.pageIndex! - 1) * body.pageSize!;
      await route.fulfill({
        status: fixture.readStatus,
        json:
          fixture.readStatus === 200
            ? {
                success: true,
                data: {
                  items: filtered.slice(start, start + body.pageSize!),
                  totalCount: filtered.length,
                  pageIndex: body.pageIndex,
                  pageSize: body.pageSize,
                  totalPages: Math.ceil(filtered.length / body.pageSize!),
                },
              }
            : {
                success: false,
                error: {
                  code: "UNEXPECTED",
                  message: "模拟供应商读取失败",
                },
              },
      });
    },
  );
  await page.route(
    (url) =>
      url.origin === "https://localhost:4173" && url.pathname === savePath,
    async (route) => {
      const request = route.request();
      expect(request.method()).toBe("POST");
      expect(request.headers().authorization).toBe("Bearer " + session.token);
      expect(request.headers()["content-type"]).toBe("application/json");
      const body = request.postDataJSON() as SupplierBatch;
      fixture.writes.push(structuredClone(body));
      await fixture.writeGate;
      await respondToSave(route, fixture, body);
    },
  );
  await page.goto("/login");
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page
    .getByLabel("密码", { exact: true })
    .fill("supplier-browser-test-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  await page.goto("/workspace?view=supplier-management");
  await expect(
    pane(page).getByRole("heading", { name: "供应商管理", exact: true }),
  ).toBeVisible();
  if (permissions.includes("Suppliers.Supplier.View"))
    await expect(field(page, 1, "供应商代码")).toHaveValue("SUP-001");
  return fixture;
}

/** 模拟真实整批事务回执及当前 PG Version 行为，损坏回执不得作为部分成功接纳依据。 */
async function respondToSave(
  route: Route,
  fixture: SupplierFixture,
  request: SupplierBatch,
) {
  if (fixture.mode === "response-lost") {
    await route.abort("connectionreset");
    return;
  }
  const failures: Partial<Record<SaveMode, { status: number; code: string }>> =
    {
      "business-unknown": { status: 422, code: "BUSINESS_BLOCKED" },
      "outer-failure": { status: 200, code: "BUSINESS_BLOCKED" },
      "server-failure": { status: 500, code: "UNEXPECTED" },
      "rate-limited": { status: 429, code: "RATE_LIMITED" },
      forbidden: { status: 403, code: "FORBIDDEN" },
    };
  const failure = failures[fixture.mode];
  if (failure) {
    await route.fulfill({
      status: failure.status,
      json: {
        success: false,
        data: null,
        error: { code: failure.code, message: "模拟失败：服务器结果仍需核实" },
      },
    });
    return;
  }
  const create = request.createItems ?? [];
  const update = request.updateItems ?? [];
  const remove = request.deleteItems ?? [];
  const results: SaveItemResult[] = [
    ...create.map<SaveItemResult>((entry) => ({
      clientKey: entry.clientKey,
      rowIndex: entry.rowIndex,
      operation: "Create",
      uid: uid(fixture.nextUid++),
      success: true,
      oldVersion: null,
      newVersion: 1,
    })),
    ...update.map<SaveItemResult>((entry) => ({
      clientKey: entry.clientKey,
      rowIndex: entry.rowIndex,
      operation: "Update",
      uid: entry.uid,
      success: true,
      oldVersion: entry.version! + 1,
      newVersion: entry.version! + 1,
    })),
    ...remove.map<SaveItemResult>((entry) => ({
      clientKey: entry.clientKey,
      rowIndex: entry.rowIndex,
      operation: "Delete",
      uid: entry.uid,
      success: true,
      oldVersion: entry.version,
      newVersion: entry.version,
    })),
  ];
  if (fixture.mode === "success") {
    const deleted = new Set(remove.map((entry) => entry.uid));
    fixture.rows = [
      ...fixture.rows
        .filter((row) => !deleted.has(row.uid))
        .map((row) => {
          const changed = update.find((entry) => entry.uid === row.uid);
          return changed
            ? {
                ...row,
                ...changed.data,
                uid: row.uid,
                version: row.version! + 1,
              }
            : row;
        }),
      ...create.map((entry) => {
        const receipt = results.find(
          (item) => item.clientKey === entry.clientKey,
        )!;
        expect(receipt.uid).toBeTruthy();
        return { ...entry.data, uid: receipt.uid!, version: 1 };
      }),
    ];
  }
  let responseItems = results;
  if (fixture.mode === "incomplete") responseItems = [];
  if (fixture.mode === "duplicate") responseItems = [results[0]!, results[0]!];
  if (fixture.mode === "wrong-version")
    responseItems = results.map((entry) => ({ ...entry, newVersion: 999 }));
  if (fixture.mode === "partial")
    responseItems = results.map((entry, index) => ({
      ...entry,
      success: index === 0,
      newVersion: index === 0 ? entry.newVersion : null,
    }));
  await route.fulfill({
    json: {
      success: true,
      data: {
        totalCount: results.length,
        items: responseItems,
        successCount: fixture.mode === "partial" ? 1 : results.length,
        failedCount: fixture.mode === "partial" ? results.length - 1 : 0,
        allSucceeded: fixture.mode !== "partial",
        addSuccessCount: create.length,
        updateSuccessCount: update.length,
        deleteSuccessCount: remove.length,
      },
    },
  });
}

/** 页签星号消失与再次可编辑共同证明完整回执接纳，不能仅凭 HTTP 200 判 clean。 */
async function expectClean(page: Page) {
  await expect(
    page.getByRole("tab", { name: "供应商管理", exact: true }),
  ).toBeVisible();
  await expect(field(page, 1, "供应商名称")).toBeEditable();
}

/** 等待当前确认框真正卸载，避免动画期间误定位到下一阶段弹窗。 */
async function cancelDialog(page: Page, title?: string) {
  const dialog = title
    ? page.getByRole("dialog", { name: title, exact: true })
    : page.getByRole("dialog");
  await dialog.getByRole("button", { name: "取消", exact: true }).click();
  await expect(dialog).toHaveCount(0);
}

/** 业务模拟只能命中两个供应商端点，任何其他 API 访问与运行时错误都使验收失败。 */
test.afterEach(async ({ page }) => {
  const fixture = observations.get(page);
  if (!fixture) return;
  expect(fixture.session.errors, "供应商页面不能产生运行时异常").toEqual([]);
  const allowed = [`POST ${pagePath}`, `POST ${savePath}`];
  expect(
    fixture.session.apiCalls.filter(
      (call) => !authenticationCalls.includes(call) && !allowed.includes(call),
    ),
    "未审核业务请求必须被测试拒绝",
  ).toEqual([]);
});

test("服务端分页与查询复用生成操作，不把未加载页数据当成删除", async ({
  page,
}) => {
  const fixture = await initialize(page, supplierPermissions, 22);
  expect(fixture.reads[0]).toMatchObject({ pageIndex: 1, pageSize: 20 });
  await pane(page).locator(".ant-pagination-next").click();
  await expect(field(page, 1, "供应商代码")).toHaveValue("SUP-021");
  expect(fixture.reads.at(-1)).toMatchObject({ pageIndex: 2, pageSize: 20 });
  await pane(page).getByLabel("供应商查询", { exact: true }).fill("SUP-022");
  await pane(page).getByRole("button", { name: "查询", exact: true }).click();
  await expect(field(page, 1, "供应商代码")).toHaveValue("SUP-022");
  expect(fixture.reads.at(-1)).toMatchObject({
    keyword: "SUP-022",
    pageIndex: 1,
  });
  expect(fixture.writes).toEqual([]);
});

test("查询与分页保护当前输入，取消不读取，刷新失败保留原行与基线", async ({
  page,
}) => {
  const fixture = await initialize(page, supplierPermissions, 22);
  await field(page, 1, "供应商名称").fill("保留未提交名称");
  const reads = fixture.reads.length;
  await pane(page).locator(".ant-pagination-next").click();
  await cancelDialog(page, "处理未保存供应商修改");
  expect(fixture.reads).toHaveLength(reads);
  await pane(page).getByLabel("供应商查询", { exact: true }).fill("SUP-022");
  await pane(page).getByRole("button", { name: "查询", exact: true }).click();
  await cancelDialog(page, "处理未保存供应商修改");
  await expect(field(page, 1, "供应商名称")).toHaveValue("保留未提交名称");
  expect(fixture.reads).toHaveLength(reads);
  fixture.readStatus = 500;
  await pane(page).getByRole("button", { name: "刷新", exact: true }).click();
  await page
    .getByRole("dialog", { name: "处理未保存供应商修改", exact: true })
    .getByRole("button", { name: "放弃并继续", exact: true })
    .click();
  await expect(pane(page)).toContainText("模拟供应商读取失败");
  await expect(field(page, 1, "供应商代码")).toHaveValue("SUP-001");
  await expect(field(page, 1, "供应商名称")).toHaveValue("保留未提交名称");
  expect(fixture.writes).toEqual([]);
});

test("增改删一次冻结，完整成功接纳新身份并使用真实新版本继续保存", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1217, height: 906 });
  const fixture = await initialize(page);
  await page.screenshot({
    path: ".artifacts/供应商管理.png",
    fullPage: true,
  });
  await field(page, 1, "供应商名称").fill("修改供应商名称");
  await pane(page)
    .getByRole("button", { name: /新增供应商$/ })
    .click();
  await field(page, 3, "供应商代码").fill("SUP-NEW");
  await field(page, 3, "供应商名称").fill("新增供应商");
  await field(page, 3, "单据关联号").fill("新关联号");
  await pane(page)
    .locator("tr.ant-table-row")
    .nth(1)
    .getByRole("checkbox")
    .first()
    .check();
  await pane(page)
    .getByRole("button", { name: "删除选中", exact: true })
    .click();
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expectClean(page);
  expect(fixture.writes).toHaveLength(1);
  const batch = fixture.writes[0]!;
  expect(batch.createItems).toHaveLength(1);
  expect(batch.updateItems).toHaveLength(1);
  expect(batch.deleteItems).toHaveLength(1);
  expect(batch.updateItems![0]).toMatchObject({
    uid: uid(1),
    version: 7,
    clientKey: uid(1),
    data: {
      supplierCode: "SUP-001",
      supplierName: "修改供应商名称",
      contactEmail: "contact1@example.test",
      paymentTerms: "月结30天",
      zheKouBeiZhu: "保留折扣说明",
    },
  });
  expect(batch.deleteItems![0]).toMatchObject({ uid: uid(2), version: 9 });
  expect(batch.createItems![0]!.data).toMatchObject({
    supplierCode: "SUP-NEW",
    supplierName: "新增供应商",
    docRelateNo: "新关联号",
  });
  await expect(field(page, 2, "供应商代码")).toHaveValue("SUP-NEW");
  await field(page, 2, "联系人").fill("创建后修改");
  await field(page, 1, "供应商名称").fill("第二次修改");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expectClean(page);
  expect(fixture.writes[1]!.updateItems).toEqual(
    expect.arrayContaining([
      expect.objectContaining({ uid: uid(1), version: 8 }),
      expect.objectContaining({
        uid: uid(100),
        version: 1,
        clientKey: batch.createItems![0]!.clientKey,
      }),
    ]),
  );
});

test("新增再删除不发请求，既有行删除恢复保留未完成数字输入", async ({
  page,
}) => {
  const fixture = await initialize(page);
  await pane(page)
    .getByRole("button", { name: /新增供应商$/ })
    .click();
  await pane(page)
    .locator("tr.ant-table-row")
    .nth(2)
    .getByRole("checkbox")
    .first()
    .check();
  await pane(page)
    .getByRole("button", { name: "删除选中", exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "供应商管理", exact: true }),
  ).toBeVisible();
  await field(page, 1, "默认交期").fill("-");
  await pane(page)
    .locator("tr.ant-table-row")
    .first()
    .getByRole("checkbox")
    .first()
    .check();
  await pane(page)
    .getByRole("button", { name: "删除选中", exact: true })
    .click();
  await pane(page)
    .getByRole("button", { name: "恢复删除", exact: true })
    .click();
  await expect(field(page, 1, "默认交期")).toHaveValue("-");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expect(field(page, 1, "默认交期")).toHaveAttribute(
    "aria-invalid",
    "true",
  );
  expect(fixture.writes).toEqual([]);
});

test("首次无效数字未失焦与隐藏页关闭均保护，保存校验不派发", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const tabId = (await pane(page).getAttribute("data-tab-id"))!;
  const tab = page.locator(`.ant-tabs-tab[data-node-key="${tabId}"]`);
  await field(page, 1, "折扣").fill("-");
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toContainText("有未保存输入");
  await cancelDialog(page);
  await page.getByRole("menuitem", { name: /首页$/ }).click();
  await expect(pane(page)).toHaveCount(0);
  await tab.locator(".ant-tabs-tab-remove").click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "保存并关闭", exact: true })
    .click();
  await expect(field(page, 1, "折扣")).toHaveValue("-");
  await expect(field(page, 1, "折扣")).toHaveAttribute("aria-invalid", "true");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(fixture.writes).toEqual([]);
  await field(page, 1, "折扣").press("Escape");
  await expect(field(page, 1, "折扣")).toHaveValue("0.95");
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(tab).toHaveCount(0);
});

test("查看权限不开放写入，不派发供应商保存", async ({ page }) => {
  const fixture = await initialize(page, ["Suppliers.Supplier.View"]);
  await expect(field(page, 1, "供应商名称")).not.toBeEditable();
  for (const name of ["新增供应商", "保存", "删除选中"])
    await expect(
      pane(page).getByRole("button", { name: new RegExp(`${name}$`) }),
    ).toBeDisabled();
  expect(fixture.writes).toEqual([]);
});

test("隐藏页关闭先激活再保存，最后未失焦名称进入冻结请求", async ({ page }) => {
  const fixture = await initialize(page);
  const tabId = (await pane(page).getAttribute("data-tab-id"))!;
  const tab = page.locator(`.ant-tabs-tab[data-node-key="${tabId}"]`);
  await field(page, 1, "供应商名称").fill("最后未失焦名称");
  await page.getByRole("menuitem", { name: /首页$/ }).click();
  await expect(pane(page)).toHaveCount(0);
  await tab.locator(".ant-tabs-tab-remove").click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "保存并关闭", exact: true })
    .click();
  await expect(tab).toHaveCount(0);
  expect(fixture.writes).toHaveLength(1);
  expect(fixture.writes[0]!.updateItems![0]).toMatchObject({
    uid: uid(1),
    version: 7,
    data: { supplierName: "最后未失焦名称" },
  });
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("公共表格向下填充仅修改选中区域，保存仍由供应商完整回执接纳", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const source = field(page, 1, "联系人").locator("xpath=ancestor::td");
  const target = field(page, 2, "联系人").locator("xpath=ancestor::td");
  await source.click({ position: { x: 3, y: 3 } });
  await target.click({ position: { x: 3, y: 3 }, modifiers: ["Shift"] });
  await pane(page)
    .getByRole("button", { name: "向下填充", exact: true })
    .click();
  await expect(field(page, 2, "联系人")).toHaveValue("联系人1");
  await expect(field(page, 2, "供应商名称")).toHaveValue("供应商样本2");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expectClean(page);
  expect(fixture.writes).toHaveLength(1);
  expect(fixture.writes[0]!.updateItems).toHaveLength(1);
  expect(fixture.writes[0]!.updateItems![0]).toMatchObject({
    uid: uid(2),
    version: 9,
    data: { contactPerson: "联系人1", supplierName: "供应商样本2" },
  });
});

test("原始输入恢复原值解除保护，不把聚焦或曾编辑当成永久 dirty", async ({
  page,
}) => {
  const fixture = await initialize(page);
  await field(page, 1, "供应商名称").fill("临时输入");
  await field(page, 1, "供应商名称").fill("供应商样本1");
  await page.locator(".ant-tabs-tab-active .ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(pane(page)).toHaveCount(0);
  expect(fixture.writes).toEqual([]);
});

test("零比例基线逐键输入保留中间原文，恢复零或取消解除保护，保存提交完整小数", async ({
  page,
}) => {
  const fixture = await initialize(page, supplierPermissions, 2, 0);
  const discount = field(page, 1, "折扣");
  await expect(discount).toHaveValue("0");
  await discount.fill("");
  await discount.pressSequentially("0.0");
  await expect(discount).toHaveValue("0.0");
  await expect(pane(page)).toContainText("业务修改：否；未提交输入：否");
  await discount.pressSequentially("5");
  await expect(discount).toHaveValue("0.05");
  await expect(pane(page)).toContainText("业务修改：否；未提交输入：是");
  await discount.fill("0");
  await expect(pane(page)).toContainText("业务修改：否；未提交输入：否");
  await expect(
    page.getByRole("tab", { name: "供应商管理", exact: true }),
  ).toBeVisible();
  await discount.fill("");
  await discount.pressSequentially("0.05");
  await discount.press("Escape");
  await expect(discount).toHaveValue("0");
  await expect(pane(page)).toContainText("业务修改：否；未提交输入：否");
  await discount.fill("");
  await discount.pressSequentially("0.05");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expectClean(page);
  expect(fixture.writes).toHaveLength(1);
  expect(fixture.writes[0]!.updateItems![0]).toMatchObject({
    uid: uid(1),
    version: 7,
    data: { zheKou: 0.05 },
  });
  await expect(discount).toHaveValue("0.05");
});

for (const mode of [
  "business-unknown",
  "outer-failure",
  "server-failure",
  "rate-limited",
  "forbidden",
  "response-lost",
  "incomplete",
  "duplicate",
  "wrong-version",
  "partial",
] as const)
  test(`${mode} 保留整批冻结，不接纳、不重发，隐藏页全部关闭不能绕过`, async ({
    page,
  }) => {
    const fixture = await initialize(page);
    fixture.mode = mode;
    await field(page, 1, "供应商名称").fill("第一行冻结输入");
    await field(page, 2, "供应商名称").fill("第二行冻结输入");
    await pane(page).getByRole("button", { name: /保存$/ }).click();
    await expect(
      pane(page).getByRole("button", { name: "导出恢复记录", exact: true }),
    ).toBeVisible();
    await expect(field(page, 1, "供应商名称")).not.toBeEditable();
    await expect(field(page, 2, "供应商名称")).toHaveValue("第二行冻结输入");
    for (const name of ["保存", "放弃修改", "新增供应商", "刷新"])
      await expect(
        pane(page).getByRole("button", { name: new RegExp(`${name}$`) }),
      ).toBeDisabled();
    await page.getByRole("menuitem", { name: /首页$/ }).click();
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expect(pane(page)).toBeVisible();
    await expect(field(page, 1, "供应商名称")).toHaveValue("第一行冻结输入");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(fixture.writes).toHaveLength(1);
  });

test("保存期间本页只读，可切换其他页，回执归原供应商会话", async ({ page }) => {
  const fixture = await initialize(page);
  let release!: () => void;
  fixture.writeGate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await field(page, 1, "供应商名称").fill("原页保存内容");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expect.poll(() => fixture.writes.length).toBe(1);
  await expect(field(page, 1, "供应商名称")).not.toBeEditable();
  await page.getByRole("menuitem", { name: /首页$/ }).click();
  release();
  await page.getByRole("tab", { name: /供应商管理/ }).click();
  await expectClean(page);
  await expect(field(page, 1, "供应商名称")).toHaveValue("原页保存内容");
  expect(fixture.writes).toHaveLength(1);
});

test("导出冻结请求的真实文件，取消任何阶段继续保护，两次确认才关闭", async ({
  page,
}) => {
  const fixture = await initialize(page);
  fixture.mode = "business-unknown";
  await field(page, 1, "供应商名称").fill("恢复记录冻结名称");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expect(
    pane(page).getByRole("button", { name: "导出恢复记录", exact: true }),
  ).toBeVisible();
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  const downloading = page.waitForEvent("download");
  await pane(page)
    .getByRole("button", { name: "导出恢复记录", exact: true })
    .click();
  const download = await downloading;
  const content = await readFile((await download.path())!, "utf8");
  const record = JSON.parse(content) as {
    recordId: string;
    interfaceOperation: string;
    frozenSubmission: SupplierBatch;
  };
  expect(download.suggestedFilename()).toBe(
    `SwiftExport-恢复记录-${record.recordId}.json`,
  );
  expect(record.interfaceOperation).toContain(savePath);
  expect(record.frozenSubmission).toEqual(fixture.writes[0]);
  expect(content).not.toMatch(
    /Authorization|access_token|password|CommandId|csrf-test-only|test-signature-only/,
  );
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", { name: "确认记录已妥善保留", exact: true }),
  ).toContainText(record.recordId);
  await cancelDialog(page, "确认记录已妥善保留");
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "确认记录已妥善保留", exact: true })
    .getByRole("button", { name: "已妥善保留", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", { name: "确认终止本地恢复", exact: true }),
  ).toContainText("服务器结果仍未确认");
  await cancelDialog(page, "确认终止本地恢复");
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(pane(page)).toBeVisible();
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "确认记录已妥善保留", exact: true })
    .getByRole("button", { name: "已妥善保留", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "确认终止本地恢复", exact: true })
    .getByRole("button", { name: "终止并关闭", exact: true })
    .click();
  await expect(page.locator('[data-testid="supplier-management"]')).toHaveCount(
    0,
  );
  expect(fixture.writes).toHaveLength(1);
});

test("导出文件生成失败继续冻结，不能被当作已保全后人工退出", async ({
  page,
}) => {
  const fixture = await initialize(page);
  fixture.mode = "response-lost";
  await field(page, 1, "供应商名称").fill("导出失败仍保留");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  await expect(
    pane(page).getByRole("button", { name: "导出恢复记录", exact: true }),
  ).toBeVisible();
  await page.evaluate(() => {
    URL.createObjectURL = () => {
      throw new Error("测试平台文件生成失败");
    };
  });
  await pane(page)
    .getByRole("button", { name: "导出恢复记录", exact: true })
    .click();
  await expect(pane(page)).toContainText("导出失败");
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(field(page, 1, "供应商名称")).toHaveValue("导出失败仍保留");
  await expect(
    pane(page).getByRole("button", { name: /保存$/ }),
  ).toBeDisabled();
  expect(fixture.writes).toHaveLength(1);
});

test("撤权阻止未知记录导出和终止，失读权遮蔽已打开供应商页面", async ({
  page,
}) => {
  const fixture = await initialize(page);
  fixture.mode = "business-unknown";
  await field(page, 1, "供应商名称").fill("撤权前输入");
  await pane(page).getByRole("button", { name: /保存$/ }).click();
  const downloading = page.waitForEvent("download");
  await pane(page)
    .getByRole("button", { name: "导出恢复记录", exact: true })
    .click();
  await downloading;
  fixture.session.identity.permissions = ["Suppliers.Supplier.View"];
  await page.getByRole("button", { name: "刷新权限", exact: true }).click();
  const downloads: string[] = [];
  page.on("download", (download) =>
    downloads.push(download.suggestedFilename()),
  );
  await pane(page)
    .getByRole("button", { name: "导出恢复记录", exact: true })
    .click();
  await expect(
    pane(page).getByText(/当前权限不允许保全|当前权限不允许导出/),
  ).toBeVisible();
  expect(downloads).toEqual([]);
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(field(page, 1, "供应商名称")).toHaveValue("撤权前输入");
  fixture.session.identity.permissions = [];
  await page.getByRole("button", { name: "刷新权限", exact: true }).click();
  await expect(pane(page)).toHaveCount(0);
  await expect(
    page.getByText("当前没有页面读取权限", { exact: true }),
  ).toBeVisible();
  expect(fixture.writes).toHaveLength(1);
});
