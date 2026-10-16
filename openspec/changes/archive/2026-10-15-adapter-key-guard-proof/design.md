# Design — provar a chamada da trava da chave do conector

O problema é de **uma linha**: `Program.cs:207`. Tudo aqui é sobre alcançá-la num teste sem pagar por isso com
estrutura de produção, com infraestrutura, ou com um teste que afirma texto.

Os números citados foram medidos em 2026-10-15, nesta máquina (Windows 11, .NET 10, containers de dev de pé), com um
spike descartável que foi removido. A tabela completa está no `proposal.md`.

---

## D1 — O teste compõe o host pelo caminho real, com `WebApplicationFactory<Program>`

**Decisão.** O teste usa `WebApplicationFactory<Program>` (pacote `Microsoft.AspNetCore.Mvc.Testing`), que invoca o
**entry point de verdade** do host — o `Program.cs` inteiro, com as mesmas 160 linhas de registro, a mesma ordem e a
mesma chamada da linha 207.

**Por que isto e não um harness que repita a composição.** Um harness que registrasse as mesmas coisas e chamasse a
trava provaria o harness, não o host — é exatamente o buraco que esta change existe para fechar. O valor do teste é
ser **indiferente à forma da ligação**: se amanhã a trava passar a ser chamada por um `IHostedService`, por um
`IStartupFilter` ou por um método de extensão outro, o teste continua valendo. É a propriedade que o
`proposal.md` pede e a única que pega a remoção "seja qual for a forma de ligação".

**A dúvida factual que isto tinha, e como foi resolvida.** Não era óbvio que o entry point rodasse **além** do
`builder.Build()`: se o harness capturasse o host no evento de construção e interrompesse o `Program.cs` ali, a linha
207 nunca executaria e o teste seria uma fraude silenciosa — passaria sem provar nada. Medido: o experimento A fez o
`Program.cs` real falhar na **linha 69**, com a mensagem do próprio host sobre o cofre, e o experimento B trouxe a
exceção da **linha 207** limpa até a asserção. O entry point roda, e a exceção de subida chega ao teste sem embrulho.

---

## D2 — A configuração entra por **variável de ambiente**, e não pelo harness

**Decisão.** A fábrica de teste define as chaves que o host lê na subida como variáveis de ambiente, antes de criar o
cliente.

**Por quê.** O `Program.cs` lê `builder.Configuration` de forma **ansiosa**, no corpo do arquivo — linha 45
(`Connector:Environment`), 63 (`ConnectionStrings:Blob`), 66 (`ConnectionStrings:Sql`), 69 (`SecretStore`), 83
(`PlatformEstablishments`), 111 (`ConnectionStrings:ServiceBus`). O `ConfigureAppConfiguration` é um callback aplicado
só **na** construção do host, isto é, depois dessas leituras.

Variável de ambiente é lida pelo `WebApplication.CreateBuilder(args)` da linha 39, antes de tudo. E não é um truque de
teste: é a mesma porta por onde a configuração entra em produção.

> **Correção medida na implementação (2026-10-15).** O spike havia concluído que *nem* `ConfigureAppConfiguration`
> *nem* `UseSetting` chegam a tempo. **Só a primeira metade se confirmou.** Medido com uma sonda — um
> `SecretStore:VaultUri` inválido posto por cada mecanismo, e a pergunta "o host morre na linha 69, ou chega na 207?":
> `ConfigureAppConfiguration` **não chegou** (o host chegou na 207 e a trava recusou, com a sonda ignorada);
> `UseSetting` **chegou** (o host morreu na linha 69 com o valor da sonda). O `UseSetting` entra na configuração do
> host, e não num callback.
>
> A decisão **fica como está** — variável de ambiente, como aprovado —, mas a consequência vale o registro: o
> `UseSetting` dispensaria o isolamento da seção abaixo, porque não é do processo. É conversa de outra fatia, não
> desta, e está nas pendências do `STATUS.md`.

**O cuidado que isso cria.** Variável de ambiente é do **processo inteiro**, e o xUnit roda classes em paralelo. As
variáveis são postas e retiradas pela própria classe de teste, e ela fica fora da execução paralela — como, é tarefa
(grupo 2). Um teste que contamina a vizinhança é pior do que o buraco que ele fecha.

---

## D3 — O `RoleEndpointHarness` fica como está. São dois harnesses, com duas finalidades

**Decisão.** Nada no `RoleEndpointHarness` muda, exceto o comentário que ficou parcialmente falso.

**Por quê.** O harness de papel (design D8 da `connector-navigation-and-admin-json`) compõe **pedaços** do host de
propósito: sobe um `TestServer` com a autenticação de verdade e as extensões de mapeamento de verdade, e nada mais.
Essa é a virtude dele — é rápido, não depende de configuração nenhuma, e o que ele prova é o registro das rotas. O que
falta aqui é o oposto: o `Program.cs` **inteiro**, com todas as suas exigências de configuração.

Fundi-los degradaria os dois: o harness de papel passaria a precisar das seis chaves de configuração da subida para
testar um 403.

O comentário da classe diz hoje que a composição inteira do host "continua sendo provada pelo ponta a ponta e pela
prova manual". Depois desta change isso fica **parcialmente** falso — uma linha dela passa a ter teste — e a frase é
ajustada, sem prometer mais do que existe. Dívida só se risca quando está paga, e aqui ela é paga pela metade de uma
linha.

---

## D4 — A infraestrutura aponta para **portas mortas**, de propósito

**Decisão.** O teste sobrescreve `ConnectionStrings:Sql`, `ConnectionStrings:Blob`, `ConnectionStrings:ServiceBus` e
`SecretStore:VaultUri` para endereços de loopback **onde não há nada escutando**.

**Por que não simplesmente deixar o `appsettings.json` do host valer.** Porque funcionaria — e seria perigoso. O
caminho **verde** do teste não toca infraestrutura nenhuma: a exceção da linha 207 acontece antes da 214, que é a
primeira a abrir conexão. Mas o caminho **vermelho** toca. Medido, experimento D: com a linha 207 comentada e os
containers de dev de pé, o teste vermelho **subiu o host inteiro e escreveu no banco de dev** — migrate, seed,
8 segundos. Um teste cujo modo de falha mexe na base de alguém é inaceitável; e o modo de falha é justamente o que
acontece na máquina de quem está mexendo no `Program.cs`.

Com as portas mortas, os dois caminhos ficam honestos:

| Situação | O que acontece | Quanto custa |
|---|---|---|
| a trava é chamada (hoje) | `InvalidOperationException` da trava, mensagem completa | ~0,7 s, nada tocado |
| a linha 207 é apagada | `SqlException`, "conexão recusada" | ~17 s, **nada escrito** |

Nos dois casos o teste é determinístico e não depende de a infra local estar de pé — nem se beneficia de ela estar.

**Efeito colateral aceito:** o vermelho custa 17 s, contra 0,7 s do verde. É o preço de não escrever no banco de
ninguém, e é pago só por quem acabou de quebrar a trava.

---

## D5 — A asserção é sobre o **tipo e a mensagem**, nunca "lançou alguma coisa"

**Decisão.** O teste afirma `InvalidOperationException` **e** que a mensagem nomeia o adapter repetido (`Avalara`) e as
duas plataformas que o declaram (`Avalara` e `Outra`).

**Por quê.** Um `Assert.Throws<Exception>` frouxo passaria no experimento E — a `SqlException` que acontece quando a
trava **não** é chamada também é uma exceção. O teste ficaria verde exatamente no cenário que ele existe para pegar:
verde-fraude, a pior categoria. A asserção sobre a mensagem não é capricho de texto: é o que distingue "a trava
recusou" de "a subida morreu por outro motivo".

Isso cria um acoplamento ao texto da mensagem. É acoplamento **desejado**: a mensagem é contratual — ela nomeia o
adapter e as plataformas para que quem sobe o host saiba qual pacote tirar, e isso vira cenário na spec. O teste afirma
os **nomes** que a mensagem tem que conter, e não a frase inteira.

---

## D6 — O caso que passa não sobe o host

**Decisão.** "Dois adapters da mesma plataforma passam" — o caminho do AvaTax, escrito na própria interface —
**não** ganha um teste de host. Ele continua provado nos `AdapterDeclarationGuardTests`, que ficam exatamente como
estão.

**Por quê.** Para o host **passar** da linha 207 sem conflito, ele segue para a 214 (`MigrateProcessingSchemaAsync`) e
precisa de SQL de verdade. Um teste que precisa de infra acaba pulado, e um teste pulado não prova nada — é a razão
escrita no cabeçalho do `FiscalHub.Host.Tests.csproj`, e ela continua valendo.

O que faltava não era a regra (coberta, 4 casos), e sim **a chamada**. Um teste que prova a chamada é suficiente para
provar a chamada; provar a regra de novo, por um caminho mais caro, seria duplicata — dois lugares para manter a mesma
verdade.

---

## D7 — A alternativa estrutural, avaliada e **recusada**

A outra saída seria eliminar o ponto de esquecimento: fazer a conferência acontecer como **consequência** de registrar
as declarações — um `AddAdapterDeclaration()` que confere ao registrar, um `IValidateOptions`, um `IStartupFilter` —
em vez de uma linha separada que alguém pode apagar.

**Recusada, e a razão é que ela deixou de ser necessária.** A instrução era parar e perguntar se este fosse o caminho
honesto. Não é, e o motivo é factual: a opção estrutural se justificava pela hipótese de que a chamada **não** fosse
testável. O spike mostrou que é, por **0,7 s** e **zero linha de produção**. Mudar a forma como todos os adapters se
registram, para ganhar o que um arquivo de teste já entrega, é pagar em estrutura o que já foi pago em teste.

Vale o registro do que ela custaria, caso o Marcelo queira revisitar: tocar o ponto de registro de **todo** adapter de
saída (hoje `AddAvalaraComplianceDispatcher`, amanhã os outros), trocar um `AddSingleton<IAdapterDeclaration, …>`
transparente por um método próprio — e, principalmente, **mover a conferência para antes de as declarações estarem
todas reunidas**: conferir no registro significa conferir incrementalmente, uma declaração contra as anteriores, o que
muda a mensagem de erro (que hoje nomeia *todas* as plataformas que declaram o nome repetido) e a ordem em que o erro
aparece. Não é gratuito, e não é obviamente melhor.

**O que continua verdadeiro, e vale dizer:** o teste novo prova que a trava **é chamada**; ele não torna a chamada
impossível de esquecer num caminho de composição futuro — um segundo host, um worker separado. Se isso aparecer, a
conversa estrutural volta, e aí com motivo próprio. Está nas Open Questions.

---

## D8 — Por que o requisito é publicado agora

Detalhado no `proposal.md` ("Capabilities"). Em uma frase: o teste fecha o caminho de apagar a **linha**, e só a spec
fecha o de remover **trava e teste juntos** — e hoje quem fizesse isso não violaria nada escrito.

---

## Riscos

- **O teste quebra quando o host ganhar uma exigência de configuração nova.** Se alguém acrescentar ao `Program.cs`
  uma leitura ansiosa obrigatória, o teste falha até que a chave entre na lista de variáveis de ambiente. **Isso é
  informação, não ruído:** o teste está dizendo que a subida passou a exigir mais. O risco real é alguém ler isso como
  "o teste é chato" e desligá-lo — por isso a lista de chaves fica comentada no arquivo, com o porquê de cada uma.
- **Dependência nova no projeto de teste.** `Microsoft.AspNetCore.Mvc.Testing` é o pacote oficial do
  `WebApplicationFactory`, mesma versão do framework (10.0.0), e só no projeto de teste — nada vai para produção. Sem
  aviso de auditoria no restore do spike; o build com ele presente ficou em 0 avisos. Confirmado na tarefa de
  verificação, com build limpo.
- **O `MvcTestingAppManifest` resolve o content root para o projeto do Host**, e por isso o `appsettings.json` do host
  é carregado no teste. As variáveis de ambiente sobrescrevem o que importa (D4), mas o teste **lê** aquele arquivo.
  Se uma chave obrigatória nova nascer só no `appsettings.json`, o teste continua verde e a lacuna não aparece. É
  limite conhecido, anotado aqui: esta fatia prova a chamada da trava, e não a completude da configuração.
- **O vermelho é lento (17 s).** Aceito, e explicado no D4.

---

## Open Questions — respondidas pelo Marcelo em 2026-10-15

1. **O requisito publicado é a escolha certa, ou isto é backfill proibido?** **Entra.** Não é backfill por três razões,
   escritas no `proposal.md` ("Capabilities"): a trava nasceu hoje e o delta da change que a criou a omitiu; o
   requisito é de comportamento, e não de processo; e é o único que fecha o caminho "removo trava e teste juntos".
   Fica `ADDED` em `platform-account-connectors`. **E o requisito descreve o que o sistema faz, não que existe teste:**
   a recusa de subida e a frase que nomeia o adapter repetido e as duas plataformas. Nada de "MUST ter teste" nele.
2. **ADR? Não.** Nenhuma fronteira se move. Mas "como compor o host inteiro num teste" é técnica que a próxima fatia
   vai querer, e ela não pode ficar só no histórico da change: vai num **cabeçalho do próprio arquivo de teste** (por
   que variável de ambiente e não `ConfigureAppConfiguration`, por que portas mortas, e o custo de cada caso) e numa
   linha do `docs/STATUS.md`.
3. **A conversa estrutural fica com gatilho marcado: sim.** Registrada como pendência, com o gatilho explícito —
   **quando nascer um segundo caminho de composição** (outro host, um worker), a opção 2 do D7 volta à mesa, porque aí
   o teste de um caminho deixa de cobrir o outro. O custo estimado no D7 vai junto.

---

## Suposições

- **O spike reproduz o que a implementação vai fazer.** O que foi medido é o mesmo desenho descrito aqui —
  `WebApplicationFactory<Program>`, variáveis de ambiente, portas mortas, declaração em conflito, asserção sobre tipo e
  mensagem. A implementação é escrever isso como teste de verdade, com nome e cuidado de isolamento (D2), e não um
  desenho novo a descobrir.
- **Os números valem para esta máquina.** 0,7 s do verde e 17 s do vermelho são medições locais com a infra de dev
  rodando. Num CI limpo, o verde tende a ser igual (não depende de nada) e o vermelho pode variar com o timeout de TCP
  do agente. As tarefas remedem.
- **O `InternalsVisibleTo` existente basta**, e o `Program` gerado pelas top-level statements continua visível ao
  projeto de teste. Provado no spike; se um dia o `Program.cs` ganhar uma declaração `public partial class Program`
  explícita, nada muda.
- **A infra local do Marcelo não foi tocada.** Os containers continuam de pé; o spike só apontou o teste para portas
  onde não há nada.
