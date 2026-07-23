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

## Planejados

Decisões já tomadas em conversa, a serem registradas conforme as fatias avançam:

- Ports & adapters (arquitetura hexagonal).
- Blob + Azure SQL em vez de CosmosDB.
- Esteira: event notification + claim-check.
- Validação só de integração (sem revalidar XSD).
- Customização por tenant: escada config → keyed → custom; isolamento por stamp.
- Persistência do perfil do tenant em arquivo versionado por stamp.
