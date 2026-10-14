# ADR-0037: Conector → módulo → integração — a integração é a unidade de endpoint, fila e lote

- **Status:** Aceito
- **Data:** 2026-10-13
- **Revisa:**
  - **[ADR-0036](0036-conta-de-plataforma-e-varios-conectores.md):** o conector deixa de ter **uma** URL base e passa a
    ter uma por versão de API. O rascunho daquele ADR dizia que os módulos ficavam no tenant; não ficam.
  - **[ADR-0029](0029-integracao-automatica-e-o-poll.md):** a integração automática deixa de ser um interruptor do
    perfil e passa a ser por integração, onde ela existe.
  - **`module-navigation`:** o módulo deixa de ser uma lista do tenant e passa a vir do conector.
- **Complementa:** [ADR-0036](0036-conta-de-plataforma-e-varios-conectores.md).
- **Change OpenSpec:** `openspec/changes/platform-account-and-connectors`, junto com o ADR-0036 (a forma de dado: módulos,
  integrações, URLs base por versão, caminho e lote). As filas por integração (§4) e a recusa por registro (§6) ficam
  para as fatias seguintes.

> **Nota de 2026-10-14.** A decisão continua valendo. Cinco pontos mudaram, ou ficaram para depois, na implementação
> (change `platform-account-and-connectors`, design D5, D7, D10 e D17), e ficam registrados aqui para quem ler só este
> documento:
>
> 1. **O agendamento não foi tocado.** O "não aparece no agendamento" do §2 e o "o agendamento passa a agrupar por
>    conector e módulo" das consequências ficam para a fatia do agendamento por integração: o agendamento de hoje não tem
>    seletor de integração. A integração sem caminho já não despacha: o envio a trata como configuração do conector, sem
>    requisição.
> 2. **A tela mostra todas as bases que o adapter declara**, e não só as que os módulos ligados usam (§3). A exigência
>    ficou na gravação: uma integração ligada sem a base da versão dela é recusada com 400, que nomeia a versão e as
>    integrações que a usam. O "a tela do conector diz isso em vez de falhar no primeiro envio" vale pela recusa, e não
>    por um campo que aparece.
> 3. **O tenant sem conector mostra o Fiscal.** A barra lateral é a soma dos módulos ligados nos conectores (§7), mas um
>    tenant sem conector nenhum responde `["Fiscal"]`, e não a lista vazia, que esconderia as notas já gravadas.
> 4. **A declaração carrega também a plataforma e o nome do adapter.** Com o AvaTax, plataforma e adapter deixam de ser
>    a mesma string. A gravação casa os nomes pedidos — plataforma, adapter, módulo e integração — contra os declarados,
>    sem distinguir caixa, e grava a forma declarada; as leituras continuam exatas.
> 5. **O caminho tem um estado de migração.** A integração de notas nasce da migração com o caminho nulo, que o host
>    preenche uma vez na subida com o `Avalara:DocumentsPath`; depois disso o caminho é só da tela. Nulo e em branco são
>    os dois inativos, e só o nulo é preenchido. O `poll.enabled` das consequências não desceu de nível: ele vive nas
>    settings de entrada, que a change não tocou.

## Contexto

O Tax Compliance não recebe só nota fiscal. Ele recebe também:

| Domínio | O que entra | Base | Cadência |
|---|---|---|---|
| Fiscal | nota já emitida no ERP, de entrada ou de saída | v2, em `/dfe` | contínua |
| Contábil | plano de contas, lançamento, saldo | **v1**, um endpoint por tipo | rara, em lote |
| Inventário | Bloco K: K200, K210/K215, K230 | **v1**, um endpoint por registro | rara, em lote, volumosa |

E o AvaTax tem cálculo e emissão **na mesma base**, mudando só o caminho final.

Três fatos desmontam o modelo de "um adapter de saída com uma URL":

1. **a base muda dentro do mesmo conector** — o Fiscal usa a v2 e o resto usa a v1;
2. **cada tipo de carga tem seu endpoint**, e a Avalara os versiona de forma independente;
3. **as cargas não podem concorrer** — o cliente precisa subir o inventário sem que isso prenda o K230 atrás na fila.

Some-se que nem todo cliente tem todos os domínios, e que o `/dfe` manda **uma nota por requisição** enquanto os
endpoints da v1 aceitam **um lote**, com tamanho que varia por tipo de registro.

**A pergunta que fechou a decisão:** qual é o menor nível em que essas coisas variam? Endpoint, fila e tamanho de lote
variam todos juntos, e variam abaixo do módulo. É esse nível que precisa de nome.

## Decisão

### 1. Três níveis, e o do meio é só apresentação

- **Conector** — o adapter, as URLs base e o que é da conexão. Vive sob a conta de plataforma (ADR-0036).
- **Módulo** — o domínio: Fiscal, Contábil, Inventário. É o que o cliente contrata e o que vira item de menu. **Não tem
  comportamento próprio.**
- **Integração** — a unidade operacional. Tem **endpoint próprio**, **fila própria** e **tamanho de lote próprio**.

O Fiscal do Tax Compliance tem uma integração (Notas Fiscais); o Contábil tem três (plano de contas, lançamento,
saldo); o Inventário tem uma por grupo de registro do Bloco K.

> **"Capacidade" foi um nome errado que eu inventei no caminho.** A coisa que o conector declara saber fazer e a coisa
> que tem endpoint e fila são a mesma, vista de dois lados. Fica **integração**, que é como o negócio já a chama.

### 2. O adapter declara; o perfil liga

- **O adapter declara**, por conector: quais módulos atende, quais integrações cada módulo tem, qual base cada
  integração usa, e **se a API daquela integração aceita lote**. Isso é característica da plataforma, não escolha de
  quem configura.
- **O perfil liga** o que o cliente contratou, e preenche o que é dele: o caminho do endpoint e, onde há lote, o
  tamanho.
- **No máximo um conector ligado por integração, por tenant.** A regra sobrevive à chegada do AvaTax porque "Notas
  Fiscais" (captura, no Tax Compliance) e "Emissão" (no AvaTax) são integrações **distintas** do mesmo módulo Fiscal.
  Dois conectores na mesma integração é erro de configuração, recusado na gravação: não é escolha em runtime nem "o
  primeiro ganha".
- **Uma integração sem endpoint preenchido fica inativa.** Ela existe, não aparece no agendamento e não consome fila.
  Bloquear a conclusão da configuração em vez disso produziria conector pela metade, porque quem configura raramente
  tem todos os caminhos na mão na primeira sessão.

### 3. As URLs base são do conector, e são mais de uma

O conector guarda **uma URL base por versão de API que o adapter declara**, e não dois campos fixos chamados v1 e v2 —
"v1" e "v2" são o caso da Avalara, não uma propriedade do modelo. Cada integração diz qual base usa.

A tela pede **só as bases que os módulos ligados usam**: marcando apenas o Fiscal, aparece um campo; ligando o Contábil
depois, a base da v1 passa a ser exigida, e a tela do conector diz isso em vez de falhar no primeiro envio.

### 4. Uma fila por integração

Cada integração ligada tem fila própria. É o que permite subir o inventário sem prender o K230, e carregar o plano de
contas sem disputar com o lançamento.

**Não é prioridade numa fila só.** Prioridade ordena; ela não isola. Um lote de 31 mil itens com prioridade baixa ainda
ocupa o consumidor, e o que se pede aqui é que uma carga longa **não atrase** outra — isso é isolamento, e isolamento se
faz com fila e consumidor próprios.

### 5. O tamanho do lote é por integração

Onde o adapter declara que a API aceita lote, o perfil guarda quantos registros vão por requisição — K200 com 100,
K210/K215 com 50, cada um independente. Onde não aceita, não existe campo: o `/dfe` é uma nota por chamada, e um campo
desabilitado ali faria quem configura procurar onde habilitar.

### 6. A recusa é por registro, e não por lote

Um lote de 100 com um registro ruim carrega os outros 99. Portanto:

- o resultado de uma carga é **parcial por natureza**: `29.880 de 31.204`;
- reprocessar os recusados relê **só esses registros** no ERP, e não o lote que os continha;
- nenhum contador do produto pode supor que uma carga é tudo-ou-nada.

### 7. Os módulos saem do tenant e passam para o conector

O `Modules` era uma lista do tenant, marcada à mão, que só desenhava a barra lateral. Isso não sobrevive a dois
conectores: o AvaTax não tem módulo nenhum do Contábil, e o Tax Compliance tem os três. **O módulo é do conector**, e a
barra lateral mostra a soma dos módulos ligados em todos os conectores do cliente.

## Consequências

- **O `ConnectorProfileRequest` muda de forma de novo**, agora para carregar módulos e integrações por conector. Como é
  forma de dado, entra na mesma fatia do ADR-0036 — fazer em duas migrações o que cabe em uma é pagar duas vezes.
- **O `module-navigation` precisa de delta:** ele registra que o módulo é lista do tenant e que é apresentação.
- **O agendamento passa a agrupar por conector e módulo** no dropdown de tipo de integração, mostrando só o que está
  ligado.
- **O `poll.enabled` do ADR-0029 desce de nível.** Ele era um interruptor do perfil; passa a valer por integração, onde
  a integração tem varredura. Uma carga contábil não tem coletor — ela é disparada.
- **Não entra agora:** nenhum conector de AvaTax, nenhuma integração de cálculo ou emissão, nenhum endpoint real do
  Contábil ou do Inventário. Entra a **forma** que os comporta.

## Alternativas consideradas

- **Manter "capacidade" e "módulo" como conceitos separados.** Rejeitada no §1: eram o mesmo fato com dois nomes, e dois
  nomes para uma coisa é como nasce a pergunta "qual deles manda".
- **Módulo no tenant, como estava.** Rejeitada pelo exemplo que a derrubou: o cálculo do AvaTax não tem módulo, e o Tax
  Compliance tem três. Uma aba "Módulos" no nível do cliente mentiria sobre a quem se aplica.
- **Uma fila só com prioridades.** Rejeitada no §4.
- **Dois campos fixos, `baseUrlV1` e `baseUrlV2`.** Rejeitada: é o vocabulário de um fornecedor dentro do modelo. O
  adapter declara as bases de que precisa, e a Avalara precisa de duas.
- **Tamanho de lote global do conector.** Rejeitada: o que cabe num lote depende do tamanho do registro, e um item de
  Bloco K não tem o peso de um lançamento contábil.
