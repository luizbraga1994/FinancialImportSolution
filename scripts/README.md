# scripts/

Scripts SQL de apoio para o banco MySQL do FinancialImport.

## Arquivos

- **`01_InitialCreate.sql`** — criação completa do banco (tabelas, índices,
  constraints) para MySQL 8.0+.

- **`reconciliar-baixa-migrations.sql`** — reconcilia o `__EFMigrationsHistory`
  quando a aplicação não sobe com o erro
  `MySqlException: Table 'usuarios' already exists` ao aplicar migrations no
  startup. Isso acontece quando o banco já tem o schema mas o histórico de
  migrations está vazio/desatualizado (o EF tenta recriar tudo desde a
  `InitialCreate`). O script preserva os dados reais, recria apenas as tabelas
  novas do módulo de Baixa e marca as migrations antigas como aplicadas, para
  que o EF aplique só as 4 migrations da Baixa no próximo startup. Leia o
  cabeçalho do arquivo antes de executar. As 4 tabelas do módulo de Baixa são
  `BaixaArquivo`, `BaixaLinha`, `BaixaSapDispatch` e `MapeamentoBandeiraCartao`
  — **não** confunda esta última com a tabela BASE `MapeamentoFilialSap`, que
  pertence à `InitialCreate` e nunca deve ser dropada pela reconciliação.

- **`recuperar-mapeamentofilialsap.sql`** — recria a tabela BASE
  `MapeamentoFilialSap` caso ela tenha sido apagada por engano (uma versão
  anterior do `reconciliar-baixa-migrations.sql` dropava `mapeamentofilialsap`
  em vez de `MapeamentoBandeiraCartao`). Restaura a consistência entre o schema
  e o `__EFMigrationsHistory`. Leia o cabeçalho antes de executar.

## Contexto (migrations)

As migrations do EF Core aplicam-se automaticamente no startup
(`Program.cs → db.Database.MigrateAsync()`). Ordem atual:

| # | Migration |
|---|-----------|
| 1 | 20260407000000_InitialCreate |
| 2 | 20260408000000_AddMissingFkIndexes |
| 3 | 20260408162944_AddReferenciaIndex |
| 4 | 20260409000000_AddMessagingAndRichLogs |
| 5 | 20260409000001_AddSystemSettings |
| 6 | 20260507000000_AddCostingCodeToImportLine |
| 7 | 20260528000000_ChangeImportLineUniqueIndexToPerFile |
| 8 | 20260528000001_ChangeDispatchUniqueIndexToPerFile |
| 9 | 20260608000000_AddImportLineFlagsCoveringIndex |
| 10 | 20260622000000_AddReceivableSettlement |
| 11 | 20260622000001_SettlementMultiplePaymentsPerInvoice |
| 12 | 20260622000002_DropCardBrandMapping |
| 13 | 20260622000003_AddSettlementDocumentDate |

Em um banco novo/vazio, basta subir a aplicação: as 13 migrations são aplicadas
automaticamente. Os scripts acima só são necessários para reconciliar um banco
que ficou com schema sem histórico.
