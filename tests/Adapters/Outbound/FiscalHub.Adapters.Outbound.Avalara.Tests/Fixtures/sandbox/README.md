# Respostas reais do sandbox

Envelopes da quarta foto (ADR-0027), gravados pelo hub contra o sandbox da plataforma e já redigidos, e as respostas da
listagem, curadas à mão no mesmo formato (abaixo). Viram testes de reprodução (design D15 da change
`connect-avalara-sandbox`). Nenhum arquivo daqui pode ter credencial ou token: o teste `SandboxFixtureTests` varre a pasta.

| Arquivo | Forma | Origem |
|---|---|---|
| `recusa-no-envio.json` | HTTP 400, ProblemDetails com `errors` por campo | `avalara.response.submit.json` da nota `brmf\|BRMF12-30000001`, em 2026-09-27, no host `api-gateway.sandbox.avalarabrasil.com.br` |
| `listagem-empresas-top5.json` | `{"value": [...]}`, 5 empresas em ordem de `empresaId`, com o `idPortalCompany` fora do que o hub pede | `GET /taxcompliance/v2/empresa?$top=5&$orderby=empresaId`, em 2026-10-06, no host `api-gateway.sandbox.avalarabrasil.com.br`, pelo Postman do Marcelo |
| `listagem-empresas-skip2.json` | `{"value": [...]}`, o terceiro e o quarto itens do `top5`: o `$skip` respeitado, e a ordem estável entre requisições | `GET /taxcompliance/v2/empresa?$top=2&$orderby=empresaId&$skip=2`, idem |
| `listagem-empresas-vazia.json` | `{"value": []}`: a página vazia também vem em envelope | `GET /taxcompliance/v2/empresa?$top=5&$orderby=empresaId&$skip=999`, idem |
| `identificadores-da-conta.json` | só os identificadores da conta: 8 `empresaId` e 14 `contribuinteId`, sem CNPJ, código ou descrição | `listing --tenant tenant-a --ids` da sonda, em 2026-10-08, no mesmo host. É a lista que o `SandboxFixtureTests` usa para recusar valor real nas fixtures inventadas e no mock (change `platform-listing-shape`, D10) |

Não exercitados, e por isso sem arquivo (não se fabrica resposta): o aceite, a consulta de status e a recusa de credencial
pelo hub. Ver `docs/avalara-sandbox-primeiro-envio.md`.

## As respostas da listagem (change `platform-listing-shape`)

A listagem não é fotografada pelo hub. As três respostas vieram de chamadas diretas pelo Postman, e foram curadas à mão
para o formato do envelope, com o `exchange` `listing`:

- **a query fica em `request.query`.** A forma depende dela: com opções de query, o envelope; sem nenhuma, o array puro. O
  `request.url` continua sem query, como na foto;
- **o host é fato, e não suposição:** é o `sandbox.baseUrl` do perfil do tenant-a no banco de dev,
  `https://api-gateway.sandbox.avalarabrasil.com.br/`;
- **o status 200 é derivado, e não lido,** e o campo `note` de cada arquivo diz por quê. É a mesma inferência que prova o
  `$select` aceito. Os cabeçalhos e o horário, que o Postman não mostrou, ficam de fora;
- **a curadoria é por uma lista do que fica:** o `empresaId` e o `codigoCIA` ficam reais, porque a reprodução depende
  deles (a `skip2` traz o terceiro e o quarto itens da `top5`). Todo outro valor de item sai como `[mascarado]`. O
  `SandboxFixtureTests` impõe a regra, e recusa qualquer CNPJ nesses arquivos.

Sem resposta gravada, e por isso sem arquivo: o `/empresa` sem opções de query, que devolve o array puro (verificado em
2026-10-02), e o `/contribuinte`, que devolve o envelope (verificado em 2026-10-02, sempre chamado com query). A query
exata do hub, com o `$select`, também não foi gravada: o que ela prova, o 2xx, o defeito de dev já provou.
