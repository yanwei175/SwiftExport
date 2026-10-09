import { spawnSync } from "node:child_process";
import { X509Certificate } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { createSecureContext } from "node:tls";
import { fileURLToPath } from "node:url";

const webRoot = fileURLToPath(new URL("../", import.meta.url));
const certificatePath = path.join(
  webRoot,
  ".artifacts/https/swiftexport-localhost.pem",
);
const keyPath = path.join(
  webRoot,
  ".artifacts/https/swiftexport-localhost.key",
);

/** 执行固定开发证书命令并限制等待；不生成、清理、导入或信任证书，不输出私钥。 */
function runCertificateTool(args) {
  const result = spawnSync("dotnet", ["dev-certs", "https", ...args], {
    encoding: "utf8",
    windowsHide: true,
    timeout: 30000,
  });
  if (result.error || result.status !== 0)
    throw new Error(
      "开发证书检查或导出失败。请确认 .NET 10 SDK 可用，并由用户先完成 dotnet dev-certs https --trust；本脚本不会改变证书信任。",
      { cause: result.error },
    );
  return result.stdout;
}

/** 只读检查CLI报告的有效、可导出、完整信任的开发证书，避免启动时静默创建另一张证书。 */
function getTrustedThumbprints() {
  const certificates = JSON.parse(
    runCertificateTool(["--check-trust-machine-readable"]),
  );
  const now = Date.now();
  const trusted = certificates.filter(
    (certificate) =>
      certificate.TrustLevel === "Full" &&
      certificate.IsHttpsDevelopmentCertificate === true &&
      certificate.IsExportable === true &&
      Date.parse(certificate.ValidityNotBefore) <= now &&
      Date.parse(certificate.ValidityNotAfter) > now,
  );
  if (!trusted.length)
    throw new Error(
      "未找到受信任且可导出的 localhost 开发证书。请由用户执行 dotnet dev-certs https --trust 后重试；脚本不会自动创建或信任证书。",
    );
  return new Set(trusted.map((certificate) => certificate.Thumbprint));
}

/** 仅读取固定忽略目录中的证书和私钥，核对实际证书信任指纹与密钥匹配；不向日志输出内容。 */
function readTrustedPair(trusted) {
  const cert = fs.readFileSync(certificatePath);
  const key = fs.readFileSync(keyPath);
  const certificate = new X509Certificate(cert);
  if (
    !trusted.has(certificate.fingerprint.replaceAll(":", "")) ||
    !certificate.checkHost("localhost")
  )
    throw new Error("已导出的证书与当前受信任 localhost 开发证书不一致。");
  createSecureContext({ cert, key });
  return { cert, key };
}

/** 为开发/验收服务器复用当前受信任证书；私钥只导出到已忽略 .artifacts，生产构建不得调用。 */
export function prepareDevelopmentCertificate() {
  const trusted = getTrustedThumbprints();
  try {
    return readTrustedPair(trusted);
  } catch {
    fs.mkdirSync(path.dirname(certificatePath), { recursive: true });
    runCertificateTool([
      "--export-path",
      certificatePath,
      "--format",
      "Pem",
      "--no-password",
    ]);
    // 导出后再次核对实际指纹与密钥；不把导出成功等同于浏览器已信任。
    return readTrustedPair(trusted);
  }
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  prepareDevelopmentCertificate();
  process.stdout.write(
    "开发证书已准备：.artifacts/https（已忽略）。入口使用 https://localhost。\n",
  );
}
