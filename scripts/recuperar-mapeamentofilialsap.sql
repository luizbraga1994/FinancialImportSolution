-- =============================================================================
-- Recuperação — recria a tabela BASE `MapeamentoFilialSap`
-- =============================================================================
-- QUANDO USAR
--   Uma versão anterior (com bug) do `reconciliar-baixa-migrations.sql` dropava
--   `mapeamentofilialsap` por engano (confundindo-a com a tabela do módulo de
--   Baixa `MapeamentoBandeiraCartao`). Se você rodou aquele script, a tabela
--   BASE `MapeamentoFilialSap` — criada pela migration InitialCreate e mapeada
--   no AppDbContext — foi apagada, deixando o schema inconsistente com o
--   `__EFMigrationsHistory` (que continua marcando a InitialCreate como aplicada).
--
-- O QUE ESTE SCRIPT FAZ (idempotente)
--   Recria `MapeamentoFilialSap` exatamente como a InitialCreate a define,
--   restaurando a consistência entre schema e histórico. Não mexe em dados de
--   outras tabelas nem no histórico de migrations.
--
-- OBS: os dados que existiam nessa tabela (de-para de filiais) foram perdidos no
--   DROP e precisarão ser recadastrados. A estrutura, porém, volta ao correto.
--
-- COMO RODAR
--   Selecione o schema `financialimport` e execute tudo. Depois suba a aplicação
--   normalmente — o EF aplicará as 4 migrations da Baixa que ainda faltam.
-- =============================================================================

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
