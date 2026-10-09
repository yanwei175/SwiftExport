import { Select, Space, Typography } from "antd";
import type { AriaAttributes } from "react";

/** 候选只传控件所需投影，不持有 DTO、权限模型或读取服务。 */
export interface LookupOption<Key extends string | number> {
  value: Key;
  label: string;
  disabled?: boolean;
}

/** 搜索词与选中值分别受控；远程查询、授权和输入接纳均由调用方执行。 */
export interface LookupSelectProps<
  Key extends string | number,
> extends AriaAttributes {
  id?: string;
  label: string;
  value: Key | null;
  options: readonly LookupOption<Key>[];
  selectedOption?: LookupOption<Key>;
  searchValue: string;
  onSearchChange(text: string): void;
  onChange(value: Key | null): void;
  mode?: "local" | "remote";
  readOnly?: boolean;
  disabled?: boolean;
  loading?: boolean;
  error?: string;
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
export function LookupSelect<Key extends string | number>({
  id,
  label,
  value,
  options,
  selectedOption,
  searchValue,
  onSearchChange,
  onChange,
  mode = "local",
  readOnly = false,
  disabled = false,
  loading = false,
  error,
  ...aria
}: LookupSelectProps<Key>) {
  const blocked = readOnly || disabled;
  return (
    <Space orientation="vertical" style={{ width: "100%" }}>
      <Select<Key, LookupOption<Key>>
        {...aria}
        id={id}
        aria-label={aria["aria-label"] ?? label}
        value={value}
        options={preserveSelectedOption(options, value, selectedOption)}
        disabled={blocked}
        loading={loading}
        allowClear
        status={error ? "error" : undefined}
        style={{ width: "100%", minWidth: 150 }}
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
          if (!blocked) onChange(next ?? null);
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
