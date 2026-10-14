import { expect, test } from "@playwright/test";
import type { Locator, Page, Route } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { authenticationCalls, mockBrowserSession } from "./会话模拟";
import type {
  SwiftExportContractsDTOsSystemDtosSysDictTypeDto as DictionaryType,
  SwiftExportContractsDTOsSystemDtosSysDictDataDto as DictionaryItem,
  SwiftExportContractsDTOsSystemDtosRequestsSysDictTypeBatchSaveRequest as TypeBatch,
  SwiftExportContractsDTOsSystemDtosRequestsSysDictDataBatchSaveRequest as ItemBatch,
} from "../../app/shared/api/generated/client";

const dictionaryPermissions = [
  "System.Dictionary.View",
  "System.Dictionary.Create",
  "System.Dictionary.Edit",
  "System.Dictionary.Delete",
];
const typePath = "/api/sysdicttype/batch-save";
const itemPath = "/api/sysdictdata/batch-save";
const typesPath = "/api/sysdicttype/all";
const itemPrefix = "/api/sysdictdata/by-code/";
type SaveMode =
  | "success"
  | "business-unknown"
  | "response-lost"
  | "incomplete"
  | "rate-limited";
type SessionFixture = Awaited<ReturnType<typeof mockBrowserSession>>;

/** 每个测试独立持有模拟服务端快照，真实页面仍经生成操作和统一请求边界。 */
interface DictionaryFixture {
  session: SessionFixture;
  types: DictionaryType[];
  items: Record<string, DictionaryItem[]>;
  typesStatus: number;
  itemsStatus: number;
  itemsGate?: Promise<void>;
  saveMode: SaveMode;
  writes: { path: string; body: TypeBatch | ItemBatch }[];
  nextUid: number;
}
const observations = new WeakMap<Page, DictionaryFixture>();

/** 只模拟有效非空 Guid，便于区分本地逻辑身份和服务器创建身份。 */
function uid(index: number): string {
  return `12345678-1234-4321-8765-${index.toString(16).padStart(12, "0")}`;
}

/** 定位可见 Activity 子树，隐藏表单不能被测试误判为活动编辑区。 */
function pane(page: Page): Locator {
  return page.locator('[data-testid="dictionary-management"]:visible');
}

/** 登录仅使用模拟 Cookie 协议；业务路径精确白名单并拒绝任何意外请求。 */
async function initialize(
  page: Page,
  permissions = dictionaryPermissions,
  options: {
    itemsGate?: Promise<void>;
    waitForItems?: boolean;
    overflowData?: boolean;
  } = {},
) {
  const session = await mockBrowserSession(page, {
    permissions: [...permissions],
  });
  // 初次异步加载也必须先连接同一 Form，避免未挂载实例读写被测试遗漏。
  page.on("console", (entry) => {
    if (/Instance created by `useForm` is not connected/.test(entry.text()))
      session.errors.push(entry.text());
  });
  const fixture: DictionaryFixture = {
    session,
    types: [
      {
        uid: uid(1),
        version: 3,
        dictCode: "WebStyles",
        dictName: "样式",
        sortOrder: 1,
        isSystem: false,
        remark: "类型原备注",
        groupCode: "WebGroup",
        groupName: "保留分组",
        parentId: null,
      },
      {
        uid: uid(2),
        version: 2,
        dictCode: "WebOther",
        dictName: "其他",
        sortOrder: 2,
        isSystem: false,
      },
      {
        uid: uid(3),
        version: 5,
        dictCode: "Unit",
        dictName: "单位",
        sortOrder: 3,
        isSystem: true,
      },
    ],
    items: {
      WebStyles: [
        {
          uid: uid(11),
          version: 7,
          dictCode: "WebStyles",
          dataNameEn: "Polished",
          dataNameZh: "亮面",
          sortOrder: 1,
          isEnabled: true,
          isSystem: false,
          cssClass: "retained-style",
          remark: "原项备注",
        },
        {
          uid: uid(12),
          version: 9,
          dictCode: "WebStyles",
          dataNameEn: "Brushed",
          dataNameZh: "拉丝",
          sortOrder: 2,
          isEnabled: true,
          isSystem: false,
        },
      ],
      WebOther: [],
      Unit: [
        {
          uid: uid(13),
          version: 4,
          dictCode: "Unit",
          dataNameEn: "Piece",
          dataNameZh: "件",
          sortOrder: 1,
          isEnabled: true,
          isSystem: true,
        },
      ],
    },
    typesStatus: 200,
    itemsStatus: 200,
    itemsGate: options.itemsGate,
    saveMode: "success",
    writes: [],
    nextUid: 100,
  };
  // 大数据仅用于滚动验收，仍是浏览器内精确模拟，不创建真实字典或访问运行库。
  if (options.overflowData) {
    for (let index = 1; index <= 24; index++) {
      const code = `WebExtra${index}`;
      fixture.types.push({
        uid: uid(200 + index),
        version: 1,
        dictCode: code,
        dictName: `滚动类型${index}`,
        sortOrder: 3 + index,
        isSystem: false,
      });
      fixture.items[code] = [];
    }
    for (let index = 3; index <= 40; index++) {
      fixture.items.WebStyles!.push({
        uid: uid(300 + index),
        version: 1,
        dictCode: "WebStyles",
        dataNameEn: `Style ${index}`,
        dataNameZh: `样式${index}`,
        sortOrder: index,
        isEnabled: true,
        isSystem: false,
      });
    }
  }
  observations.set(page, fixture);
  await page.route(
    (url) => url.pathname === typesPath,
    async (route) => {
      expect(route.request().method()).toBe("GET");
      expect(route.request().headers().authorization).toBe(
        "Bearer " + session.token,
      );
      await route.fulfill({
        status: fixture.typesStatus,
        json:
          fixture.typesStatus === 200
            ? { success: true, data: fixture.types }
            : {
                success: false,
                error: { code: "UNEXPECTED", message: "模拟字典读取失败" },
              },
      });
    },
  );
  await page.route(
    (url) =>
      url.pathname.startsWith(itemPrefix) &&
      decodeURIComponent(url.pathname.slice(itemPrefix.length)) in
        fixture.items,
    async (route) => {
      expect(route.request().method()).toBe("GET");
      expect(route.request().headers().authorization).toBe(
        "Bearer " + session.token,
      );
      const code = decodeURIComponent(
        new URL(route.request().url()).pathname.slice(itemPrefix.length),
      );
      await fixture.itemsGate;
      // 隐藏导致客户端取消后不再交付旧响应，回来必须由新的合法读取完成初始化。
      if (route.request().failure()) return;
      await route.fulfill({
        status: fixture.itemsStatus,
        json:
          fixture.itemsStatus === 200
            ? { success: true, data: fixture.items[code] }
            : {
                success: false,
                error: { code: "UNEXPECTED", message: "模拟字典项读取失败" },
              },
      });
    },
  );
  for (const path of [typePath, itemPath]) {
    await page.route(
      (url) => url.pathname === path,
      async (route) => {
        expect(route.request().method()).toBe("POST");
        expect(route.request().headers().authorization).toBe(
          "Bearer " + session.token,
        );
        expect(route.request().headers()["content-type"]).toBe(
          "application/json",
        );
        const body = route.request().postDataJSON() as TypeBatch | ItemBatch;
        fixture.writes.push({ path, body: structuredClone(body) });
        await respondToSave(route, fixture, path, body);
      },
    );
  }
  await page.goto("/login");
  await page.getByLabel("用户名", { exact: true }).fill("tester");
  await page
    .getByLabel("密码", { exact: true })
    .fill("dictionary-browser-test-only");
  await page.getByRole("button", { name: "登录", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "退出登录", exact: true }),
  ).toBeVisible();
  await page.goto("/workspace?view=dictionary-management");
  await expect(
    pane(page).getByRole("heading", { name: "字典管理", exact: true }),
  ).toBeVisible();
  if (options.waitForItems !== false) {
    await pane(page).getByLabel("选择字典WebStyles", { exact: true }).click();
    await expect(
      pane(page).getByLabel("字典项1英文名称", { exact: true }),
    ).toHaveValue("Polished");
  }
  return fixture;
}

/** 按真实整批回执形状模拟服务器，未知结果绝不改为假成功或行级接纳。 */
async function respondToSave(
  route: Route,
  fixture: DictionaryFixture,
  path: string,
  request: TypeBatch | ItemBatch,
) {
  if (fixture.saveMode === "response-lost") {
    await route.abort("connectionreset");
    return;
  }
  if (
    fixture.saveMode === "business-unknown" ||
    fixture.saveMode === "rate-limited"
  ) {
    const blocked = fixture.saveMode === "business-unknown";
    await route.fulfill({
      status: blocked ? 422 : 429,
      json: {
        success: false,
        error: {
          code: blocked ? "BUSINESS_BLOCKED" : "RATE_LIMITED",
          message: blocked
            ? "模拟业务拒绝：不能证明未写入"
            : "模拟重复请求短路拒绝",
        },
      },
    });
    return;
  }
  const create = request.createItems ?? [];
  const update = request.updateItems ?? [];
  const remove = request.deleteItems ?? [];
  const results = [
    ...create.map((entry) => ({
      clientKey: entry.clientKey,
      rowIndex: entry.rowIndex,
      operation: "Create",
      uid: uid(fixture.nextUid++),
      success: true,
      oldVersion: null,
      newVersion: 1,
    })),
    ...update.map((entry) => ({
      clientKey: entry.clientKey,
      rowIndex: entry.rowIndex,
      operation: "Update",
      uid: entry.uid,
      success: true,
      oldVersion:
        entry.version === 0 && entry.data?.isBuiltIn === true
          ? 0
          : entry.version! + 1,
      newVersion: entry.version! + 1,
    })),
    ...remove.map((entry) => ({
      clientKey: entry.clientKey,
      rowIndex: entry.rowIndex,
      operation: "Delete",
      uid: entry.uid,
      success: true,
      oldVersion: entry.version,
      newVersion: entry.version,
    })),
  ];
  // 真实字典端点原子提交；截断回执时不修改快照，测试只验证前端不能乐观接纳。
  if (fixture.saveMode === "success") {
    const apply = <T extends { uid?: string; version?: number }>(
      current: T[],
      batch: TypeBatch | ItemBatch,
    ): T[] => {
      const deleted = new Set(remove.map((entry) => entry.uid));
      return [
        ...current
          .filter((entry) => !deleted.has(entry.uid))
          .map((entry) => {
            const changed = update.find(
              (candidate) => candidate.uid === entry.uid,
            );
            return changed
              ? ({
                  ...entry,
                  ...changed.data,
                  uid: entry.uid,
                  version: entry.version! + 1,
                } as T)
              : entry;
          }),
        ...(batch.createItems ?? []).map((entry) => {
          const receipt = results.find(
            (candidate) => candidate.clientKey === entry.clientKey,
          )!;
          return { ...entry.data, uid: receipt.uid, version: 1 } as T;
        }),
      ];
    };
    if (path === typePath) {
      fixture.types = apply(fixture.types, request);
      for (const entry of create) {
        const code = (entry.data as DictionaryType).dictCode!;
        fixture.items[code] = [];
      }
    } else {
      const code =
        ((create[0]?.data ?? update[0]?.data) as DictionaryItem | undefined)
          ?.dictCode ??
        Object.keys(fixture.items).find((key) =>
          fixture.items[key]!.some((entry) =>
            remove.some((candidate) => candidate.uid === entry.uid),
          ),
        );
      expect(code).toBeDefined();
      fixture.items[code!] = apply(fixture.items[code!]!, request);
    }
  }
  await route.fulfill({
    json: {
      success: true,
      data: {
        totalCount: results.length,
        items: fixture.saveMode === "incomplete" ? [] : results,
        successCount: results.length,
        failedCount: 0,
        allSucceeded: true,
        addSuccessCount: create.length,
        updateSuccessCount: update.length,
        deleteSuccessCount: remove.length,
      },
    },
  });
}

/** 按明确确认阶段定位，等待退出动画完成后再复核原页签及所有弹窗。 */
async function cancelDialog(page: Page, title: string) {
  const dialog = page.getByRole("dialog", { name: title, exact: true });
  await dialog.getByRole("button", { name: "取消", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("dialog")).toHaveCount(0);
}

/** 服务端回执已接纳后页签星号应消失，不用内部 reducer 状态充当证据。 */
async function expectClean(page: Page) {
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toBeEditable();
}

test.afterEach(async ({ page }) => {
  const fixture = observations.get(page);
  if (!fixture) return;
  expect(fixture.session.errors, "字典页面不能产生 React 或运行时异常").toEqual(
    [],
  );
  const business = fixture.session.apiCalls.filter(
    (call) => !authenticationCalls.includes(call),
  );
  const allowed = new Set([
    `GET ${typesPath}`,
    `POST ${typePath}`,
    `POST ${itemPath}`,
    ...Object.keys(fixture.items).map(
      (code) => `GET ${itemPrefix}${encodeURIComponent(code)}`,
    ),
  ]);
  expect(
    business.filter((call) => !allowed.has(call)),
    "任何未审核业务请求必须被测试拒绝",
  ).toEqual([]);
});

test("首次明细读取中隐藏只静默取消，回来自动完成同一页签加载", async ({
  page,
}) => {
  let releaseItems!: () => void;
  const itemsGate = new Promise<void>((resolve) => {
    releaseItems = resolve;
  });
  const fixture = await initialize(page, dictionaryPermissions, {
    itemsGate,
    waitForItems: false,
  });
  const itemRead = `GET ${itemPrefix}WebStyles`;
  await expect
    .poll(
      () => fixture.session.apiCalls.filter((call) => call === itemRead).length,
    )
    .toBeGreaterThan(0);
  await expect(pane(page)).toContainText("保存状态：loading");
  const tabId = (await pane(page).getAttribute("data-tab-id"))!;
  const tab = page.locator(`.ant-tabs-tab[data-node-key="${tabId}"]`);
  const originalReads = fixture.session.apiCalls.filter(
    (call) => call === itemRead,
  ).length;
  await page.getByRole("menuitem", { name: /首页$/ }).click();
  await expect(pane(page)).toHaveCount(0);
  await expect(page.getByRole("dialog")).toHaveCount(0);
  fixture.itemsGate = undefined;
  releaseItems();
  await tab.getByRole("tab").click();
  await expect(pane(page).getByLabel("字典编码", { exact: true })).toHaveValue(
    "WebStyles",
  );
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toHaveValue("Polished");
  await expect(pane(page)).toContainText("保存状态：idle");
  await expect(pane(page).getByRole("alert")).toHaveCount(0);
  await expect(page.getByText(/请求已取消|请求结果无法确认/)).toHaveCount(0);
  expect(
    fixture.session.apiCalls.filter((call) => call === itemRead),
  ).toHaveLength(originalReads + 1);
  expect(fixture.writes).toEqual([]);
});

test("字典标题状态同排，类型工具为同行图标且列表等高分两行", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1217, height: 906 });
  const fixture = await initialize(page);
  const title = await pane(page)
    .getByRole("heading", { name: "字典管理", exact: true })
    .boundingBox();
  const state = await pane(page)
    .getByText("保存状态：idle", { exact: true })
    .boundingBox();
  expect(title).not.toBeNull();
  expect(state).not.toBeNull();
  expect(
    Math.abs(title!.y + title!.height / 2 - state!.y - state!.height / 2),
  ).toBeLessThan(4);
  const refresh = pane(page).getByRole("button", {
    name: "刷新字典",
    exact: true,
  });
  const add = pane(page).getByRole("button", {
    name: "新增字典类型",
    exact: true,
  });
  await expect(refresh.getByRole("img")).toBeVisible();
  await expect(add.getByRole("img")).toBeVisible();
  expect((await refresh.textContent())!.trim()).toBe("");
  expect((await add.textContent())!.trim()).toBe("");
  const cardTitle = await pane(page)
    .getByText("字典类型", { exact: true })
    .boundingBox();
  const toolBoxes = await Promise.all([
    refresh.boundingBox(),
    add.boundingBox(),
  ]);
  for (const box of toolBoxes) {
    expect(box).not.toBeNull();
    expect(
      Math.abs(cardTitle!.y + cardTitle!.height / 2 - box!.y - box!.height / 2),
    ).toBeLessThan(4);
  }
  const heights: number[] = [];
  for (const [code, name] of [
    ["WebStyles", "样式"],
    ["WebOther", "其他"],
    ["Unit", "单位"],
  ]) {
    const button = pane(page).getByRole("button", {
      name: `选择字典${code}`,
      exact: true,
    });
    const box = await button.boundingBox();
    const nameBox = await button
      .getByText(name!, { exact: true })
      .boundingBox();
    const codeText = button.getByText(code!, { exact: true });
    const codeBox = await codeText.boundingBox();
    expect(box).not.toBeNull();
    heights.push(box!.height);
    expect(nameBox!.y + nameBox!.height).toBeLessThanOrEqual(codeBox!.y);
    await expect(codeText).toHaveCSS("font-size", "12px");
  }
  expect(Math.max(...heights) - Math.min(...heights)).toBeLessThan(1);
  expect(heights[0]).toBe(60);
  // 用实际几何关系验收紧凑间距，不把 CSS 类名或声明字符串当作用户可见效果。
  const tabs = await page.getByRole("tablist").boundingBox();
  expect(title!.y - tabs!.y - tabs!.height).toBeGreaterThanOrEqual(0);
  expect(title!.y - tabs!.y - tabs!.height).toBeLessThanOrEqual(20);
  expect(fixture.writes).toEqual([]);
  await page.screenshot({
    path: ".artifacts/字典界面调整.png",
    fullPage: true,
  });
});

test("字典分栏可拖动且左右独立滚动，充足空间无外层滚动，小窗口关键操作可达", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1217, height: 906 });
  const fixture = await initialize(page, dictionaryPermissions, {
    overflowData: true,
  });
  const view = pane(page);
  const typeCard = view.locator(".dictionary-type-card");
  const properties = view.locator(".dictionary-type-properties");
  const itemsCard = view.locator(".dictionary-items-card");
  const typeList = view.getByRole("region", {
    name: "字典类型列表",
    exact: true,
  });
  const tableBody = itemsCard.locator(".ant-table-body");
  await expect(typeList).toBeVisible();
  await expect(tableBody).toBeVisible();
  await expect
    .poll(() => tableBody.evaluate((element) => element.clientHeight))
    .toBeGreaterThan(40);
  const typeBox = (await typeCard.boundingBox())!;
  const propertiesBox = (await properties.boundingBox())!;
  const itemsBox = (await itemsCard.boundingBox())!;
  expect(Math.abs(typeBox.y - propertiesBox.y)).toBeLessThan(1.5);
  expect(
    Math.abs(typeBox.y + typeBox.height - itemsBox.y - itemsBox.height),
  ).toBeLessThan(2);
  expect(
    await typeList.evaluate(
      (element) => element.scrollHeight - element.clientHeight,
    ),
  ).toBeGreaterThan(100);
  expect(
    await tableBody.evaluate(
      (element) => element.scrollHeight - element.clientHeight,
    ),
  ).toBeGreaterThan(100);
  const tabDisplay = page.locator(".tab-display:visible");
  expect(
    await tabDisplay.evaluate(
      (element) => element.scrollHeight - element.clientHeight,
    ),
  ).toBeLessThanOrEqual(2);
  expect(
    await page.evaluate(() => {
      const root = document.scrollingElement!;
      return root.scrollHeight - root.clientHeight;
    }),
  ).toBeLessThanOrEqual(2);

  /** 观察工具栏及正文实际可视范围，分栏收窄不得把公共表格操作挤到卡片外。 */
  async function expectGridControlsInViewport(stage: string) {
    for (const name of ["向下填充", "表格显示设置"]) {
      await expect(
        itemsCard.getByRole("button", { name, exact: true }),
        `${stage}：${name}必须完整留在可视区`,
      ).toBeInViewport({ ratio: 1 });
    }
    const bodyBox = (await tableBody.boundingBox())!;
    const cardBodyBox = (await itemsCard
      .locator(":scope > .ant-card-body")
      .boundingBox())!;
    const geometry = JSON.stringify({ stage, bodyBox, cardBodyBox });
    expect(bodyBox.x, geometry).toBeGreaterThanOrEqual(cardBodyBox.x - 1);
    expect(bodyBox.x + bodyBox.width, geometry).toBeLessThanOrEqual(
      cardBodyBox.x + cardBodyBox.width + 1,
    );
  }
  await expectGridControlsInViewport("拖动前");

  // 使用安装的 Ant Splitter 原生 separator 实际拖动，不直接写宽度或组件内部状态。
  const separator = view
    .locator(".ant-splitter")
    .getByRole("separator", { name: "", exact: true });
  await expect(separator).toHaveAttribute("aria-orientation", "vertical");
  await view
    .getByLabel("字典项1英文名称", { exact: true })
    .fill("拖动仍保留输入");
  const dragBox = (await separator.boundingBox())!;
  await page.mouse.move(
    dragBox.x + dragBox.width / 2,
    dragBox.y + dragBox.height / 2,
  );
  await page.mouse.down();
  await page.mouse.move(
    dragBox.x + dragBox.width / 2 + 80,
    dragBox.y + dragBox.height / 2,
    { steps: 8 },
  );
  await page.mouse.up();
  await expect
    .poll(async () => (await typeCard.boundingBox())!.width)
    .toBeGreaterThan(typeBox.width + 50);
  const propertiesAfterDrag = (await properties.boundingBox())!;
  expect(propertiesAfterDrag.width).toBeLessThan(propertiesBox.width - 50);
  await expect(view.getByLabel("字典项1英文名称", { exact: true })).toHaveValue(
    "拖动仍保留输入",
  );
  await expect(view.getByLabel("字典编码", { exact: true })).toHaveValue(
    "WebStyles",
  );
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expectGridControlsInViewport("拖动后");

  // 鼠标滚轮只移动各自列表内容，卡片标题、属性与表格表头应保持在原位置。
  const typeHeaderY = (await typeCard
    .getByText("字典类型", { exact: true })
    .boundingBox())!.y;
  const propertyY = (await properties.boundingBox())!.y;
  await typeList.hover();
  await page.mouse.wheel(0, 10_000);
  await expect
    .poll(() => typeList.evaluate((element) => element.scrollTop))
    .toBeGreaterThan(0);
  await expect(
    typeList.getByLabel("选择字典WebExtra24", { exact: true }),
  ).toBeInViewport({ ratio: 1 });
  expect(
    Math.abs(
      (await typeCard.getByText("字典类型", { exact: true }).boundingBox())!.y -
        typeHeaderY,
    ),
  ).toBeLessThan(1.5);
  expect(
    Math.abs((await properties.boundingBox())!.y - propertyY),
  ).toBeLessThan(1.5);
  const tableHeader = itemsCard.getByRole("columnheader", {
    name: /^英文名称/,
  });
  const tableHeaderY = (await tableHeader.boundingBox())!.y;
  await tableBody.hover();
  await page.mouse.wheel(0, 10_000);
  await expect
    .poll(() => tableBody.evaluate((element) => element.scrollTop))
    .toBeGreaterThan(0);
  await expect(
    view.getByLabel("字典项40英文名称", { exact: true }),
  ).toBeInViewport({ ratio: 1 });
  expect(
    Math.abs((await tableHeader.boundingBox())!.y - tableHeaderY),
  ).toBeLessThan(1.5);
  expect(
    Math.abs((await properties.boundingBox())!.y - propertyY),
  ).toBeLessThan(1.5);
  expect(await tabDisplay.evaluate((element) => element.scrollTop)).toBe(0);
  await page.screenshot({
    path: ".artifacts/字典分栏与内部滚动.png",
    fullPage: true,
  });

  await page.setViewportSize({ width: 900, height: 640 });
  const save = view.getByRole("button", { name: "保存字典项", exact: true });
  await save.scrollIntoViewIfNeeded();
  await expect(save).toBeInViewport({ ratio: 1 });
  const firstInput = view.getByLabel("字典项1英文名称", { exact: true });
  await firstInput.scrollIntoViewIfNeeded();
  await expect(firstInput).toBeInViewport({ ratio: 1 });
  await expect(firstInput).toHaveValue("拖动仍保留输入");
  await expect(
    view.getByRole("button", { name: "导出恢复记录", exact: true }),
  ).toHaveCount(0);
  expect(fixture.writes).toEqual([]);
});

test("真实生成链提交字典项修改，保留隐藏字段并用回执版本继续编辑", async ({
  page,
}) => {
  const fixture = await initialize(page);
  await pane(page)
    .getByLabel("字典项1英文名称", { exact: true })
    .fill("Mirror");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expectClean(page);
  expect(fixture.writes).toHaveLength(1);
  const first = fixture.writes[0]!;
  expect(first.path).toBe(itemPath);
  expect(first.body.createItems ?? []).toEqual([]);
  expect(first.body.deleteItems ?? []).toEqual([]);
  expect(first.body.updateItems).toHaveLength(1);
  expect(first.body.updateItems![0]).toMatchObject({
    uid: uid(11),
    version: 7,
    clientKey: uid(11),
    data: {
      dictCode: "WebStyles",
      dataNameEn: "Mirror",
      dataNameZh: "亮面",
      cssClass: "retained-style",
      remark: "原项备注",
    },
  });
  await pane(page).getByLabel("字典项1中文名称", { exact: true }).fill("镜面");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expectClean(page);
  expect(fixture.writes[1]!.body.updateItems![0]).toMatchObject({
    uid: uid(11),
    version: 8,
    clientKey: uid(11),
    data: { dataNameEn: "Mirror", dataNameZh: "镜面" },
  });
});

test("新增类型接纳服务器身份，后续修改及删除使用真实 Uid 和 Version", async ({
  page,
}) => {
  const fixture = await initialize(page);
  await pane(page)
    .getByRole("button", { name: "新增字典类型", exact: true })
    .click();
  await pane(page).getByLabel("字典编码", { exact: true }).fill("WebNew");
  await pane(page).getByLabel("字典名称", { exact: true }).fill("新字典");
  await pane(page)
    .getByRole("button", { name: "保存类型", exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  const created = fixture.writes[0]!.body.createItems![0]!;
  expect(fixture.writes[0]!.path).toBe(typePath);
  expect(created.data).toMatchObject({
    dictCode: "WebNew",
    dictName: "新字典",
  });
  expect(created.clientKey).toBeTruthy();
  const serverType = fixture.types.find(
    (entry) => entry.dictCode === "WebNew",
  )!;
  expect(created.data!.uid).not.toBe(serverType.uid);
  await pane(page).getByLabel("字典名称", { exact: true }).fill("已改名称");
  await pane(page)
    .getByRole("button", { name: "保存类型", exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  expect(fixture.writes[1]!.body.updateItems![0]).toMatchObject({
    uid: serverType.uid,
    version: 1,
    data: { dictCode: "WebNew", dictName: "已改名称" },
  });
  await pane(page)
    .getByRole("button", { name: "删除类型", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "删除字典类型", exact: true })
    .getByRole("button", { name: "删除", exact: true })
    .click();
  await expect(
    pane(page).getByLabel("选择字典WebNew", { exact: true }),
  ).toHaveCount(0);
  expect(fixture.writes[2]!.body.deleteItems![0]).toMatchObject({
    uid: serverType.uid,
    version: 2,
  });
});

test("字典项增改删一次冻结，未删行及新增行完整接纳", async ({ page }) => {
  const fixture = await initialize(page);
  await pane(page)
    .getByLabel("字典项1中文名称", { exact: true })
    .fill("修改亮面");
  await pane(page)
    .getByRole("button", { name: "新增字典项", exact: true })
    .click();
  await pane(page).getByLabel("字典项3英文名称", { exact: true }).fill("Matte");
  await pane(page).getByLabel("字典项3中文名称", { exact: true }).fill("哑光");
  await pane(page)
    .locator("tr.ant-table-row")
    .nth(1)
    .getByRole("checkbox")
    .first()
    .check();
  await pane(page)
    .getByRole("button", { name: "删除选中项", exact: true })
    .click();
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  expect(fixture.writes).toHaveLength(1);
  const batch = fixture.writes[0]!.body;
  expect(batch.createItems).toHaveLength(1);
  expect(batch.updateItems).toHaveLength(1);
  expect(batch.deleteItems).toHaveLength(1);
  expect(batch.createItems![0]).toMatchObject({
    data: { dictCode: "WebStyles", dataNameEn: "Matte", dataNameZh: "哑光" },
  });
  expect(batch.updateItems![0]).toMatchObject({ uid: uid(11), version: 7 });
  expect(batch.deleteItems![0]).toMatchObject({ uid: uid(12), version: 9 });
  await expect(
    pane(page).getByLabel("字典项2英文名称", { exact: true }),
  ).toHaveValue("Matte");
  await pane(page)
    .getByLabel("字典项2备注", { exact: true })
    .fill("新行已拥有服务器身份");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  expect(fixture.writes[1]!.body.updateItems![0]).toMatchObject({
    uid: uid(100),
    version: 1,
    clientKey: batch.createItems![0]!.clientKey,
  });
});

test("首次无效排序未失焦与隐藏页关闭都受保护，校验失败不派发", async ({
  page,
}) => {
  const fixture = await initialize(page);
  const original = pane(page);
  const tabId = (await original.getAttribute("data-tab-id"))!;
  const tab = page.locator(`.ant-tabs-tab[data-node-key="${tabId}"]`);
  await original.getByLabel("字典项1排序", { exact: true }).fill("-");
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(page.getByRole("dialog")).toContainText("有未保存输入");
  await cancelDialog(page, "字典管理有未保存输入");
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await cancelDialog(page, "字典管理有未保存输入");
  await page.getByRole("menuitem", { name: /首页$/ }).click();
  await expect(original).toHaveCount(0);
  await tab.locator(".ant-tabs-tab-remove").click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "保存并关闭", exact: true })
    .click();
  await expect(
    pane(page).getByLabel("字典项1排序", { exact: true }),
  ).toHaveValue("-");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(fixture.writes).toEqual([]);
  await pane(page).getByLabel("字典项1排序", { exact: true }).press("Escape");
  await expect(
    pane(page).getByLabel("字典项1排序", { exact: true }),
  ).toHaveValue("1");
  await tab.locator(".ant-tabs-tab-remove").click();
  await expect(tab).toHaveCount(0);
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("切换类型及刷新统一保护草稿，重载失败保留原会话", async ({ page }) => {
  const fixture = await initialize(page);
  const originalReads = fixture.session.apiCalls.filter(
    (call) => call === `GET ${typesPath}`,
  ).length;
  await pane(page)
    .getByLabel("字典项1英文名称", { exact: true })
    .fill("保留的草稿");
  await pane(page).getByLabel("选择字典WebOther", { exact: true }).click();
  await expect(
    page.getByRole("dialog", { name: "处理未保存字典修改", exact: true }),
  ).toBeVisible();
  await cancelDialog(page, "处理未保存字典修改");
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toHaveValue("保留的草稿");
  await pane(page)
    .getByRole("button", { name: "刷新字典", exact: true })
    .click();
  await cancelDialog(page, "处理未保存字典修改");
  expect(
    fixture.session.apiCalls.filter((call) => call === `GET ${typesPath}`),
  ).toHaveLength(originalReads);
  await pane(page)
    .getByRole("button", { name: "放弃字典项变更", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "放弃字典项变更", exact: true })
    .getByRole("button", { name: "放弃", exact: true })
    .click();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toHaveValue("Polished");
  fixture.typesStatus = 500;
  await pane(page)
    .getByRole("button", { name: "刷新字典", exact: true })
    .click();
  await expect(pane(page)).toContainText("模拟字典读取失败");
  await expect(pane(page).getByLabel("字典编码", { exact: true })).toHaveValue(
    "WebStyles",
  );
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toHaveValue("Polished");
  expect(fixture.writes).toEqual([]);
});

test("查看权限保持只读，不派发任何字典写入", async ({ page }) => {
  const fixture = await initialize(page, ["System.Dictionary.View"]);
  await expect(
    pane(page).getByLabel("字典名称", { exact: true }),
  ).toBeDisabled();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).not.toBeEditable();
  for (const name of [
    "新增字典类型",
    "保存类型",
    "删除类型",
    "新增字典项",
    "保存字典项",
    "删除选中项",
  ])
    await expect(
      pane(page).getByRole("button", { name, exact: true }),
    ).toBeDisabled();
  expect(fixture.writes).toEqual([]);
});

test("系统类型禁止改删，系统项保留独立的可编辑及不可删除语义", async ({
  page,
}) => {
  const fixture = await initialize(page);
  await pane(page).getByLabel("选择字典Unit", { exact: true }).click();
  await expect(pane(page)).toContainText("系统类型只读");
  await expect(
    pane(page).getByLabel("字典名称", { exact: true }),
  ).toBeDisabled();
  await expect(
    pane(page).getByRole("button", { name: "删除类型", exact: true }),
  ).toBeDisabled();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toBeEditable();
  await pane(page)
    .locator("tr.ant-table-row")
    .first()
    .getByRole("checkbox")
    .first()
    .check();
  await expect(
    pane(page).getByRole("button", { name: "删除选中项", exact: true }),
  ).toBeDisabled();
  expect(fixture.writes).toEqual([]);
});

test("零版本内置字典加载干净，固定字段只读且首次保存保持身份", async ({
  page,
}) => {
  const fixture = await initialize(page);
  // 模拟服务端目录的缺失记录回退，类型及明细无需先落库即可参与现有维护页。
  fixture.types.push({
    uid: uid(4),
    version: 0,
    dictCode: "FlowTestFunction",
    dictName: "流量测试功能",
    sortOrder: 4,
    isBuiltIn: true,
    isSystem: true,
  });
  fixture.items.FlowTestFunction = [
    {
      uid: uid(14),
      version: 0,
      dictCode: "FlowTestFunction",
      dataNameEn: "Default",
      dataNameZh: "默认",
      sortOrder: 1,
      isEnabled: true,
      isBuiltIn: true,
      isSystem: true,
    },
  ];
  await pane(page)
    .getByRole("button", { name: "刷新字典", exact: true })
    .click();
  await pane(page)
    .getByLabel("选择字典FlowTestFunction", { exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  await expect(
    pane(page).getByLabel("字典名称", { exact: true }),
  ).toBeDisabled();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).not.toBeEditable();
  await expect(
    pane(page).getByLabel("字典项1启用", { exact: true }),
  ).toBeDisabled();
  await expect(
    pane(page).getByLabel("字典项1中文名称", { exact: true }),
  ).toBeEditable();
  await expect(
    pane(page).getByRole("button", { name: "保存字典项", exact: true }),
  ).toBeDisabled();
  await expect(
    pane(page).getByRole("button", { name: "新增字典项", exact: true }),
  ).toBeEnabled();
  expect(fixture.writes).toEqual([]);
  await pane(page)
    .locator("tr.ant-table-row")
    .first()
    .getByRole("checkbox")
    .first()
    .check();
  await expect(
    pane(page).getByRole("button", { name: "删除选中项", exact: true }),
  ).toBeDisabled();
  await pane(page)
    .getByLabel("字典项1中文名称", { exact: true })
    .fill("默认功能");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  expect(fixture.writes).toHaveLength(1);
  expect(fixture.writes[0]!.body.createItems).toEqual([]);
  expect(fixture.writes[0]!.body.updateItems![0]).toMatchObject({
    uid: uid(14),
    version: 0,
    data: {
      isBuiltIn: true,
      dataNameEn: "Default",
      dataNameZh: "默认功能",
      isEnabled: true,
    },
  });
  // 首次回执建立版本1基线；再次编辑仍使用同一个稳定身份和普通更新协议。
  await pane(page)
    .getByLabel("字典项1中文名称", { exact: true })
    .fill("默认功能二");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expect(
    page.getByRole("tab", { name: "字典管理", exact: true }),
  ).toBeVisible();
  expect(fixture.writes[1]!.body.updateItems![0]).toMatchObject({
    uid: uid(14),
    version: 1,
  });
});

test("已验证重复请求短路解除锁并保留草稿，用户明确再次保存才派发", async ({
  page,
}) => {
  const fixture = await initialize(page);
  fixture.saveMode = "rate-limited";
  await pane(page)
    .getByLabel("字典项1英文名称", { exact: true })
    .fill("短路后草稿");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expect(
    page.getByText("模拟重复请求短路拒绝", { exact: true }),
  ).toBeVisible();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toBeEditable();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).toHaveValue("短路后草稿");
  expect(fixture.writes).toHaveLength(1);
  fixture.saveMode = "success";
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expectClean(page);
  expect(fixture.writes).toHaveLength(2);
  expect(fixture.writes[1]!.body.updateItems![0]).toMatchObject({
    uid: uid(11),
    version: 7,
  });
});

for (const mode of [
  "business-unknown",
  "response-lost",
  "incomplete",
] as const) {
  test(`${mode} 不接纳或解锁，批量关闭不能绕过未知恢复`, async ({ page }) => {
    const fixture = await initialize(page);
    fixture.saveMode = mode;
    await pane(page)
      .getByLabel("字典项1英文名称", { exact: true })
      .fill("冻结原输入");
    await pane(page)
      .getByRole("button", { name: "保存字典项", exact: true })
      .click();
    await expect(
      pane(page).getByRole("button", { name: "导出恢复记录", exact: true }),
    ).toBeVisible();
    await expect(
      pane(page).getByLabel("字典项1英文名称", { exact: true }),
    ).not.toBeEditable();
    await expect(
      pane(page).getByLabel("字典项1英文名称", { exact: true }),
    ).toHaveValue("冻结原输入");
    await expect(
      pane(page).getByRole("button", { name: "保存字典项", exact: true }),
    ).toBeDisabled();
    await expect(
      pane(page).getByRole("button", { name: "放弃字典项变更", exact: true }),
    ).toBeDisabled();
    await page.getByRole("button", { name: "关闭全部", exact: true }).click();
    await expect(pane(page)).toBeVisible();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(fixture.writes).toHaveLength(1);
  });
}

test("未知恢复记录与冻结请求对应，取消任一确认仍保护，两次确认才人工终止", async ({
  page,
}) => {
  const fixture = await initialize(page);
  fixture.saveMode = "business-unknown";
  await pane(page)
    .getByLabel("字典项1英文名称", { exact: true })
    .fill("需要核实的冻结内容");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expect(
    pane(page).getByRole("button", { name: "导出恢复记录", exact: true }),
  ).toBeVisible();
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(pane(page)).toBeVisible();
  const downloading = page.waitForEvent("download");
  await pane(page)
    .getByRole("button", { name: "导出恢复记录", exact: true })
    .click();
  const download = await downloading;
  const record = JSON.parse(
    await readFile((await download.path())!, "utf8"),
  ) as {
    recordId: string;
    interfaceOperation: string;
    frozenSubmission: unknown;
  };
  expect(download.suggestedFilename()).toBe(
    `SwiftExport-恢复记录-${record.recordId}.json`,
  );
  expect(record.interfaceOperation).toContain(itemPath);
  expect(record.frozenSubmission).toMatchObject({
    createItems: [],
    deleteItems: [],
    updateItems: [
      {
        uid: uid(11),
        version: 7,
        clientKey: uid(11),
        data: {
          dictCode: "WebStyles",
          dataNameEn: "需要核实的冻结内容",
          dataNameZh: "亮面",
          cssClass: "retained-style",
          remark: "原项备注",
        },
      },
    ],
  });
  expect(JSON.stringify(record)).not.toMatch(
    /Authorization|access_token|password|CommandId|csrf-test-only|test-signature-only/,
  );
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", { name: "确认记录已妥善保留", exact: true }),
  ).toContainText(record.recordId);
  await cancelDialog(page, "确认记录已妥善保留");
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(pane(page)).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "确认记录已妥善保留", exact: true })
    .getByRole("button", { name: "已妥善保留", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", { name: "确认记录已妥善保留", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("dialog", { name: "确认终止本地恢复", exact: true }),
  ).toContainText("服务器结果仍未确认");
  await cancelDialog(page, "确认终止本地恢复");
  await expect(pane(page)).toBeVisible();
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await page
    .getByRole("dialog", { name: "确认记录已妥善保留", exact: true })
    .getByRole("button", { name: "已妥善保留", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", { name: "确认记录已妥善保留", exact: true }),
  ).toHaveCount(0);
  await page
    .getByRole("dialog", { name: "确认终止本地恢复", exact: true })
    .getByRole("button", { name: "终止并关闭", exact: true })
    .click();
  await expect(
    page.locator('[data-testid="dictionary-management"]'),
  ).toHaveCount(0);
  expect(fixture.writes).toHaveLength(1);
});

test("恢复文件生成失败不构成保全，也不能人工终止页签", async ({ page }) => {
  const fixture = await initialize(page);
  fixture.saveMode = "business-unknown";
  await pane(page)
    .getByLabel("字典项1英文名称", { exact: true })
    .fill("导出失败仍保留");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  await expect(
    pane(page).getByRole("button", { name: "导出恢复记录", exact: true }),
  ).toBeVisible();
  // 模拟浏览器生成临时文件 URL 失败，不修改业务会话或调用 feature 内部方法。
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
  await expect(pane(page)).toBeVisible();
  await expect(
    pane(page).getByRole("button", { name: "保存字典项", exact: true }),
  ).toBeDisabled();
  expect(fixture.writes).toHaveLength(1);
});

test("未知冻结后撤销写权限，导出及已导出记录的人工终止均不能绕过权限", async ({
  page,
}) => {
  const fixture = await initialize(page);
  fixture.saveMode = "business-unknown";
  await pane(page)
    .getByLabel("字典项1英文名称", { exact: true })
    .fill("撤权后仍需合法核实");
  await pane(page)
    .getByRole("button", { name: "保存字典项", exact: true })
    .click();
  const downloading = page.waitForEvent("download");
  await pane(page)
    .getByRole("button", { name: "导出恢复记录", exact: true })
    .click();
  await downloading;
  fixture.session.identity.permissions = ["System.Dictionary.View"];
  await page.getByRole("button", { name: "刷新权限", exact: true }).click();
  await expect(
    pane(page).getByLabel("字典项1英文名称", { exact: true }),
  ).not.toBeEditable();
  const downloads: string[] = [];
  page.on("download", (download) =>
    downloads.push(download.suggestedFilename()),
  );
  await pane(page)
    .getByRole("button", { name: "导出恢复记录", exact: true })
    .click();
  await expect(page.getByText(/当前权限不允许保全/)).toBeVisible();
  expect(downloads).toEqual([]);
  await pane(page)
    .getByRole("button", { name: "终止本地恢复", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.getByRole("button", { name: "关闭全部", exact: true }).click();
  await expect(pane(page)).toBeVisible();
  expect(fixture.writes).toHaveLength(1);
});
