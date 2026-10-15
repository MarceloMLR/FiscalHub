## Why

A trava da **chave do conector** existe, está certa e tem teste. O que não tem teste é **quem a chama**.

`AdapterDeclarationGuard.EnsureDistinctAdapterDeclarations()` (`src/FiscalHub.Host/AdapterDeclarationGuard.cs`) recusa
duas declarações de adapter com o mesmo nome, de plataformas diferentes. Ela existe porque o desenho afirma, em dois
lugares, que **a chave do conector é o nome do adapter** — na própria interface (`IAdapterDeclaration.Adapter`: "o nome
do adapter: a chave do conector"; `Label`: "a identidade do conector continua sendo o `Adapter`, comparado exato") e no
design da change `connector-navigation-and-admin-json` —, mas a unicidade só era conferida **dentro de uma conta**: o
`ConnectorProfileService` recusa "o adapter '…' aparece N vezes, e a conta tem um conector por adapter", o que é por
conta, e não entre as declarações do deploy. Duas plataformas declarando o mesmo nome passavam, e a barra lateral
ficaria com duas chaves iguais: dois grupos indistinguíveis, e o Fiscal de um abrindo a tela do outro.

A **função** tem teste: `tests/FiscalHub.Host.Tests/AdapterDeclarationGuardTests.cs`, 4 casos, provado por inversão
(trocado o `g.Count() > 1` por `> 2`, 1 falha de 23; restaurado e conferido por hash). A **chamada** é uma linha no
`Program.cs`, logo depois do `builder.Build()`:

```csharp
app.Services.EnsureDistinctAdapterDeclarations();   // Program.cs:207
```

Apagar essa linha não quebra teste nenhum. A trava simplesmente para de existir, em silêncio. O limite está anotado na
própria tarefa 4.0 da change que a criou ("o teste cobre a guarda, e não a chamada dela no `Program.cs` — ao contrário
do D8, em que parar de chamar a extensão some com as rotas, aqui parar de chamar não quebra nada") e repetido no D8.

**É o mesmo padrão que mordeu duas vezes no dashboard em 2026-10-15:** a função que decide está certa e testada, e quem
a chama não tem teste. Os dois defeitos daquele dia moravam exatamente aí. A change `dashboard-component-test-stack`
fechou essa classe de limite do lado do dashboard e **registrou esta pendência como P1**, explicitamente fora do escopo
dela: "jsdom não alcança `src/`, e a solução aqui é harness de host". Está em "Riscos e pendências de higiene" do
`docs/STATUS.md`.

## O que foi investigado antes de propor

A pergunta factual da proposta era: **dá para compor o host pelo caminho real do `Program.cs` num teste, sem SQL, Blob
e Service Bus?** Foi medido, com um spike descartável (escrito, executado e removido; a árvore ficou limpa).

**A resposta é sim, e custa menos do que se supunha.** Os números, medidos em 2026-10-15 nesta máquina:

| # | Experimento | Resultado |
|---|---|---|
| A | `WebApplicationFactory<Program>` + `ConfigureAppConfiguration` | O `Program.cs` **real executou** e falhou na linha 69 com a mensagem do próprio host: *"SecretStore:VaultUri precisa ser uma URI https absoluta, e não ''. O host não sobe sem o cofre."* |
| B | A, com a configuração por **variável de ambiente** e uma declaração em conflito injetada | `InvalidOperationException` **da trava real**, com a mensagem completa: *"o adapter 'Avalara' é declarado por 'Avalara', 'Outra'…"* — em **563–691 ms** |
| C | B, com SQL, Blob, Service Bus **e** Key Vault apontados para **portas mortas** | A mesma exceção, ~1 s. **Nenhuma infraestrutura é necessária** |
| D | **Inversão:** linha 207 comentada, infra local de pé | **Nenhuma exceção** — a subida completou em 8 s (migrou o schema e semeou) |
| E | **Inversão:** linha 207 comentada, portas mortas | `SqlException` ("o computador remoto recusou a conexão"), em 17 s |
| — | Suíte `FiscalHub.Host.Tests` com o teste novo | 23 aprovados + 1, **1 s** no total |
| — | Pacote necessário | `Microsoft.AspNetCore.Mvc.Testing` 10.0.0 — restore 22,81 s, sem aviso de auditoria |

Três achados importam para o desenho:

1. **O entry point roda além do `builder.Build()`.** Era a dúvida real (se o harness capturasse o host no "HostBuilt"
   e interrompesse ali, a linha 207 nunca executaria e o teste seria uma fraude). O experimento A mostra o `Program.cs`
   executando, e o B mostra a exceção da linha 207 chegando limpa ao teste.
2. **A configuração do WAF chega tarde demais.** `ConfigureAppConfiguration` e `UseSetting` só são aplicados quando o
   host é construído — depois das leituras ansiosas que o `Program.cs` faz em `builder.Configuration` (linhas 45, 63,
   66, 69, 83, 111). O que chega a tempo é **variável de ambiente**, lida pelo `CreateBuilder` na linha 39. Isso não é
   contorno: é a mesma porta por onde a configuração entra em produção.
3. **A linha 207 vem antes de toda a infraestrutura** (`MigrateProcessingSchemaAsync` é a 214). Por isso o caminho
   feliz do teste — conflito, exceção — nunca toca banco, blob, fila nem cofre.

E um achado que **vira decisão de desenho** (D4): no experimento D, com a linha apagada e a infra local de pé, o teste
vermelho **subiu o host inteiro e escreveu no banco de dev** (migrate + seed, 8 s). Um teste cujo caminho vermelho mexe
na base de alguém é inaceitável. Por isso o teste fixa as portas mortas de propósito: verde sem infra, e vermelho
**sem escrever em lugar nenhum**.

**Nenhuma linha de produção precisa mudar.** O `InternalsVisibleTo("FiscalHub.Host.Tests")` já existe no
`FiscalHub.Host.csproj` — posto lá pelo harness de papel do D8 —, e é o que torna o `Program` gerado pelas top-level
statements visível ao projeto de teste. O spike compilou e rodou sem tocar em `src/`.

## What Changes

### 1. Um teste que compõe o host pelo caminho real da subida

Um arquivo novo, `tests/FiscalHub.Host.Tests/HostStartupGuardTests.cs`, com uma fábrica
`WebApplicationFactory<Program>` que:

- aponta **toda** a infraestrutura para portas mortas, por variável de ambiente;
- registra uma segunda declaração de adapter em conflito com a `Avalara` real (`FakeAdapterDeclaration("Outra",
  "Avalara")`, o duplo que o `RoleEndpointHarness` já tem);
- afirma que a subida falha com `InvalidOperationException` **cuja mensagem nomeia o adapter repetido e as duas
  plataformas**.

A afirmação é sobre o tipo **e** a mensagem, e isso é essencial: apagada a linha 207, o que acontece não é "outra
exceção" — é `SqlException` (experimento E) ou **exceção nenhuma** (experimento D). Um `Assert.Throws` frouxo passaria
no caso E. O teste fica vermelho nos dois mundos.

### 2. O caminho do AvaTax continua passando, e sem subir o host duas vezes

O segundo caso — **dois adapters da mesma plataforma**, que é o caminho de crescimento escrito na própria interface —
**não** ganha um segundo host. Compor o host sem conflito significaria chegar na linha 214 e precisar de SQL, e um
teste que precisa de infra acaba pulado (é a razão escrita no `FiscalHub.Host.Tests.csproj`). Esse caso já é coberto
pelos `AdapterDeclarationGuardTests`, que ficam como estão. O teste novo cobre **só** o que falta: que a trava é
chamada.

### 3. A regra sai do comentário e vira requisito publicado

Hoje a regra da chave única entre declarações vive em comentário de código e numa change arquivada. Nenhuma spec a
publica — conferido. Isso é metade do problema que esta change existe para fechar (ver "Capabilities").

### 4. A pendência P1 é riscada do `docs/STATUS.md`

Com evidência, e não por otimismo.

## Capabilities

### Added Capabilities

- **`platform-account-connectors`**: um requisito novo, **"A chave do conector é única entre as declarações do
  deploy"**, em `## ADDED Requirements`.

**Por que há delta, e por que ele não é backfill.** O CLAUDE.md §8 proíbe converter código legado em spec, e o §9
repete ("não converta o código legado em spec de uma vez"). A regra desta change já existe em código — então a
pergunta é legítima, e foi **decidida pelo Marcelo**: o requisito entra. O alvo daquelas regras é a documentação
retroativa **em massa** na adoção do OpenSpec, e este caso não é esse, por três razões — escritas aqui para que quem
ler depois não ache que a regra foi ignorada por descuido:

1. **A trava não é legado.** Ela nasceu em 2026-10-15, na change `connector-navigation-and-admin-json`, cujo delta
   cobriu a árvore de navegação e deixou de fora a invariante de que ela depende. Publicar agora corrige uma **omissão
   daquela fatia** — não documenta o passado.
2. **O requisito é de comportamento, e não de processo.** O que ele descreve é observável: o host se recusa a subir, e
   diz qual adapter está repetido e quais plataformas o declaram.
3. **É o único que fecha o segundo caminho de morte.** Hoje não há nada escrito a desrespeitar: quem apagasse trava e
   teste na mesma fatia não violaria requisito nenhum.

O objetivo da change é que **a trava não possa ser apagada em silêncio**, e ela pode ser apagada de duas formas.

| Como a trava morre | O que fecha |
|---|---|
| alguém apaga a linha 207 do `Program.cs` | o teste novo |
| alguém decide que a regra é arbitrária e remove trava **e** teste na mesma fatia | só a spec |

O teste sozinho fecha metade. Hoje não há requisito a desrespeitar: quem removesse os dois não violaria nada escrito, e
`openspec validate` ficaria verde. E o repositório trata a spec como fonte da verdade (CLAUDE.md §8) — deixar o teste
novo ser a **única** definição escrita do comportamento inverteria isso.

**Por que `ADDED`, e não `MODIFIED`.** `platform-account-connectors` é a dona do `IAdapterDeclaration` — o requisito
"Cada adapter de saída MUST declarar, em código…" (`spec.md:158-162`) mora lá. Caberia emendar a unicidade nele, mas
`## MODIFIED Requirements` obriga a carregar o requisito inteiro com todos os seus cenários, e esta regra é
**autônoma**: ela fala da relação entre declarações, não do conteúdo de uma. Requisito próprio, cenários próprios,
nenhum texto alheio arrastado.

**O requisito descreve o sistema, e não a suíte.** Decisão do Marcelo, e ela muda o texto do delta: o requisito fala
da **recusa de subida** e da frase que nomeia o adapter repetido e as duas plataformas. Não há "MUST ter teste" nele —
ainda que o repositório tenha precedente disso (`platform-response-trace`, `module-navigation`). Quem garante que a
trava é chamada é o teste desta change; quem define o que a trava faz é o requisito. São dois papéis, e misturá-los
publicaria a ferramenta no lugar do comportamento.

### Capabilities que NÃO mudam, e por quê

A régua é a da change anterior, e ela é estreita: **o delta vai onde uma frase publicada fica falsa, ou onde o
requisito deixa de fora o que o defeito atravessa.** Fora disso, não há delta — e isso é conclusão, não esquecimento.

- **`module-navigation` — nenhum delta.** É quem sofreria a consequência (dois grupos indistinguíveis na barra
  lateral), e por isso é a candidata óbvia. Mas o que ela publica é a **forma da árvore** — o grupo é um conector,
  rotulado pelo rótulo declarado, "e nunca pela plataforma nem pela chave do adapter" (`spec.md:129-130`). Nada ali
  fica falso, e nada ali está incompleto: a spec descreve o que a barra lateral desenha a partir de declarações
  válidas; **a validade das declarações é da spec que as define.** Pôr a regra nas duas seria duas donas para uma
  verdade.
- **`dashboard-brand-identity`, `platform-response-trace`, e qualquer spec de tela — nenhum delta.** Nenhum arquivo de
  `dashboard/` é tocado.
- **Nenhuma capability nova.** Uma `host-composition` teria como conteúdo "como o host é montado", que é detalhe de
  wiring, não capacidade do produto, e separaria a regra do requisito que a motiva.

### Sobre ADR

**Não há ADR nesta change.** A regra do repositório é ADR para decisão de arquitetura, e nenhuma fronteira se move
aqui: nada de produção muda, nenhuma porta nasce, nenhum adapter troca de lado. A decisão com consequência a jusante —
**como se compõe o host inteiro num teste** — é de técnica de teste, fica registrada no `design.md` (D1 a D5), e se
reverte apagando um arquivo e uma linha de `.csproj`. Se o Marcelo discordar, o ADR é barato depois; está nas Open
Questions.

## Non-goals

- **Refatorar produção para "ficar testável".** Nenhuma extração, nenhum ponto de extensão, nenhuma mudança no
  `Program.cs` nem no `AdapterDeclarationGuard.cs`. O spike provou que não é preciso. **Se alguma tarefa parecer exigir
  isso, pare e diga** — é a opção estrutural, e a decisão é do Marcelo (design, D7).
- **Mover a trava para dentro do registro das declarações.** É a alternativa estrutural, avaliada e **recusada** no
  `design.md` (D7). Muda estrutura de produção para um ganho que o teste já entrega.
- **Revisitar a regra.** A chave do conector continua sendo o nome do adapter, comparado exato. Não está em discussão:
  a change é sobre **provar** a trava.
- **Teste que afirma que o `Program.cs` contém determinado texto.** Prova a letra, não o comportamento, e quebra em
  qualquer refatoração inocente. Recusado explicitamente.
- **Subir o host do caso que passa.** O caminho sem conflito chega ao `MigrateProcessingSchemaAsync` e precisaria de
  SQL. Dois adapters da mesma plataforma continuam provados onde já estão, nos `AdapterDeclarationGuardTests`.
- **Teste de ponta a ponta, ou harness que suba o host com infraestrutura de verdade.** Outra conversa, outro custo.
  Um teste que precisa de infra acaba pulado, e um teste pulado não prova nada.
- **Estender o `RoleEndpointHarness`.** Ele compõe **pedaços** do host de propósito, e é bom nisso. O que falta aqui é
  o oposto: o `Program.cs` inteiro. São dois harnesses com duas finalidades (design, D3).
- **Provar as outras linhas do `Program.cs`.** `MigrateProcessingSchemaAsync`, `FillPendingIntegrationPathAsync`,
  `LocalSeed`, `SeedDevDataAsync`, a ordem do middleware — nada disso entra. A fatia é uma linha, a 207.
- **Qualquer arquivo de `dashboard/`.** jsdom não alcança `src/`, e esta pendência é .NET.
- **Cobertura como meta numérica.** Nenhum limiar, nenhum relatório, nenhuma porta de CI.

## Impact

- **Produção: nada.** Nenhum `.cs` de `src/`, nenhum endpoint, nenhum `.csproj` de produção. Isto é requisito, e não
  acaso: é o que separa esta change da opção estrutural.
- **`tests/FiscalHub.Host.Tests/FiscalHub.Host.Tests.csproj`**: uma entrada nova,
  `Microsoft.AspNetCore.Mvc.Testing` 10.0.0 — a casa do `WebApplicationFactory`. O
  `Microsoft.AspNetCore.TestHost` já está lá. O comentário de cabeçalho do arquivo, que hoje diz que o projeto sobe
  "um TestServer mínimo… sem SQL, sem Blob e sem Service Bus", ganha a segunda finalidade.
- **Arquivo de teste novo (1)**: `tests/FiscalHub.Host.Tests/HostStartupGuardTests.cs`.
- **`tests/FiscalHub.Host.Tests/RoleEndpointHarness.cs`**: o `FakeAdapterDeclaration` é reusado como está. O comentário
  de classe do harness, que hoje diz que a composição inteira do host "continua sendo provada pelo ponta a ponta e pela
  prova manual", fica **parcialmente falso** e é ajustado — uma linha dela passa a ter teste.
- **Spec**: um delta, `platform-account-connectors`, um requisito **adicionado**.
- **Docs**: `docs/STATUS.md` — a pendência **P1** sai de "Riscos e pendências de higiene", com a evidência. As P2 e P3
  (dashboard) **ficam**: não são desta fatia.
- **Suíte**: `FiscalHub.Host.Tests` passa de 23 para 24 testes. O teste novo custa ~0,7 s.
- **Dashboard**: nada. `npm test` e `npm run build` não são tocados — rodados na verificação final como guarda.
