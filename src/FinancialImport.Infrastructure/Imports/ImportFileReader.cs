using System.Globalization;
using ClosedXML.Excel;
using FinancialImport.Application.Imports;
using Microsoft.AspNetCore.Http;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Reads an uploaded file into an <see cref="ImportFileContext"/>.
/// Supports CSV/TXT (delimited) and XLSX (via ClosedXML). The resulting
/// headers/rows are consumed by layout parsers.
/// </summary>
public static class ImportFileReader
{
    public static async Task<ImportFileContext> ReadAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (extension == ".xlsx")
        {
            return ReadXlsx(file.FileName, fileBytes);
        }

        return ReadDelimited(file.FileName, fileBytes);
    }

    private static ImportFileContext ReadXlsx(string fileName, byte[] fileBytes)
    {
        using var inputStream = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(inputStream);

        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Arquivo XLSX nao contem nenhuma planilha.");

        var usedRange = worksheet.RangeUsed();
        if (usedRange == null)
        {
            return new ImportFileContext
            {
                FileName = fileName,
                FileBytes = fileBytes
            };
        }

        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count == 0)
        {
            return new ImportFileContext
            {
                FileName = fileName,
                FileBytes = fileBytes
            };
        }

        var headerRow = rows[0];
        var headers = headerRow.Cells()
            .Select(c => (c.GetString() ?? string.Empty).Trim())
            .ToArray();

        var dataRows = new List<ImportRow>();
        foreach (var row in rows.Skip(1))
        {
            // Skip fully empty rows
            if (row.Cells().All(c => c.IsEmpty()))
                continue;

            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(headers[i])) continue;

                // Column indices in ClosedXML are 1-based.
                var cell = row.Cell(i + 1);
                var value = CellToString(cell);
                dict[headers[i]] = string.IsNullOrWhiteSpace(value) ? null : value;
            }
            dataRows.Add(new ImportRow(dict));
        }

        return new ImportFileContext
        {
            FileName = fileName,
            FileBytes = fileBytes,
            Headers = headers,
            Rows = dataRows
        };
    }

    private static string CellToString(IXLCell cell)
    {
        if (cell == null || cell.IsEmpty()) return string.Empty;

        switch (cell.DataType)
        {
            case XLDataType.DateTime:
                return cell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            case XLDataType.Number:
                // Preserve full decimal precision using invariant format so
                // downstream parsers can read it. Brazilian formatting is
                // handled later by ImportRow.GetDecimal.
                return cell.GetDouble().ToString("G17", CultureInfo.InvariantCulture);

            case XLDataType.Boolean:
                return cell.GetBoolean().ToString();

            default:
                return (cell.GetString() ?? string.Empty).Trim();
        }
    }

    private static ImportFileContext ReadDelimited(string fileName, byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes);
        using var reader = new StreamReader(ms);

        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
        }

        if (lines.Count == 0)
        {
            return new ImportFileContext
            {
                FileName = fileName,
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
            FileName = fileName,
            FileBytes = fileBytes,
            Headers = headers,
            Rows = rows
        };
    }
}
