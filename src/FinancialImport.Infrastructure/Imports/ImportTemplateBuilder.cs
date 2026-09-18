using ClosedXML.Excel;

namespace FinancialImport.Infrastructure.Imports;

/// <summary>
/// Generates the empty XLSX template for the LCM import (single-account layout).
/// Each row maps to ONE SAP journal line on its own account, with the Debit/Credit
/// exactly as provided — the journal balances by the sum of the rows that share the
/// same Referencia (SAP "Lançamento Contábil Manual" style). Column names must stay
/// aligned with Layout2Parser aliases.
/// </summary>
public static class ImportTemplateBuilder
{
    public static byte[] BuildLayout2Template()
    {
        var headers = new[]
        {
            "Referencia",
            "Conta Contabil",
            "Valor Debito",
            "Valor Credito",
            "Data Lancamento",
            "Data Vencimento",
            "Data Documento",
            "Observacao Linha",
            "Filial",
            "Centro de Custo",
            "Seq Lancamento"
        };

        var d = new DateTime(2026, 7, 16);

        // Sample: a single journal (same Referencia) with 4 lines that balance
        // (D 22,00 = C 22,00). Each row is ONE SAP line on its own Conta Contabil.
        var sample = new object?[][]
        {
            new object?[] { "EDITORA - HOTMART", "111030040005", 0m,     10.00m, d, d, d, "Tarifa Hotmart",                        1, null, "1" },
            new object?[] { "EDITORA - HOTMART", "411020010004", 10.00m, 0m,     d, d, d, "Tarifa Hotmart",                        1, null, "2" },
            new object?[] { "EDITORA - HOTMART", "411020010004", 0m,     12.00m, d, d, d, "Reclamada - Estorno de Tarifa Hotmart", 1, null, "3" },
            new object?[] { "EDITORA - HOTMART", "111030040005", 12.00m, 0m,     d, d, d, "Reclamada - Estorno de Tarifa Hotmart", 1, null, "4" },
        };

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("LCM");

        for (var col = 0; col < headers.Length; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(30, 64, 175);
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (var r = 0; r < sample.Length; r++)
        {
            var values = sample[r];
            for (var c = 0; c < values.Length; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (values[c])
                {
                    case null: break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "dd/MM/yyyy"; break;
                    case decimal dec: cell.Value = dec; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case int i: cell.Value = i; break;
                    default: cell.Value = values[c]!.ToString(); break;
                }
            }
        }

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        // ─── Instructions sheet ──────────────────────────────────────────────
        var instr = workbook.Worksheets.Add("Instrucoes");
        var instructions = new (string Field, string Description, string Required)[]
        {
            ("Referencia",       "Identificador do lancamento. Linhas com a MESMA Referencia + datas iguais sao agrupadas em UM lancamento SAP.", "Obrigatorio"),
            ("Conta Contabil",   "Conta contabil (ex: 1.1.1.02.002) OU codigo de Parceiro de Negocio (ex: F00012). Codigos com letras sao reconhecidos automaticamente como Parceiro de Negocio (ShortName no SAP).", "Obrigatorio"),
            ("Valor Debito",     "Valor a DEBITO na conta da linha. Preencha Debito OU Credito por linha.", "Condicional"),
            ("Valor Credito",    "Valor a CREDITO na conta da linha. Preencha Debito OU Credito por linha.", "Condicional"),
            ("Data Lancamento",  "Data do lancamento contabil (dd/MM/yyyy).", "Obrigatorio"),
            ("Data Vencimento",  "Data de vencimento. Se omitida, usa Data Lancamento.", "Opcional"),
            ("Data Documento",   "Data do documento. Se omitida, usa Data Lancamento.", "Opcional"),
            ("Observacao Linha", "Historico/memo que aparece na linha do lancamento no SAP.", "Obrigatorio"),
            ("Filial",           "Codigo da filial (BPL ID). Mapeamento em Empresas > Filiais.", "Condicional"),
            ("Centro de Custo",  "Codigo do centro de custo (CostingCode) da linha.", "Opcional"),
            ("Seq Lancamento",   "Numero sequencial da linha dentro do arquivo. Entra na chave de deduplicacao quando configurado.", "Opcional"),
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
        instr.Column(2).Width = 85;
        instr.Column(3).Width = 14;
        instr.Range(2, 2, instructions.Length + 1, 2).Style.Alignment.WrapText = true;
        instr.SheetView.FreezeRows(1);

        var row = instructions.Length + 3;
        instr.Cell(row, 1).Value = "COMO FUNCIONA";
        instr.Cell(row, 1).Style.Font.Bold = true;
        instr.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(250, 204, 21);
        instr.Range(row, 1, row, 3).Merge();
        row++;

        var howTo = new[]
        {
            "• Cada linha da planilha vira UMA linha do lancamento no SAP, na propria Conta Contabil.",
            "• Preencha Valor Debito OU Valor Credito na linha (o outro fica 0,00).",
            "• Linhas com a MESMA Referencia + mesmas datas formam UM lancamento SAP.",
            "• O lancamento deve BALANCEAR: soma dos Debitos = soma dos Creditos no grupo.",
            "",
            "EXEMPLO (ver aba LCM) — Referencia 'EDITORA - HOTMART':",
            "  111030040005  Credito 10,00  (Tarifa Hotmart)",
            "  411020010004  Debito  10,00  (Tarifa Hotmart)",
            "  411020010004  Credito 12,00  (Reclamada - Estorno de Tarifa Hotmart)",
            "  111030040005  Debito  12,00  (Reclamada - Estorno de Tarifa Hotmart)",
            "  Total: D 22,00 = C 22,00 (balanceado)",
            "",
            "CONTAS CONTABEIS vs PARCEIROS DE NEGOCIO:",
            "  - Somente digitos e pontos (ex: 1.1.1.02.002) → Conta Contabil (AccountCode)",
            "  - Contem alguma letra (ex: F00012)          → Parceiro de Negocio (ShortName)",
            "",
            "AUTO-COMPLETE DE CONTAS: o sistema resolve o digito verificador contra o",
            "plano de contas do SAP. Voce pode escrever '1612001100002' ou '1612001100002-0'."
        };
        foreach (var text in howTo)
        {
            instr.Cell(row, 1).Value = text;
            instr.Range(row, 1, row, 3).Merge();
            row++;
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
