-- =============================================================================
-- Reconciliação de migrations — módulo "Baixa de Notas de Saída"
-- =============================================================================
-- QUANDO USAR
--   Use este script quando a aplicação NÃO SOBE com o erro:
--       MySqlException: Table 'usuarios' already exists
--   ao aplicar migrations no startup (Program.cs -> db.Database.MigrateAsync()).
--
-- CAUSA
--   O banco já possui o schema (tabelas existem), mas a tabela de controle
--   `__EFMigrationsHistory` está VAZIA / desatualizada. Sem o histórico, o EF
--   considera TODAS as migrations pendentes e tenta recriar a `Usuarios`
--   (InitialCreate), falhando com "already exists". Como o MySQL não faz
--   rollback de DDL, uma execução interrompida pode deixar tabelas criadas sem
--   gravar o histórico — gerando este descompasso.
--
-- O QUE ESTE SCRIPT FAZ (preserva os dados reais)
--   1. Remove APENAS as tabelas novas do módulo de Baixa (baixaarquivo/
--      baixalinha/baixasapdispatch) e a mapeamentofilialsap. Essas tabelas não
--      guardam dados de negócio quando a app nunca chegou a subir — logo é
--      seguro recriá-las. Os dados reais (usuarios, importacao*, configuracao*,
--      logsistema, etc.) NÃO são tocados.
--   2. Garante a tabela `__EFMigrationsHistory`.
--   3. Marca as 9 migrations ANTIGAS (schema base já existente) como aplicadas.
--   Ao subir a aplicação, o EF aplica só as 4 migrations novas da Baixa,
--   deixando o schema e o histórico (13 no total) no estado final correto.
--
-- ANTES DE RODAR
--   Confirme que as tabelas da Baixa estão vazias (a app não subia, então devem
--   estar):  SELECT COUNT(*) FROM baixaarquivo;
--   Se tiver dados que você queira manter, NÃO rode e fale com o time.
--
-- COMO RODAR
--   Selecione o schema correto (ex.: `financialimport`) e execute tudo.
-- =============================================================================

-- 1) Remove as tabelas novas da Baixa (sem dados) para o EF recriá-las do zero.
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `baixalinha`;
DROP TABLE IF EXISTS `baixasapdispatch`;
DROP TABLE IF EXISTS `baixaarquivo`;
DROP TABLE IF EXISTS `mapeamentofilialsap`;
SET FOREIGN_KEY_CHECKS = 1;

-- 2) Garante a tabela de histórico do EF Core.
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL,
  CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

-- 3) Marca as 9 migrations ANTIGAS como já aplicadas (o schema base já existe).
--    As 4 migrations da Baixa (2026062200000x) ficam de fora de propósito —
--    o EF as aplica no próximo startup, recriando o schema da Baixa corretamente.
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`,`ProductVersion`) VALUES
('20260407000000_InitialCreate','9.0.0'),
('20260408000000_AddMissingFkIndexes','9.0.0'),
('20260408162944_AddReferenciaIndex','9.0.0'),
('20260409000000_AddMessagingAndRichLogs','9.0.0'),
('20260409000001_AddSystemSettings','9.0.0'),
('20260507000000_AddCostingCodeToImportLine','9.0.0'),
('20260528000000_ChangeImportLineUniqueIndexToPerFile','9.0.0'),
('20260528000001_ChangeDispatchUniqueIndexToPerFile','9.0.0'),
('20260608000000_AddImportLineFlagsCoveringIndex','9.0.0');

-- 4) (Opcional) Confira o resultado esperado ANTES de subir a app:
--    Deve retornar 9 linhas (as antigas). As 4 da Baixa serão adicionadas
--    automaticamente no startup, totalizando 13.
-- SELECT MigrationId FROM `__EFMigrationsHistory` ORDER BY MigrationId;
