# Tarefas — provar a chamada da trava da chave do conector

A fatia é **uma linha**: `src/FiscalHub.Host/Program.cs:207`. A ordem é a do risco: primeiro a baseline e o pacote
(grupo 1), depois o teste vermelho antes de ser verde (grupo 2), depois a prova por inversão — que é a única coisa que
justifica a change existir (grupo 3), e só então a spec, os comentários que ficaram falsos e o `STATUS.md` (grupo 4).

- **Grupo 1:** baseline medida, pacote entra, suíte de hoje conferida.
- **Grupo 2:** o teste que compõe o host pelo caminho real, com a declaração em conflito.
- **Grupo 3:** a prova por inversão — apagada a linha 207, o teste **tem** que ficar vermelho.
- **Grupo 4:** a spec, os comentários, o `STATUS.md` e a verificação final.

Cada grupo de código termina com `dotnet build` (**0 avisos** — `TreatWarningsAsErrors`) e `dotnet test`. Com o Host
rodando, usar `--artifacts-path obj/guard` (dentro do repositório, nunca fora).

Uma tarefa só recebe `[x]` **com evidência** — a saída do teste, do build ou do comando. Tarefa movida ou feita pela
metade fica **aberta**, com a anotação do que faltou.

**Se alguma tarefa parecer exigir mudança em `src/` (produção), mover a trava para o registro das declarações, um teste
que afirma o texto do `Program.cs`, infraestrutura de verdade, ou qualquer arquivo de `dashboard/`: pare e diga.** São
os não-objetivos do `proposal.md`, e nenhum deles cabe aqui.

---

## Baseline a confirmar (medida no spike, 2026-10-15, nesta máquina)

| O quê | Número |
|---|---|
| `dotnet build` da solução | 1m38.9s, **0 avisos, 0 erros** |
| `FiscalHub.Host.Tests` | **23 aprovados**, 0 falhas |
| restore do `Microsoft.AspNetCore.Mvc.Testing` 10.0.0 | 22,81 s, sem aviso de auditoria |
| o teste novo, verde | ~0,7 s |
| o teste novo, vermelho (sem infra) | ~17 s |

Divergindo nesta execução, **vale o número novo**, e a divergência fica anotada.

**Medido na implementação, 2026-10-15:**

| O quê | Número medido | Contra a baseline |
|---|---|---|
| `dotnet build` da solução | 1m17.9s (baseline) / 53,95s (final), **0 avisos, 0 erros** | mais rápido |
| `FiscalHub.Host.Tests`, antes | **23 aprovados**, 0 falhas | igual |
| `FiscalHub.Host.Tests`, depois | **24 aprovados**, 0 falhas | +1, como previsto |
| solução inteira, depois | **1378 aprovados**, 0 falhas, 42 ignorados | +1 |
| restore do `Microsoft.AspNetCore.Mvc.Testing` 10.0.0 | **3,68 s**, sem aviso de auditoria | bem mais rápido (cache) |
| o teste novo, verde | **635–728 ms** | igual (~0,7 s) |
| o teste novo, vermelho (portas mortas) | **~21 s** (execução de 23,06 s com a sonda junto) | ~17 s → ~21 s |

**Duas divergências de comportamento, e não de número**, anotadas em 2.1 e 2.2: o "vermelho de configuração" do 2.1
não aconteceu (o content root resolve para o Host, e o `appsettings` dele supre o que falta), e o `UseSetting`
**chega** a tempo, ao contrário do que o D2 supunha — só o `ConfigureAppConfiguration` chega tarde.

---

## 1. A baseline, e o pacote

- [x] 1.1 Registrar a baseline **antes** de qualquer mudança: `dotnet build` e `dotnet test` da solução, guardando a
  contagem por assembly e os avisos. A guarda do repositório fecha com os dois.
  **Evidência:** `dotnet build --artifacts-path obj/guard` → **0 avisos, 0 erros**, 1m17.92s (o spike mediu 1m38.9s;
  vale o número novo). `dotnet test --artifacts-path obj/guard` → **1377 aprovados, 0 falhas, 42 ignorados** em 11
  assemblies, com `FiscalHub.Host.Tests` em **23 aprovados**.
- [x] 1.2 Acrescentar `Microsoft.AspNetCore.Mvc.Testing` **10.0.0** ao
  `tests/FiscalHub.Host.Tests/FiscalHub.Host.Tests.csproj` — a casa do `WebApplicationFactory`. **Só no projeto de
  teste**; nada em produção. Evidência: o diff do `.csproj` e a saída do restore.
  **Conferir o aviso de auditoria:** o repositório trata aviso como erro e já tem um pin de segurança por causa disso
  (`System.Security.Cryptography.Pkcs`, `STATUS.md`). Se o pacote trouxer GHSA, **pare e diga** — não pinar por conta
  própria.
  **Evidência:** uma linha nova no `.csproj`, com o comentário do porquê. Restore em **3,68 s**, "restaurado",
  **sem aviso de auditoria** (nenhum NU1902/NU1903) — e o build final fechou em 0 avisos. Nada em `src/`.
- [x] 1.3 Atualizar o comentário de cabeçalho do `.csproj`, que hoje descreve uma finalidade só ("um TestServer mínimo…
  sem SQL, sem Blob e sem Service Bus"). Ele passa a ter duas: aquela, e a composição do host inteiro — que também não
  precisa de infraestrutura, e é por isso que continua cabendo aqui.
  **Evidência:** o cabeçalho agora numera as duas finalidades e mantém a razão comum ("sem SQL, sem Blob e sem Service
  Bus, nos dois casos"), dizendo por que a segunda também dispensa infra: a trava corre antes da primeira conexão.
- [x] 1.4 **A suíte de hoje não muda.** `dotnet test` do `FiscalHub.Host.Tests` depois do pacote: os mesmos 23
  aprovados, 0 falhas. Evidência: a saída, comparada com a de 1.1.
  **Evidência:** `Aprovado! – Com falha: 0, Aprovado: 23, Ignorado: 0, Total: 23` — igual à de 1.1.

## 2. O teste que compõe o host pelo caminho real

- [x] 2.1 **Vermelho primeiro, e do tipo certo.** Criar `tests/FiscalHub.Host.Tests/HostStartupGuardTests.cs` com a
  asserção final já escrita, antes da fábrica funcionar. O vermelho esperado aqui é de compilação ou da configuração
  que falta — **não** vale como prova de nada; a prova é o grupo 3. Anotar qual vermelho apareceu.
  **Divergência do spike, e ela importa.** O vermelho **não apareceu**: com a asserção escrita e a fábrica sem
  configuração nenhuma, o teste já passou (2 s). O experimento A do spike não se reproduziu porque o
  `MvcTestingAppManifest` resolve o content root para o projeto do Host, e o `appsettings.Development.json` dele supre
  o cofre (e o `appsettings.json`, as três cadeias). Isso **não** torna o 2.2 opcional — faz o contrário: sem as
  portas mortas, o teste aponta para a infra de dev **de verdade**, e é o caminho vermelho que escreveria nela (D4).
- [x] 2.2 A fábrica: `WebApplicationFactory<Program>` apontando **toda** a infraestrutura para portas mortas, por
  **variável de ambiente** (design D2 e D4). As chaves, e o porquê de cada uma, ficam **comentadas no arquivo** — é o
  que impede que um teste quebrado por configuração nova seja lido como "teste chato" (design, Riscos):

  | Chave | Por que o host a exige na subida |
  |---|---|
  | `ConnectionStrings__Sql` | `AddSqlProcessingStore`, linha 66 |
  | `ConnectionStrings__Blob` | `new BlobServiceClient(...)`, linha 63 |
  | `ConnectionStrings__ServiceBus` | `AddServiceBusDocumentQueue`, linha 111 |
  | `SecretStore__VaultUri`, `SecretStore__Credential`, `SecretStore__EmulatorCertificateThumbprint` | `AddKeyVaultSecretStore`, linha 69 — "sem a seção, o host não sobe" |
  | `Avalara__DocumentsPath` | `FillPendingIntegrationPathAsync`, linha 219 |

  **Por que variável de ambiente, e não `ConfigureAppConfiguration`:** as leituras do `Program.cs` são ansiosas e
  acontecem antes de o harness poder configurar qualquer coisa (design D2; medido no spike — o host morreu na linha 69
  reclamando do cofre vazio).
  **Evidência:** as 7 chaves entraram na tabela `DeadPorts` do `DeadPortHostFactory`, cada uma com o porquê ao lado,
  todas em `127.0.0.1:9` (a porta de descarte). O cofre satisfaz as três condições dele numa porta morta: URI https
  absoluta, credencial `Emulator` e impressão fixada só valem em loopback.
  **O D2 foi conferido nesta execução, com uma sonda** (`SecretStore:VaultUri` inválido, e a pergunta "o host morre na
  linha 69 ou chega na 207?") — e **só metade se confirmou**: por `ConfigureAppConfiguration` a sonda **não** chegou
  (o teste passou, a trava recusou); por `UseSetting` **chegou** (`Assert.Contains` falhou com a mensagem
  *"SecretStore:VaultUri precisa ser uma URI…"*). A decisão fica na variável de ambiente, como aprovado; a correção
  está registrada no `design.md` (D2) e no `STATUS.md`, porque ela dispensaria o 2.3.
- [x] 2.3 **O isolamento das variáveis de ambiente.** Elas são do processo inteiro e o xUnit roda classes em paralelo:
  pôr e retirar na própria classe, e mantê-la fora da execução paralela (`[Collection]` própria, ou o equivalente que
  o projeto já use). Evidência: a suíte inteira verde, e os 23 testes de hoje com os mesmos resultados — um teste que
  contamina a vizinhança é pior do que o buraco que ele fecha.
  **Evidência:** `[CollectionDefinition(..., DisableParallelization = true)]` na `HostStartupCollection`, e a fábrica
  guarda o valor anterior de cada chave — **inclusive o "não existia"** — e o devolve no `Dispose`. A suíte do host
  fechou em **24 aprovados, 0 falhas**, com os 23 de antes intactos.
- [x] 2.4 A declaração em conflito: registrar `FakeAdapterDeclaration("Outra", "Avalara")` — o duplo que o
  `RoleEndpointHarness.cs` já tem, reusado como está — contra a `AvalaraDeclaration` real que o host registra
  (`AvalaraServiceCollectionExtensions.AdapterName` é `"Avalara"`). Injetar pelo `ConfigureServices` da fábrica, que
  **chega a tempo** porque corre durante a construção do host, e não antes dela.
  **Evidência:** reusado sem nenhuma alteração; a mensagem da exceção no verde nomeia `'Avalara'` e `'Outra'`, que é a
  prova de que a declaração chegou ao contêiner antes da conferência.
- [x] 2.5 A asserção: `InvalidOperationException` **e** a mensagem contendo `Avalara` e `Outra` (design D5). **Nunca**
  `Assert.Throws<Exception>` nem "lançou alguma coisa" — isso passaria no cenário em que a trava foi removida, que é
  exatamente o que o teste existe para pegar.
  **Evidência:** `Assert.Throws<InvalidOperationException>` + dois `Assert.Contains` ordinais. **E a necessidade da
  asserção estrita foi provada, não argumentada** — ver 3.1.
- [x] 2.6 Verde, e medir. Evidência: a saída, com o tempo do teste. Comparar com os ~0,7 s da baseline.
  **Evidência:** `Aprovado … [728 ms]` na primeira medição e `[635 ms]` na segunda — na faixa dos ~0,7 s do spike.
  A suíte do host passou de 23 para **24 aprovados**.
- [x] 2.7 **Conferir que o verde não tocou infraestrutura.** Com as portas mortas, não há o que tocar — mas a conta tem
  que fechar pelo tempo: um teste que tentasse SQL custaria dezenas de segundos, e não centenas de milissegundos.
  Anotar o tempo como a evidência disso.
  **Evidência:** 635–728 ms, contra os **~21 s** que o mesmo teste custa quando chega de fato ao SQL (3.1). A conta
  fecha: são duas ordens de grandeza, e o caminho que tenta conexão é visivelmente o outro.

## 3. A prova por inversão — a única coisa que justifica a change

- [x] 3.1 **Apagar a linha 207** do `Program.cs` (`app.Services.EnsureDistinctAdapterDeclarations();`) e rodar a suíte.
  **O teste novo tem que ficar vermelho.** Registrar: quantos falharam de quantos, o nome do teste, o tipo da exceção
  e o tempo. Esperado pelo spike: `SqlException` ("o computador remoto recusou a conexão"), ~17 s.
  **Evidência:** `1 de 2 falhou`,
  `HostStartupGuardTests.The_host_refuses_to_start_when_two_platforms_declare_the_same_adapter [FAIL]` —
  *"Assert.Throws() Failure: Exception type was not an exact match. Actual: typeof(Microsoft.Data.SqlClient.SqlException)
  — Erro de rede … O computador remoto recusou a conexão de rede."* Tempo total **23,06 s** (a tentativa de conexão
  respondeu por ~21 s). A pilha para em `Program.cs:214`, no `MigrateProcessingSchemaAsync`.
  **"2 de 2" porque a prova da asserção frouxa correu junto, de propósito:** um `[Fact]` temporário com
  `Assert.ThrowsAny<Exception>`, na mesma fábrica, **passou** — `Aprovado … Loose_assertion_probe [20 s]` — enquanto a
  asserção estrita falhava. É a prova direta do D5: a versão frouxa ficaria **verde** exatamente no cenário em que a
  trava foi removida. A sonda foi apagada em seguida.
- [x] 3.2 **Restaurar a linha, e conferir a restauração por hash** do arquivo — o método já usado nas tarefas 1.2, 4.8
  e 4.10 da `connector-navigation-and-admin-json`. Rodar a suíte de novo: tudo verde. Evidência: o hash antes e depois,
  e as duas saídas.
  **Evidência:** `sha256` antes e depois **idêntico** —
  `5a8ff79962a70406ea6050aaa1c5401d549361efbe3be3381fa9504871be482c`; e `git diff src/` **vazio**. Suíte do host de
  volta a `Com falha: 0, Aprovado: 24`, com o teste novo em 635 ms.
- [x] 3.3 **Anotar o que a inversão mostrou sobre o modo de falha**, e não só que falhou. O spike mediu dois modos
  diferentes conforme a infra local esteja de pé ou não (`proposal.md`, experimentos D e E). Se nesta execução o
  vermelho vier por "não lançou exceção nenhuma" em vez de `SqlException`, isso significa que a infra local **está** de
  pé e que as portas mortas do 2.2 não estão valendo — **pare e investigue**: é a diferença entre um teste que não
  escreve em banco nenhum e um que escreve no banco de dev.
  **Evidência:** o vermelho veio como `SqlException`, **conexão recusada** — o modo do experimento E, e não o do D.
  As portas mortas estão valendo: o caminho vermelho morre pedindo conexão ao `127.0.0.1:9`, e **não escreve em lugar
  nenhum**. A pilha confirma onde: `Program.cs:214`, a primeira linha que abre conexão, sete depois da trava.

## 4. A spec, os comentários e o `STATUS.md`

- [x] 4.1 A frase do `RoleEndpointHarness.cs` que diz que a composição inteira do host "continua sendo provada pelo
  ponta a ponta e pela prova manual" fica **parcialmente** falsa: ajustar, **sem prometer mais do que existe** — o que
  passou a ter teste é a chamada da trava, e não a composição inteira (design D3).
  **Evidência:** a frase ganhou a exceção nomeada — "com **uma** exceção … a chamada da trava da chave do conector tem
  teste próprio … **É uma linha do `Program.cs`, e não a composição inteira**" —, mais a razão de os dois harnesses
  continuarem separados. Dívida paga pela metade de uma linha, e escrita como tal.
- [x] 4.2 Conferir se o comentário da tarefa 4.0 citado no `AdapterDeclarationGuard.cs` e o limite anotado no
  `tasks.md` arquivado precisam de alguma correção de verdade. **A change arquivada não se reescreve** — se algo lá
  ficou falso, isso vira anotação no `STATUS.md`, e não edição de artefato arquivado.
  **Evidência:** o `AdapterDeclarationGuard.cs` **não precisou de correção** — ele descreve a regra e nunca afirmou
  nada sobre teste. O que ficou falso foi o limite do `tasks.md` arquivado da `connector-navigation-and-admin-json`
  (linha 182: "o teste cobre a guarda, e não a chamada dela no `Program.cs`"). O arquivo arquivado **não foi tocado**;
  a anotação está no `STATUS.md`, no mesmo bullet que risca o P1.
- [x] 4.3 Sincronizar o delta de spec: o requisito **adicionado** em `platform-account-connectors`. Se o Marcelo tiver
  decidido a Open Question 1 pelo "sem delta", é aqui que o arquivo `specs/` sai — e a razão fica escrita.
  **Decisão do Marcelo: o delta entra**, `ADDED` em `platform-account-connectors`. **Mas o requisito descreve o que o
  sistema faz, e não que existe teste** — e isso mudou o texto: saíram os dois bullets de "MUST ter teste" e o cenário
  "A trava da subida, provada por teste"; entraram, no lugar, a exigência de que a recusa aconteça **na subida** e
  **antes de qualquer infraestrutura**, com o cenário correspondente ("a recusa vem antes da infraestrutura").
  As três razões de isto não ser backfill proibido estão escritas no `proposal.md`.
  **O arquivo `specs/` não foi publicado em `openspec/specs/`:** o sync é parte do archive, que é passo do Marcelo
  depois da revisão. O delta fica na change.
- [x] 4.4 `docs/STATUS.md`: riscar a pendência **P1** de "Riscos e pendências de higiene", com a evidência (o nome do
  teste e o resultado da inversão do 3.1). **As P2 e P3 ficam** — são do dashboard, e não desta fatia. Acrescentar a
  sessão desta change, no padrão das anteriores.
  **Evidência:** o P1 está riscado (`~~…~~`) com o nome do teste e o resultado da inversão; **P2 e P3 ficam intactos**.
  Entraram ainda, na mesma seção viva: a **pendência derivada do D7 com o gatilho explícito** (um segundo caminho de
  composição — outro host, um worker) e o custo estimado; e a linha "**como compor o host inteiro num teste**", que é
  a resposta da Open Question 2 (sem ADR; a técnica mora no cabeçalho do arquivo de teste). A sessão nova está no fim
  do arquivo, no padrão das anteriores, e diz que a change **ainda não foi arquivada**.
- [x] 4.5 **Verificação final, os quatro:** `dotnet build` (0 avisos), `dotnet test` (a solução inteira, com a
  contagem por assembly), e — como guarda, mesmo esta fatia não tocando `dashboard/` — `npm test` e `npm run build`.
  Registrar os números.
  **Evidência:** `dotnet build --artifacts-path obj/guard` → **0 avisos, 0 erros** (53,95 s).
  `dotnet test --artifacts-path obj/guard` → **1378 aprovados, 0 falhas, 42 ignorados** em 11 assemblies (eram 1377;
  o +1 é o teste novo), com `FiscalHub.Host.Tests` em **24**. `npm test` → **18 arquivos / 177 testes**, 0 falhas.
  `npm run build` → verde, bundle no **mesmo hash** (`assets/index-Dy-tPrZW.js`, 997.17 kB; o único aviso é o de
  tamanho de chunk, anterior a esta change).
- [x] 4.6 `openspec validate --all --strict`: verde, com a contagem.
  **Evidência:** `Totals: 29 passed, 0 failed (29 items)` — as 28 specs publicadas + esta change, ainda não arquivada.
