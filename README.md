# FinancialImportSolution

Sistema de importação de lançamentos contábeis para o **SAP Business One**, construído em **.NET 10** com arquitetura em camadas. Oferece interface Web (MVC/Razor) e API REST, integrando-se ao SAP via **Service Layer** e descobrindo empresas disponíveis diretamente no **SAP HANA**.

---

## Sumário

- [Visão geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Stack tecnológica](#stack-tecnológica)
- [Pré-requisitos](#pré-requisitos)
- [Configuração](#configuração)
- [Banco de dados](#banco-de-dados)
- [Como executar](#como-executar)
- [Endpoints da API](#endpoints-da-api)
- [Layouts de importação](#layouts-de-importação)
- [Modelo de permissões](#modelo-de-permissões)
- [Integrações SAP](#integrações-sap)
- [Testes](#testes)
- [Logs e auditoria](#logs-e-auditoria)

---

## Visão geral

O **FinancialImportSolution** automatiza o processo de importação de lançamentos contábeis em massa para o SAP Business One. Fluxo resumido:

1. Usuário faz login na aplicação e seleciona a empresa (database) SAP desejada.
2. Faz upload de um arquivo (XLSX, CSV ou TXT) com os lançamentos.
3. O sistema detecta o layout automaticamente, valida cada linha e mostra uma pré-visualização agrupada.
4. Após confirmação, os lançamentos válidos são enviados ao SAP via Service Layer (`POST /b1s/v1/JournalEntries`).
5. Todo o histórico, status (incluindo erros do SAP) e auditoria ficam registrados no MySQL para consulta posterior.

Principais recursos:

- Detecção automática de layout (dois layouts nativos suportados).
- Deduplicação por hash de arquivo e de linha (impede reimportação acidental).
- Múltiplos usuários com controle granular por perfil, permissão e empresa permitida.
- Mapeamento de filial do arquivo → `BPLId` do SAP por empresa.
- Sessões SAP gerenciadas por usuário/empresa com expiração.
- Download de template XLSX pronto (Layout 2).
- Autenticação dupla: **JWT** na API e **Cookies** na Web.
- Logs estruturados com **Serilog** (console + arquivo rotativo diário).

---

## Arquitetura

Arquitetura em camadas com duas "fachadas" (API e Web) compartilhando as mesmas camadas de aplicação e infraestrutura:

```
┌──────────────────────┐      ┌──────────────────────┐
│  FinancialImport.Web │      │  FinancialImport.Api │
│   (MVC + Razor)      │      │   (REST + Swagger)   │
└──────────┬───────────┘      └──────────┬───────────┘
           │                             │
           └──────────────┬──────────────┘
                          │
              ┌───────────▼────────────┐
              │  Application (CQRS,    │
              │  serviços, parsers,    │
              │  validators)           │
              └───────────┬────────────┘
                          │
              ┌───────────▼────────────┐
              │  Domain (entidades,    │
              │  enums, constantes)    │
              └───────────┬────────────┘
                          │
    ┌─────────────────────┼─────────────────────┐
    │                     │                     │
┌───▼────────┐   ┌────────▼────────┐   ┌────────▼────────┐
│ Infrastruc │   │ Integration.Sap │   │ Integration.Hana│
│ (EF Core,  │   │ (Service Layer) │   │ (ADO.NET HANA)  │
│  MySQL,    │   │                 │   │                 │
│  JWT, Hash)│   │                 │   │                 │
└────────────┘   └─────────────────┘   └─────────────────┘
```

Padrões aplicados:

- **Clean/Layered Architecture** — dependências sempre apontando para o Domain.
- **Repository Pattern** para acesso a dados (`IImportRepository`).
- **Strategy Pattern** para parsing de layouts (`ILayoutImportParser`).
- **Dependency Injection** via métodos de extensão por camada.
- **Context Accessors** (`IUserContext`, `ICompanyContext`) resolvem o usuário/empresa atual a partir do `HttpContext`.
- **FluentValidation** para regras de negócio sobre linhas importadas.
- **Serilog** para logs estruturados.

---

## Estrutura do repositório

```
FinancialImportSolution/
├── src/
│   ├── FinancialImport.Domain/           # Entidades, enums, constantes de permissões
│   ├── FinancialImport.Application/      # Serviços, parsers, validators, DTOs
│   ├── FinancialImport.Infrastructure/   # EF Core, MySQL, repositórios, JWT, hashing
│   ├── FinancialImport.Integration.Sap/  # Integração com SAP Service Layer
│   ├── FinancialImport.Integration.Hana/ # Descoberta de empresas via SAP HANA
│   ├── FinancialImport.Shared/           # Abstrações compartilhadas (IClock, etc.)
│   ├── FinancialImport.Api/              # REST API + Swagger + JWT
│   └── FinancialImport.Web/              # MVC + Razor Views (UI principal)
├── tests/
│   └── FinancialImport.Tests/            # Testes unitários (xUnit)
├── scripts/
│   └── 01_InitialCreate.sql              # Script inicial do banco MySQL
├── Directory.Build.props                 # TargetFramework net10.0, Nullable enable
└── FinancialImportSolution.slnx          # Solução (.slnx)
```

---

## Stack tecnológica

| Camada / Tópico      | Tecnologia                                                   |
| -------------------- | ------------------------------------------------------------ |
| Runtime              | .NET 10.0                                                    |
| Linguagem            | C# (LangVersion latest, `Nullable enable`)                   |
| Web / API            | ASP.NET Core MVC + Razor Views, Swashbuckle (Swagger)        |
| ORM                  | Entity Framework Core 9.0                                    |
| Banco principal      | MySQL 8.0+ (provider Pomelo.EntityFrameworkCore.MySql 9.0)   |
| Banco externo        | SAP HANA (via `Sap.Data.Hana` ADO.NET)                       |
| Integração ERP       | SAP Business One Service Layer (REST / JSON)                 |
| Auth (API)           | JWT Bearer (HS256)                                           |
| Auth (Web)           | Cookie Authentication (8h, sliding)                          |
| Validação            | FluentValidation 11.10                                       |
| Leitura XLSX         | ClosedXML 0.104                                              |
| Logging              | Serilog (Console + File rolling diário)                      |
| Testes               | xUnit 2.9                                                    |

---

## Pré-requisitos

- **.NET SDK 10.0**
- **MySQL 8.0+** acessível para a aplicação
- Acesso ao **SAP Business One Service Layer** (URL, usuário e senha manager)
- Acesso ao **SAP HANA** (para descoberta de empresas) + cliente `Sap.Data.Hana` instalado
  - Caminho padrão (Windows): `C:\Program Files\sap\hdbclient\dotnetcore\v8.0\Sap.Data.Hana.Net.v8.0.dll`
- Porta 3306 liberada para o MySQL

---

## Configuração

As configurações ficam em `appsettings.json` / `appsettings.Development.json` dentro dos projetos `FinancialImport.Api` e `FinancialImport.Web`. Nunca versione senhas reais — use **User Secrets** ou variáveis de ambiente em produção.

### Connection string (MySQL)

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=FinancialImport;User=root;Password=******;Port=3306;"
}
```

### JWT (usado pela API)

```json
"Jwt": {
  "SecretKey": "chave-com-no-minimo-32-caracteres",
  "Issuer": "FinancialImport",
  "Audience": "FinancialImportClients",
  "ExpirationMinutes": 480,
  "RefreshExpirationMinutes": 1440
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

### SAP HANA (descoberta de empresas)

```json
"HanaDbConnection": {
  "Server": "seu-hana",
  "Port": 30015,
  "Database": "SBOCOMMON",
  "UserID": "SYSTEM",
  "Password": "******",
  "MaxPoolSize": 100,
  "MinPoolSize": 10,
  "ConnectionTimeout": 60,
  "CommandTimeout": 300,
  "ProviderInvariantName": "Sap.Data.Hana",
  "ProviderAssemblyPath": "C:\\Program Files\\sap\\hdbclient\\dotnetcore\\v8.0\\Sap.Data.Hana.Net.v8.0.dll"
}
```

### Parsing de layout

```json
"LayoutParsing": {
  "DefaultTipoLancLayout1": "D"
}
```

### Admin padrão (seed inicial)

```json
"AdminSeed": {
  "Login": "admin",
  "Password": "Admin@123",
  "Email": "admin@financialimport.local"
}
```

> Troque a senha do admin imediatamente após o primeiro login.

---

## Banco de dados

O schema é criado automaticamente na inicialização via **EF Core Migrations** (`context.Database.Migrate()`), e um `DatabaseSeeder` cria o usuário admin e as permissões padrão.

Também existe o script completo `scripts/01_InitialCreate.sql` para criação manual do banco em ambientes onde migrations automáticas não são desejadas.

### Tabelas principais

| Tabela                     | Descrição                                                    |
| -------------------------- | ------------------------------------------------------------ |
| `Usuarios`                 | Usuários do sistema, flag `AdminGlobal`                      |
| `Perfis`                   | Perfis de acesso (papéis)                                    |
| `Permissoes`               | Permissões granulares por código                             |
| `UsuarioPerfil`            | N:N usuário × perfil                                         |
| `PerfilPermissao`          | N:N perfil × permissão                                       |
| `UsuarioEmpresaPermitida`  | Controla quais empresas (databases SAP) o usuário acessa     |
| `AuditoriaLogin`           | Log de tentativas de login (IP, user agent, sucesso/falha)   |
| `SessaoEmpresaUsuario`     | Sessões SAP ativas por usuário/empresa com expiração         |
| `ImportacaoArquivo`        | Arquivos importados (status, layout, hash, contagens)        |
| `ImportacaoLinha`          | Linhas individuais do arquivo (status, resposta SAP, hash)   |
| `MapeamentoFilialSap`      | Mapeamento de filial do arquivo → `BPLId` do SAP             |
| `ConfiguracaoLayout`       | Layouts de importação configuráveis                          |
| `ConfiguracaoLayoutCampo`  | Campos de cada layout                                        |
| `LogSistema`               | Log geral da aplicação                                       |
| `Regras`                   | Parâmetros/regras chave-valor                                |

Deduplicação é garantida por índices únicos compostos:
- `ImportacaoArquivo`: `(CompanyDb, HashArquivo)` — mesmo arquivo não é importado 2×.
- `ImportacaoLinha`: `(CompanyDb, HashChaveNegocio)` — mesma linha não é enviada 2× ao SAP.

---

## Como executar

### 1. Clonar e restaurar

```bash
git clone https://github.com/luizbraga1994/financialimportsolution.git
cd financialimportsolution
dotnet restore FinancialImportSolution.slnx
```

### 2. Configurar `appsettings.Development.json`

Ajuste `ConnectionStrings:DefaultConnection`, `Jwt:SecretKey`, `SapServiceLayer` e `HanaDbConnection` tanto em `src/FinancialImport.Api` quanto em `src/FinancialImport.Web`.

### 3. Compilar

```bash
dotnet build FinancialImportSolution.slnx
```

### 4. Rodar a aplicação Web (MVC)

```bash
dotnet run --project src/FinancialImport.Web/FinancialImport.Web.csproj
```

Acesse `https://localhost:7000` (ou a porta configurada) e faça login com as credenciais do seed (`admin` / `Admin@123`).

### 5. Rodar a API REST

```bash
dotnet run --project src/FinancialImport.Api/FinancialImport.Api.csproj
```

Swagger disponível em `/swagger`. Use `POST /api/v1/auth/login` para obter o token JWT e depois envie `Authorization: Bearer <token>` nas demais chamadas.

> Na primeira execução, o EF Core aplica todas as migrations e o `DatabaseSeeder` cria o usuário admin e as permissões básicas.

---

## Endpoints da API

Todos os endpoints (exceto login e health) exigem JWT Bearer e, quando aplicável, uma policy de autorização.

### Autenticação — `/api/v1/auth`

| Método | Rota     | Descrição                                        |
| ------ | -------- | ------------------------------------------------ |
| POST   | `/login` | Autentica usuário, retorna JWT + refresh token   |
| GET    | `/me`    | Dados do usuário autenticado (claims atuais)     |

### Empresas — `/api/v1/companies`

| Método | Rota      | Descrição                                                      |
| ------ | --------- | -------------------------------------------------------------- |
| GET    | `/`       | Lista empresas acessíveis ao usuário (via HANA + permissões)   |
| POST   | `/login`  | Abre sessão SAP na empresa selecionada                         |
| POST   | `/logout` | Encerra sessão SAP ativa                                       |

### Importações — `/api/v1/imports`

| Método | Rota                        | Policy                    | Descrição                           |
| ------ | --------------------------- | ------------------------- | ----------------------------------- |
| POST   | `/preview`                  | `ImportarLancamentos`     | Upload + parse + validação          |
| POST   | `/{importFileId}/confirm`   | `ImportarLancamentos`     | Processa e envia lançamentos ao SAP |
| POST   | `/{importFileId}/reprocess` | `ReprocessarImportacao`   | Reprocessa linhas com erro          |
| GET    | `/history`                  | `VisualizarHistorico`     | Histórico paginado de importações   |
| GET    | `/{importFileId}/lines`     | `VisualizarHistorico`     | Linhas paginadas de uma importação  |

### Administração

| Controller                  | Rota base                      | Policy                 |
| --------------------------- | ------------------------------ | ---------------------- |
| `UsersApiController`        | `/api/v1/users`                | `GerenciarUsuarios`    |
| `ProfilesApiController`     | `/api/v1/profiles`             | `GerenciarPerfis`      |
| `PermissionsApiController`  | `/api/v1/permissions`          | `GerenciarPermissoes`  |
| `BranchMappingApiController`| `/api/v1/branch-mappings`      | `GerenciarUsuarios`    |
| `LogsApiController`         | `/api/v1/logs`                 | `VisualizarLogs`       |
| `HealthApiController`       | `/health`                      | (anônimo)              |

Todas as respostas são encapsuladas em `ApiResponse<T>`; listagens usam `PagedResult<T>`.

---

## Layouts de importação

Dois parsers nativos (ambos implementam `ILayoutImportParser`) — o layout correto é resolvido automaticamente inspecionando os cabeçalhos do arquivo:

### Layout 1 — técnico

Identificado pelos cabeçalhos `CODCONTACONTABIL` e `DTLANCCONTABIL`. Outros campos: `TIPOLANC`, `REFERENCIA`, `VLR`, etc. Quando `TIPOLANC` não é informado, o valor default vem de `LayoutParsing:DefaultTipoLancLayout1` (por padrão `"D"` = débito).

### Layout 2 — amigável

Identificado pelos cabeçalhos `Conta Contabil`, `Valor Credito`, `Valor Debito`. É o layout recomendado para usuários finais. Um **template XLSX** pode ser baixado diretamente pela UI (`/Import/DownloadTemplate`) ou gerado programaticamente pelo `ImportTemplateBuilder`.

Formatos aceitos: **XLSX** (via `ClosedXML`), **CSV** e **TXT** (parser delimitado). O primeiro sheet / primeira linha é sempre tratado como cabeçalho.

Validações de linha (FluentValidation):
- Conta contábil obrigatória
- Data de lançamento válida
- Valores numéricos válidos (débito OU crédito)
- Outros campos obrigatórios por layout

---

## Modelo de permissões

Autorização é baseada em **códigos de permissão** (não em roles estáticas). Cada policy do ASP.NET Core corresponde a um código:

| Código                    | Descrição                             |
| ------------------------- | ------------------------------------- |
| `importar_lancamentos`    | Importar lançamentos contábeis        |
| `visualizar_historico`    | Visualizar histórico de importações   |
| `reprocessar_importacao`  | Reprocessar importações com erro      |
| `trocar_company`          | Trocar de empresa SAP                 |
| `gerenciar_usuarios`      | CRUD de usuários                      |
| `gerenciar_perfis`        | CRUD de perfis                        |
| `gerenciar_permissoes`    | CRUD de permissões                    |
| `visualizar_logs`         | Visualizar logs do sistema            |

Relacionamentos:

- Usuário → Perfis (N:N)
- Perfil → Permissões (N:N)
- Usuário → Empresas permitidas (N:N) — só enxerga/loga em empresas autorizadas.
- Flag `AdminGlobal` no usuário ignora todas as restrições.

---

## Integrações SAP

### SAP Service Layer (`FinancialImport.Integration.Sap`)

- **`SapCompanySessionService`** — faz `POST /b1s/v1/Login` com `CompanyDB`/user/password, armazena `SessionId` e `RouteId` no `SapSessionStore` e gerencia logout. Opção `IgnoreSslErrors` disponível para ambientes com certificados self-signed.
- **`SapJournalEntryService`** — faz `POST /b1s/v1/JournalEntries` incluindo cabeçalhos `B1SESSION` e `ROUTEID`, tratando respostas de erro do SAP (`error.message.value`) e registrando payloads em log.

Usa `IHttpClientFactory` com um named client `SapServiceLayer` configurado a partir de `SapServiceLayerOptions` (timeouts, retries, headers).

### SAP HANA (`FinancialImport.Integration.Hana`)

- **`SapCompanyDiscoveryService`** — carrega dinamicamente o provider `Sap.Data.Hana` (`DbProviderFactory`) e consulta `SBOCOMMON.SRGC` para retornar a lista de empresas disponíveis (`dbName`, `cmpName`, `cmpStatus`). É usado na tela de login/troca de empresa para mostrar apenas as empresas que o usuário tem permissão de acessar.

---

## Testes

Projeto: `tests/FinancialImport.Tests` (xUnit 2.9.2).

```bash
dotnet test FinancialImportSolution.slnx
```

> A suíte atual é um stub; testes adicionais devem cobrir parsers, validators, `ImportService` (preview/process) e a integração com SAP (com HttpClient mockado).

---

## Logs e auditoria

- **Serilog** configurado em `Program.cs` com sinks `Console` e `File` (rotativo diário em `logs/log-.txt`).
- Nível mínimo: `Information`.
- Todas as tentativas de login ficam em `AuditoriaLogin` (IP, user agent, sucesso/falha, motivo).
- Eventos gerais da aplicação ficam em `LogSistema` (indexado por `DateTime` e `UserId`).
- Erros não tratados da API são capturados pelo `GlobalExceptionMiddleware` e retornados como `ApiResponse` padronizado.

---

## Licença

Projeto interno — consulte o proprietário do repositório para detalhes de licenciamento.
