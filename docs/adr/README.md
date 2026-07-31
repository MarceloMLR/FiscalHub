# Architecture Decision Records (ADRs)

Registro das decisões de arquitetura do FiscalHub — o "porquê" por trás de cada escolha.
Cada ADR é curto, datado e imutável: se uma decisão muda, cria-se um novo ADR que
substitui o anterior (em vez de reescrever a história).

## Índice

| ADR | Decisão | Status |
|-----|---------|--------|
| [0001](0001-modelos-de-dominio-por-tipo.md) | Modelos de domínio por tipo de documento, não um modelo canônico único | Aceito |
| [0002](0002-entrada-gatilho-descoberta-busca.md) | Entrada: separação gatilho / descoberta / busca, com portas genéricas | Aceito |
| [0003](0003-despacho-assincrono-status-integracao.md) | Despacho assíncrono e status de integração normalizado | Aceito |
| [0004](0004-esteira-nucleo-puro-retry-nativo.md) | Esteira: núcleo puro, retry/DLQ nativos, envio direto | Aceito |
| [0005](0005-idempotencia-no-banco.md) | Idempotência no banco (índice único) para o rastreio | Aceito |
| [0006](0006-rastreabilidade-fotos-no-blob.md) | Rastreabilidade por "fotos" (domínio/destino) no Blob | Aceito |
| [0007](0007-poll-de-status-e-idempotencia-por-estado.md) | Poll de status assíncrono e idempotência por estado | Aceito |
| [0008](0008-gatilho-por-service-bus.md) | Gatilho por Service Bus (fila + claim-check) | Aceito |
| [0009](0009-gatilho-de-ingestao-por-drop.md) | Gatilho de ingestão por drop no Blob (local; Event Grid no cloud) | Aceito |
| [0010](0010-dead-letter-visivel-e-poll-resiliente.md) | Dead-letter visível e poll resiliente | Aceito |
| [0011](0011-stack-do-dashboard.md) | Stack do dashboard (Vite + React + MUI + TanStack Query) | Aceito |
| [0012](0012-ef-migrations.md) | Migrations de schema (EF Core) no lugar de EnsureCreated | Aceito |
| [0013](0013-diretorio-de-empresas-como-porta.md) | Diretório de empresas/filiais como porta (JSON no dev, Avalara no cloud) | Aceito |
| [0014](0014-integracao-manual-por-descoberta-pull.md) | Integração manual por descoberta pull (porta genérica, adapter local no dev) | Aceito |
| [0015](0015-idempotencia-por-gatilho.md) | Idempotência por gatilho (evento dedupa, manual recarrega) | Aceito |
| [0016](0016-idempotencia-por-conteudo.md) | Idempotência por conteúdo (nota de entrada pode ser corrigida) | Aceito |
| [0017](0017-agendador-e-execucao-compartilhada.md) | Agendador in-process + execução compartilhada (runner) | Aceito |
| [0018](0018-autenticacao-jwt-e-escopo-por-tenant.md) | Autenticação (JWT próprio + PBKDF2) e escopo por tenant | Aceito |
| [0019](0019-perfil-de-conector-por-tenant.md) | Perfil de conector por tenant (config em banco, segredos por referência) | Aceito |
| [0020](0020-topologia-de-deploy-por-cliente-e-frontend-unico.md) | Topologia de deploy (backend por-cliente, frontend único, subdomínio) | Aceito |

## Planejados

Decisões já tomadas em conversa, a serem registradas conforme as fatias avançam:

- Ports & adapters (arquitetura hexagonal).
- Blob + Azure SQL em vez de CosmosDB.
- Esteira: event notification + claim-check.
- Validação só de integração (sem revalidar XSD).
- Customização por tenant: escada config → keyed → custom; isolamento por stamp.
- Persistência do perfil do tenant em arquivo versionado por stamp.
