# FiscalHub — Hub de Integração Fiscal (.NET / Azure)

Middleware de integração que recebe notas fiscais **já emitidas** (de um ERP ou de XML),
as traduz para um modelo de domínio interno (a "verdade" do conector) e as despacha para
um sistema de compliance tributário (estilo Avalara). Nasce preparado para a **Reforma
Tributária** (IBS/CBS/IS), com fontes e destinos plugáveis.

> **Princípio:** pensamento de produção, escopo de demo. O raciocínio e a arquitetura são
> de nível sênior; a implementação é uma fatia vertical que roda de ponta a ponta, com
> pontos de extensão visíveis e documentação caprichada.

## O problema

A Reforma Tributária (NT 2025.002 da NF-e) introduziu IBS, CBS e Imposto Seletivo por item,
com obrigatoriedade a partir de 2026. Integrar ERPs a plataformas de compliance, carregando
fielmente esses novos campos sem perda, é uma dor real. O FiscalHub é o "encaixe universal"
entre origem e destino.

## O que ele é (e o que não é)

**É:** middleware de integração. Recebe → traduz → despacha, com esteira resiliente.

**Não é:** emissor de notas. Não assina com certificado, não transmite à SEFAZ, não
revalida estrutura (a nota já foi autorizada — cStat 100) e **não calcula imposto** (isso é
do sistema de compliance).

## Arquitetura

Ports & adapters (hexagonal): o núcleo de domínio no centro; ERP (origem) e compliance
(destino) são plugáveis nas bordas. As dependências apontam para dentro.

- **Núcleo (`FiscalHub.Domain`)** — modelos por tipo de documento + envelope fino. _(esta fatia)_
- **Portas (`FiscalHub.Application`)** — interfaces de entrada (origem agnóstica) e saída
  (compliance agnóstico) + orquestração. _(próxima fatia)_
- **Esteira resiliente** — evento magro (só o ID) → fila (Service Bus) → busca o payload na
  origem (claim-check) → mapeia → despacha. Com idempotência, retry/backoff, DLQ e rastreio.
- **Adapters** — entrada (XML real; D365 mock) e saída (estilo Avalara real; export stub).
- **Dashboard React** — lista, status, DLQ, reprocessamento, busca.

Decisões registradas em [`docs/adr`](docs/adr).

## Real × mock × extensão

| Peça | Estado |
|------|--------|
| Domínio Mercadoria + IBS/CBS/IS + envelope | ✅ Real _(esta fatia)_ |
| Esteira resiliente | ⏳ Planejado |
| Adapter de entrada XML (NF-e) | ⏳ Planejado (real) |
| Adapter de saída estilo Avalara | ⏳ Planejado (real, API mock) |
| Dashboard React | ⏳ Planejado |
| Adapter de entrada estilo D365 | ⏳ Planejado (mock, prova de extensão) |

## Stack

.NET 10 (LTS) / C# · React · Azure serverless (Static Web Apps, Functions/Container Apps,
Service Bus, Blob, Azure SQL serverless, Event Grid, Application Insights). Dev local de
graça com Docker + Azurite.

## Como rodar

> _A ser preenchido conforme as fatias avançam._ Pré-requisito: SDK do .NET 10.
>
> ```bash
> dotnet build FiscalHub.slnx
> ```

## Reforma Tributária (reform-ready)

O modelo `Mercadoria` carrega o **Grupo UB** da NT 2025.002 (IBS/CBS/IS, CST e cClassTrib)
por item como cidadão de primeira classe. "Reform-ready" aqui significa **transportar esses
campos sem perda** da origem até o destino — não validar nem apurar o imposto.

## Decisões / o que eu faria diferente

> _Seção viva — atualizada ao fim de cada fatia._
