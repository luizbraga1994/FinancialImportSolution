using ClosedXML.Excel;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Generates empty XLSX templates for the supported import layouts.
/// Columns must stay aligned with Layout2Parser — renaming them here
/// without also updating the parser's FindColumn() aliases will break
/// the import detection.
/// </summary>
public static class ImportTemplateBuilder
{
    public static byte[] BuildLayout2Template()
    {
        // Headers — the first column is "Referencia" (the business/deduplication
        // key, written to SAP Ref1/Ref2). The parser still accepts the legacy
        // name "Observacao" for backwards compatibility, but new downloads use
        // the semantically correct name.
        var headers = new[]
        {
            "Referencia",
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

        // Sample rows: one credit + one debit, both with SeqLancamento populated
        // so the user sees the field is optional-but-recommended.
        var sample = new object?[][]
        {
            new object?[]
            {
                "VENDA_CREDITO_001",
                "1612001100002",
                "4999200000008",
                1503.22m,
                0m,
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                "VR REF JUROS S/ EMPRESTIMO CREDITO PESSOAL CCB N. 16119",
                1,
                "001"
            },
            new object?[]
            {
                "PAG_FORNECEDOR_045",
                "2101001100001",
                "1112001100003",
                0m,
                2750.00m,
                new DateTime(2026, 2, 19),
                new DateTime(2026, 2, 19),
                new DateTime(2026, 2, 19),
                "PAGAMENTO NF 12345 FORNECEDOR XYZ LTDA",
                1,
                "002"
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

        // Sample rows
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
        sheet.SheetView.FreezeRows(1);

        // ─── Instructions sheet ──────────────────────────────────────────────
        var instr = workbook.Worksheets.Add("Instrucoes");
        var instructions = new (string Field, string Description, string Required)[]
        {
            ("Referencia",          "Identificador unico do lancamento. Usado como chave de deduplicacao e enviado ao SAP como Ref1/Ref2.", "Obrigatorio"),
            ("Conta Contabil",      "Codigo da conta contabil principal (debito ou credito).", "Obrigatorio"),
            ("Conta Contrapartida", "Codigo da conta contabil de contrapartida.", "Obrigatorio"),
            ("Valor Credito",       "Valor a credito. Preencha APENAS um dos campos (Credito ou Debito) por linha.", "Condicional"),
            ("Valor Debito",        "Valor a debito. Preencha APENAS um dos campos (Credito ou Debito) por linha.", "Condicional"),
            ("Data Lancamento",     "Data do lancamento contabil (formato dd/MM/yyyy).", "Obrigatorio"),
            ("Data Vencimento",     "Data de vencimento do documento. Se omitida, usa Data Lancamento.", "Opcional"),
            ("Data Documento",      "Data do documento fiscal. Se omitida, usa Data Lancamento.", "Opcional"),
            ("Observacao Linha",    "Historico/memo que aparece na linha do lancamento no SAP.", "Obrigatorio"),
            ("Filial",              "Codigo da filial (BPL ID). Mapeamento definido em Empresas > Filiais.", "Condicional"),
            ("Seq Lancamento",      "Numero sequencial do lancamento dentro do arquivo. Entra na chave de deduplicacao quando configurado.", "Opcional"),
        };

        instr.Cell(1, 1).Value = "Campo";
        instr.Cell(1, 2).Value = "Descricao";
        instr.Cell(1, 3).Value = "Uso";
        for (var col = 1; col <= 3; col++)
        {
            var cell = instr.Cell(1, col);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 64, 175);
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (var i = 0; i < instructions.Length; i++)
        {
            instr.Cell(i + 2, 1).Value = instructions[i].Field;
            instr.Cell(i + 2, 2).Value = instructions[i].Description;
            instr.Cell(i + 2, 3).Value = instructions[i].Required;
        }

        instr.Column(1).Width = 22;
        instr.Column(2).Width = 80;
        instr.Column(3).Width = 14;
        instr.Range(2, 2, instructions.Length + 1, 2).Style.Alignment.WrapText = true;
        instr.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
