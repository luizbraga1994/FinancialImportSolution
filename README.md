# FinancialImportSolution

Sistema de importação de lançamentos contábeis para o **SAP Business One**, construído em **.NET 10** com arquitetura em camadas limpa. Oferece interface Web (MVC/Razor) e API REST, integrando-se ao SAP via **Service Layer** e descobrindo empresas disponíveis diretamente no **SAP HANA**.

---

## Sumário

- [Visão Geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Estrutura do Repositório](#estrutura-do-repositório)
- [Stack Tecnológica](#stack-tecnológica)
- [Pré-requisitos](#pré-requisitos)
- [Configuração Completa](#configuração-completa)
- [Banco de Dados](#banco-de-dados)
- [Como Executar](#como-executar)
- [Fluxo de Importação](#fluxo-de-importação)
- [Layouts de Importação](#layouts-de-importação)
- [Endpoints da API](#endpoints-da-api)
- [Modelo de Permissões](#modelo-de-permissões)
- [Autenticação e Autorização](#autenticação-e-autorização)
- [Integrações SAP](#integrações-sap)
- [Mensageria e Padrão Outbox](#mensageria-e-padrão-outbox)
- [Logs, Auditoria e Rastreabilidade](#logs-auditoria-e-rastreabilidade)
- [Testes](#testes)
- [Deployment e Produção](#deployment-e-produção)
- [Segurança](#segurança)
- [Guia de Desenvolvimento](#guia-de-desenvolvimento)

---

## Visão Geral

O **FinancialImportSolution** automatiza o processo de importação de lançamentos contábeis em massa para o SAP Business One. Resolve o problema de operadores que precisam lançar centenas de entradas contábeis manualmente no SAP, substituindo esse processo por uma interface simples de upload de arquivo.

### Fluxo Principal

```
Usuário → Upload de arquivo (XLSX/CSV/TXT)
       → Detecção automática de layout
       → Validação de cada linha (FluentValidation)
       → Deduplicação por hash (arquivo + linha de negócio)
       → Pré-visualização agrupada por conta
       → Confirmação → POST /JournalEntries no SAP Service Layer
       → Histórico completo com status e erros do SAP no MySQL
```

### Principais Recursos

| Recurso | Descrição |
|---------|-----------|
| Detecção automática de layout | Dois layouts nativos reconhecidos por cabeçalho |
| Deduplicação | Hash de arquivo + hash de chave de negócio por linha |
| Controle de acesso granular | RBAC por perfil, permissão e empresa SAP |
| Sessões SAP por usuário | Sessão isolada por usuário/empresa com keep-alive |
| Template XLSX para download | Layout 2 pronto para preenchimento |
| Autenticação dupla | JWT na API e Cookies na Web |
| Correlation ID fim a fim | Rastreabilidade completa de cada requisição |
| Mensageria opcional | RabbitMQ (comandos) + Kafka (eventos) com Outbox |
| Idempotência no SAP | Dispatch único garantido por hash de grupo |
| Logs estruturados | Serilog + tabela `LogSistema` com índices otimizados |

---

## Arquitetura

Arquitetura em camadas (Clean Architecture) com duas "fachadas" (API REST e Web MVC) compartilhando as mesmas camadas de Aplicação, Domínio e Infraestrutura.

```
┌─────────────────────────────────────────────────────────────────┐
│                       PRESENTATION LAYER                        │
│  ┌───────────────────────────┐  ┌───────────────────────────┐   │
│  │   FinancialImport.Web     │  │   FinancialImport.Api     │   │
│  │  (MVC + Razor Views)      │  │  (REST + Swagger/OpenAPI) │   │
│  │  Cookie Auth, 11 ctrlrs   │  │  JWT Bearer, 9 ctrlrs     │   │
│  └─────────────┬─────────────┘  └─────────────┬─────────────┘   │
└────────────────┼────────────────────────────────┼───────────────┘
                 └──────────────┬─────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────┐
│                      APPLICATION LAYER                        │
│  Serviços (ImportService, AuthService, UserService...)        │
│  Parsers (Layout1Parser, Layout2Parser)                       │
│  Validators (FluentValidation por layout)                     │
│  DTOs / Commands / Queries                                    │
│  Interfaces de repositórios e integrações                     │
└───────────────────────────────┬───────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────┐
│                        DOMAIN LAYER                           │
│  Entidades (User, ImportFile, ImportLine, OutboxMessage...)   │
│  Enums (ImportStatus, LineStatus, MessageStatus...)           │
│  Constantes (PermissionCodes, LayoutNames...)                 │
└───────────────────────────────┬───────────────────────────────┘
                                │
          ┌─────────────────────┼──────────────────────┐
          │                     │                      │
┌─────────▼──────────┐ ┌────────▼────────┐ ┌──────────▼────────┐
│   Infrastructure   │ │ Integration.Sap │ │ Integration.Hana  │
│  EF Core + MySQL   │ │  SAP Service    │ │  SAP HANA ADO.NET │
│  JWT / Hashing     │ │  Layer (REST)   │ │  Company Discovery│
│  RabbitMQ / Kafka  │ │  Session Mgmt   │ │  SBOCOMMON.SRGC   │
│  Outbox/Inbox      │ └─────────────────┘ └───────────────────┘
│  Serilog Sinks     │
└────────────────────┘
             │
┌────────────▼──────────────────┐
│       FinancialImport.Shared  │
│  IClock, abstrações cruzadas  │
└───────────────────────────────┘
```

### Padrões Aplicados

| Padrão | Uso |
|--------|-----|
| Clean Architecture | Dependências sempre apontam para o Domain |
| Repository Pattern | `IImportRepository` isola acesso a dados |
| Strategy Pattern | `ILayoutImportParser` permite parsers plugáveis |
| Dependency Injection | Métodos de extensão por camada (`AddApplication()`, `AddInfrastructure()`) |
| Context Accessors | `IUserContext`, `ICompanyContext` via `HttpContext` (AsyncLocal) |
| Transactional Outbox | `MensagensOutbox` garante entrega ao broker sem 2-phase commit |
| Inbox Idempotency | `MensagensInbox` deduplica mensagens reentregues |
| Correlation ID | `CorrelationIdMiddleware` propaga ID em logs, headers e mensagens |
| Idempotent SAP Dispatch | Unique index `(CompanyDb, GroupKeyHash)` em `LancamentoSapDispatch` |
| Deduplication by Hash | SHA-256 do conteúdo do arquivo e dos campos de negócio por linha |

> Para a visão completa da evolução arquitetural (mensageria, outbox, idempotência, observabilidade), leia **[docs/architecture-evolution.md](docs/architecture-evolution.md)**.

---

## Estrutura do Repositório

```
FinancialImportSolution/
├── src/
│   ├── FinancialImport.Domain/              # Entidades, enums, constantes (~25 arquivos)
│   ├── FinancialImport.Application/         # Serviços, parsers, validators, DTOs (~45 arquivos)
│   ├── FinancialImport.Infrastructure/      # EF Core, MySQL, JWT, hashing, messaging (~40 arquivos)
│   ├── FinancialImport.Integration.Sap/     # Cliente SAP Service Layer (~8 arquivos)
│   ├── FinancialImport.Integration.Hana/    # Descoberta de empresas via HANA (~8 arquivos)
│   ├── FinancialImport.Shared/              # Abstrações compartilhadas, IClock (~16 arquivos)
│   ├── FinancialImport.Api/                 # REST API (9 controllers) com Swagger
│   └── FinancialImport.Web/                 # MVC (11 controllers) com Razor Views
├── tests/
│   └── FinancialImport.Tests/               # Testes unitários xUnit 2.9
├── scripts/
│   └── 01_InitialCreate.sql                 # Schema MySQL para criação manual
├── docs/
│   └── architecture-evolution.md            # Evolução da arquitetura e decisões técnicas
├── Directory.Build.props                     # TargetFramework net10.0, Nullable enable
└── FinancialImportSolution.slnx             # Arquivo de solução (.slnx)
```

---

## Stack Tecnológica

| Camada / Tópico | Tecnologia |
|-----------------|------------|
| Runtime | .NET 10.0 |
| Linguagem | C# (LangVersion latest, `Nullable enable`) |
| Web / API | ASP.NET Core MVC + Razor Views, Swashbuckle (Swagger/OpenAPI) |
| ORM | Entity Framework Core 9.0 |
| Banco principal | MySQL 8.0+ (Pomelo.EntityFrameworkCore.MySql 9.0) |
| Banco externo | SAP HANA (via `Sap.Data.Hana` ADO.NET, carregado dinamicamente) |
| ERP integrado | SAP Business One Service Layer (REST / JSON) |
| Autenticação (API) | JWT Bearer HS256 |
| Autenticação (Web) | Cookie Authentication (sliding 12h) |
| Validação | FluentValidation 11.10 |
| Leitura de XLSX | ClosedXML 0.104 |
| Logging | Serilog (Console + File rolling diário) |
| Mensageria (comandos) | RabbitMQ 6.8.1 (opcional, `Enabled: false` por padrão) |
| Mensageria (eventos) | Confluent.Kafka 2.6.1 (opcional, `Enabled: false` por padrão) |
| Testes | xUnit 2.9.2 + FluentAssertions 6.12.1 |

---

## Pré-requisitos

- **.NET SDK 10.0** (`dotnet --version` deve retornar `10.x.x`)
- **MySQL 8.0+** acessível pela aplicação (porta padrão 3306)
- Acesso ao **SAP Business One Service Layer** (URL, usuário `manager`, senha)
- Acesso ao **SAP HANA** para descoberta de empresas + driver `Sap.Data.Hana` instalado
  - Windows: `C:\Program Files\sap\hdbclient\dotnetcore\v8.0\Sap.Data.Hana.Net.v8.0.dll`
  - Linux: caminho personalizado via `HanaDbConnection:ProviderAssemblyPath`
- (Opcional) **RabbitMQ 3.12+** para processamento assíncrono
- (Opcional) **Apache Kafka 3.x** para eventos de domínio

---

## Configuração Completa

As configurações ficam em `appsettings.json` e `appsettings.Development.json` dentro de `src/FinancialImport.Api/` e `src/FinancialImport.Web/`.

> **IMPORTANTE**: Nunca versione senhas reais. Use **User Secrets** em desenvolvimento e **variáveis de ambiente** em produção.

### Connection String (MySQL)

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=FinancialImport;User=root;Password=******;Port=3306;CharSet=utf8mb4;"
}
```

### JWT (API REST)

```json
"Jwt": {
  "SecretKey": "chave-com-no-minimo-32-caracteres-aqui",
  "Issuer": "FinancialImport",
  "Audience": "FinancialImportClients",
  "ExpirationMinutes": 480,
  "RefreshExpirationMinutes": 1440
}
```

### SAP Service Layer

```json
"SapServiceLayer": {
  "BaseUrl": "https://seu-sap-sl:50000/",
  "UserName": "manager",
  "Password": "******",
  "Language": 29,
  "IgnoreSslErrors": true,
  "TimeoutSeconds": 180,
  "MaxRetryAttempts": 3,
  "SessionTimeoutMinutes": 25
}
```

> Em produção, defina `IgnoreSslErrors: false` e use um certificado válido.

### SAP HANA (Descoberta de Empresas)

```json
"HanaDbConnection": {
  "Server": "seu-hana-server",
  "Port": 30015,
  "Database": "SBOCOMMON",
  "UserID": "SYSTEM",
  "Password": "******",
  "MaxPoolSize": 100,
  "MinPoolSize": 10,
  "ConnectionTimeout": 60,
  "CommandTimeout": 300,
  "ProviderAssemblyPath": "C:\\Program Files\\sap\\hdbclient\\dotnetcore\\v8.0\\Sap.Data.Hana.Net.v8.0.dll"
}
```

### Importação e Parsing

```json
"LayoutParsing": {
  "DefaultTipoLancLayout1": "D"
},
"Imports": {
  "MaxFileSizeBytes": 10485760,
  "AllowedExtensions": [".csv", ".txt", ".xlsx"],
  "Processing": {
    "UseAsyncConfirmation": false,
    "MaxRetryAttempts": 3
  },
  "Deduplication": {
    "KeyFields": ["SeqLancamento", "CompanyDb", "Reference", "DebitAccount", "CreditAccount", "PostingDate", "DueDate", "Amount", "Memo", "BranchCode"]
  },
  "Truncation": {
    "MemoMaxLength": 254,
    "ReferenceMaxLength": 200,
    "LineMemoMaxLength": 254
  }
}
```

### CORS (API)

```json
"Cors": {
  "AllowedOrigins": [
    "https://localhost:7000",
    "http://localhost:5000"
  ]
}
```

### RabbitMQ (Opcional)

```json
"Messaging": {
  "RabbitMq": {
    "Enabled": false,
    "Host": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "Username": "guest",
    "Password": "guest",
    "Exchange": "financialimport.exchange",
    "DeadLetterExchange": "financialimport.dlx",
    "Channels": {
      "ImportProcess": "import.process.command",
      "ImportReprocess": "import.reprocess.command",
      "SapDispatch": "sap.dispatch.command",
      "AuditWrite": "audit.write.command"
    }
  }
}
```

### Kafka (Opcional)

```json
"Messaging": {
  "Kafka": {
    "Enabled": false,
    "BootstrapServers": "localhost:9092",
    "Topics": {
      "ImportEvents": "import.events",
      "SapEvents": "sap.events",
      "SecurityEvents": "security.events",
      "AuditEvents": "audit.events"
    }
  }
}
```

### Admin Seed (Primeiro Acesso)

```json
"AdminSeed": {
  "Login": "admin",
  "Password": "Admin@123",
  "Email": "admin@financialimport.local"
}
```

> Troque a senha do admin imediatamente após o primeiro login em produção.

---

## Banco de Dados

O schema é criado automaticamente na inicialização via **EF Core Migrations** (`context.Database.Migrate()`). O `DatabaseSeeder` cria o usuário admin, perfis padrão e permissões básicas.

Também existe o script `scripts/01_InitialCreate.sql` para criação manual em ambientes onde migrations automáticas não são desejadas (ex: DBA controla o schema).

### Tabelas do Schema

#### Segurança e Controle de Acesso

| Tabela | Descrição |
|--------|-----------|
| `Usuarios` | Contas de usuário com hash/salt da senha, flag `AdminGlobal` |
| `Perfis` | Papéis (roles) de acesso |
| `Permissoes` | Permissões granulares por código único |
| `UsuarioPerfil` | N:N usuário ↔ perfil |
| `PerfilPermissao` | N:N perfil ↔ permissão |
| `UsuarioEmpresaPermitida` | Controla quais databases SAP o usuário pode acessar |
| `AuditoriaLogin` | Log de tentativas de login (IP, user agent, sucesso/falha) |
| `SessaoEmpresaUsuario` | Sessões SAP ativas por usuário/empresa com data de expiração |

#### Importação

| Tabela | Descrição |
|--------|-----------|
| `ImportacaoArquivo` | Cabeçalho da importação: status, layout detectado, hash, contagens |
| `ImportacaoLinha` | Linhas individuais: conta, data, valor, status, resposta do SAP, hash |
| `MapeamentoFilialSap` | Mapeia código de filial do arquivo → `BPLId` do SAP por empresa |
| `LancamentoSapDispatch` | Rastreamento idempotente de despachos ao SAP |

#### Configuração

| Tabela | Descrição |
|--------|-----------|
| `ConfiguracaoLayout` | Definições de layout de importação |
| `ConfiguracaoLayoutCampo` | Campos de cada layout com metadados |
| `ConfiguracaoSistema` | Configurações chave-valor gerenciadas via DB (com UI administrativa) |
| `Regras` | Parâmetros/regras de negócio chave-valor |
| `LogSistema` | Log de auditoria estruturado com correlation ID |

#### Mensageria (Padrão Outbox)

| Tabela | Descrição |
|--------|-----------|
| `MensagensOutbox` | Mensagens pendentes de publicação no broker (transacional) |
| `MensagensInbox` | Deduplicação de mensagens consumidas (idempotência de consumidor) |

### Índices de Deduplicação

```sql
-- Mesmo arquivo não é importado duas vezes para a mesma empresa
UNIQUE INDEX IX_ImportacaoArquivo (CompanyDb, HashArquivo)

-- Mesma linha de negócio não é enviada duas vezes ao SAP
UNIQUE INDEX IX_ImportacaoLinha (CompanyDb, HashChaveNegocio)

-- Mesmo grupo não é despachado duas vezes ao SAP
UNIQUE INDEX IX_LancamentoSapDispatch (CompanyDb, GroupKeyHash)
```

---

## Como Executar

### 1. Clonar e Restaurar

```bash
git clone https://github.com/luizbraga1994/financialimportsolution.git
cd FinancialImportSolution
dotnet restore FinancialImportSolution.slnx
```

### 2. Configurar Credenciais (User Secrets — desenvolvimento)

```bash
# Para a API
cd src/FinancialImport.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=FinancialImport;User=root;Password=SUA_SENHA;Port=3306;"
dotnet user-secrets set "Jwt:SecretKey" "sua-chave-secreta-com-32-chars-minimo"
dotnet user-secrets set "SapServiceLayer:Password" "sua-senha-sap"
dotnet user-secrets set "HanaDbConnection:Password" "sua-senha-hana"

# Para a Web (repetir os mesmos segredos)
cd ../FinancialImport.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."
```

### 3. Compilar

```bash
dotnet build FinancialImportSolution.slnx
```

### 4. Rodar a Aplicação Web (MVC)

```bash
dotnet run --project src/FinancialImport.Web/FinancialImport.Web.csproj
```

Acesse `https://localhost:7000` e faça login com `admin` / `Admin@123`.

### 5. Rodar a API REST

```bash
dotnet run --project src/FinancialImport.Api/FinancialImport.Api.csproj
```

Swagger disponível em `https://localhost:5000/swagger`. Obtenha o token via `POST /api/v1/auth/login` e envie `Authorization: Bearer <token>` nas demais chamadas.

### 6. Primeiro Acesso

Na primeira execução:
1. EF Core aplica todas as migrations automaticamente
2. `DatabaseSeeder` cria o usuário `admin`, permissões base e perfis padrão
3. Cache de configurações é carregado do banco
4. Troque a senha do admin imediatamente em produção

### 7. Executar Testes

```bash
dotnet test FinancialImportSolution.slnx
```

---

## Fluxo de Importação

### Fase de Preview (Upload + Validação)

```
1. Usuário faz upload do arquivo (XLSX/CSV/TXT, máx. 10 MB)
2. Sistema calcula SHA-256 do arquivo
   → Se já existe (CompanyDb, HashArquivo), retorna erro de duplicata
3. Detecta layout inspecionando cabeçalhos da primeira linha
4. Parser do layout correto processa cada linha
5. FluentValidation valida cada linha individualmente
6. BusinessKeyBuilder constrói hash dos campos de negócio configurados
   → Linhas com hash já existente no banco são marcadas como duplicatas
7. Linhas agrupadas por conta formam a pré-visualização
8. Persiste ImportacaoArquivo (status: Pending) + ImportacaoLinha no MySQL
9. Retorna preview agrupado ao usuário
```

### Fase de Confirmação

#### Modo Síncrono (`UseAsyncConfirmation: false`, padrão)

```
1. Usuário confirma o preview
2. Serviço processa imediatamente no thread da requisição
3. Para cada grupo válido:
   a. Verifica LancamentoSapDispatch (idempotência)
   b. Se novo: POST /JournalEntries no SAP Service Layer
   c. Registra DocEntry (sucesso) ou mensagem de erro (falha)
4. Atualiza status de ImportacaoArquivo e ImportacaoLinha
5. Retorna resultado completo
```

#### Modo Assíncrono (`UseAsyncConfirmation: true`)

```
1. Usuário confirma o preview
2. Serviço insere OutboxMessage na mesma transação EF
3. Resposta imediata ao usuário (status: Processing)
4. OutboxDispatcher (background service, poll 5s):
   a. Marca mensagem como InFlight
   b. Publica no RabbitMQ/Kafka
5. ImportProcessWorker consome:
   a. Verifica Inbox (idempotência de consumidor)
   b. Processa e despacha ao SAP
   c. Marca mensagem como Dispatched
6. Em caso de falha: exponential backoff + DLQ após max tentativas
```

### Deduplicação em 3 Camadas

| Nível | Mecanismo | Tabela |
|-------|-----------|--------|
| Arquivo | SHA-256 do conteúdo binário | `ImportacaoArquivo` |
| Linha de negócio | SHA-256 dos campos configurados | `ImportacaoLinha` |
| Despacho SAP | SHA-256 do grupo de lançamentos | `LancamentoSapDispatch` |

---

## Layouts de Importação

Dois parsers nativos implementam `ILayoutImportParser`. O layout correto é detectado automaticamente pelos cabeçalhos da primeira linha do arquivo.

### Layout 1 — Técnico

Identificado pelo cabeçalho `CODCONTACONTABIL`.

| Coluna | Obrigatório | Descrição |
|--------|-------------|-----------|
| `CODCONTACONTABIL` | Sim | Código da conta contábil |
| `DTLANCCONTABIL` | Sim | Data do lançamento (dd/MM/yyyy) |
| `TIPOLANC` | Não | D = Débito, C = Crédito (default configurável) |
| `REFERENCIA` | Não | Referência do lançamento |
| `VLR` | Sim | Valor do lançamento |
| `MEMO` | Não | Histórico/observação |
| `CODFILIAL` | Não | Código da filial (mapeado para BPLId) |

> Quando `TIPOLANC` não é informado, usa o valor de `LayoutParsing:DefaultTipoLancLayout1` (padrão: `"D"`).

### Layout 2 — Amigável (Recomendado para Usuários Finais)

Identificado pelo cabeçalho `Conta Contabil`.

| Coluna | Obrigatório | Descrição |
|--------|-------------|-----------|
| `Conta Contabil` | Sim | Código da conta contábil |
| `Data Lancamento` | Sim | Data (dd/MM/yyyy) |
| `Valor Credito` | Cond. | Valor a crédito (preencher OU débito) |
| `Valor Debito` | Cond. | Valor a débito (preencher OU crédito) |
| `Referencia` | Não | Referência do lançamento |
| `Memo` | Não | Histórico/observação |
| `Filial` | Não | Código da filial |

Um **template XLSX** pode ser baixado em `/Import/DownloadTemplate` (Web) ou gerado via `ImportTemplateBuilder`.

### Formatos de Arquivo Aceitos

| Formato | Extensão | Parser |
|---------|----------|--------|
| Excel | `.xlsx` | ClosedXML (primeiro sheet) |
| CSV | `.csv` | Delimitador vírgula, primeira linha = cabeçalho |
| Texto delimitado | `.txt` | Delimitador ponto-e-vírgula ou tab |

### Validações por Linha (FluentValidation)

- Conta contábil obrigatória e não vazia
- Data de lançamento válida e não futura (configurável)
- Valor numérico válido e maior que zero
- Layout 2: exatamente um dos valores (débito OU crédito) deve ser preenchido
- Truncamento automático: Memo (254 chars), Referência (200 chars)

---

## Endpoints da API

Todos os endpoints (exceto `/health` e `/api/v1/auth/login`) exigem `Authorization: Bearer <token>`.

### Autenticação — `/api/v1/auth`

| Método | Rota | Descrição | Auth |
|--------|------|-----------|------|
| `POST` | `/login` | Autentica usuário, retorna JWT + refresh token | Anônimo |
| `GET` | `/me` | Dados do usuário autenticado e permissões | JWT |

**Request de login:**
```json
{
  "login": "admin",
  "password": "Admin@123"
}
```

**Response de login:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "...",
    "expiresAt": "2025-01-01T16:00:00Z"
  }
}
```

### Empresas — `/api/v1/companies`

| Método | Rota | Descrição | Policy |
|--------|------|-----------|--------|
| `GET` | `/` | Lista empresas acessíveis (HANA + permissões do usuário) | JWT |
| `POST` | `/login` | Abre sessão SAP na empresa selecionada | `TrocarCompany` |
| `POST` | `/logout` | Encerra sessão SAP ativa | JWT |

### Importações — `/api/v1/imports`

| Método | Rota | Policy | Descrição |
|--------|------|--------|-----------|
| `POST` | `/preview` | `ImportarLancamentos` | Upload + parse + validação, retorna pré-visualização |
| `POST` | `/{importFileId}/confirm` | `ImportarLancamentos` | Processa e envia lançamentos ao SAP |
| `POST` | `/{importFileId}/reprocess` | `ReprocessarImportacao` | Reprocessa linhas com erro |
| `GET` | `/history` | `VisualizarHistorico` | Histórico paginado de importações |
| `GET` | `/{importFileId}/lines` | `VisualizarHistorico` | Linhas paginadas de uma importação |

**Preview Request** (`multipart/form-data`):
```
file: [arquivo binário]
```

**Preview Response:**
```json
{
  "success": true,
  "data": {
    "importFileId": "guid",
    "detectedLayout": "Layout2",
    "totalLines": 50,
    "validLines": 48,
    "duplicateLines": 2,
    "groups": [
      {
        "debitAccount": "1.1.1.01",
        "creditAccount": "2.1.1.01",
        "totalAmount": 1500.00,
        "lineCount": 3
      }
    ]
  }
}
```

### Administração

| Controller | Rota Base | Policy | Operações |
|------------|-----------|--------|-----------|
| `UsersApiController` | `/api/v1/users` | `GerenciarUsuarios` | CRUD usuários, trocar senha |
| `ProfilesApiController` | `/api/v1/profiles` | `GerenciarPerfis` | CRUD perfis, atribuir permissões |
| `PermissionsApiController` | `/api/v1/permissions` | `GerenciarPermissoes` | Listar/visualizar permissões |
| `BranchMappingApiController` | `/api/v1/branch-mappings` | `GerenciarUsuarios` | Mapeamento filial → BPLId por empresa |
| `LogsApiController` | `/api/v1/logs` | `VisualizarLogs` | Consulta logs e auditoria |
| `HealthApiController` | `/health` | Anônimo | Health check (EF DbContext) |

**Formato de resposta padrão:**
```json
{
  "success": true,
  "data": { ... },
  "message": null,
  "errors": []
}
```

**Paginação:**
```json
{
  "success": true,
  "data": {
    "items": [...],
    "totalCount": 150,
    "page": 1,
    "pageSize": 20,
    "totalPages": 8
  }
}
```

---

## Modelo de Permissões

A autorização é baseada em **códigos de permissão** (não em roles estáticas). Cada policy do ASP.NET Core é gerada dinamicamente a partir dos códigos:

```csharp
// Gerado para cada código em PermissionCodes.All
options.AddPolicy(code, policy =>
    policy.RequireAssertion(ctx =>
        ctx.User.HasClaim("global_admin", "true") ||
        ctx.User.HasClaim("permission", code)));
```

### Permissões Disponíveis

| Código | Descrição | Perfil Padrão |
|--------|-----------|---------------|
| `importar_lancamentos` | Importar lançamentos contábeis | Administrador, Operador |
| `visualizar_historico` | Visualizar histórico de importações | Administrador, Operador |
| `reprocessar_importacao` | Reprocessar importações com erro | Administrador, Operador |
| `trocar_company` | Trocar de empresa SAP | Administrador, Operador |
| `visualizar_filiais` | Visualizar filiais disponíveis | Administrador, Operador |
| `gerenciar_usuarios` | CRUD de usuários e empresas permitidas | Administrador |
| `gerenciar_perfis` | CRUD de perfis | Administrador |
| `gerenciar_permissoes` | Gerenciar permissões e atribuições | Administrador |
| `visualizar_logs` | Visualizar logs e auditoria do sistema | Administrador |

### Hierarquia de Acesso

```
Usuário
  ├── AdminGlobal = true  →  acesso irrestrito a tudo
  └── AdminGlobal = false
        ├── Perfis (N:N)
        │     └── Permissões (N:N)  →  define o que pode fazer
        └── Empresas Permitidas (N:N)  →  define onde pode operar
```

### Perfis Padrão

| Perfil | Permissões |
|--------|------------|
| **Administrador** | Todas as permissões, acesso a todas as empresas |
| **Operador** | importar, visualizar histórico, reprocessar, trocar empresa, visualizar filiais |

---

## Autenticação e Autorização

### API REST (JWT Bearer)

```
POST /api/v1/auth/login
  → Retorna accessToken (8h) + refreshToken (24h)
  → Claims: user_id, email, global_admin, permission (uma por permissão)

Headers das requisições autenticadas:
  Authorization: Bearer eyJhbGciOiJIUzI1NiJ9...
```

O `SecretKey` do JWT é carregado do banco (`ConfiguracaoSistema`) na inicialização via `DbConfigureJwtBearerOptions`, permitindo rotação sem redeploy.

### Web MVC (Cookie Authentication)

```
POST /Account/Login
  → Valida credenciais, cria claims identity
  → Seta cookie seguro HTTPOnly com sliding expiration (12h)

Logout:
  POST /Account/Logout
  → Invalida cookie + encerra sessão SAP ativa
```

### SAP Session Management

Cada usuário mantém uma sessão SAP isolada por empresa:

```
POST /api/v1/companies/login  (ou Web /Company/Login)
  → SapCompanySessionService faz POST /b1s/v1/Login no SAP
  → Armazena SessionId + RouteId em SapSessionStore (memória)
  → Persiste em SessaoEmpresaUsuario (MySQL) para recovery
  → JavaScript client-side envia keep-alive a cada 25min
  → Timeout: 30min sem atividade
```

---

## Integrações SAP

### SAP Service Layer (`FinancialImport.Integration.Sap`)

**`SapCompanySessionService`**
- `POST /b1s/v1/Login` com `CompanyDB`, usuário e senha
- Armazena `B1SESSION` e `ROUTEID` no `SapSessionStore`
- Gerencia logout (`POST /b1s/v1/Logout`)
- Suporte a `IgnoreSslErrors` para ambientes com certificados self-signed

**`SapJournalEntryService`**
- `POST /b1s/v1/JournalEntries` com headers `B1SESSION` e `ROUTEID`
- Payload mapeado a partir de `ImportLine` entities
- Trata `error.message.value` das respostas de erro do SAP
- Registra payload de request/response em `LogSistema`
- Retry automático com backoff configurável (`MaxRetryAttempts`)

Usa `IHttpClientFactory` com named client `SapServiceLayer`:
```csharp
services.AddHttpClient("SapServiceLayer", client => {
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
```

### SAP HANA (`FinancialImport.Integration.Hana`)

**`SapCompanyDiscoveryService`**
- Carrega `Sap.Data.Hana` dinamicamente via `DbProviderFactory` (evita dependência hard em DLL)
- Consulta `SBOCOMMON.SRGC` para listar empresas disponíveis
- Retorna: `dbName` (CompanyDB), `cmpName` (nome da empresa), status
- Cruzado com `UsuarioEmpresaPermitida` para filtrar por permissão do usuário

```sql
SELECT "CompanyDB", "CompanyName", "CompStat"
FROM "SBOCOMMON"."SRGC"
WHERE "CompStat" = 'A'
ORDER BY "CompanyName"
```

---

## Mensageria e Padrão Outbox

A mensageria é **completamente opcional** (desabilitada por padrão). Quando habilitada, garante entrega de mensagens sem 2-phase commit via padrão Transactional Outbox.

### Quando Usar Qual Broker

| Caso de Uso | Broker | Canal/Topic |
|-------------|--------|-------------|
| Processar importação (transacional) | RabbitMQ | `import.process.command` |
| Reprocessar com DLQ e backoff | RabbitMQ | `import.reprocess.command` |
| Despachar ao SAP (confiável) | RabbitMQ | `sap.dispatch.command` |
| Evento de validação de importação | Kafka | `import.events` |
| Evento de sucesso/falha no SAP | Kafka | `sap.events` |
| Evento de segurança (login negado) | Kafka | `security.events` |
| Evento de auditoria | Kafka | `audit.events` |

### Ciclo de Vida do Outbox

```
[Business Transaction]
  INSERT MensagensOutbox (status: Pending)
  INSERT/UPDATE dados de negócio
  COMMIT (atômico)

[OutboxDispatcher - background, poll 5s]
  SELECT ... WHERE Status = 'Pending' LIMIT 50
  UPDATE Status = 'InFlight'
  PUBLISH to RabbitMQ/Kafka
  UPDATE Status = 'Dispatched' (sucesso)
  UPDATE Status = 'Failed', RetryCount++ (falha)

[Após MaxRetryAttempts]
  UPDATE Status = 'DeadLettered'
  INSERT LogSistema (categoria: Error)
```

### Idempotência de Consumidor

```
[Consumer recebe mensagem]
  SELECT FROM MensagensInbox WHERE Consumer = ? AND MessageId = ?
  → Existe: ACK sem reprocessar (já processado)
  → Não existe:
      INSERT MensagensInbox
      [Processar mensagem]
      UPDATE ProcessedAt
```

---

## Logs, Auditoria e Rastreabilidade

### Serilog

Configurado em `Program.cs` com sinks:
- **Console** — todos os ambientes (formato compacto)
- **File** — rolling diário em `logs/log-.txt`
  - Desenvolvimento: retenção 7 dias, nível Debug
  - Produção: retenção 60 dias, nível Information

Enriquecimento automático: `MachineName`, `Environment`, `Application`, `CorrelationId`.

### Correlation ID (Fim a Fim)

Cada requisição HTTP recebe um `CorrelationId` único:

```
Request chega → CorrelationIdMiddleware
  → Lê X-Correlation-Id do header (se presente)
  → Ou gera novo GUID
  → Armazena em ICorrelationContextAccessor (AsyncLocal)
  → Propaga em:
       - Response header: X-Correlation-Id
       - Serilog LogContext: CorrelationId
       - Tabela LogSistema: CorrelationId
       - Headers de mensagens RabbitMQ/Kafka
       - Payload de request ao SAP
```

### Tabela LogSistema

| Coluna | Descrição |
|--------|-----------|
| `Categoria` | Técnico / Funcional / Auditoria / Integração / Segurança |
| `Nivel` | Information / Warning / Error / Critical |
| `Operacao` | Preview / Confirmar / Despachar / Processar / Login / etc. |
| `CorrelationId` | Rastreamento fim a fim |
| `UsuarioId` | Quem executou |
| `ImportacaoArquivoId` | Qual arquivo (quando aplicável) |
| `DuracaoMs` | Tempo de execução em milissegundos |
| `Mensagem` | Descrição estruturada |
| `Dados` | JSON com contexto adicional |
| `StackTrace` | Stack trace completo (somente em erros) |
| `Hostname` | Servidor que processou |

**Índices para consulta eficiente:**
- `IX_LogSistema_DataHora`
- `IX_LogSistema_UsuarioId`
- `IX_LogSistema_CorrelationId`
- `IX_LogSistema_Categoria_Nivel`
- `IX_LogSistema_ImportacaoArquivoId`

### AuditoriaLogin

Todas as tentativas de login são registradas:
- Timestamp, usuário, IP, User-Agent
- Sucesso ou falha + motivo da falha
- Útil para análise de segurança e suporte

---

## Testes

**Projeto:** `tests/FinancialImport.Tests` (xUnit 2.9.2 + FluentAssertions 6.12.1)

```bash
dotnet test FinancialImportSolution.slnx
dotnet test FinancialImportSolution.slnx --collect:"XPlat Code Coverage"
```

### Cobertura Planejada

| Componente | Tipo de Teste |
|------------|---------------|
| `Layout1Parser` / `Layout2Parser` | Unitário — cenários válidos, inválidos, edge cases |
| `ImportLineValidator` | Unitário — cada regra FluentValidation |
| `BusinessKeyBuilder` | Unitário — hash determinístico, campos configurados |
| `ImportService.PreviewAsync` | Unitário — mock repositório e parsers |
| `ImportService.ConfirmAsync` | Unitário — mock SAP client |
| `SapJournalEntryService` | Integração — `HttpClient` mockado |
| `OutboxDispatcher` | Unitário — mock broker e repositório |
| `DatabaseSeeder` | Integração — banco em memória |

---

## Deployment e Produção

### Variáveis de Ambiente (recomendado)

```bash
# Connection string
ConnectionStrings__DefaultConnection="Server=prod-mysql;Database=FinancialImport;..."

# JWT
Jwt__SecretKey="chave-super-secreta-producao-32chars"

# SAP
SapServiceLayer__BaseUrl="https://sap-prod:50000/"
SapServiceLayer__Password="senha-producao"
SapServiceLayer__IgnoreSslErrors="false"

# HANA
HanaDbConnection__Server="hana-prod"
HanaDbConnection__Password="senha-hana-producao"
```

### Health Check

```
GET /health
→ 200 OK: { "status": "Healthy", "checks": { "database": "Healthy" } }
→ 503: banco inacessível
```

### Migrations em Produção

Opção 1 — Automático (padrão):
```csharp
// Em Program.cs — aplica migrations no startup
context.Database.Migrate();
```

Opção 2 — Manual (recomendado para prod):
```bash
dotnet ef migrations script --project src/FinancialImport.Infrastructure \
  --startup-project src/FinancialImport.Api \
  --output migration.sql
# Revisar e executar migration.sql no DBA
```

### Considerações de Produção

| Item | Recomendação |
|------|-------------|
| SSL/TLS | Certificado válido para SAP Service Layer e aplicação web |
| `IgnoreSslErrors` | Sempre `false` em produção |
| Logs | Garantir `logs/` com permissão de escrita e rotação/retenção configurada |
| Forwarded Headers | Middleware ativo para proxy reverso (nginx/IIS/caddy) |
| CORS | Origens específicas, sem wildcard |
| Admin Seed | Trocar senha após primeiro deploy |
| Backups MySQL | Rotina de backup do schema `FinancialImport` |
| Connection Pool | Ajustar `MaxPoolSize` conforme carga esperada |

### Proxy Reverso (exemplo nginx)

```nginx
server {
    listen 443 ssl;
    server_name financialimport.empresa.com;

    location / {
        proxy_pass http://localhost:7000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## Segurança

### Credenciais e Segredos

- **Nunca** versionar `appsettings.json` com senhas reais
- Desenvolvimento: **User Secrets** (`dotnet user-secrets`)
- Produção: **variáveis de ambiente** ou cofre de segredos (Azure Key Vault, AWS Secrets Manager, etc.)

### Hashing de Senhas

Senhas armazenadas com **PBKDF2 + salt único por usuário**:
```
Hash = PBKDF2(password, salt, iterations, algorithm)
Colunas: SenhaHash (varbinary), SenhaSalt (varbinary)
```

### JWT

- Algoritmo: HS256
- `SecretKey` mínimo 32 caracteres
- Carregado do banco na inicialização (rotação sem redeploy)
- `ClockSkew` configurável para tolerância de clock entre servidores

### CORS

```json
// Produção: origens específicas
"Cors": {
  "AllowedOrigins": ["https://financialimport.empresa.com"]
}
// Desenvolvimento: permite localhost em múltiplas portas
```

### Cabeçalhos de Segurança

Recomendado adicionar via middleware ou proxy reverso:
```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Content-Security-Policy: default-src 'self'
Strict-Transport-Security: max-age=31536000
```

### Auditoria

- Todas as autenticações registradas em `AuditoriaLogin`
- Todas as operações críticas registradas em `LogSistema`
- Registros imutáveis (apenas INSERT, sem UPDATE/DELETE)
- Correlation ID em todos os registros para rastreabilidade

---

## Guia de Desenvolvimento

### Adicionando um Novo Layout de Importação

1. Criar classe `Layout3Parser : ILayoutImportParser` em `FinancialImport.Application/Imports/Parsers/`
2. Implementar `CanHandle(string[] headers)` — retorna `true` para o cabeçalho identificador
3. Implementar `ParseAsync(Stream file)` — retorna `IEnumerable<ImportLineDto>`
4. Criar `Layout3Validator : AbstractValidator<ImportLineDto>` em `.../Validators/`
5. Registrar no DI em `FinancialImport.Application/DependencyInjection/ServiceCollectionExtensions.cs`
6. O `LayoutDetector` resolverá automaticamente o parser correto

### Adicionando uma Nova Permissão

1. Adicionar constante em `FinancialImport.Domain/Constants/PermissionCodes.cs`
2. A policy é gerada automaticamente (loop sobre `PermissionCodes.All`)
3. O `DatabaseSeeder` criará a entrada em `Permissoes` no próximo startup
4. Atribuir ao perfil desejado via UI administrativa ou migration de dados

### Convenções de Código

- **Nomenclatura**: PascalCase para tipos/métodos, camelCase para variáveis locais
- **Async/Await**: todos os métodos que tocam I/O são assíncronos
- **Nullable**: habilitado — todos os tipos de referência são não-nulos por padrão
- **Validation**: FluentValidation para regras de negócio, Data Annotations para DTO simples
- **Logging**: `ILogger<T>` injetado, structured logging com propriedades nomeadas

### Estrutura de um Serviço Típico

```csharp
public class ExemploService(
    IExemploRepository repository,
    IUserContext userContext,
    ILogger<ExemploService> logger,
    IClock clock) : IExemploService
{
    public async Task<ResultDto> ExecutarAsync(Command cmd, CancellationToken ct = default)
    {
        logger.LogInformation("Iniciando {Operacao} para usuário {UserId}",
            nameof(ExecutarAsync), userContext.UserId);

        var entidade = await repository.ObterAsync(cmd.Id, ct);
        // ... lógica de negócio
        await repository.SalvarAsync(entidade, ct);

        return ResultDto.FromEntidade(entidade);
    }
}
```

### Rodando com RabbitMQ Local (Docker)

```bash
docker run -d --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  rabbitmq:3-management

# Management UI: http://localhost:15672 (guest/guest)
```

Habilitar em `appsettings.Development.json`:
```json
"Messaging": { "RabbitMq": { "Enabled": true } }
```

### Rodando com Kafka Local (Docker)

```bash
docker run -d --name kafka \
  -p 9092:9092 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
  confluentinc/cp-kafka:latest
```

Habilitar em `appsettings.Development.json`:
```json
"Messaging": { "Kafka": { "Enabled": true } }
```

---

## Licença

Projeto interno — consulte o proprietário do repositório para detalhes de licenciamento.
