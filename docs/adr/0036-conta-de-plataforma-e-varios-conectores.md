# ADR-0036: A conta de plataforma — a credencial sobe, o perfil ganha conectores, e o ambiente vira o deploy

- **Status:** Aceito
- **Data:** 2026-10-13
- **Revisa:**
  - **[ADR-0019](0019-perfil-de-conector-por-tenant.md):** o perfil deixa de ter **um** adapter de saída e passa a ter uma
    coleção de conectores. O `Environment` sai dos campos comuns.
  - **[ADR-0020](0020-topologia-de-deploy-por-cliente-e-frontend-unico.md):** a topologia ganha a segunda dimensão. Não é
    uma instância por cliente, é uma instância por **(cliente, ambiente)**.
  - **[ADR-0027](0027-credencial-por-tenant-e-resposta-da-plataforma.md):** a credencial deixa de ser do conector e passa a
    ser da **conta de plataforma**. O caminho do segredo perde o `outbound` e perde o ambiente.
- **Complementado por:** [ADR-0037](0037-conector-modulo-integracao.md) (o que vive dentro de um conector),
  [ADR-0038](0038-uma-esteira-tres-estilos-de-saida.md) (o que a esteira faz com ele) e
  [ADR-0039](0039-dashboard-por-conector-e-o-tecnico-do-admin.md) (como ele aparece na tela).
- **Change OpenSpec:** `openspec/changes/platform-account-and-connectors`, junto com o ADR-0037. As capacidades são
  `platform-account-connectors`, `connector-secret-references`, `connector-credential-test`,
  `avalara-tenant-authentication`, `avalara-document-contract`, `avalara-establishment-listing`,
  `platform-establishment-resolution`, `compliance-dispatch-outcome` e `module-navigation`.

> **Nota de 2026-10-14.** A decisão continua valendo. Três pontos mudaram na implementação (change
> `platform-account-and-connectors`, design D1, D11 e D13) e ficam registrados aqui para quem ler só este documento:
>
> 1. **Fora do conector fica só a credencial da conta.** O §2 diz que "um campo de segredo nas settings de um conector é
>    recusado". A gravação recusa o `clientId` e o `clientSecret`, que são a credencial da plataforma. Um segredo próprio
>    do produto (`apiKey`…) segue a regra de nomes e vai ao cofre com escopo de conector
>    (`fh-{tenant}--connector--{adapter}--{campo}`), porque não é a credencial da conta.
> 2. **A conta guarda também o endpoint de token.** O desenho do §1 mostra a conta com o `clientId` e a referência do
>    segredo. Ela ganhou o `tokenUrl`, sem derivação da URL base de um conector, e o `Avalara:TokenPath` saiu do host:
>    derivado de um conector, o endpoint mudaria com a base, e dois conectores da mesma conta pediriam dois tokens. Ele é
>    obrigatório para enviar e para testar, e não para gravar.
> 3. **Só a conversão do ambiente é irreversível, e a migração não é só mecânica.** Foram três migrações, cada uma na sua
>    transação. A primeira (o ambiente sai do perfil) tem o `Down` que lança, como diz a última consequência. As outras
>    duas (a conta e o conector; os módulos e as integrações) têm volta: o `Down` delas recusa, antes de qualquer escrita,
>    o que a forma anterior não comporta. E o `Up` das três recusa o dado ambíguo em vez de adivinhar — ambiente em branco
>    ou sem seção, adapter fora da grafia declarada, `clientSecret` em texto, chave em outra caixa —, nomeando o que
>    corrigir. Um script só de leitura, em `docs/preflight/`, acha essas linhas antes do deploy.

## Contexto

O FiscalHub nasceu com **um** destino. O `TenantConnectorProfile` tem um `OutboundAdapter` e um blob de settings, as
settings de saída aninham `{ "sandbox": {…}, "production": {…} }`, e o nome do segredo no cofre é
`fh-{tenant}--outbound--sandbox--clientsecret`. A singularidade do destino e a escolha de ambiente vazaram para a
**forma do dado** e para o **nome da chave do cofre**.

A Avalara vende mais que o Tax Compliance: vende também o **AvaTax**, com cálculo de imposto e emissão. O primeiro
cliente real entra agora, e config viva é a coisa mais cara de migrar depois: o que hoje é uma migração EF numa base
sem cliente vira, com cliente em produção, janela e risco.

**A pergunta que fechou a decisão:** do que existe hoje, o que é *forma de dado* — e portanto caro de mudar com cliente
dentro — e o que é *nome*, que se troca a qualquer momento? Só a primeira categoria entra agora.

Duas respostas do Marcelo deram a forma:

- **A credencial é por ambiente, não por ferramenta.** A TMSA tem um `clientId`/`clientSecret` para produção e outro
  para sandbox; o token do sandbox serve o Tax Compliance e o AvaTax de sandbox.
- **Cada ambiente vira um deploy.** `tmsa.fiscosyshub.com.br` e `tmsa.sandbox.fiscosyshub.com.br`, com grupos de
  recurso separados.

## Decisão

### 1. O perfil deixa de ter um conector e passa a ter uma coleção

```
tenant
└── conta de plataforma: Avalara         ← clientId + referência do segredo
    ├── conector: Tax Compliance         ← URLs base + o que o ADR-0037 descreve
    └── conector: AvaTax
```

- **A conta** guarda a credencial da plataforma. Uma conta por plataforma, por tenant.
- **Cada conector** é identificado pelo **adapter**, único dentro da conta. Nada de chave separada: a chave precisa ser
  estável (entra no nome do segredo) e segura para o cofre, e o nome do adapter já é as duas coisas, além de já ser a
  identidade que o código usa. Inventar uma segunda identidade criaria a pergunta "o que fazer quando divergem".
- **A entrada (ERP) e os chamados continuam um cada.** Só a saída era plural.

### 2. A credencial é da conta, e nunca do conector

A credencial é uma só para todos os conectores da conta: o token emitido vale para todos os produtos daquela plataforma
naquele ambiente. Logo:

- **Nenhum conector tem credencial própria.** Um `clientId` ou um campo de segredo nas settings de um conector é
  recusado na gravação.
- **O cache de token chaveia pela conta**, e não pelo conector. Sem isso, dois conectores pedem dois tokens para a mesma
  credencial e a plataforma vê o dobro de emissão sem motivo — e, pior, a mesma referência `kv:` acaba duplicada em dois
  lugares da config, onde pode divergir.

**Por que a credencial é campo tipado e não um bloco dentro de um blob.** É o ADR-0019 aplicado, não contrariado: a
regra dele é "campo comum a todos os adapters é tipado; o que muda por adapter é blob". Credencial OAuth de client
credentials é comum a toda plataforma de compliance. O que muda por produto continua blob, no conector.

### 3. O ambiente sai do perfil e vira propriedade da instância

O ADR-0020 já pusera uma instância por cliente. A topologia ganha a dimensão de ambiente: **uma instância por
(cliente, ambiente)**, com grupos de recurso e hosts separados. Com isso a instância **sabe** em que ambiente roda:

- o `Environment` sai do `ConnectorProfileRequest` e do formulário;
- as settings de saída perdem o aninhamento `{ sandbox, production }` — uma instância, um conjunto;
- o caminho do segredo deixa de dizer `outbound.sandbox.clientSecret` e passa a dizer `platform.avalara.clientSecret`;
- cada instância tem um cofre, um ambiente, uma credencial.

Isso apaga um risco que existe hoje: a configuração de produção e a de sandbox moram na mesma instância, e o que separa
as duas é o valor de um campo. Com o deploy separado, mandar para produção por engano de seleção deixa de ser possível
**por construção** — o mesmo argumento que o ADR-0020 usou para o login cruzado.

**O eixo não é o do ASP.NET.** Development/Staging/Production diz como o host se comporta; este diz contra que ambiente
da plataforma o cliente integra. Um host em Development aponta para o sandbox da Avalara, e os dois eixos não coincidem.
O host recusa subir sem o valor, ou com um valor que não é um dos dois: um padrão silencioso aqui é a mesma classe de
erro que a mudança veio apagar.

### 4. O vocabulário

"Adapter de saída", no singular, sai do nome do campo, das specs e da tela, e vira **conector**. "Ambiente" sai da tela
e vira um selo lido da instância.

## Consequências

- **A migração é mecânica e acontece com a base vazia.** O perfil existente tem um conector e um ambiente: vira um
  perfil com uma lista de um. Feita depois, com cliente em produção, seria janela e risco.
- **O segredo não migra junto.** O nome muda, e o cofre é endereçado por nome. A migração é EF Core e **não fala com o
  cofre**: pôr uma dependência de rede dentro de uma migração de schema faria a subida do host depender do cofre estar
  no ar, e uma migração que falha pela metade por timeout é bem pior que reconfigurar um campo. Depois dela o segredo
  aparece como não configurado, e o envio falha alto apontando o campo.
- **A seção do ambiente que não é o da instância é descartada.** Configuração que nada lê é configuração em que alguém,
  meses depois, acredita.
- **O cofre perde a dimensão de ambiente.** Um cofre por instância, uma credencial dentro.
- **O fan-out do ADR-0020 passa a casar por (cliente, ambiente)**, e a lista de conectores vira o metadado de "quem usa
  qual adapter" — agora plural, que é o que o pipeline já queria consultar.
- **Custo assumido:** com o deploy separado por ambiente, não há como pilotar um produto novo em sandbox *contra os
  dados de produção* — a instância de sandbox tem outra base. É o preço do isolamento, e é o mesmo que o ADR-0020 já
  pagou entre clientes.
- **A migração é irreversível.** O `Down` lança. Uma migração EF não lê `IConfiguration`, e depois do `Up` nenhuma linha
  guarda o ambiente — não há de onde reconstruir a seção descartada. Fingir um `Down` que grava um ambiente inventado
  seria pior que admitir que não há volta. Como não há cliente em produção, o custo é recriar a base de desenvolvimento.

## Alternativas consideradas

- **Manter tudo em JSON, com a conta como um objeto dentro das settings de saída.** Rejeitada: mantém a credencial — a
  única coisa do perfil com referência ao cofre — num lugar que só a varredura de nomes acha, e uma coleção dentro de um
  blob não tem como ter chave única garantida pelo banco.
- **Uma chave de conector separada do nome do adapter.** Rejeitada no §1.
- **Ambiente por conector** — Tax Compliance em produção e AvaTax em sandbox na mesma instância. Rejeitada pela
  topologia do §3: o deploy por ambiente a torna impossível, e essa impossibilidade é a proteção, não a limitação.
- **Deixar tudo como está e migrar quando o AvaTax existir.** Rejeitada pelo motivo que abriu o ADR: com cliente em
  produção, config viva não se refatora, se migra.
