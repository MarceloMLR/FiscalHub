# STATUS — FiscalHub

Documento de handoff entre sessões/máquinas. Atualizado ao fim de cada expediente.
Para retomar: leia este arquivo + os [ADRs](adr/) + o [brief de infra](infrastructure-brief.md).
(O "como trabalhamos" — Modo Mentor — vem do prompt inicial; re-cole ao abrir uma sessão nova.)

**Última atualização:** 2026-06-11

## Onde estamos

**Marco 1 — núcleo COMPLETO e testado (sem Azure):**
- Domínio `GoodsInvoice` (NF-e 55) + bloco da Reforma IBS/CBS/IS; envelope fino; 5 portas.
- Esteira `DocumentPipeline<T>.ProcessAsync`: idempotência → busca → validação → envio → registro.
- Lógica real testada: `NfeXmlParser` (XML→domínio), `GoodsInvoiceToAvalara` (domínio→Avalara),
  `GoodsInvoiceValidator` (validação de integração).
- Adapter de saída Avalara: `AvalaraComplianceDispatcher` + mock (`tools/MockComplianceApi`),
  tradução de status, gancho de token no-op, tratamento de 204.
- **24 testes verdes.** 4 ADRs (0001–0004). Estrutura hexagonal visível (`src/Adapters/{Inbound,Outbound}`).
- BMAD instalado; `project-context.md` gerado; fluxo de orquestração em uso.

## Em andamento

- **Fase de Infrastructure** (a casca de Azure), conduzida no BMAD com revisão de arquitetura.

## Próximos passos

1. **Cache de token Avalara** — feito à mão (concorrência + expiração + por tenant). _Próxima sessão._
2. Poll worker de status (com limite de consulta + status `Unconfirmed` — ver brief).
3. `XmlGoodsInvoiceSource` (Blob), `SqlProcessingStore` (Azure SQL), trigger de Service Bus.
4. Composição/DI + roteamento por tipo. Dev local (Docker + Azurite).
5. Dashboard React; adapters de prova de extensão (D365 mock, file export).

## Decisões recentes

- Estilo de envio: chamada direta no pipeline (fila de saída fica como evolução) — ADR-0004.
- 204 no `CheckStatus` = ainda pendente (`Submitted`), não erro.
- Poll terá limite (deadline + tentativas) e status `Unconfirmed` para "sem retorno da plataforma"
  (≠ rejeição de negócio) — a implementar.
- Commits em PT (sem acento); código/identificadores em inglês; termos fiscais BR mantidos.

## Threads abertas

- Versionar `docs/infrastructure-brief.md` e ignorar `_bmad-output/` no `.gitignore`.
- Documento na DLQ não grava `IntegrationError` no store — resolver na fatia de dashboard/DLQ.
- Seção "como construí com agentes" no README (narrativa do diferencial).
