# Respostas reais do sandbox

Envelopes da quarta foto (ADR-0027), gravados pelo hub contra o sandbox da plataforma e já redigidos. Viram testes de
reprodução (design D15 da change `connect-avalara-sandbox`). Nenhum arquivo daqui pode ter credencial ou token: o teste
`SandboxFixtureTests` varre a pasta.

| Arquivo | Forma | Origem |
|---|---|---|
| `recusa-no-envio.json` | HTTP 400, ProblemDetails com `errors` por campo | `avalara.response.submit.json` da nota `brmf\|BRMF12-30000001`, em 2026-09-27, no host `api-gateway.sandbox.avalarabrasil.com.br` |

Não exercitados, e por isso sem arquivo (não se fabrica resposta): o aceite, a consulta de status e a recusa de credencial
pelo hub. Ver `docs/avalara-sandbox-primeiro-envio.md`.
