-- =============================================================================
-- Reconciliação COMPLETA do banco FinancialImport (não-destrutiva)
-- =============================================================================
-- QUANDO USAR
--   A aplicação não sobe no startup com:
--       MySqlException: Table 'usuarios' already exists
--   e o log diz "Applying 13 pending migration(s)" — ou seja, o schema base já
--   existe mas o `__EFMigrationsHistory` está vazio/desatualizado, então o EF
--   tenta recriar tudo desde a InitialCreate.
--
-- PRÉ-REQUISITO (IMPORTANTE)
--   Este script marca as 9 migrations BASE como aplicadas. Isso só é correto se
--   o schema base já estiver no estado das 9 migrations (banco criado PELAS
--   migrations do EF e que apenas perdeu o histórico). Confirme antes rodando o
--   diagnóstico:
--
--       SELECT COUNT(*) FROM information_schema.COLUMNS
--        WHERE TABLE_SCHEMA = DATABASE()
--          AND TABLE_NAME = 'ImportacaoLinha'
--          AND COLUMN_NAME = 'CentroCusto';
--
--   Se retornar 1  -> schema base completo, PODE rodar este script.
--   Se retornar 0  -> o schema foi criado pelo 01_InitialCreate.sql (incompleto:
--                     faltam CentroCusto e índices posteriores). NÃO rode este
--                     script — fale com o time / recrie o banco pelas migrations.
--
-- O QUE ESTE SCRIPT FAZ (idempotente, preserva os dados de negócio)
--   1. Recria a tabela BASE `MapeamentoFilialSap` caso tenha sido apagada por
--      engano (versão antiga do reconciliar-baixa-migrations.sql fazia isso).
--   2. Remove as 4 tabelas do módulo de Baixa (sem dados de negócio quando a app
--      nunca subiu) para o EF recriá-las no estado final.
--   3. Garante a tabela `__EFMigrationsHistory`.
--   4. Marca as 9 migrations BASE como aplicadas (INSERT IGNORE).
--   5. Remove qualquer marca das 4 migrations da Baixa (20260622%).
--   Ao subir a app, o EF aplica só as 4 migrations da Baixa, deixando schema +
--   histórico (13) no estado final correto.
--
-- COMO RODAR
--   Selecione o schema `financialimport` e execute tudo. Depois suba a app.
--   NÃO limpe o `__EFMigrationsHistory` depois — deixe a app aplicar as 4 da Baixa.
-- =============================================================================

-- 1) Recria a tabela BASE MapeamentoFilialSap se estiver ausente.
CREATE TABLE IF NOT EXISTS `MapeamentoFilialSap` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `CompanyDb` varchar(50) NOT NULL,
    `CodigoFilialArquivo` varchar(20) NOT NULL,
    `BPLId` int NOT NULL,
    `NomeFilial` varchar(120) NOT NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    CONSTRAINT `PK_MapeamentoFilialSap` PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_MapeamentoFilialSap_CompanyDb_CodigoFilialArquivo` (`CompanyDb`, `CodigoFilialArquivo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2) Remove as 4 tabelas do módulo de Baixa para o EF recriá-las do zero.
--    (NÃO inclua MapeamentoFilialSap aqui — essa é BASE, não é da Baixa.)
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `baixalinha`;
DROP TABLE IF EXISTS `baixasapdispatch`;
DROP TABLE IF EXISTS `baixaarquivo`;
DROP TABLE IF EXISTS `MapeamentoBandeiraCartao`;
SET FOREIGN_KEY_CHECKS = 1;

-- 3) Garante a tabela de histórico do EF Core.
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL,
  CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

-- 4) Marca as 9 migrations BASE como aplicadas (schema base já existe).
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

-- 5) Remove qualquer marca das migrations da Baixa (para o EF reaplicá-las).
DELETE FROM `__EFMigrationsHistory` WHERE `MigrationId` LIKE '20260622%';

-- 6) (Conferência) Deve retornar as 9 migrations base. As 4 da Baixa entram
--    automaticamente no startup, totalizando 13.
SELECT `MigrationId` FROM `__EFMigrationsHistory` ORDER BY `MigrationId`;
