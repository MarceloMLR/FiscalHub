# FiscalHub — Pacote de integração D365 F&O

Este diretório guarda o **lado Dynamics 365 Finance & Operations** da integração: as **data entities**
que publicamos (contrato de nome fixo, ADR-0022), o **business event** que dispara o gatilho, e o
**deployable package** que o cliente instala. Não faz parte do build .NET do middleware — é metadado do
F&O, com toolchain própria (Visual Studio + F&O dev tools, PPAC/Azure DevOps).

## Por que existe

O dado fiscal vem sempre da localização BR padrão (`FiscalDocument_BR`), mas ela normalmente não está
exposta no OData; cada implementador cria uma entidade pública com nome variável (`fiscaldocument_br2`…).
Em vez de customizar o adapter por cliente, **nós publicamos** entidades de **nome fixo**
(`FS_FISCALDOCUMENT_BR`) como projeção sobre as tabelas padrão. O adapter sempre lê o mesmo nome.

## Fases (fazemos uma de cada vez)

0. **Setup e conexão** — preparar o Visual Studio e conectar ao ambiente de dev (UDE).
   Guia: [`00-setup-e-conexao-do-visual-studio.md`](00-setup-e-conexao-do-visual-studio.md).
1. **Entidades** ← nosso foco. Criar e publicar `FS_FISCALDOCUMENT_BR` e ver respondendo no OData.
   Guia: [`01-criar-e-publicar-data-entity.md`](01-criar-e-publicar-data-entity.md).
2. **Gatilho**: conectar o Service Bus + criar o Business Event que dispara no status.
3. **Pacote**: exportar o deployable package e testar o import (no nosso próprio ambiente primeiro).

## Status atual

- [ ] Fase 1 — data entity `FS_FISCALDOCUMENT_BR` publicada e testada no OData
- [ ] Fase 2 — Business Event → Azure Service Bus
- [ ] Fase 3 — deployable package + import validado
