-- =====================================================================================
--  LIMPEZA DEFENSIVA DO BANCO portalfiscalhub
-- =====================================================================================
--  CONTEXTO: enquanto o app FinancialImport apontava, por engano, para o banco
--  `portalfiscalhub` (variavel de ambiente de maquina do PortalFiscalHub), o seeder
--  do FinancialImport inseriu dados nele:
--    - ate 10 permissoes (importar_lancamentos, baixar_notas_saida, ...);
--    - possivelmente os perfis "Administrador" e "Operador" + vinculos.
--
--  Este script remove SOMENTE as linhas IDENTICAS ao que o seeder grava — casando
--  Codigo+Nome (permissoes) e Nome+Descricao (perfis) EXATOS. Assim, se o
--  portalfiscalhub tiver uma permissao/perfil proprio com o mesmo Codigo/Nome mas
--  conteudo diferente, ele NAO e tocado.
--
--  SEGURANCA:
--    * roda dentro de uma transacao (START TRANSACTION);
--    * mostra o que vai remover ANTES e o resultado DEPOIS;
--    * NAO faz COMMIT sozinho — voce revisa e digita COMMIT; (ou ROLLBACK;).
--
--  COMO RODAR (MySQL Workbench, conectado ao servidor SAP-ALBIER-INT2):
--    1) Faca um backup do portalfiscalhub, se possivel.
--    2) Abra este arquivo e execute TUDO ATE a linha marcada "-- >>> PARE AQUI".
--    3) Confira os grids "ANTES" e "DEPOIS".
--    4) Se estiver correto, execute:  COMMIT;
--       Se algo parecer errado, execute:  ROLLBACK;
-- =====================================================================================

USE portalfiscalhub;

START TRANSACTION;

-- -------------------------------------------------------------------------------------
-- (ANTES) O que sera removido
-- -------------------------------------------------------------------------------------

-- Permissoes do FinancialImport presentes no portalfiscalhub (casando Codigo+Nome exatos)
SELECT 'PERMISSOES a remover' AS info, Id, Codigo, Nome, Grupo
FROM Permissoes
WHERE (Codigo, Nome) IN (
    ('importar_lancamentos',  'Importar Lancamentos'),
    ('baixar_notas_saida',    'Baixar Notas de Saida'),
    ('visualizar_historico',  'Visualizar Historico'),
    ('reprocessar_importacao','Reprocessar Importacao'),
    ('trocar_company',        'Trocar Company'),
    ('visualizar_filiais',    'Visualizar Filiais'),
    ('gerenciar_usuarios',    'Gerenciar Usuarios'),
    ('gerenciar_perfis',      'Gerenciar Perfis'),
    ('gerenciar_permissoes',  'Gerenciar Permissoes'),
    ('visualizar_logs',       'Visualizar Logs')
);

-- Perfis do FinancialImport presentes no portalfiscalhub (casando Nome+Descricao exatos)
SELECT 'PERFIS a remover' AS info, Id, Nome, Descricao
FROM Perfis
WHERE (Nome, Descricao) IN (
    ('Administrador', 'Perfil com acesso total ao sistema'),
    ('Operador',      'Perfil operacional para importacoes')
);

-- Vinculos (PerfilPermissao) que apontam para esses perfis/permissoes
SELECT 'VINCULOS a remover' AS info, pp.Id, pp.PerfilId, pp.PermissaoId
FROM PerfilPermissao pp
WHERE pp.PermissaoId IN (
        SELECT Id FROM Permissoes WHERE (Codigo, Nome) IN (
            ('importar_lancamentos',  'Importar Lancamentos'),
            ('baixar_notas_saida',    'Baixar Notas de Saida'),
            ('visualizar_historico',  'Visualizar Historico'),
            ('reprocessar_importacao','Reprocessar Importacao'),
            ('trocar_company',        'Trocar Company'),
            ('visualizar_filiais',    'Visualizar Filiais'),
            ('gerenciar_usuarios',    'Gerenciar Usuarios'),
            ('gerenciar_perfis',      'Gerenciar Perfis'),
            ('gerenciar_permissoes',  'Gerenciar Permissoes'),
            ('visualizar_logs',       'Visualizar Logs')
        )
      )
   OR pp.PerfilId IN (
        SELECT Id FROM Perfis WHERE (Nome, Descricao) IN (
            ('Administrador', 'Perfil com acesso total ao sistema'),
            ('Operador',      'Perfil operacional para importacoes')
        )
      );

-- -------------------------------------------------------------------------------------
-- (DELETE) Remocao — vinculos primeiro, depois permissoes, depois perfis
-- -------------------------------------------------------------------------------------

DELETE FROM PerfilPermissao
WHERE PermissaoId IN (
        SELECT Id FROM Permissoes WHERE (Codigo, Nome) IN (
            ('importar_lancamentos',  'Importar Lancamentos'),
            ('baixar_notas_saida',    'Baixar Notas de Saida'),
            ('visualizar_historico',  'Visualizar Historico'),
            ('reprocessar_importacao','Reprocessar Importacao'),
            ('trocar_company',        'Trocar Company'),
            ('visualizar_filiais',    'Visualizar Filiais'),
            ('gerenciar_usuarios',    'Gerenciar Usuarios'),
            ('gerenciar_perfis',      'Gerenciar Perfis'),
            ('gerenciar_permissoes',  'Gerenciar Permissoes'),
            ('visualizar_logs',       'Visualizar Logs')
        )
      )
   OR PerfilId IN (
        SELECT Id FROM Perfis WHERE (Nome, Descricao) IN (
            ('Administrador', 'Perfil com acesso total ao sistema'),
            ('Operador',      'Perfil operacional para importacoes')
        )
      );

DELETE FROM Permissoes
WHERE (Codigo, Nome) IN (
    ('importar_lancamentos',  'Importar Lancamentos'),
    ('baixar_notas_saida',    'Baixar Notas de Saida'),
    ('visualizar_historico',  'Visualizar Historico'),
    ('reprocessar_importacao','Reprocessar Importacao'),
    ('trocar_company',        'Trocar Company'),
    ('visualizar_filiais',    'Visualizar Filiais'),
    ('gerenciar_usuarios',    'Gerenciar Usuarios'),
    ('gerenciar_perfis',      'Gerenciar Perfis'),
    ('gerenciar_permissoes',  'Gerenciar Permissoes'),
    ('visualizar_logs',       'Visualizar Logs')
);

DELETE FROM Perfis
WHERE (Nome, Descricao) IN (
    ('Administrador', 'Perfil com acesso total ao sistema'),
    ('Operador',      'Perfil operacional para importacoes')
);

-- -------------------------------------------------------------------------------------
-- (DEPOIS) Confirmacao — estas tres consultas devem voltar VAZIAS
-- -------------------------------------------------------------------------------------

SELECT 'PERMISSOES restantes (deve ser 0 linhas)' AS info, Id, Codigo, Nome
FROM Permissoes
WHERE (Codigo, Nome) IN (
    ('importar_lancamentos',  'Importar Lancamentos'),
    ('baixar_notas_saida',    'Baixar Notas de Saida'),
    ('visualizar_historico',  'Visualizar Historico'),
    ('reprocessar_importacao','Reprocessar Importacao'),
    ('trocar_company',        'Trocar Company'),
    ('visualizar_filiais',    'Visualizar Filiais'),
    ('gerenciar_usuarios',    'Gerenciar Usuarios'),
    ('gerenciar_perfis',      'Gerenciar Perfis'),
    ('gerenciar_permissoes',  'Gerenciar Permissoes'),
    ('visualizar_logs',       'Visualizar Logs')
);

SELECT 'PERFIS restantes (deve ser 0 linhas)' AS info, Id, Nome, Descricao
FROM Perfis
WHERE (Nome, Descricao) IN (
    ('Administrador', 'Perfil com acesso total ao sistema'),
    ('Operador',      'Perfil operacional para importacoes')
);

-- >>> PARE AQUI. Revise os grids acima.
--     Se estiver tudo certo:   COMMIT;
--     Se algo parecer errado:  ROLLBACK;

-- =====================================================================================
--  OPCIONAL (NAO execute sem alinhar com o time do PortalFiscalHub):
--  O self-heal do FinancialImport pode ter ADICIONADO colunas em `Permissoes`
--  (Nome, Ativo, Grupo) e RELAXADO para NULL colunas que eram NOT NULL
--  (ex.: Modulo, Ordem, Descricao). Isso e, em geral, INOFENSIVO deixar como esta —
--  colunas extras anulaveis nao atrapalham o PortalFiscalHub.
--
--  Reverter NOT NULL so e seguro se NAO houver linhas com NULL nessas colunas.
--  Avalie caso a caso, por exemplo:
--     -- ALTER TABLE Permissoes MODIFY COLUMN Descricao varchar(200) NOT NULL;
--     -- ALTER TABLE Permissoes MODIFY COLUMN Modulo    <tipo original> NOT NULL;
--     -- ALTER TABLE Permissoes DROP COLUMN Grupo;   -- so se o PortalFiscalHub nao usar
--  Recomendacao: deixar como esta, a menos que o time do PortalFiscalHub peca.
-- =====================================================================================
