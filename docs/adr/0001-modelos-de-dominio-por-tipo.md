# ADR-0001: Modelos de domínio por tipo de documento, não um modelo canônico único

- **Status:** Aceito
- **Data:** 2026-06-08

## Contexto

O hub recebe diferentes tipos de documento fiscal (mercadoria/NF-e 55, transporte/CT-e 57,
serviço/NFS-e). Precisamos de uma representação interna — a "verdade" do conector — que
seja estável quando se troca o ERP de origem ou o sistema de compliance de destino.

A tentação natural é criar um único "modelo canônico" que represente qualquer documento.

## Decisão

Cada tipo de documento tem seu **próprio modelo de domínio coeso** (`Mercadoria` para a
NF-e 55, e futuramente `Transporte` para o CT-e 57, etc.). Não existe um modelo único que
sirva para todos.

Para a esteira tratar todos de forma uniforme, existe um **envelope fino** comum
(`DocumentoEnvelope`) que carrega só o mínimo (id, tipo, tenant, status, chave natural,
correlação, timestamps) — e **não** o corpo do documento.

## Alternativas consideradas

- **Modelo canônico único** — um só tipo com todos os campos possíveis. Prós: um lugar só.
  Contras: vira um *god model* cheio de campo opcional ("nullable soup"); mercadoria,
  transporte e serviço são domínios diferentes, e forçá-los no mesmo molde acopla coisas
  que não têm relação e quebra invariantes. Cada mudança em um tipo arrisca os outros.

- **Modelos por tipo (escolhido)** — cada documento com seu modelo. Prós: coesão, invariantes
  honestas por tipo, evolução isolada. Contras: algum código de esteira precisa lidar com
  múltiplos tipos — resolvido pelo envelope fino.

## Consequências

- Adicionar um novo tipo (CT-e, NFS-e) é criar um novo modelo + seus mappers, sem tocar nos
  existentes — prova a extensibilidade no Marco 2.
- O envelope precisa de disciplina para permanecer mínimo: se ganhar campos de negócio, o
  *god model* volta pela porta dos fundos.
- A esteira opera sobre o envelope; só os adapters/mappers conhecem o modelo concreto.
