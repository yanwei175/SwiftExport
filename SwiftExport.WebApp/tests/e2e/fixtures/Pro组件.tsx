import { Activity, useCallback, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  App,
  Button,
  ConfigProvider,
  Form,
  Input,
  Space,
  Typography,
} from "antd";
import zhCN from "antd/locale/zh_CN";
import { ProForm } from "@ant-design/pro-components/form";
import { EditableProTable } from "@ant-design/pro-components/table/editable";
import { DragSortTable } from "@ant-design/pro-components/table/drag-sort";
import type { ProColumns } from "@ant-design/pro-components/table";
import type { EditableProTableProps } from "@ant-design/pro-components/table/editable";
import { ProDescriptions } from "@ant-design/pro-components/descriptions";
import { ProList } from "@ant-design/pro-components/list";
import { LookupSelect } from "../../../app/shared/ui/候选选择";
import { parseIntegerText } from "../../../app/shared/format/数值转换";
import "antd/dist/reset.css";

/** 夹具将文本作为 Form 当前值，Uid 只作稳定行身份；不连接业务 DTO 或服务器。 */
interface ProFixtureLine {
  uid: string;
  name: string;
  quantity: string;
}
/** 同一外层 Form 拥有头、行和多选当前值；不存在同步的编辑集合或行 useState。 */
interface ProFixtureValues {
  name: string;
  lines: ProFixtureLine[];
  categories: string[];
}
const initialValues: ProFixtureValues = {
  name: "Pro验收单据",
  lines: [
    { uid: "a", name: "Pro明细A", quantity: "2" },
    { uid: "b", name: "Pro明细B", quantity: "3" },
  ],
  categories: [],
};
const categoryOptions = [
  { value: "a", label: "分类A" },
  { value: "b", label: "分类B" },
  { value: "c", label: "分类C" },
];
const initialOrder = [
  { uid: "a", name: "排序A" },
  { uid: "b", name: "排序B" },
  { uid: "c", name: "排序C" },
];

/** 只将有效数字投影成业务值；无效 raw 继续留在原 Form 并独立产生输入保护。 */
function projectValues(values: ProFixtureValues, baseline: ProFixtureValues) {
  return {
    ...values,
    lines: values.lines.map((line, index) => {
      const number = parseIntegerText(line.quantity);
      return {
        ...line,
        quantity:
          number.valid && number.value !== null && number.value > 0
            ? number.value
            : Number(baseline.lines[index]?.quantity),
      };
    }),
  };
}

/** 独立浏览器夹具实际渲染 Pro 组件；仅模拟一次校验与本地接纳，不登记成业务 feature。 */
function ProComponentsHarness() {
  const [form] = Form.useForm<ProFixtureValues>();
  const baselineRef = useRef(structuredClone(initialValues));
  const [, revise] = useState(0);
  const [visible, setVisible] = useState(true);
  const [phase, setPhase] = useState<"idle" | "preparing">("idle");
  const phaseRef = useRef(phase);
  phaseRef.current = phase;
  const [saveCount, setSaveCount] = useState(0);
  const [notice, setNotice] = useState("");
  const [search, setSearch] = useState("");
  const [order, setOrder] = useState(initialOrder);
  const [dragCount, setDragCount] = useState(0);

  /** Form 变更只重算摘要，不复制当前字段；展示隐藏时实例和基线仍属于该控制器。 */
  const publish = useCallback(() => {
    revise((value) => value + 1);
  }, []);
  /** 未首次连接时使用只读初始化投影，连接后始终从同一 Form 读取。 */
  function readValues(): ProFixtureValues {
    const fields = form.getFieldsValue(true);
    return fields.lines ? fields : initialValues;
  }
  const values = readValues();
  const summary = {
    dirty:
      JSON.stringify(projectValues(values, baselineRef.current)) !==
      JSON.stringify(projectValues(baselineRef.current, baselineRef.current)),
    hasUncommittedInput: values.lines.some((line) => {
      const parsed = parseIntegerText(line.quantity);
      return !parsed.valid || parsed.value === null || parsed.value <= 0;
    }),
  };

  /** Escape 只恢复目标字段的原基线；其他行、头及多选草稿不受影响。 */
  const cancelCell = useCallback(
    (index: number, field: "name" | "quantity") => {
      if (phaseRef.current !== "idle") return;
      const baseline = baselineRef.current.lines[index];
      if (!baseline) return;
      form.setFields([
        {
          name: ["lines", index, field],
          value: baseline[field],
          errors: [],
        },
      ]);
      publish();
    },
    [form, publish],
  );
  /** 统一校验通过才增加本地提交计数并更新基线，失败不提交、不清除原始输入。 */
  async function save() {
    if (phase !== "idle" || !visible) return;
    setPhase("preparing");
    try {
      await form.validateFields();
      const current = readValues();
      if (
        current.lines.some((line) => {
          const parsed = parseIntegerText(line.quantity);
          return !parsed.valid || parsed.value === null || parsed.value <= 0;
        })
      )
        throw new Error("数量校验失败");
      const frozen = structuredClone(current);
      baselineRef.current = frozen;
      setSaveCount((count) => count + 1);
      setNotice("完整本地校验通过，没有业务 HTTP 或数据库写入。");
    } catch {
      setNotice("Pro 头行校验失败，原始输入保留，未发送保存请求。");
    } finally {
      setPhase("idle");
      publish();
    }
  }
  /** 明确放弃时只重置当前夹具的 Form 基线；不会重建活动行身份。 */
  function restore() {
    if (phase !== "idle") return;
    form.setFieldsValue(structuredClone(baselineRef.current));
    form.setFields([
      { name: "name", errors: [] },
      { name: "categories", errors: [] },
    ]);
    for (let index = 0; index < baselineRef.current.lines.length; index += 1)
      form.setFields([
        { name: ["lines", index, "name"], errors: [] },
        { name: ["lines", index, "quantity"], errors: [] },
      ]);
    setNotice("");
    publish();
  }
  // 列及编辑器函数保持身份稳定，摘要重绘不得使正输入的单元格重新挂载并失焦。
  const columns = useMemo<ProColumns<ProFixtureLine>[]>(
    () => [
      {
        title: "名称",
        dataIndex: "name",
        width: 230,
        // 默认错误 Popover 会按校验状态改变输入父节点；内联错误保持原始编辑 DOM 与焦点。
        errorType: "default",
        formItemProps: {
          rules: [
            { required: true, whitespace: true, message: "名称不能为空。" },
          ],
        },
        formItemRender: (_schema, { record }) => {
          const index = initialValues.lines.findIndex(
            (line) => line.uid === record?.uid,
          );
          return (
            <Input
              aria-label={`Pro行${index + 1}名称`}
              onKeyDown={(event) => {
                if (event.key === "Escape") {
                  event.preventDefault();
                  cancelCell(index, "name");
                }
              }}
            />
          );
        },
      },
      {
        title: "数量",
        dataIndex: "quantity",
        width: 160,
        errorType: "default",
        // 用 Input 保留原始数字文本；InputNumber 不作为未完成数字输入的权威。
        formItemProps: {
          rules: [
            {
              validator: async (_rule, raw: string) => {
                const parsed = parseIntegerText(raw ?? "");
                if (!parsed.valid || parsed.value === null || parsed.value <= 0)
                  throw new Error("数量必须是完整正整数。");
              },
            },
          ],
        },
        formItemRender: (_schema, { record }) => {
          const index = initialValues.lines.findIndex(
            (line) => line.uid === record?.uid,
          );
          return (
            <Input
              aria-label={`Pro行${index + 1}数量`}
              inputMode="numeric"
              onKeyDown={(event) => {
                if (event.key === "Escape") {
                  event.preventDefault();
                  cancelCell(index, "quantity");
                }
              }}
            />
          );
        },
      },
      { title: "稳定身份", dataIndex: "uid", editable: false, width: 100 },
    ],
    [cancelCell],
  );
  const editable = useMemo<
    EditableProTableProps<ProFixtureLine, Record<string, unknown>>["editable"]
  >(
    () => ({
      type: "multiple",
      editableKeys: initialValues.lines.map((line) => line.uid),
      form,
      actionRender: () => [],
    }),
    [form],
  );

  return (
    <main style={{ padding: 24 }}>
      <Space orientation="vertical" style={{ width: "100%" }}>
        <Typography.Text data-testid="pro-owner">
          当前值唯一所有者：同一外层 Ant Form。EditableProTable 使用 name
          路径，基线独立用于比较。
        </Typography.Text>
        <Button onClick={() => setVisible((current) => !current)}>
          {visible ? "隐藏 Pro 展示" : "显示 Pro 展示"}
        </Button>
        <Typography.Text data-testid="pro-summary">
          {JSON.stringify(summary)}
        </Typography.Text>
        <Typography.Text data-testid="pro-current">
          {JSON.stringify(values)}
        </Typography.Text>
        <Typography.Text data-testid="pro-save-count">
          {saveCount}
        </Typography.Text>
        <Typography.Text data-testid="pro-notice">{notice}</Typography.Text>
        <Activity mode={visible ? "visible" : "hidden"}>
          <ProForm<ProFixtureValues>
            name="pro-compatibility"
            form={form}
            initialValues={initialValues}
            preserve
            disabled={phase !== "idle"}
            onValuesChange={publish}
            submitter={false}
            omitNil={false}
            dateFormatter={false}
            syncToUrl={false}
            syncToInitialValues={false}
            autoFocusFirstInput={false}
            isKeyPressSubmit={false}
          >
            <Form.Item
              label="Pro单据名称"
              name="name"
              rules={[
                {
                  required: true,
                  whitespace: true,
                  message: "单据名称不能为空。",
                },
              ]}
            >
              <Input aria-label="Pro单据名称" />
            </Form.Item>
            <Form.Item label="Pro多选分类" name="categories">
              <LookupSelect
                label="Pro多选分类"
                selectionMode="multiple"
                value={[]}
                options={categoryOptions}
                searchValue={search}
                onSearchChange={setSearch}
                readOnly={phase !== "idle"}
                // Form.Item 注入真实 value/onChange；此默认仅满足受控组件的必填接口。
                onChange={() => undefined}
              />
            </Form.Item>
            <Button
              disabled={phase !== "idle"}
              onClick={() => {
                form.setFieldValue("categories", []);
                publish();
              }}
            >
              清空 Pro 多选分类
            </Button>
            <EditableProTable<ProFixtureLine>
              name="lines"
              rowKey="uid"
              columns={columns}
              recordCreatorProps={false}
              pagination={false}
              search={false}
              options={false}
              revalidateOnFocus={false}
              dateFormatter={false}
              editable={editable}
              columnsState={{ value: {}, onChange: () => undefined }}
            />
            <Space>
              <Button disabled={phase !== "idle"} onClick={() => void save()}>
                统一保存 Pro
              </Button>
              <Button disabled={phase !== "idle"} onClick={restore}>
                恢复 Pro 基线
              </Button>
            </Space>
          </ProForm>
        </Activity>
        <Typography.Text data-testid="pro-drag-order">
          {JSON.stringify(order.map((row) => row.uid))}
        </Typography.Text>
        <Typography.Text data-testid="pro-drag-count">
          {dragCount}
        </Typography.Text>
        <DragSortTable<(typeof initialOrder)[number]>
          headerTitle="拖行排序兼容"
          rowKey="uid"
          dataSource={order}
          search={false}
          options={false}
          pagination={false}
          revalidateOnFocus={false}
          dragSortKey="sort"
          columns={[
            { title: "排序", key: "sort", width: 80 },
            { title: "名称", dataIndex: "name" },
            { title: "稳定身份", dataIndex: "uid" },
          ]}
          dragSortHandlerRender={(row) => (
            <span
              data-testid={`pro-drag-uid-${row.uid}`}
              aria-label={`拖动排序${row.uid}`}
              style={{ cursor: "move", display: "inline-block", padding: 12 }}
            >
              ☰
            </span>
          )}
          onDragSortEnd={(_before, _after, next) => {
            setOrder(next);
            setDragCount((count) => count + 1);
          }}
          columnsState={{ value: {}, onChange: () => undefined }}
        />
        <section data-testid="pro-descriptions">
          <ProDescriptions<{ uid: string; name: string }>
            title="ProDescriptions 详情兼容"
            dataSource={{ uid: "description-a", name: "只读详情示例" }}
            columns={[
              { title: "名称", dataIndex: "name" },
              { title: "身份", dataIndex: "uid" },
            ]}
          />
        </section>
        <section data-testid="pro-list">
          <ProList<(typeof initialOrder)[number]>
            headerTitle="ProList 项目兼容"
            rowKey="uid"
            dataSource={initialOrder}
            search={false}
            options={false}
            pagination={false}
            revalidateOnFocus={false}
            columns={[
              { title: "名称", dataIndex: "name", listSlot: "title" },
              { title: "身份", dataIndex: "uid", listSlot: "description" },
            ]}
            columnsState={{ value: {}, onChange: () => undefined }}
          />
        </section>
      </Space>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(
  <ConfigProvider locale={zhCN}>
    <App>
      <ProComponentsHarness />
    </App>
  </ConfigProvider>,
);
