# FiscalHub — Painel

Dashboard de leitura do FiscalHub: lista os documentos com status e mostra a rastreabilidade
(as três fotos: fonte → domínio → destino) de cada um. Vite + React + TypeScript, MUI (DataGrid)
e TanStack Query.

## Rodando

Requer o host da API de pé (porta 5200) e os containers (`docker compose up -d`) — veja
[`docs/RUNNING.md`](../docs/RUNNING.md).

```bash
cd dashboard
npm install
npm run dev
```

Abre em `http://localhost:5173`. A tabela se atualiza sozinha a cada 5s (o status muda quando o
poll confirma). Clique numa linha pra ver as três fotos.

A URL da API vem de `VITE_API_BASE_URL` (default `http://localhost:5200`) — copie `.env.example`
para `.env` se precisar mudar.

## Estrutura

```
src/
  api/client.ts              fetch + tipos (espelham os DTOs do C#)
  features/documents/
    DocumentsPage.tsx         tabela (DataGrid) + painel de detalhe
    DocumentDetail.tsx        as três fotos, em abas
    StatusChip.tsx            badge colorido por status
    useDocuments.ts           useQuery com auto-refetch
    useTrace.ts               useQuery do /trace, categoriza as fotos
```

Sem autenticação nesta fatia (tela de leitura sobre dados locais); em produção o `GET /documents`
fica atrás de auth e filtrado por tenant.
