/** 查询缓存的身份范围；会话编号在换用户或登出时递增。 */
export interface QueryIdentity {
  readonly userId: string;
  readonly identityEpoch: number;
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
      operation,
      input,
    ] as const,
  me: (identity: QueryIdentity) =>
    [
      "identity",
      identity.identityEpoch,
      identity.userId,
      "auth.me",
      null,
    ] as const,
};
