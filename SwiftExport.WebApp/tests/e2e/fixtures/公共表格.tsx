import { Activity, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  App,
  Button,
  Checkbox,
  ConfigProvider,
  Select,
  Space,
  Typography,
} from "antd";
import zhCN from "antd/locale/zh_CN";
import { PreferenceDataGrid } from "../../../app/shared/ui/偏好表格";
import {
  UserPreferencesProvider,
  useUserPreferences,
} from "../../../app/shared/preferences/用户偏好上下文";
import type {
  SwiftDataGridColumn,
  SwiftDataGridFillDownIntent,
  SwiftDataGridRowKey,
} from "../../../app/shared/ui/swift-data-grid";
import "antd/dist/reset.css";

/** 夹具只用本地行展示受控编辑，顺序特意不同于行 key 的排序，不代表业务 DTO。 */
interface FixtureRow {
  key: string;
  name: string;
  quantity: string;
  immutable: string;
  writable: boolean;
}
type EditableField = "name" | "quantity" | "immutable";
const longChoice =
  "Exposed Shower Columns & Mixers — very long selected label that must never expand the fixed column";
const visibleRows: readonly FixtureRow[] = [
  {
    key: "b",
    name: "可见首行",
    quantity: "7",
    immutable: "锁定B",
    writable: true,
  },
  {
    key: "a",
    name: "只读目标",
    quantity: "2",
    immutable: "锁定A",
    writable: false,
  },
  {
    key: "c",
    name: "可写目标C",
    quantity: "3",
    immutable: "锁定C",
    writable: true,
  },
  {
    key: "d",
    name: "可写目标D",
    quantity: "4",
    immutable: "锁定D",
    writable: true,
  },
];

/** 使用真实公共 Grid，夹具自己接纳文本/取消/填充/分页意图，不登记为业务页面。 */
function ControlledGridHarness() {
  const [selected, setSelected] = useState<SwiftDataGridRowKey[]>([]);
  const [raw, setRaw] = useState<Record<string, string>>({});
  const [readOnly, setReadOnly] = useState(false);
  const [hidden, setHidden] = useState(false);
  const [narrow, setNarrow] = useState(false);
  const preferences = useUserPreferences();
  const [pageIndex, setPageIndex] = useState(3);
  const [pageRequest, setPageRequest] = useState<number | null>(null);
  const [fillHistory, setFillHistory] = useState<
    { columns: { columnKey: string; source: string; targets: string[] }[] }[]
  >([]);
  const [cancelled, setCancelled] = useState("");
  const [shortcut, setShortcut] = useState<{
    key: string;
    target: string;
    prevented: boolean;
  } | null>(null);
  useEffect(() => {
    /** 在 Grid 冒泡处理后记录 defaultPrevented，不实际打开 Chrome 收藏界面。 */
    const observeShortcut = (event: KeyboardEvent) => {
      if (!event.ctrlKey || event.key.toLowerCase() !== "d") return;
      setShortcut({
        key: event.key,
        target: event.target instanceof HTMLElement ? event.target.tagName : "",
        prevented: event.defaultPrevented,
      });
    };
    document.addEventListener("keydown", observeShortcut);
    return () => document.removeEventListener("keydown", observeShortcut);
  }, []);

  /** 短期原始文本只属于夹具调用方；公共 Grid 不储存这份值。 */
  function getText(row: FixtureRow, field: EditableField): string {
    return raw[`${row.key}:${field}`] ?? row[field];
  }

  /** 夹具显式接纳映射字段；列 key 只标识交互，Grid 不读取或复制行属性。 */
  function fillDown(intent: SwiftDataGridFillDownIntent<FixtureRow>) {
    setRaw((current) => {
      const next = { ...current };
      for (const column of intent.columns) {
        for (const field of column.fields) {
          if (field !== "name" && field !== "quantity" && field !== "immutable")
            continue;
          const source = getText(column.source, field);
          for (const row of column.targets)
            next[`${row.key}:${field}`] = source;
        }
      }
      return next;
    });
    setFillHistory((current) => [
      ...current,
      {
        columns: intent.columns.map((column) => ({
          columnKey: column.columnKey,
          source: column.source.key,
          targets: column.targets.map((row) => row.key),
        })),
      },
    ]);
  }

  const columns: SwiftDataGridColumn<FixtureRow>[] = [
    ...(["name", "quantity", "immutable"] as const).map((field) => ({
      key: field,
      title: { name: "名称", quantity: "数量", immutable: "只读列" }[field],
      width: field === "name" ? 180 : 130,
      readOnly: field === "immutable",
      editor: {
        getText: (row: FixtureRow) => getText(row, field),
        getLabel: (row: FixtureRow) => `${row.key}-${field}`,
        inputMode:
          field === "quantity" ? ("numeric" as const) : ("text" as const),
        getError: (row: FixtureRow) => {
          const text = getText(row, field);
          if (field === "name" && !text.trim()) return "名称不能为空。";
          if (field === "quantity" && !/^\d+$/.test(text))
            return "数量尚未完成。";
          return undefined;
        },
        onChange: (row: FixtureRow, text: string) =>
          setRaw((current) => ({ ...current, [`${row.key}:${field}`]: text })),
        onCancel: (row: FixtureRow) => {
          setRaw((current) => {
            const next = { ...current };
            delete next[`${row.key}:${field}`];
            return next;
          });
          setCancelled(`${row.key}:${field}`);
        },
      },
    })),
    {
      key: "description",
      title: "自定义展示",
      width: 180,
      render: (row) => `自定义-${row.key}`,
    },
    {
      key: "choice",
      title: "长候选",
      width: 160,
      editor: {
        kind: "custom",
        getLabel: (row) => `${row.key}-choice`,
        render: ({ inputAttributes, readOnly: disabled }) => (
          <Select
            {...inputAttributes}
            style={{ width: "100%", minWidth: 0 }}
            value="long-choice"
            disabled={disabled}
            options={[{ value: "long-choice", label: longChoice }]}
          />
        ),
      },
    },
  ];
  return (
    <main style={{ padding: 24 }}>
      <Space orientation="vertical" style={{ width: "100%" }}>
        <Checkbox
          checked={readOnly}
          onChange={(event) => setReadOnly(event.target.checked)}
        >
          表格只读
        </Checkbox>
        <Space>
          <Button onClick={() => setSelected([])}>清除选择</Button>
          <Button onClick={() => setHidden((value) => !value)}>
            {hidden ? "显示表格" : "隐藏表格"}
          </Button>
          <Button onClick={() => setNarrow((value) => !value)}>
            {narrow ? "恢复宽容器" : "使用窄容器"}
          </Button>
          <Button onClick={() => setSelected(["d", "off-page", "a", "c", "b"])}>
            使用乱序及跨页选择
          </Button>
          <Button
            disabled={pageRequest === null}
            onClick={() => {
              if (pageRequest !== null) setPageIndex(pageRequest);
              setPageRequest(null);
            }}
          >
            接受分页请求
          </Button>
        </Space>
        <Typography.Text data-testid="selection-value">
          {JSON.stringify(selected)}
        </Typography.Text>
        <Typography.Text data-testid="fill-history">
          {JSON.stringify(fillHistory)}
        </Typography.Text>
        <Typography.Text data-testid="cancel-value">
          {cancelled}
        </Typography.Text>
        <Typography.Text data-testid="shortcut-value">
          {JSON.stringify(shortcut)}
        </Typography.Text>
        <Typography.Text data-testid="page-request">
          {pageRequest ?? "无"}
        </Typography.Text>
        <Typography.Text data-testid="storage-status">
          {preferences.storageStatus}
        </Typography.Text>
        <div
          data-testid="grid-container"
          style={{ width: narrow ? 520 : 1100, maxWidth: "100%" }}
        >
          <Activity mode={hidden ? "hidden" : "visible"}>
            <PreferenceDataGrid<FixtureRow>
              preferenceId="controlled-grid"
              rows={visibleRows}
              getRowKey={(row) => row.key}
              columns={columns}
              readOnly={readOnly}
              canEdit={(row) => row.writable}
              selectedKeys={selected}
              onSelectionChange={setSelected}
              onFillDown={fillDown}
              showRowNumber
              pagination={{
                pageIndex,
                pageSize: 10,
                total: 40,
                // 这里只记录意图；父级明确接纳之前，表格不能自行换页或重编行身份。
                onChange: (nextPage) => setPageRequest(nextPage),
              }}
            />
          </Activity>
        </div>
      </Space>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(
  // 公共 UI 夹具仅提供 Ant 上下文；应用启动恢复由独立会话/工作区场景验收。
  <ConfigProvider locale={zhCN}>
    <App>
      <UserPreferencesProvider userId="grid-fixture-user">
        <ControlledGridHarness />
      </UserPreferencesProvider>
    </App>
  </ConfigProvider>,
);
