# ADR-0019: Perfil de conector por tenant (config em banco, segredos por referência)

- **Status:** Aceito
- **Data:** 2026-07-24

## Contexto

Cada cliente (tenant) roda uma combinação própria de adapters: entrada por um ERP (Dynamics 365,
iScala…) e saída por uma plataforma de compliance (Avalara, Thomson Reuters…). As credenciais são
por **ambiente** (sandbox e produção têm secret/token diferentes), o **schema de settings muda por
adapter**, e a **capacidade de tempo real varia** (tem cliente que só integra agendado/manual). Até
aqui tudo era single-tenant hardcoded (um Avalara, uma fonte). Precisávamos declarar isso por tenant.

## Decisão

- Um **`TenantConnectorProfile` por tenant**: adapter de entrada + settings, adapter de saída +
  settings, ambiente ativo e flag de tempo real. Os **campos comuns são tipados** (ambiente,
  realtime, nomes dos adapters); as **settings de cada adapter são um blob JSON** — cada adapter tem
  seu próprio schema, então um blob por adapter é mais fiel que colunas fixas.
- **Segredos NÃO ficam em claro.** As settings guardam **referências** (`kv:...`), nunca o valor —
  em produção resolvidas no **Key Vault**. No banco fica o não-secreto (adapter, urls, flags) + as
  referências.
- **Config gerida por Admin**: `GET`/`PUT /connector` exigem a role `Admin` e são **escopados ao
  tenant do usuário** (ninguém edita o perfil de outro tenant).
- `GET /info` passa a ler o **ambiente do perfil do tenant** (cada tenant o seu), no lugar de uma
  config global.

## Alternativas consideradas

- **Colunas tipadas fixas pra toda config** — rígido demais: cada adapter novo (Thomson, outro ERP)
  exigiria migração. O blob JSON por adapter absorve schemas heterogêneos.
- **Guardar segredos no banco** — recusado; segredo fora do app (Key Vault), por referência.

## Consequências

- Adicionar um adapter novo = novo nome + schema de settings próprio, **sem migração** de banco.
- A **resolução do adapter por tenant em runtime** (uma factory que lê o perfil e injeta as settings
  no dispatcher/fonte) é o próximo passo — aqui ficou o modelo + persistência + config. Enquanto ela
  não chega, o processamento ainda usa o adapter único registrado; o perfil já dirige o `/info`.
- Perfis de dev semeados: tenant-a (Dynamics 365, tempo real) e tenant-b (iScala, sem tempo real),
  os dois pra Avalara com credenciais próprias por ambiente — demonstram o modelo.
