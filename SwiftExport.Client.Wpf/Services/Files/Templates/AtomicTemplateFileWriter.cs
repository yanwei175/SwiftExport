using System.IO;

namespace SwiftExportClient.Services.Files.Templates;

/// <summary>将已生成的模板结果写入同目录独占临时文件，并在最后一步发布，避免损坏既有输出。</summary>
internal static class AtomicTemplateFileWriter
{
    /// <summary>检查本地 XLSX 目标；不自动创建目录，不在默认模式下覆盖已有文件。</summary>
    internal static string ValidateTarget(string targetPath, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new FileOperationException("InvalidOutputPath", "Excel 输出路径不能为空。");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(targetPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FileOperationException("InvalidOutputPath", "Excel 输出路径无效。", exception);
        }
        if (!string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new FileOperationException("UnsupportedOutputFormat", "模板输出只支持 XLSX 文件。");
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
            throw new FileOperationException("OutputDirectoryNotFound", "Excel 输出目录不存在。");
        if (!overwrite && File.Exists(fullPath))
            throw new FileOperationException("OutputAlreadyExists", "输出文件已经存在，默认不覆盖。");
        return fullPath;
    }

    /// <summary>在发布前检查取消；可由宿主在会话状态锁内执行最终移动，发布完成后不再把后到的取消误报为失败。</summary>
    internal static async Task WriteAsync(
        MemoryStream content,
        string fullPath,
        bool overwrite,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken,
        Action<Action>? publishCommit = null)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".swiftexport-{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new FileOperationProgress("WritingOutput"));
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                content.Position = 0;
                await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            progress?.Report(new FileOperationProgress("PublishingOutput"));
            cancellationToken.ThrowIfCancellationRequested();
            // 同目录单次文件系统移动是提交点；会话型宿主在同一提交保护中再次核对身份与取消。
            void Publish()
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, fullPath, overwrite);
            }
            if (publishCommit is null) Publish();
            else publishCommit(Publish);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new FileOperationException("OutputWriteFailed", "Excel 文件写入或发布失败。", exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 清理失败不能遮蔽写入错误；临时文件名不包含单据或用户信息。
            }
        }

        ReportCompleted(progress);
    }

    /// <summary>完成后的进度观察者不能推翻已经发布成功的文件结果。</summary>
    internal static void ReportCompleted(IProgress<FileOperationProgress>? progress)
    {
        try
        {
            progress?.Report(new FileOperationProgress("Completed"));
        }
        catch (Exception)
        {
            // 文件已完成提交，进度接收方的异常不应被描述为文件写入失败。
        }
    }
}
