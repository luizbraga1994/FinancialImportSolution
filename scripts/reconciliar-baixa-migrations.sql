-- =============================================================================
-- Reconciliação de migrations — módulo "Baixa de Notas de Saída"
-- =============================================================================
-- QUANDO USAR
--   A aplicação não sobe no startup com:
--       MySqlException: Table 'usuarios' already exists
--   (o EF acha que TODAS as migrations estão pendentes) OU a app sobe mas as
--   tabelas da Baixa não existem (histórico marcado como aplicado sem as tabelas).
--
-- CAUSA
--   Descompasso entre o schema real e a tabela de controle `__EFMigrationsHistory`
--   (schema criado fora das migrations, execução interrompida — o MySQL não faz
--   rollback de DDL — ou histórico preenchido "a mais").
--
-- O QUE ESTE SCRIPT FAZ (idempotente, preserva os dados reais)
--   1. Remove APENAS as tabelas novas do módulo de Baixa (sem dados de negócio
--      quando a app nunca subiu) para o EF recriá-las do zero, no estado final.
--   2. Garante a tabela `__EFMigrationsHistory`.
--   3. Zera qualquer marca das migrations da Baixa (20260622%) e garante as 9
--      migrations ANTIGAS marcadas como aplicadas (schema base já existente).
--   Ao subir a app, o EF aplica só as 4 migrations da Baixa (cria as tabelas,
--   ajusta índices, cria/remove a MapeamentoBandeiraCartao e adiciona DataDocumento),
--   deixando schema + histórico (13) no estado final correto.
--
--   Serve tanto para o caso "histórico vazio" quanto para o caso
--   "histórico com as 13 mas tabelas da Baixa ausentes".
--
-- ANTES DE RODAR
--   Confirme que as tabelas da Baixa estão vazias (a app não subia, devem estar):
--       SELECT COUNT(*) FROM baixaarquivo;
--   Se houver dados que você queira manter, NÃO rode e fale com o time.
--
-- COMO RODAR
--   Selecione o schema correto (ex.: `financialimport`) e execute tudo.
-- =============================================================================

-- 1) Remove as tabelas novas da Baixa (sem dados) para o EF recriá-las do zero.
--    ATENCAO: sao as 4 tabelas do modulo de Baixa. NAO inclua `MapeamentoFilialSap`
--    aqui — essa e uma tabela BASE (criada pela InitialCreate) e nao pertence a Baixa.
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `baixalinha`;
DROP TABLE IF EXISTS `baixasapdispatch`;
DROP TABLE IF EXISTS `baixaarquivo`;
DROP TABLE IF EXISTS `MapeamentoBandeiraCartao`;
SET FOREIGN_KEY_CHECKS = 1;

-- 2) Garante a tabela de histórico do EF Core.
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL,
  CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

-- 3a) Remove qualquer marca das migrations da Baixa (para o EF reaplicá-las).
DELETE FROM `__EFMigrationsHistory` WHERE `MigrationId` LIKE '20260622%';

-- 3b) Garante as 9 migrations ANTIGAS marcadas como aplicadas (schema base existe).
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

-- 4) (Opcional) Deve retornar 9 linhas. As 4 da Baixa serão adicionadas
--    automaticamente no startup, totalizando 13.
-- SELECT MigrationId FROM `__EFMigrationsHistory` ORDER BY MigrationId;
