using ClosedXML.Excel;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Generates empty XLSX templates for the supported import layouts.
/// </summary>
public static class ImportTemplateBuilder
{
    public static byte[] BuildLayout2Template()
    {
        var headers = new[]
        {
            "Observacao",
            "Conta Contabil",
            "Conta Contrapartida",
            "Valor Credito",
            "Valor Debito",
            "Data Lancamento",
            "Data Vencimento",
            "Data Documento",
            "Observacao Linha",
            "Filial",
            "Seq Lancamento"
        };

        var sample = new object?[][]
        {
            new object?[]
            {
                "VENDA_CREDITO",
                "1612001100002",
                "4999200000008",
                1503.22m,
                0m,
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                "VR REF JUROS S/ EMPRESTIMO CREDITO PESSOAL CCB N. 16119",
                1,
                null
            }
        };

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Layout2");

        // Header row
        for (var col = 0; col < headers.Length; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 64, 175);
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Sample row
        for (var r = 0; r < sample.Length; r++)
        {
            var values = sample[r];
            for (var c = 0; c < values.Length; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                var value = values[c];
                switch (value)
                {
                    case null:
                        break;
                    case DateTime dt:
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = "dd/MM/yyyy";
                        break;
                    case decimal dec:
                        cell.Value = dec;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case int i:
                        cell.Value = i;
                        break;
                    default:
                        cell.Value = value.ToString();
                        break;
                }
            }
        }

        sheet.Columns().AdjustToContents();

        // Freeze header
        sheet.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
