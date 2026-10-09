/** 输入为 feature 已解释的说明，不绑定某个后端错误包装或 HTTP 状态。 */
export interface FieldIssue {
  field?: string | null;
  message: string;
}

/** 服务端字段与 UI 路径的语义映射由 feature 提供，行定位始终使用稳定 ClientKey。 */
export type FieldLocation =
  | { kind: "form"; name: readonly (string | number)[] }
  | { kind: "cell"; clientKey: string; columnKey: string }
  | { kind: "page" };

/** 结果可直接呈现或转交 Form.setFields，错误不会成为字段当前值或 dirty 来源。 */
export interface LocatedFieldIssues {
  form: { name: (string | number)[]; errors: string[] }[];
  cells: { clientKey: string; columnKey: string; errors: string[] }[];
  page: string[];
}

/** 聚合已明确映射的错误；未知字段保留为页级说明，不猜中文消息或使用 RowIndex 对账。 */
export function locateFieldIssues(
  issues: readonly FieldIssue[],
  resolveField: (field: string) => FieldLocation | undefined,
): LocatedFieldIssues {
  const result: LocatedFieldIssues = { form: [], cells: [], page: [] };
  for (const issue of issues) {
    const location = issue.field ? resolveField(issue.field) : undefined;
    if (location?.kind === "form") {
      const key = JSON.stringify(location.name);
      let field = result.form.find((item) => JSON.stringify(item.name) === key);
      if (!field) {
        field = { name: [...location.name], errors: [] };
        result.form.push(field);
      }
      field.errors.push(issue.message);
    } else if (location?.kind === "cell") {
      let cell = result.cells.find(
        (item) =>
          item.clientKey === location.clientKey &&
          item.columnKey === location.columnKey,
      );
      if (!cell) {
        cell = {
          clientKey: location.clientKey,
          columnKey: location.columnKey,
          errors: [],
        };
        result.cells.push(cell);
      }
      cell.errors.push(issue.message);
    } else result.page.push(issue.message);
  }
  return result;
}
