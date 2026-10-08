// 首批端点由运行时 OpenAPI 截取；保留 HTTP 状态联合类型，错误响应由调用方显式消费。
export default {
  firstWebContract: {
    input: './openapi.json',
    output: {
      target: './generated/client.ts',
      client: 'fetch',
      mode: 'single',
      urlEncodeParameters: true,
      override: { fetch: { includeHttpResponseReturnType: true, forceSuccessResponse: false } },
    },
  },
};
