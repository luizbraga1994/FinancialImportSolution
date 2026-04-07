-- =============================================================
-- FinancialImport - Script de Inicializacao MySQL
-- Cria o banco, todas as tabelas, indices e constraints
-- Compativel com MySQL 8.0+
-- =============================================================

CREATE DATABASE IF NOT EXISTS `FinancialImport`
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE `FinancialImport`;

-- =============================================================
-- Tabela de controle de migrations do EF Core
-- =============================================================
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 1. Usuarios
-- =============================================================
CREATE TABLE IF NOT EXISTS `Usuarios` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Login` varchar(80) NOT NULL,
    `Nome` varchar(120) NOT NULL,
    `Email` varchar(120) NOT NULL,
    `SenhaHash` varbinary(256) NOT NULL,
    `SenhaSalt` varbinary(128) NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    `Bloqueado` tinyint(1) NOT NULL DEFAULT 0,
    `AdminGlobal` tinyint(1) NOT NULL DEFAULT 0,
    `DataCriacao` datetime(6) NOT NULL,
    `DataUltimoLogin` datetime(6) NULL,
    `UsuarioCriacao` varchar(80) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_Usuarios_Login` (`Login`),
    UNIQUE INDEX `IX_Usuarios_Email` (`Email`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 2. Perfis
-- =============================================================
CREATE TABLE IF NOT EXISTS `Perfis` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Nome` varchar(80) NOT NULL,
    `Descricao` varchar(200) NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_Perfis_Nome` (`Nome`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 3. Permissoes
-- =============================================================
CREATE TABLE IF NOT EXISTS `Permissoes` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Codigo` varchar(80) NOT NULL,
    `Nome` varchar(120) NOT NULL,
    `Descricao` varchar(200) NULL,
    `Grupo` varchar(80) NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_Permissoes_Codigo` (`Codigo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 4. UsuarioPerfil (N:N entre Usuarios e Perfis)
-- =============================================================
CREATE TABLE IF NOT EXISTS `UsuarioPerfil` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UsuarioId` bigint NOT NULL,
    `PerfilId` bigint NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_UsuarioPerfil_UsuarioId_PerfilId` (`UsuarioId`, `PerfilId`),
    CONSTRAINT `FK_UsuarioPerfil_Usuarios_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuarios` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_UsuarioPerfil_Perfis_PerfilId` FOREIGN KEY (`PerfilId`) REFERENCES `Perfis` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 5. PerfilPermissao (N:N entre Perfis e Permissoes)
-- =============================================================
CREATE TABLE IF NOT EXISTS `PerfilPermissao` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PerfilId` bigint NOT NULL,
    `PermissaoId` bigint NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_PerfilPermissao_PerfilId_PermissaoId` (`PerfilId`, `PermissaoId`),
    CONSTRAINT `FK_PerfilPermissao_Perfis_PerfilId` FOREIGN KEY (`PerfilId`) REFERENCES `Perfis` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_PerfilPermissao_Permissoes_PermissaoId` FOREIGN KEY (`PermissaoId`) REFERENCES `Permissoes` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 6. UsuarioEmpresaPermitida (companies permitidas por usuario)
-- =============================================================
CREATE TABLE IF NOT EXISTS `UsuarioEmpresaPermitida` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UsuarioId` bigint NOT NULL,
    `CompanyDb` varchar(50) NOT NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_UsuarioEmpresaPermitida_UsuarioId_CompanyDb` (`UsuarioId`, `CompanyDb`),
    CONSTRAINT `FK_UsuarioEmpresaPermitida_Usuarios_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuarios` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 7. AuditoriaLogin (log de tentativas de login)
-- =============================================================
CREATE TABLE IF NOT EXISTS `AuditoriaLogin` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UsuarioId` bigint NULL,
    `LoginInformado` varchar(80) NOT NULL,
    `Sucesso` tinyint(1) NOT NULL,
    `EnderecoIp` varchar(64) NULL,
    `UserAgent` varchar(200) NULL,
    `DataHora` datetime(6) NOT NULL,
    `MotivoFalha` varchar(200) NULL,
    PRIMARY KEY (`Id`),
    INDEX `IX_AuditoriaLogin_DataHora` (`DataHora`),
    CONSTRAINT `FK_AuditoriaLogin_Usuarios_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuarios` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 8. SessaoEmpresaUsuario (sessoes SAP ativas)
-- =============================================================
CREATE TABLE IF NOT EXISTS `SessaoEmpresaUsuario` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UsuarioId` bigint NOT NULL,
    `CompanyDb` varchar(50) NOT NULL,
    `CompanyName` varchar(120) NOT NULL,
    `SapUserName` varchar(80) NOT NULL,
    `SessionId` varchar(120) NOT NULL,
    `RouteId` varchar(120) NULL,
    `ExpiraEm` datetime(6) NOT NULL,
    `Ativa` tinyint(1) NOT NULL DEFAULT 1,
    `DataLogin` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    INDEX `IX_SessaoEmpresaUsuario_UsuarioId` (`UsuarioId`),
    INDEX `IX_SessaoEmpresaUsuario_CompanyDb` (`CompanyDb`),
    CONSTRAINT `FK_SessaoEmpresaUsuario_Usuarios_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuarios` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 9. ImportacaoArquivo (arquivos importados)
-- =============================================================
CREATE TABLE IF NOT EXISTS `ImportacaoArquivo` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UsuarioId` bigint NOT NULL,
    `CompanyDb` varchar(50) NOT NULL,
    `NomeArquivoOriginal` varchar(200) NOT NULL,
    `HashArquivo` varchar(64) NOT NULL,
    `LayoutDetectado` varchar(80) NOT NULL,
    `FilialPadrao` varchar(20) NULL,
    `UsarFilialArquivo` tinyint(1) NOT NULL DEFAULT 1,
    `Status` varchar(40) NOT NULL,
    `QuantidadeLinhas` int NOT NULL DEFAULT 0,
    `QuantidadeValidas` int NOT NULL DEFAULT 0,
    `QuantidadeInvalidas` int NOT NULL DEFAULT 0,
    `QuantidadeImportadas` int NOT NULL DEFAULT 0,
    `QuantidadeDuplicadas` int NOT NULL DEFAULT 0,
    `QuantidadeComErro` int NOT NULL DEFAULT 0,
    `DataImportacao` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_ImportacaoArquivo_CompanyDb_HashArquivo` (`CompanyDb`, `HashArquivo`),
    INDEX `IX_ImportacaoArquivo_UsuarioId` (`UsuarioId`),
    INDEX `IX_ImportacaoArquivo_CompanyDb` (`CompanyDb`),
    CONSTRAINT `FK_ImportacaoArquivo_Usuarios_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuarios` (`Id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 10. ImportacaoLinha (linhas de cada importacao)
-- =============================================================
CREATE TABLE IF NOT EXISTS `ImportacaoLinha` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `ImportacaoArquivoId` bigint NOT NULL,
    `HashLinha` varchar(64) NOT NULL,
    `HashChaveNegocio` varchar(64) NOT NULL,
    `SeqLancamento` varchar(60) NULL,
    `Referencia` varchar(120) NOT NULL,
    `ContaContabil` varchar(30) NOT NULL,
    `ContaContrapartida` varchar(30) NOT NULL,
    `DataLancamento` datetime(6) NOT NULL,
    `DataVencimento` datetime(6) NOT NULL,
    `DataDocumento` datetime(6) NOT NULL,
    `Valor` decimal(18,2) NOT NULL,
    `ValorCredito` decimal(18,2) NULL,
    `ValorDebito` decimal(18,2) NULL,
    `HistoricoLinha` varchar(200) NOT NULL,
    `Filial` varchar(20) NULL,
    `CompanyDb` varchar(50) NOT NULL,
    `Status` varchar(40) NOT NULL,
    `MensagemValidacao` varchar(400) NULL,
    `MensagemRetornoSap` varchar(400) NULL,
    `DocEntrySap` int NULL,
    `JsonOrigem` json NULL,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_ImportacaoLinha_CompanyDb_HashChaveNegocio` (`CompanyDb`, `HashChaveNegocio`),
    INDEX `IX_ImportacaoLinha_ImportacaoArquivoId` (`ImportacaoArquivoId`),
    CONSTRAINT `FK_ImportacaoLinha_ImportacaoArquivo_ImportacaoArquivoId` FOREIGN KEY (`ImportacaoArquivoId`) REFERENCES `ImportacaoArquivo` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 11. LogSistema (logs gerais da aplicacao)
-- =============================================================
CREATE TABLE IF NOT EXISTS `LogSistema` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `DataHora` datetime(6) NOT NULL,
    `Nivel` varchar(20) NOT NULL,
    `Origem` varchar(80) NOT NULL,
    `UsuarioId` bigint NULL,
    `CompanyDb` varchar(50) NULL,
    `CorrelationId` varchar(60) NULL,
    `Mensagem` varchar(400) NOT NULL,
    `Detalhes` longtext NULL,
    PRIMARY KEY (`Id`),
    INDEX `IX_LogSistema_DataHora` (`DataHora`),
    INDEX `IX_LogSistema_UsuarioId` (`UsuarioId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 12. MapeamentoFilialSap (de-para filial arquivo -> BPLId SAP)
-- =============================================================
CREATE TABLE IF NOT EXISTS `MapeamentoFilialSap` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `CompanyDb` varchar(50) NOT NULL,
    `CodigoFilialArquivo` varchar(20) NOT NULL,
    `BPLId` int NOT NULL,
    `NomeFilial` varchar(120) NOT NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_MapeamentoFilialSap_CompanyDb_CodigoFilialArquivo` (`CompanyDb`, `CodigoFilialArquivo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 13. ConfiguracaoLayout (layouts de importacao cadastrados)
-- =============================================================
CREATE TABLE IF NOT EXISTS `ConfiguracaoLayout` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `NomeLayout` varchar(80) NOT NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    `Descricao` varchar(200) NULL,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_ConfiguracaoLayout_NomeLayout` (`NomeLayout`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 14. ConfiguracaoLayoutCampo (campos de cada layout)
-- =============================================================
CREATE TABLE IF NOT EXISTS `ConfiguracaoLayoutCampo` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `LayoutId` bigint NOT NULL,
    `NomeColunaOrigem` varchar(120) NOT NULL,
    `NomeCampoInterno` varchar(120) NOT NULL,
    `Obrigatorio` tinyint(1) NOT NULL DEFAULT 0,
    `TipoDado` varchar(40) NOT NULL,
    `Ordem` int NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ConfiguracaoLayoutCampo_ConfiguracaoLayout_LayoutId` FOREIGN KEY (`LayoutId`) REFERENCES `ConfiguracaoLayout` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- 15. Regras (parametros e configuracoes do sistema)
-- =============================================================
CREATE TABLE IF NOT EXISTS `Regras` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Chave` varchar(120) NOT NULL,
    `Valor` varchar(400) NOT NULL,
    `EscopoCompanyDb` varchar(50) NULL,
    `Ativo` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_Regras_Chave` (`Chave`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =============================================================
-- Registrar esta migration como aplicada
-- =============================================================
INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260407000000_InitialCreate', '9.0.0');

SELECT 'Script de inicializacao concluido com sucesso!' AS Resultado;
