# ADR-0040: Azure DevOps — um pipeline por artefato, e o pacote D365 versionado e guardado por release

- **Status:** Proposto
- **Data:** 2026-10-16

## Contexto

O repositório vai sair do GitHub para um projeto **FiscosysHub** na organização `adsnit` do Azure DevOps. É um repositório único: o hub (.NET), o dashboard, o pacote D365 e a documentação, e depois outros ERPs, como o Oracle.

Três forças:

1. **O pacote D365 para cliente precisa sair de pipeline.** O Create Deployment Package do Visual Studio, no UDE, gera o zip sem os binários do modelo (`d365/03`). A Microsoft manda gerar o pacote completo do LCS, e o unified package, no Azure DevOps.
2. **Cada artefato muda no seu tempo.** Um deploy do hub de um cliente Oracle não pode disparar nada do D365, e uma mudança no hub não pode gerar um pacote D365 novo.
3. **Os clientes D365 ficam em versões diferentes do pacote.** Uma versão nova não vai para todos de uma vez, então as anteriores têm de continuar disponíveis e documentadas.

## Decisão

**Um pipeline por artefato, disparado só pela pasta dele, e o pacote D365 com dois tipos de build: verificação, automática e descartável, e release, manual, versionada e guardada.**

### Pipelines

| Pipeline | Roda quando muda | Faz |
|---|---|---|
| `hub-ci` | `src/`, `tests/`, `Directory.Build.props`, `global.json`, `FiscalHub.slnx` | build e testes do .NET |
| `dashboard-ci` | `dashboard/` | `npm ci`, testes e build |
| `d365-pacote` | o metadado do modelo X++, `d365/build/` e o próprio YAML | compila contra os NuGet da versão base e gera os dois formatos |
| `oracle-*` (futuro) | `oracle/` | o mesmo molde |
| `hub-release`, `dashboard-release` (depois) | manual, com aprovação | deploy por cliente, com o fan-out do ADR-0020, e do host estático |

- Documentação, `openspec/`, `.claude/` e as ferramentas do `d365/` não disparam build.
- No Azure Repos, o gatilho `pr:` do YAML não vale: a validação de PR é pela branch policy da `main`, com o mesmo filtro de pasta.
- O gatilho fica no YAML principal, porque template não aceita gatilho.

### O pacote D365

- **Build de verificação.** Roda quando o modelo muda, na `main` e nos PRs que mexem nele.
  - Compila e gera os dois formatos.
  - Confere se o zip tem `AOSService/Packages/files/dynamicsax-fiscosyshubintegration.<versão>.zip` com os binários.
  - O artefato some pela retenção do projeto. **Não vai para cliente.**
- **Release.** Roda à mão, com a versão como parâmetro.
  - Publica os dois formatos e as notas da versão como Universal Package `fiscosyshubintegration-d365`, num feed só de releases (`fiscosys-releases`), promovido à view `@Release`.
  - Marca o commit com a tag `d365/v<versão>`.
  - A retenção de runs não apaga Universal Packages, e a view `@Release` fica fora da retenção por tempo: **toda versão liberada continua disponível até alguém apagar à mão.**
- **Versão em semver.**
  - MINOR: entidade ou campo novo, que é o caso normal.
  - PATCH: correção sem mudar o contrato.
  - MAJOR: remover ou renomear. Evitar.
  - A versão base do D365 (hoje 10.0.47) vai no nome e no CHANGELOG. Cliente abaixo de uma base nova fica na última versão da base antiga.
- **NuGet da Microsoft** da versão base (compilador, Platform, Application 1 e 2, Application Suite): num feed `d365-nuget` do projeto, fixados num `packages.config`. São baixados pelo Visual Studio (Power Platform Tools), sem projeto no LCS.
- **Registro no repositório:**
  - `d365/CHANGELOG.md`: o que entrou em cada versão e a versão base. É o texto que vai no e-mail do pacote.
  - `d365/implantacoes.md`: por cliente, os ambientes, se é LCS ou PPAC, a versão do D365 e a versão do pacote instalada.

### Antes de mandar para o cliente

Aplicar o release no fiscosysdev pelo caminho de um cliente, e não pelo Visual Studio:

1. Baixar o release do feed.
2. Aplicar o unified package com `pac package deploy`, que é como um cliente PPAC aplica.
3. Rodar o aceite: Postman e `validar-ambiente.js`.

O formato do LCS não tem onde ser aplicado aqui. Os binários são os mesmos do unified, e o primeiro ambiente LCS que recebe o pacote é sempre o sandbox do cliente.

### Regra para o hub

**O hub tem de funcionar com a versão mais antiga do pacote instalada em algum cliente.** Campo no `$select` que o pacote do cliente não tem responde 400. Quando o hub passar a ler um campo novo, ele precisa saber a versão do pacote daquele cliente: pelo perfil de conector (ADR-0019) ou pelo `$metadata`. O hub não muda agora; a regra fica registrada.

## Em aberto (decidir antes de criar)

1. **Layout do D365 no repositório.** O `.rnrproj` e o `Descriptor` vivem hoje fora dele, e o pipeline precisa dos dois dentro.
   - (a) Layout padrão do X++, com `d365/metadata/FiscosysHubIntegration/...` e `d365/projects/`, e o Visual Studio apontando para o repositório. Acaba o "gravar em três lugares".
   - (b) Manter `d365/model` e o pipeline monta a pasta.
2. **Agente do build D365.**
   - (a) O agente da Microsoft, com o feed de NuGet.
   - (b) Agente self-hosted na máquina com o metadado da 10.0.47. Dispensa os NuGet, mas o build só roda com a máquina ligada.
3. **O GitHub depois da migração:** arquivar (somente leitura) ou manter como espelho.

## Alternativas consideradas

- **Um pipeline só para o repositório inteiro:** cada mudança rodaria tudo e geraria pacote D365 sem motivo. Recusado.
- **Gerar o pacote no Visual Studio:** no UDE ele sai sem os binários (`d365/03`). Recusado.
- **Guardar os releases só como artefato de pipeline:** a retenção do projeto apaga os runs e os artefatos, e manter cada run à mão é frágil. Ficou o Universal Package num feed de releases.
- **Versão automática a cada build como versão de cliente:** a numeração andaria a cada merge, sem relação com o que foi liberado. Ficou a versão de verificação automática e a de release manual.

## Consequências

- **Melhora:**
  - deploy de Oracle, de hub ou de dashboard não toca no D365;
  - qualquer versão liberada do pacote pode ser baixada a qualquer momento;
  - o CHANGELOG e o registro de implantações dizem o que cada cliente tem;
  - o pacote é testado pelo caminho do cliente antes de sair.
- **Custa:**
  - manter o feed dos NuGet da Microsoft, que ocupa boa parte dos 2 GiB gratuitos do Azure Artifacts;
  - instalar as extensões "Dynamics 365 Finance and Operations Tools" e "Power Platform Build Tools" na organização.
- **Abre:** o mesmo molde para o pacote de outro ERP, e o fan-out de release do hub (ADR-0020), com o perfil de conector guardando a versão do pacote de cada cliente.

## Referências

- `d365/03-deploy-e-promocao.md`: o pacote do Visual Studio vazio, os dois modelos de ambiente e o all-in-one.
- [Build X++ com agente hospedado e NuGet](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/dev-tools/hosted-build-automation)
- [CI/CD no UDE e o download dos NuGet pelo Visual Studio](https://learn.microsoft.com/power-platform/developer/unified-experience/finance-operations-pipelines)
- [Create Deployable Package nos dois formatos](https://learn.microsoft.com/dynamics365/fin-ops-core/dev-itpro/dev-tools/pipeline-create-deployable-package)
- [PR trigger no Azure Repos é por branch policy](https://learn.microsoft.com/azure/devops/pipelines/yaml-schema/pr)
- [Retenção de runs (não governa Universal Packages)](https://learn.microsoft.com/azure/devops/pipelines/policies/retention)
- [Universal Packages no pipeline](https://learn.microsoft.com/azure/devops/pipelines/artifacts/universal-packages)
- [Armazenamento do Azure Artifacts](https://learn.microsoft.com/azure/devops/artifacts/artifact-storage)
