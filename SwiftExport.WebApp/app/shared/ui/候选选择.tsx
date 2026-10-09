import { Select, Space, Typography } from "antd";
import type { AriaAttributes } from "react";

/** 候选只传控件所需投影，不持有 DTO、权限模型或读取服务。 */
export interface LookupOption<Key extends string | number> {
  value: Key;
  label: string;
  disabled?: boolean;
}

/** 搜索词与选中值分别受控；远程查询、授权和输入接纳均由调用方执行。 */
interface LookupSelectCommonProps<
  Key extends string | number,
> extends AriaAttributes {
  id?: string;
  label: string;
  options: readonly LookupOption<Key>[];
  searchValue: string;
  onSearchChange(text: string): void;
  mode?: "local" | "remote";
  readOnly?: boolean;
  disabled?: boolean;
  loading?: boolean;
  error?: string;
}

/** 单选和多选仅改变选值类型；搜索模式、授权和读取仍由原入口负责。 */
export type LookupSelectProps<Key extends string | number> =
  LookupSelectCommonProps<Key> &
    (
      | {
          selectionMode?: "single";
          value: Key | null;
          selectedOption?: LookupOption<Key>;
          selectedOptions?: never;
          onChange(value: Key | null): void;
        }
      | {
          selectionMode: "multiple";
          value: readonly Key[];
          selectedOptions?: readonly LookupOption<Key>[];
          selectedOption?: never;
          onChange(value: Key[]): void;
        }
    );

/** 多选缺项也只补显示标签；不把缺项或重复项变成新的合法可选候选。 */
export function preserveSelectedOptions<Key extends string | number>(
  options: readonly LookupOption<Key>[],
  values: readonly Key[],
  selectedOptions: readonly LookupOption<Key>[] = [],
): LookupOption<Key>[] {
  const missing = [...new Set(values)].filter(
    (value) => !options.some((option) => option.value === value),
  );
  return [
    ...missing.map((value) => ({
      value,
      label:
        selectedOptions.find((option) => option.value === value)?.label ??
        String(value),
      disabled: true,
    })),
    ...options,
  ];
}

/** 合并当前项仅用于显示，不允许把当前页之外的旧值重新选成未授权候选。 */
export function preserveSelectedOption<Key extends string | number>(
  options: readonly LookupOption<Key>[],
  value: Key | null,
  selectedOption?: LookupOption<Key>,
): LookupOption<Key>[] {
  if (value === null || options.some((option) => option.value === value))
    return [...options];
  return [
    {
      value,
      label:
        selectedOption?.value === value ? selectedOption.label : String(value),
      disabled: true,
    },
    ...options,
  ];
}

/** 本地/远程共用 Ant Select 呈现，结果缺项或搜索改变不自动清除业务选中值。 */
export function LookupSelect<Key extends string | number>(
  props: LookupSelectProps<Key>,
) {
  const {
    id,
    label,
    value,
    options,
    selectedOption,
    selectedOptions,
    selectionMode,
    searchValue,
    onSearchChange,
    onChange,
    mode = "local",
    readOnly = false,
    disabled = false,
    loading = false,
    error,
    ...aria
  } = props;
  const blocked = readOnly || disabled;
  return (
    <Space
      orientation="vertical"
      style={{ width: "100%", minWidth: 0, maxWidth: "100%" }}
    >
      <Select<Key | Key[], LookupOption<Key>>
        {...aria}
        id={id}
        aria-label={aria["aria-label"] ?? label}
        mode={selectionMode === "multiple" ? "multiple" : undefined}
        value={selectionMode === "multiple" ? [...value] : value}
        options={
          selectionMode === "multiple"
            ? preserveSelectedOptions(options, value, selectedOptions)
            : preserveSelectedOption(options, value, selectedOption)
        }
        maxTagCount={selectionMode === "multiple" ? "responsive" : undefined}
        disabled={blocked}
        loading={loading}
        allowClear
        status={error ? "error" : undefined}
        style={{ width: "100%", minWidth: 0, maxWidth: "100%" }}
        showSearch={{
          searchValue,
          autoClearSearchValue: false,
          onSearch: (text) => {
            if (!blocked) onSearchChange(text);
          },
          filterOption:
            mode === "remote"
              ? false
              : (text, option) =>
                  Boolean(
                    option?.label
                      .toLocaleLowerCase()
                      .includes(text.toLocaleLowerCase()),
                  ),
        }}
        onChange={(next) => {
          if (blocked) return;
          if (selectionMode === "multiple")
            onChange(Array.isArray(next) ? next : []);
          else onChange(Array.isArray(next) ? null : (next ?? null));
        }}
        notFoundContent={loading ? "正在读取候选…" : (error ?? "暂无匹配候选")}
      />
      {error && (
        <Typography.Text type="danger" role="alert">
          {error}
        </Typography.Text>
      )}
    </Space>
  );
}
