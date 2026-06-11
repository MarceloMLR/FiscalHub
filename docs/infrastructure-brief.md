# Infraestrutura — Brief de Construção (Marco 1)

Documento de contexto para construir a casca de infraestrutura do FiscalHub. Serve de entrada
para agentes (Claude Code / BMAD) e como referência humana. Leia junto com o
[README](../README.md) e os [ADRs](adr/).

## Princípio

Pensamento de produção, escopo de demo. A infraestrutura **implementa as portas já definidas**
no núcleo (que está testado) — ela não inventa contratos novos nem altera o domínio.

## O que já existe (não tocar nos contratos)

Núcleo pronto e testado, em `src/FiscalHub.Domain` e `src/FiscalHub.Application`:

- Modelo de domínio `GoodsInvoice` (NF-e 55) com o bloco da reforma (IBS/CBS/IS).
- Portas: `IInboundSource<T>`, `IDocumentDiscovery`, `IComplianceDispatcher<T>`,
  `IProcessingStore`, `IDocumentValidator<T>`.
- Esteira: `DocumentPipeline<T>.ProcessAsync` (idempotência → busca → validação → envio → registro).
- Lógica real testada: `NfeXmlParser` (XML→domínio) e `GoodsInvoiceToAvalara` (domínio→Avalara).

A infraestrutura **conecta** isso ao Azure. Cada peça abaixo implementa uma porta existente.

## Peças a construir (ordem sugerida)

### 1. Mock da API de compliance
Serviço HTTP pequeno que simula a Avalara: recebe o god json, devolve um GUID (aceito), e tem um
endpoint de status que, consultado pelo GUID, devolve "carregado" ou "erro". Permite testar o
fluxo assíncrono ponta a ponta sem a Avalara real.

### 2. `AvalaraComplianceDispatcher : IComplianceDispatcher<GoodsInvoice>`
No projeto `src/Adapters/Outbound/FiscalHub.Adapters.Outbound.Avalara`. Usa `HttpClient`:
- `SubmitAsync`: chama `GoodsInvoiceToAvalara.Map`, serializa (camelCase), faz POST no mock,
  devolve `IntegrationReceipt` (GUID + `Submitted`).
- `CheckStatusAsync`: consulta o GUID, traduz o status nativo para `IntegrationStatus`.
- **Cache de token** (OAuth client credentials): token por **empresa/tenant** (não por CNPJ),
  validade 24h, renova ao expirar, thread-safe (um busca, os outros reusam). `clientId`/
  `clientSecret` vêm de config/Key Vault — **nunca** commitados.
- Testável: a lógica de cache de token e a tradução de status merecem teste (com `HttpClient` falso).

### 3. `XmlGoodsInvoiceSource : IInboundSource<GoodsInvoice>`
No projeto do adapter de XML. Lê o XML cru do **Blob** (pelo `Locator` da referência) e chama o
`NfeXmlParser`. O parsing já é testado; aqui é só o acesso ao Blob.

### 4. `SqlProcessingStore : IProcessingStore`
Projeto novo `src/FiscalHub.Infrastructure`. Persiste rastreio em **Azure SQL** (EF Core ou Dapper):
- `AlreadySubmittedAsync` (idempotência por tenant + chave natural),
- `RecordSubmissionAsync` / `RecordRejectionAsync`.
- Guarda também o **run** (agrupamento — ver ADR a criar) e o status de integração por documento.

### 5. Workers (Azure Functions)
Projeto `src/FiscalHub.Workers`:
- **Trigger de Service Bus** que desserializa a mensagem (referência + contexto) e chama
  `DocumentPipeline.ProcessAsync`. Exceção propaga → retry/DLQ nativos do Service Bus.
- **Ingresso**: drop de XML no Blob → Event Grid → enfileira a referência (um evento por documento).
- **Poll de status**: mensagem agendada que consulta documentos `Submitted` e atualiza o status.
  Tem **limite** (deadline configurável por tenant + teto de tentativas): se a plataforma não
  confirmar dentro da janela (ex.: 204 eterno por falha/lentidão da Avalara), para de consultar e
  marca o documento como `Unconfirmed` — **status novo a adicionar** ao `IntegrationStatus`. Não é
  rejeição de negócio (`IntegrationError`), é falta de retorno da plataforma; no dashboard vira
  candidato a **recheck manual**, não a correção da nota. (Vira ADR ao implementar.)

### 6. Composição e roteamento por tipo
Composition root (na Api/Workers): registra no DI as implementações, resolve as credenciais do
**perfil do tenant** (arquivo versionado por stamp), e roteia por `DocumentType` para o
`DocumentPipeline<T>` certo (hoje só `GoodsInvoice`).

### 7. Dev local sem custo
`docker-compose` com Azurite (Blob/Queue), SQL em container e emulador do Service Bus. Meta: rodar
~90% do fluxo localmente, sem gastar no Azure.

## Decisões e restrições (resumo dos ADRs)

- **Claim-check**: a fila carrega só a referência; o documento é buscado na origem (ADR-0004).
- **Retry/DLQ nativos** do Service Bus — não reinventar (ADR-0004).
- **Envio direto** no pipeline (não fila de saída separada) — fila de saída fica como evolução (ADR-0004).
- **Status normalizado**: cada adapter traduz o status nativo para `IntegrationStatus`; o dashboard
  é agnóstico de plataforma (ADR-0003).
- **DTOs da Avalara `internal`** — o formato externo fica preso no adapter.
- **Segredos** (tokens, connection strings, SAS) nunca no git — Key Vault / user-secrets / env.

## Não fazer

- Não alterar o domínio nem as portas (são o contrato estável).
- Não revalidar XSD nem calcular imposto (fora de escopo, por design).
- Não expor os DTOs da Avalara nem o status nativo para fora do adapter.
- Não acoplar um adapter de origem a um de destino (tradução passa sempre pelo domínio).

## Definição de pronto

- Cada peça implementa a porta correspondente e compila na solução.
- Lógica não trivial (cache de token, tradução de status, store) coberta por testes.
- O fluxo roda localmente (docker + Azurite): drop de XML → esteira → mock Avalara → status no store.
- Sem segredos no repositório.
