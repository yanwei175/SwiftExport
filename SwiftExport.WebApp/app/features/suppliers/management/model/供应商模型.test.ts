import { describe, expect, it } from "vitest";
import {
  createSupplierRow,
  createSupplierRows,
  parseSupplierInput,
  projectSupplierValues,
  validateSupplierValues,
} from "./供应商模型";
import type { SupplierDto } from "./供应商模型";
import { isRowDirty } from "../../../../shared/editing/编辑集合";

const uid = "11111111-1111-1111-1111-111111111111";

describe("供应商字段、默认值与精确输入", () => {
  it("新增只默认 CNY，单据关联号须填写，空数字保持 null", () => {
    const row = createSupplierRow();
    expect(row).toMatchObject({ uid: "", version: 0, origin: "created" });
    expect(row.clientKey).toBeTruthy();
    expect(row.current).toMatchObject({
      currency: "CNY",
      docRelateNo: "",
      zheKou: null,
      taxRate: null,
      defaultLeadTime: null,
    });
    expect(validateSupplierValues(row.current)).toEqual({
      supplierCode: "供应商代码不能为空。",
      supplierName: "供应商名称不能为空。",
      docRelateNo: "单据关联号不能为空。",
    });
    expect(isRowDirty(row)).toBe(true);
  });

  it("服务器基线与当前值独立，修改还原不再 dirty，审计字段不进入可写投影", () => {
    const dto: SupplierDto = {
      uid,
      version: 3,
      supplierCode: " A ",
      supplierName: "供应商",
      docRelateNo: " D ",
      createdBy: "admin",
    };
    const row = createSupplierRows([dto])[0]!;
    expect(row.current).toMatchObject({ supplierCode: "A", docRelateNo: "D" });
    expect(projectSupplierValues(dto)).not.toHaveProperty("createdBy");
    row.current.supplierName = "后来值";
    expect(row.baseline.supplierName).toBe("供应商");
    expect(dto.supplierName).toBe("供应商");
    expect(isRowDirty(row)).toBe(true);
    row.current.supplierName = "供应商";
    expect(isRowDirty(row)).toBe(false);
    expect(() =>
      createSupplierRows([dto, { ...dto, uid: uid.toUpperCase() }]),
    ).toThrow("唯一身份");
    expect(() => createSupplierRows([{ ...dto, version: 0 }])).toThrow(
      "基线版本",
    );
  });

  it("比例允许明确空值和端点边界，拒绝未完成输入及被 number 舍入的小数", () => {
    expect(parseSupplierInput("zheKou", "")).toEqual({
      valid: true,
      value: null,
    });
    expect(parseSupplierInput("taxRate", "0")).toEqual({
      valid: true,
      value: 0,
    });
    expect(parseSupplierInput("taxRate", "1.00")).toEqual({
      valid: true,
      value: 1,
    });
    expect(parseSupplierInput("zheKou", "0.13")).toEqual({
      valid: true,
      value: 0.13,
    });
    for (const raw of [
      "-",
      "0.",
      "1e-2",
      "-0.1",
      "1.01",
      "0.1234567890123456789",
    ])
      expect(parseSupplierInput("zheKou", raw)).toEqual({ valid: false });
  });

  it("默认交期使用非负 Int32，联系人与邮箱不添加后端没有的格式规则", () => {
    expect(parseSupplierInput("defaultLeadTime", "")).toEqual({
      valid: true,
      value: null,
    });
    expect(parseSupplierInput("defaultLeadTime", "2147483647")).toEqual({
      valid: true,
      value: 2147483647,
    });
    for (const raw of ["-1", "1.5", "2147483648"])
      expect(parseSupplierInput("defaultLeadTime", raw)).toEqual({
        valid: false,
      });
    const values = projectSupplierValues({
      supplierCode: "A",
      supplierName: "名称",
      docRelateNo: "D",
      contactEmail: "用户录入文本",
    });
    expect(validateSupplierValues(values)).toEqual({});
    expect(
      validateSupplierValues({
        ...values,
        taxRate: Number.NaN,
        zheKou: 2,
        defaultLeadTime: -1,
      }),
    ).toHaveProperty("taxRate");
    expect(parseSupplierInput("contactPerson", " 联系人 ")).toEqual({
      valid: true,
      value: " 联系人 ",
    });
  });
});
