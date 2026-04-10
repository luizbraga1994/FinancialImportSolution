## Arquitetura evoluída — Mensageria, Outbox, Observabilidade

> Esta seção descreve a evolução arquitetural introduzida na branch `claude/dotnet-sap-architecture-U45ol`.
> O fluxo síncrono legado continua funcionando; os componentes assíncronos são opcionais e ativados por configuração.

### Visão 360° do novo pipeline

```
            ┌──────────────┐         ┌───────────────────┐
 HTTP/Web → │ImportService │ ── tx ─▶│    AppDbContext   │
            │  (Preview/   │         │ImportFile/Line/   │
            │   Confirm)   │         │OutboxMessage rows │
            └──────┬───────┘         └──────┬────────────┘
                   │ enqueue                │ SaveChanges (atomic)
                   ▼                        │
            ┌──────────────┐                │
            │OutboxPublish │                │
            └──────┬───────┘                │
                   │                        ▼
                   │                 ┌─────────────┐
                   │                 │OutboxDispat-│
                   │                 │cherWorker   │
                   │                 └──┬───────┬──┘
                   │       RabbitMQ ◀───┘       └───▶ Kafka
                   │       (commands)                (events)
                   ▼
            ┌─────────────┐
            │Import       │
            │Processor    │◀──── ImportProcessWorker (RabbitMQ consumer)
            │(idempotent) │
            └──────┬──────┘
                   │
                   ▼
            ┌──────────────┐
            │ SAP Service  │
            │    Layer     │
            └──────────────┘
```

### Quando RabbitMQ, quando Kafka

| Necessidade                                              | Broker    | Canal/tópico                                |
| -------------------------------------------------------- | --------- | ------------------------------------------- |
| Comando transacional interno (processar importação)     | RabbitMQ  | `financialimport.import.process`            |
| Reprocessamento com DLQ e backoff                        | RabbitMQ  | `financialimport.import.reprocess`          |
| Dispatch individual de JournalEntry ao SAP               | RabbitMQ  | `financialimport.sap.dispatch`              |
| Evento de domínio: importação validada                   | Kafka     | `financialimport.import.events`             |
| Evento de domínio: dispatch SAP sucesso/falha            | Kafka     | `financialimport.sap.events`                |
| Evento de auditoria global                               | Kafka     | `financialimport.audit.events`              |
| Evento de segurança (login, permissão negada)            | Kafka     | `financialimport.security.events`           |

- **RabbitMQ** → comandos imperativos, um handler, retry/DLQ, backpressure.
- **Kafka** → eventos imutáveis (append-only), múltiplos consumidores, replay, analytics.

### Padrão Outbox Transacional

Para evitar dual-write inconsistency entre banco de negócio e broker, todo `IEventPublisher.PublishAsync` e `ICommandBus.SendAsync` **insere no `MensagensOutbox`** dentro da mesma transação EF. O `OutboxDispatcherWorker`:

1. Faz `ClaimPendingAsync` (marca `InFlight` com timeout).
2. Publica no broker (Rabbit/Kafka) com correlation id propagado.
3. Em sucesso → `MarkDispatchedAsync`; em erro → `MarkFailedAsync` com backoff exponencial (`InitialRetryDelaySeconds × RetryBackoffMultiplier^n`, limitado a `MaxRetryDelaySeconds`).
4. Após `MaxAttempts` falhas → `MarkDeadLetteredAsync` + audit log.

### Idempotência de ponta a ponta

Três camadas independentes garantem que o SAP **nunca** receba um lançamento duplicado, mesmo com retries, crashes, redeliveries ou concorrência:

1. **Outbox unique index** `(MessageId)` → o dispatcher nunca publica o mesmo envelope duas vezes.
2. **Inbox unique index** `(Consumer, MessageId)` → consumer rejeita redeliveries.
3. **`LancamentoSapDispatch` unique index** `(CompanyDb, GroupKeyHash)` → um mesmo grupo de lançamento **não** é enviado novamente; se já tem `Dispatched`, o `ImportProcessor` reusa o `SapDocEntry` existente e marca as linhas como `Imported` sem tocar o SAP.

### Deduplicação por linha + SeqLancamento

A `BusinessKeyBuilder` monta a chave usando os campos configurados em `Imports:Processing:DeduplicationKey`. **Quando `SeqLancamento` (id de controle) está presente**, ele é incluído automaticamente na chave — garantindo que:

- Linhas com o **mesmo conteúdo** mas **IDs diferentes** sejam aceitas como registros distintos.
- Linhas **legítimamente duplicadas** (mesmo SeqLancamento) sejam barradas pelo unique index `ImportacaoLinha(CompanyDb, HashChaveNegocio)`.

O `GroupKeyHash` (usado tanto para agrupar em uma única `JournalEntry` quanto para ser enviado ao SAP como `Reference`) também inclui o SeqLancamento, mantendo a coerência entre dedupe e dispatch.

### Correlation ID fim-a-fim

O `CorrelationIdMiddleware` lê/gera `X-Correlation-Id` em cada requisição HTTP e o publica no `ICorrelationContextAccessor` (AsyncLocal). Esse contexto flui para:

- `ImportFile.CorrelationId` (persistido)
- `OutboxMessage.CorrelationId` (persistido)
- Headers RabbitMQ (`X-Correlation-Id`, `X-Causation-Id`)
- Headers Kafka (idem)
- `LogSistema.CorrelationId` (audit sink)

Uma falha de suporte pode ser rastreada de Web → API → Worker → SAP apenas pela correlation id.

### Logs ricos no banco

A tabela `LogSistema` agora tem:

- `Categoria` — `Technical / Functional / Audit / Integration / Messaging / Security / Performance`
- `Operacao` — nome semântico (`Preview`, `Confirm`, `Dispatch`, `Process`, etc.)
- `CorrelationId`, `CausationId`, `MessageId`
- `ImportacaoArquivoId`, `ImportacaoLinhaId`, `SapSessionId`
- `ChaveNegocio`, `StatusAntes`, `StatusDepois`, `DuracaoMs`
- `Hostname`, `Ambiente`, `Aplicacao`
- `StackTrace` dedicado

E índices:

```sql
IX_LogSistema_DataHora
IX_LogSistema_UsuarioId
IX_LogSistema_CorrelationId
IX_LogSistema_Categoria_Nivel
IX_LogSistema_ImportacaoArquivoId
```

Escrita unificada via `IAuditLogger.WriteAsync(AuditLogEntry)` — sem strings mágicas espalhadas pelos services.

### Novas tabelas

| Tabela                      | Função                                                  |
| --------------------------- | ------------------------------------------------------- |
| `MensagensOutbox`           | Transactional outbox (commands + events)                |
| `MensagensInbox`            | Deduplicação por consumer (unique `Consumidor,MessageId`) |
| `LancamentoSapDispatch`     | Tracking idempotente de dispatch SAP                    |

### Remoção de hardcoded

Foram movidos para `appsettings.json` / Options:

- `Imports:Processing:MaxFileSizeBytes` (era 10 MB fixo no controller)
- `Imports:Processing:AllowedExtensions` (era array hardcoded)
- `Imports:Processing:MemoMaxLength / ReferenceMaxLength / LineMemoMaxLength`
- `Imports:Processing:JournalBalanceTolerance`
- `Imports:Processing:DeduplicationKey.*` — que campos entram no hash
- `Imports:Processing:UseAsyncConfirmation` — sync/async
- `Messaging:RabbitMq.*` — host, porta, user, exchange, DLX, retries, canais, routing keys
- `Messaging:Kafka.*` — bootstrap, client id, group id, tópicos
- `Messaging:Outbox.*` — intervalo, batch, max attempts
- `Security:Cookie:ExpirationHours`
- `Jwt:ClockSkewMinutes`
- Polices de autorização → geradas automaticamente a partir de `PermissionCodes.All` (antes 8 `AddPolicy(...)` duplicados)

### Configuração mínima do broker (dev)

```json
"Messaging": {
  "RabbitMq": {
    "Enabled": true,
    "HostName": "localhost",
    "UserName": "guest",
    "Password": "guest"
  },
  "Kafka": {
    "Enabled": true,
    "BootstrapServers": "localhost:9092"
  },
  "Outbox": { "Enabled": true }
}
```

Quando `Enabled = false`, os workers ficam ociosos e o pipeline síncrono legado continua funcionando — compatibilidade total.

### Observabilidade

- Health check EF via `AddDbContextCheck<AppDbContext>` em `/health`.
- Serilog enriched com `FromLogContext` + `Application` + `Environment`.
- Correlation id propagado via `X-Correlation-Id` response header.
- Audit log persistido em `LogSistema` com `DuracaoMs` por operação.

### Como testar sem brokers

O modo síncrono é ativado por `Imports:Processing:UseAsyncConfirmation = false` (default). Nesse modo:

- `PreviewAsync` grava no outbox (que fica `Pending`).
- `ConfirmAsync` chama `ImportProcessor` diretamente no request thread.
- Nenhuma mensagem é efetivamente publicada no broker (pois `Enabled=false`).
- Após ligar `Messaging:RabbitMq:Enabled=true`, os workers consomem a backlog existente automaticamente.

### Bugs corrigidos nesta evolução

1. **Não compilava**: `ImportService.PreviewAsync` referenciava `transaction.CommitAsync()` sem declarar a transação.
2. **Não compilava**: agrupamento com tipo anônimo incompatível com `IGrouping<string,…>` no fallback.
3. **Dedupe quebrada**: `BuildBusinessKey` ignorava `SeqLancamento`.
4. **Risco de duplicação no SAP**: `ProcessAsync` enviava sem registro idempotente; um retry duplicava lançamento contábil.
5. **Credenciais em plaintext**: `appsettings.json` limpo — use secrets/env vars para produção.
6. **PII em logs**: removidas as dumps de `User.Claims` inteiros.
7. **`SaveChanges` traduzindo exceções**: substituído por pipeline limpo com timestamps automáticos.
8. **N+1 na dedup**: `ExistsBusinessKeyAsync` passou a um único round-trip via `GetExistingBusinessKeysAsync(...)`.
9. **Truncation hardcoded**: `MemoMaxLength/ReferenceMaxLength/LineMemoMaxLength` agora via Options.

### Testes unitários

Rodar `dotnet test` — agora existe:

- `BusinessKeyBuilderTests` — garantias do SeqLancamento na chave
- `JournalEntryBuilderTests` — debit/credit e truncation
- `LancamentoValidatorTests` — validação de negócio
- `CorrelationContextTests` — AsyncLocal + Push/Dispose

---
