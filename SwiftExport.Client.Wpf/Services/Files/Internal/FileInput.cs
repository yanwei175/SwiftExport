using System.IO;
using System.IO.Compression;
using System.Xml;

namespace SwiftExportClient.Services.Files.Internal;

/// <summary>建立有上限的独立输入副本，并在第三方工作簿加载前检查 XLSX 容器。</summary>
internal static class FileInput
{
    /// <summary>从当前位置复制到结尾，不关闭调用方流；失败时释放已分配的内部缓冲。</summary>
    internal static async Task<MemoryStream> ReadBoundedAsync(
        Stream source,
        FileReadLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (!source.CanRead)
            throw new ArgumentException("输入流必须可读。", nameof(source));

        var result = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (count > limits.MaxInputBytes - result.Length)
                    throw new FileOperationException("InputTooLarge", "文件超过允许的输入大小。");
                await result.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            result.Position = 0;
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result.Dispose();
            throw new FileOperationException("ReadFailed", "无法完成文件输入读取。", exception);
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>仅接受标准 XLSX；拒绝宏工作簿及超限 ZIP 声明，取消或校验失败不关闭内部副本。</summary>
    internal static void ValidateXlsx(
        MemoryStream source,
        FileReadLimits limits,
        CancellationToken cancellationToken)
    {
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        source.Position = 0;
        try
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            long expandedSize = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Length > limits.MaxExpandedBytes - expandedSize)
                    throw new FileOperationException("ExpandedInputTooLarge", "Excel 文件的解压内容超过允许大小。");
                expandedSize += entry.Length;
            }

            var contentTypes = archive.GetEntry("[Content_Types].xml")
                ?? throw new FileOperationException("InvalidXlsx", "输入不是有效的 XLSX 文件。");
            using var entryStream = contentTypes.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = limits.MaxExpandedBytes,
            });
            var hasWorkbook = false;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                var contentType = reader.GetAttribute("ContentType");
                if (contentType is null)
                    continue;
                if (contentType.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)
                    || contentType.Contains("vbaProject", StringComparison.OrdinalIgnoreCase))
                    throw new FileOperationException("UnsupportedFileType", "仅支持不含宏的 .xlsx 文件。");
                hasWorkbook |= contentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
            }
            if (!hasWorkbook)
                throw new FileOperationException("UnsupportedFileType", "仅支持 .xlsx 工作簿，不支持旧版或其他 Excel 格式。");
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException)
        {
            throw new FileOperationException("InvalidXlsx", "Excel 文件容器损坏或格式无效。", exception);
        }
        finally
        {
            source.Position = 0;
        }
    }
}
