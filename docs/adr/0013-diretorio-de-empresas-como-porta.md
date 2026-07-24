# ADR-0013: Diretório de empresas/filiais como porta (adapter JSON no dev, Avalara no cloud)

- **Status:** Aceito
- **Data:** 2026-07-24

## Contexto

A tela de integração manual precisa de dropdowns de **empresa** e **filial**. Em produção, essa
lista vem da Avalara — que expõe num fluxo de **dois passos**: primeiro `companies` (devolve o
`empresaID`), depois as `branches` daquela empresa. Não queremos que o host nem o dashboard fiquem
amarrados a esse formato específico, e no dev local nem temos credencial da Avalara pra chamar.

## Decisão

- Definir uma **porta** `ICompanyDirectory` na Application, com duas operações —
  `ListCompaniesAsync()` e `ListBranchesAsync(companyCode)` — que devolvem um **modelo padrão**
  (`Company { Code, Name }`, `Branch { Code, Name }`), independente da fonte.
- Primeiro adapter: **`FiscalHub.Adapters.Directory.Json`**, que lê um `companies.json` do
  content-root. Serve o dev local sem depender de rede/credencial, e vira o seed que casa com os
  CNPJs dos XMLs de exemplo (`12345678`, `98765432`).
- A **Avalara vira outro adapter da mesma porta** quando houver credencial — encapsula o fluxo de
  dois passos (companies → empresaID → branches) e o mapeia pro mesmo modelo padrão. Host e
  dashboard não mudam.
- Host expõe `GET /companies` e `GET /companies/{code}/branches` sobre a porta.

## Alternativas consideradas

- **Chamar a Avalara direto do host/endpoint** — vaza o formato de dois passos pra cima, amarra o
  dev local à credencial e dificulta testar. A porta isola isso.
- **Ler o diretório do próprio banco de rastreio** (derivar das notas já processadas) — só conhece
  empresas que já emitiram algo; a integração manual precisa listar empresas/filiais cadastradas,
  mesmo sem movimento. Fonte errada.

## Consequências

- Trocar de fonte = escrever um adapter novo e trocar o registro no DI; nada acima da porta muda.
- O `companies.json` é **dado de dev** (versionado como seed). Em produção, o adapter Avalara
  assume e o JSON sai de cena.
- A porta já devolve o modelo que os dropdowns consomem — a Fase 2 (integração manual) pluga
  direto, sem adaptação no front.
