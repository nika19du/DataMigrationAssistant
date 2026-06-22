using Microsoft.JSInterop;

namespace DataMigrationAssistant.Blazor.Services;

public sealed class DownloadService(IJSRuntime js)
{
    public ValueTask DownloadSqlAsync(string fileName, string sql) =>
        js.InvokeVoidAsync("downloadFile", fileName, "application/sql;charset=utf-8", sql);

    public ValueTask DownloadMarkdownAsync(string fileName, string markdown) =>
        js.InvokeVoidAsync("downloadFile", fileName, "text/markdown;charset=utf-8", markdown);
}
