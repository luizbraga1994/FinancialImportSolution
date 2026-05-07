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
            "Centro de Custo",
            "Seq Lancamento"
        };

        // Sample rows: show a pre-balanced multi-line group so users see how
        // to detail a journal entry (credits + debits totaling the same value
        // in the same Referencia = single SAP journal with multiple lines).
        var sample = new object?[][]
        {
            // --- Group 1: single one-sided row (classic debit+contra) ---
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
                null,
                "001"
            },
            // --- Group 2: pre-balanced detailed entry (3 rows → 3 SAP lines) ---
            // Row 1: Valor Credito = 1503,22 on ContaContabil 1612 → SAP: 1612 Debit 1503,22
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
                "CC001",
                "002"
            },
            // Row 2: Valor Debito = 1500 on Contrapartida 4999 → SAP: 4999 Credit 1500
            new object?[]
            {
                "VENDA_CREDITO_001",
                "1612001100002",
                "4999200000008",
                0m,
                1500.00m,
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                "VR REF JUROS S/ EMPRESTIMO",
                1,
                "CC001",
                "003"
            },
            // Row 3: Valor Debito = 3,22 on Contrapartida 3281 → SAP: 3281 Credit 3,22
            new object?[]
            {
                "VENDA_CREDITO_001",
                "1612001100002",
                "3281020050001",
                0m,
                3.22m,
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                new DateTime(2026, 2, 18),
                "Taxa",
                1,
                null,
                "004"
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
            ("Referencia",          "Identificador do lancamento. Linhas com a MESMA Referencia + datas iguais sao agrupadas em UM lancamento SAP.", "Obrigatorio"),
            ("Conta Contabil",      "Codigo da conta contabil principal. O digito verificador (-0, -1, etc) e resolvido automaticamente.", "Obrigatorio"),
            ("Conta Contrapartida", "Codigo da conta contabil de contrapartida.", "Obrigatorio"),
            ("Valor Credito",       "Se preenchido, a linha SAP usa a CONTA CONTABIL no DEBITO. Preencha apenas Credito OU Debito por linha.", "Condicional"),
            ("Valor Debito",        "Se preenchido, a linha SAP usa a CONTRAPARTIDA no CREDITO. Preencha apenas Credito OU Debito por linha.", "Condicional"),
            ("Data Lancamento",     "Data do lancamento contabil (formato dd/MM/yyyy).", "Obrigatorio"),
            ("Data Vencimento",     "Data de vencimento do documento. Se omitida, usa Data Lancamento.", "Opcional"),
            ("Data Documento",      "Data do documento fiscal. Se omitida, usa Data Lancamento.", "Opcional"),
            ("Observacao Linha",    "Historico/memo que aparece na linha do lancamento no SAP.", "Obrigatorio"),
            ("Filial",              "Codigo da filial (BPL ID). Mapeamento definido em Empresas > Filiais.", "Condicional"),
            ("Centro de Custo",     "Codigo do centro de custo (CostingCode) enviado para cada linha do lancamento SAP.", "Opcional"),
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

        // ─── How-it-works block ──────────────────────────────────────────────
        var row = instructions.Length + 3;
        instr.Cell(row, 1).Value = "COMO FUNCIONA O AGRUPAMENTO";
        instr.Cell(row, 1).Style.Font.Bold = true;
        instr.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(250, 204, 21);
        instr.Range(row, 1, row, 3).Merge();
        row++;

        var howTo = new[]
        {
            "• Linhas com MESMA Referencia + MESMAS datas formam UM lancamento SAP.",
            "",
            "MODO SIMPLES (1 linha do Excel → 2 linhas SAP):",
            "Quando o grupo tem apenas valores a credito OU apenas a debito,",
            "o sistema gera a linha principal na Conta Contabil e a contrapartida",
            "na Conta Contrapartida automaticamente para balancear.",
            "",
            "MODO DETALHADO (N linhas do Excel → N linhas SAP):",
            "Quando o grupo tem linhas de credito E debito que totalizam o mesmo",
            "valor, cada linha do Excel vira UMA linha SAP:",
            "  - Linha com Valor Credito preenchido → SAP usa Conta Contabil no DEBITO",
            "  - Linha com Valor Debito preenchido → SAP usa Contrapartida no CREDITO",
            "",
            "EXEMPLO DO MODO DETALHADO (ver aba Layout2):",
            "3 linhas com mesma Referencia 'VENDA_CREDITO_001':",
            "  - Credito 1503,22 em 1612 → SAP: 1612 Debito 1503,22",
            "  - Debito 1500,00 em 4999 → SAP: 4999 Credito 1500,00",
            "  - Debito    3,22 em 3281 → SAP: 3281 Credito    3,22",
            "Total: 1503,22 D = 1503,22 C (balanceado)",
            "",
            "AUTO-COMPLETE DE CONTAS:",
            "O sistema busca o plano de contas do SAP e resolve automaticamente",
            "o digito verificador. Voce pode escrever '1612001100002' ou",
            "'1612001100002-0' — ambos funcionam."
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
