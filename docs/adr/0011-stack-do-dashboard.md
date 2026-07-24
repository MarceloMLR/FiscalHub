# ADR-0011: Stack do dashboard

- **Status:** Aceito
- **Data:** 2026-07-23

## Contexto

O backend estava pronto e testado, mas invisível. Faltava uma tela que mostrasse os documentos, o
status de cada um (que muda sozinho quando o poll confirma) e a rastreabilidade (as três fotos). É
o payoff visível de todo o backend — e um artefato de portfólio.

## Decisão

- **Vite + React + TypeScript.** TS para tipar as respostas da API (espelhando `DocumentSummary`) e
  pegar erro de contrato em tempo de compilação.
- **MUI + DataGrid** para a UI. O coração da tela é uma tabela; o DataGrid entrega ordenação, filtro
  e paginação prontos, com visual enterprise imediato — o front fica polido rápido sem virar um
  projeto à parte.
- **TanStack Query** para o server-state: cache + **refetch automático em intervalo**. O status muda
  sozinho (poll), então a tela se atualiza sem reload. Fazer isso na mão (useEffect + setInterval)
  reinventaria mal o que a lib resolve.
- **Estrutura feature-based** com hooks de dados (`useDocuments`, `useTrace`) separando "como busca"
  de "como mostra". UI-state local (`useState`); sem store global (seria exagero no escopo).
- **Sem autenticação** nesta fatia — tela de leitura sobre dados locais. Anotado onde entraria (o
  `GET /documents` atrás de auth e filtrado por tenant em produção).
- O host passa a **serializar enums como texto** (`JsonStringEnumConverter`) para a API devolver
  `status`/`tipo` legíveis, não números.

## Alternativas consideradas

- **shadcn/ui + Tailwind** — visual mais moderno e ótimo pra portfólio, mas exige montar a tabela
  compondo primitivos (mais trabalho na peça central). Faria o front virar protagonista, o que não
  é o objetivo: aqui ele complementa um back forte.
- **CSS puro** — repo mais leve, mas lento pra ficar polido.
- **Fetch manual (useEffect/setInterval)** — reinventaria cache, refetch e estados de loading/erro
  que o TanStack Query já dá.

## Consequências

- O front tem seu próprio ciclo de build (npm), separado do build .NET. Roda com `npm run dev` na
  5173, consumindo a API na 5200 (CORS liberado localmente).
- Auth e escopo por tenant ficam para uma fatia futura. Alvo de deploy: Static Web Apps.
- A tela é read-only por ora; ações (reenvio, drop pela UI) entram depois, se fizer sentido.
