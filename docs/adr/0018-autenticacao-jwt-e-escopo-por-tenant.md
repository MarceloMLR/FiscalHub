# ADR-0018: Autenticação (JWT próprio + PBKDF2) e escopo por tenant

- **Status:** Aceito
- **Data:** 2026-07-24

## Contexto

O painel e a API precisavam de login, e cada usuário só pode enxergar os dados do **seu tenant**
(multi-tenant). Duas perguntas separadas: *como o usuário prova quem é a cada request?* (sessão) e
*onde os usuários moram / como a senha é conferida?* (armazém).

## Decisão

- **JWT próprio** (não ASP.NET Core Identity completo). `POST /auth/login` valida credenciais contra
  uma tabela `Users` (senha guardada como **hash PBKDF2** — SHA-256, salt aleatório por senha, 100k
  iterações, feito só com a BCL, sem dependência) e emite um **JWT HS256** com claims
  `sub/email/name/tenant/role`. A API valida o Bearer (`AddJwtBearer`); uma **fallback policy** exige
  autenticação em **tudo**, e só `/auth/login` e o health marcam `AllowAnonymous`. `/auth/me`
  devolve a sessão pro SPA restaurar o login.
  - Por quê JWT próprio e não Identity: superfície mínima e engrenagens à mostra (claims, assinatura,
    validação) — bom pra um portfólio que se defende. Identity completo (auto-cadastro, reset de
    senha, lockout, 2FA) está fora do escopo da demo. Os dois não são exclusivos: dá pra usar
    Identity só pelo hash/armazém + JWT pra API; aqui optei por reimplementar o mínimo.
- **Token no SPA: localStorage como Bearer.** Simples e padrão pra SPA + API em portas diferentes.
  Trade-off honesto: localStorage é lido por JS, então é vulnerável a **XSS**. O hardening de
  produção é **refresh token em cookie httpOnly + access token curto na memória** (+ CSRF) — puxa
  cookie cross-origin (SameSite/Secure), CORS com credenciais e proteção CSRF, config que não vale
  pro dev local agora. Fica anotado.
- **Escopo por tenant.** O tenant vai no claim do token. Um `ITenantContext` lê o claim do
  `HttpContext`, e as consultas de leitura (documentos, grupos, execuções, agendamentos) **filtram
  por ele** — cada usuário só vê o seu. Integração manual/agendada usa o **tenant do usuário
  logado**, não do corpo da requisição. O **poll** e o **agendador** são de sistema (varrem todos os
  tenants) e não passam pelo `ITenantContext`.

## Alternativas consideradas

- **ASP.NET Core Identity completo** — robusto (ciclo de vida do usuário pronto), mas peso e telas
  demais pro escopo.
- **OIDC externo (Entra/Auth0)** — mais "produção real", mas exige registro de app/credencial e não
  roda 100% local.
- **Cookie httpOnly já agora** — mais seguro contra XSS, mais config no dev local; adiado (anotado
  como o caminho de produção).

## Consequências

- Usuários **dev semeados**: `admin@fiscalhub.local` (tenant-a, vê os dados) e `beta@fiscalhub.local`
  (tenant-b, não vê nada) — demonstram o isolamento. Senha dev `Fiscal@123` (só dev).
  (Verificado: sem token → 401; admin → 5 docs; beta → 0; senha errada → 401.)
- A **chave JWT** no appsettings é de dev; em produção é segredo (Key Vault), fora do repositório.
- Os endpoints de **debug** (`/trace`, `/drop`, download) recebem o tenant no path e **não** foram
  endurecidos com o claim — a UI só os alcança via dados já escopados, mas fica a ressalva.
