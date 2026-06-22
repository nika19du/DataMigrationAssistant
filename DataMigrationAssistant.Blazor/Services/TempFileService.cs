using DataMigrationAssistant.Core.Parsers;
using Microsoft.AspNetCore.Components.Forms;

namespace DataMigrationAssistant.Blazor.Services;

public sealed class TempFileService : IDisposable
{
    private readonly IExcelParser _excelParser;
    private string? _tempFilePath;

    public TempFileService(IExcelParser excelParser) => _excelParser = excelParser;

    public async Task<(string TempPath, IReadOnlyList<string> SheetNames, string? Error)> SaveAsync(
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        Cleanup();

        _tempFilePath = Path.ChangeExtension(Path.GetTempFileName(), ".xlsx");

        try
        {
            await using var dest = new FileStream(_tempFilePath, FileMode.Create, FileAccess.Write);
            await using var src = file.OpenReadStream(maxAllowedSize: 50L * 1024 * 1024, cancellationToken);
            await src.CopyToAsync(dest, cancellationToken);
        }
        catch (Exception ex)
        {
            Cleanup();
            return (string.Empty, [], $"Failed to save uploaded file: {ex.Message}");
        }

        var listResult = _excelParser.ListSheets(_tempFilePath);
        if (!listResult.Success)
        {
            Cleanup();
            return (string.Empty, [], listResult.Error ?? "Could not read sheet names from file.");
        }

        return (_tempFilePath, listResult.Value!, null);
    }

    public void Dispose() => Cleanup();

    private void Cleanup()
    {
        if (_tempFilePath is not null)
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
            _tempFilePath = null;
        }
    }
}
