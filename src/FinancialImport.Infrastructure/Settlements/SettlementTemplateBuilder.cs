using ClosedXML.Excel;

namespace FinancialImport.Infrastructure.Settlements;

/// <summary>
/// Generates the XLSX template for the "Baixa de Notas de Saída" spreadsheet.
/// Column names must stay aligned with SettlementSpreadsheetParser aliases.
/// </summary>
public static class SettlementTemplateBuilder
{
    private static readonly string[] Headers =
    {
        "DocPN", "Nº Nota", "Serie", "DataDocumento", "CNPJ", "Modelo",
        "FormaDePagamento", "ContaContabil", "Valor", "Desconto", "Juros",
        "DataPagamento", "QtdParcelas", "Bandeira", "Ultimo4 DigitosCartao", "Ref"
    };

    public static byte[] Build()
    {
        var doc = new DateTime(2026, 6, 19);
        var pay = new DateTime(2026, 6, 22);

        var sample = new object?[][]
        {
            // Dinheiro
            new object?[] { "369.882.528-78", "692266", null, doc, "22.810.370/0001-27", "NFS-e", "Dinheiro", "111010010001", 69.95m, 0m, 0m, pay, null, null, null, "HF123465" },
            // Transferência com desconto
            new object?[] { "317.598.758-30", "692268", null, doc, "22.810.370/0001-27", "NFS-e", "Transferência", "113010070018", 39.95m, 2m, 0m, pay, null, null, null, "HF123467" },
            // Cartão de crédito (ContaContabil opcional — conta vem da OCRC pela bandeira)
            new object?[] { "007.341.550-26", "692269", null, doc, "22.810.370/0001-27", "NFS-e", "CartaoC", null, 360.48m, 0m, 0m, pay, 5, "ELOCREDITO", "1234", "HF123468" },
            // Cartão de débito
            new object?[] { "111.849.517-99", "692578", null, doc, "22.810.370/0001-27", "NFS-e", "CartaoD", null, 458.68m, 0m, 0m, pay, 1, "ELODEBITO", "3254", "HF123469" },
            // Mesma nota, dois pagamentos (parcial em dinheiro + transferência)
            new object?[] { "100.692.536-84", "692261", null, doc, "22.810.370/0001-27", "NFS-e", "Dinheiro", "111010010001", 110.00m, 0m, 0m, pay, null, null, null, "HF123470" },
            new object?[] { "100.692.536-84", "692261", null, doc, "22.810.370/0001-27", "NFS-e", "Transferência", "111020020013", 200.60m, 0m, 0m, pay, null, null, null, "HF123471" },
        };

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Baixa");

        for (var col = 0; col < Headers.Length; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = Headers[col];
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

        BuildInstructionsSheet(workbook);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildInstructionsSheet(XLWorkbook workbook)
    {
        var instr = workbook.Worksheets.Add("Instrucoes");
        var fields = new (string Field, string Description, string Required)[]
        {
            ("DocPN",                "Documento do cliente (CPF/CNPJ). Usado para localizar a fatura em aberto no SAP.", "Obrigatorio"),
            ("Nº Nota",              "Numero da nota fiscal (OINV.Serial).", "Obrigatorio"),
            ("Serie",                "Serie da nota (OINV.SeriesStr). Pode ficar em branco.", "Opcional"),
            ("DataDocumento",        "Data do documento da nota (OINV.TaxDate), formato dd/MM/yyyy.", "Obrigatorio"),
            ("CNPJ",                 "CNPJ da filial emissora.", "Opcional"),
            ("Modelo",               "Modelo da nota (ex: NFS-e), casado com o modelo fiscal do SAP (ONFM).", "Obrigatorio"),
            ("FormaDePagamento",     "Dinheiro, Transferencia, CartaoC ou CartaoD.", "Obrigatorio"),
            ("ContaContabil",        "Conta de recebimento. Obrigatoria para Dinheiro/Transferencia. Para cartao e OPCIONAL (a conta vem do cadastro do cartao no SAP).", "Condicional"),
            ("Valor",               "Valor bruto da nota a baixar.", "Obrigatorio"),
            ("Desconto",            "Desconto concedido. Reduz o valor recebido (SumApplied = Valor - Desconto).", "Opcional"),
            ("Juros",               "Juros recebidos (nao lancado nesta versao).", "Opcional"),
            ("DataPagamento",       "Data do recebimento, formato dd/MM/yyyy.", "Obrigatorio"),
            ("QtdParcelas",         "Numero de parcelas (cartao). Mais de 1 = parcelado.", "Opcional"),
            ("Bandeira",            "Bandeira do cartao (ex: ELOCREDITO, ELODEBITO). Deve existir no cadastro de cartoes do SAP (OCRC).", "Condicional"),
            ("Ultimo4 DigitosCartao","Ultimos 4 digitos do cartao.", "Opcional"),
            ("Ref",                 "Referencia do recebimento (vai para o campo U_ReferenciaPgto do pagamento).", "Opcional"),
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

        for (var i = 0; i < fields.Length; i++)
        {
            instr.Cell(i + 2, 1).Value = fields[i].Field;
            instr.Cell(i + 2, 2).Value = fields[i].Description;
            instr.Cell(i + 2, 3).Value = fields[i].Required;
        }

        instr.Column(1).Width = 24;
        instr.Column(2).Width = 85;
        instr.Column(3).Width = 14;
        instr.Range(2, 2, fields.Length + 1, 2).Style.Alignment.WrapText = true;
        instr.SheetView.FreezeRows(1);

        var row = fields.Length + 3;
        instr.Cell(row, 1).Value = "COMO FUNCIONA";
        instr.Cell(row, 1).Style.Font.Bold = true;
        instr.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(250, 204, 21);
        instr.Range(row, 1, row, 3).Merge();
        row++;

        var howTo = new[]
        {
            "• Cada linha da planilha vira UM pagamento (Incoming Payment) no SAP.",
            "• A mesma nota pode aparecer em varias linhas (pagamento parcial / formas diferentes).",
            "• A fatura em aberto e localizada por: DocPN + Nº Nota + Serie + Modelo + DataDocumento.",
            "",
            "FORMAS DE PAGAMENTO:",
            "  - Dinheiro      → recebido na ContaContabil informada (CashAccount).",
            "  - Transferencia → recebido na ContaContabil informada (TransferAccount).",
            "  - CartaoC/CartaoD → a conta e o codigo do cartao vem do cadastro do SAP (OCRC),",
            "    casando pela Bandeira. ContaContabil nao e necessaria para cartao.",
            "",
            "DESCONTO: o valor recebido = Valor - Desconto; o desconto e registrado na fatura.",
            "",
            "REPROCESSAMENTO: se a baixa ja existir no SAP e nao estiver cancelada, a linha e",
            "ignorada (informada). Se o pagamento foi cancelado no SAP, a baixa e relancada."
        };
        foreach (var text in howTo)
        {
            instr.Cell(row, 1).Value = text;
            instr.Range(row, 1, row, 3).Merge();
            row++;
        }
    }
}
