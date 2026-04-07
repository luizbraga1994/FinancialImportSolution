using FinancialImport.Application.Imports;
using Microsoft.AspNetCore.Http;

namespace FinancialImport.Web.Services;

public interface IImportFileReader
{
    Task<ImportFileContext> ReadAsync(IFormFile file, CancellationToken cancellationToken = default);
}

public sealed class CsvImportFileReader : IImportFileReader
{
    public async Task<ImportFileContext> ReadAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);

        var fileBytes = await ReadAllBytesAsync(file, cancellationToken);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            return new ImportFileContext
            {
                FileName = file.FileName,
                FileBytes = fileBytes
            };
        }

        var delimiter = lines[0].Contains(';') ? ';' : ',';
        var headers = lines[0].Split(delimiter).Select(h => h.Trim()).ToArray();
        var rows = new List<ImportRow>();

        foreach (var line in lines.Skip(1))
        {
            var values = line.Split(delimiter);
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var value = i < values.Length ? values[i].Trim() : null;
                dict[headers[i]] = string.IsNullOrWhiteSpace(value) ? null : value;
            }
            rows.Add(new ImportRow(dict));
        }

        return new ImportFileContext
        {
            FileName = file.FileName,
            FileBytes = fileBytes,
            Headers = headers,
            Rows = rows
        };
    }

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }
}
