// Web 自带已审核 schema 和唯一生成管线；保留状态联合类型及路径编码。
export default {
  webContract: {
    input: "./openapi/首批接口.json",
    output: {
      target: "./app/shared/api/generated/client.ts",
      client: "fetch",
      mode: "single",
      urlEncodeParameters: true,
      override: {
        mutator: {
          path: "./app/shared/api/请求执行.ts",
          name: "executeRequest",
        },
        fetch: {
          includeHttpResponseReturnType: true,
          forceSuccessResponse: false,
        },
      },
    },
  },
};
