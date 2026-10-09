/** 身份编号隔离用户，权限编号隔离核权后的快照；权限变化不改变草稿所属身份。 */
export interface QueryIdentity {
  readonly userId: string;
  readonly identityEpoch: number;
  readonly permissionEpoch: number;
}

/** 规范操作、输入和身份共同构成查询键；这里不存业务草稿。 */
export const queryKeys = {
  operation: (
    identity: QueryIdentity,
    operation: string,
    input: unknown = null,
  ) =>
    [
      "identity",
      identity.identityEpoch,
      identity.userId,
      identity.permissionEpoch,
      operation,
      input,
    ] as const,
  me: (identity: QueryIdentity) =>
    [
      "identity",
      identity.identityEpoch,
      identity.userId,
      identity.permissionEpoch,
      "auth.me",
      null,
    ] as const,
};
