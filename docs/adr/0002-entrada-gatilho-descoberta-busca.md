# ADR-0002: Entrada — separação gatilho / descoberta / busca, com portas genéricas

- **Status:** Aceito
- **Data:** 2026-06-08

## Contexto

A integração pode começar de formas diferentes por cliente: agendada pela tela (busca por
período/companhia/estabelecimento), automática por evento do ERP (ex.: D365), ou manual. Além
disso, queremos uma esteira única que sirva a qualquer tipo de documento (Mercadoria hoje;
CT-e, NFS-e depois).

## Decisão

Separar três responsabilidades:

1. **Gatilho** — *quando* a integração começa (scheduler, listener de evento, ou chamada
   manual). É infraestrutura que dirige as portas; não é uma porta do núcleo.
2. **Descoberta** (`IDocumentDiscovery`) — *quais* documentos existem para certos critérios.
   Usada no modo pull; devolve referências, sem o conteúdo.
3. **Busca** (`IInboundSource<TDocument>`) — traz o documento completo por referência (o
   "fetch" do claim-check), já no modelo de domínio.

As portas de entrada e saída são **genéricas** em `TDocument`, para a esteira ser escrita uma
única vez e reutilizada por todos os tipos.

## Alternativas consideradas

- **Portas por-tipo** (`IGoodsInvoiceSource`, etc.) — explícitas e legíveis, mas N tipos geram N
  interfaces quase iguais e impedem escrever a esteira de forma genérica (exigiria duplicação ou
  reflexão). Atrita com a tese da esteira reutilizável.
- **Fundir descoberta e busca num passo** — natural num pull (uma query traz tudo), mas o modo
  evento só precisa da busca. Separar permite os dois modos sem reescrever a esteira.

## Consequências

- Um adapter pull implementa descoberta + busca; um adapter de evento implementa só a busca.
- A esteira processa documento a documento por referência, ganhando idempotência/retry por item.
- A seleção de qual origem usar é feita pelo perfil do tenant via a propriedade `Origin`.
- Generics adicionam um degrau de abstração — aceito, por servir diretamente à pluggabilidade.
