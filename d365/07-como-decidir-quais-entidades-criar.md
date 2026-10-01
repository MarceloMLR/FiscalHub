# Fase 7 - Como decidir quais entidades criar

O documento `06` ensina a escrever o XML. Este ensina a decidir **o que** escrever, que e a
parte que determina se o conector vai ficar bom.

Foi assim que as 16 entidades fiscais nasceram. O metodo vale para qualquer modulo novo -
contabil, inventario, ou outro ERP.

---

## A ordem importa

Criar entidade e barato: um arquivo, um build, um sync. **Decidir errado e caro**, porque a
entidade vira contrato de nome fixo que o adapter consome, e mudar depois custa versao e
reprocessamento.

Por isso: primeiro entender, depois medir, so entao criar.

---

## Passo 1 - Ler o conector que ja existe

Se o cliente ja tem uma integracao, ela e a melhor fonte de verdade sobre **o que o negocio
realmente precisa**. Nao sobre como fazer - sobre o que e necessario.

Peca o codigo, ou as consultas, e levante:

- quais tabelas ele le;
- como ele liga uma na outra;
- quais campos ele usa de verdade, e nao os que estao no `SELECT` por inercia;
- o que ele faz quando o dado falta.

**Exemplo real do fiscal.** O conector antigo ligava imposto ao documento por um par
`RecId` + `TableId` - um ponteiro polimorfico. Funciona, e esconde uma armadilha: `RecId` nao e
unico entre tabelas. Um `MarkupTrans.RecId` igual a `5637144578` existe ao mesmo tempo que um
documento fiscal com o mesmo numero. A ligacao certa num cliente podia virar a ligacao errada em
outro, sem erro nenhum.

Achar isso cedo mudou o desenho inteiro.

## Passo 2 - Procurar na documentacao o caminho padrao

A localizacao brasileira do F&O e antiga e tem muita tabela. Quase sempre existe uma tabela
feita para o proposito, e o conector antigo nao a usa - porque quem o escreveu nao a conhecia, ou
porque ela nao existia na epoca.

Procure na documentacao da Microsoft **como o produto liga essas coisas**, e nao como alguem
ligou.

**Exemplo real.** Procurando "como relacionar a nota e o item com os impostos", apareceu a
`FiscalDocumentTaxTrans_BR` - uma tabela com chave estrangeira declarada para a linha do
documento. Era o caminho padrao, e substituiu o ponteiro polimorfico por uma relacao de verdade.
No mesmo caminho apareceram a `FiscalDocumentMiscCharge_BR`, para encargos, e a
`FiscalDocumentTaxTransSource_BR`, de ponte.

**Exemplo do contrario.** Procurando como ligar os impostos retidos, apareceu a
`FBTaxWithholdTrans_BR`. Lendo a documentacao dela com cuidado: e tabela de **apuracao do SPED**,
nao e ligada ao documento. O retido do documento ja estava na propria
`FiscalDocumentTaxTrans_BR`, com um campo `RetainedTax = Yes`. Duas entidades sairam do caminho
de montagem por causa dessa leitura.

## Passo 3 - Comparar os dois caminhos com dado real

Este e o passo que quase todo mundo pula, e e o que pega o erro que nenhuma leitura pega.

Com as entidades candidatas publicadas, **meca contra a base de verdade** antes de fechar o
desenho:

- quantas linhas cada caminho devolve;
- quantos documentos cada um alcanca;
- onde os dois concordam e onde divergem;
- o que aparece em um e nao no outro.

**Exemplo real.** Comparando a entidade fiscal nova com a contabil que ja existia, na base da
`brmf`:

```
547 linhas de imposto, 83 de 83 documentos alcancados
439 identicas entre as duas
351 com sinal invertido
 54 com CST de IPI divergente (01 virando 51, 05 virando 55)
 54 zeradas de um lado e com valor do outro
```

Nenhum desses numeros apareceria lendo documentacao. O CST divergente virou pergunta para um
especialista fiscal; as 26 zeradas viraram item em aberto no `STATUS.md`, com uma hipotese
testada e **rejeitada** pelos dados.

A regra que saiu disso esta escrita no `STATUS.md`: *teste com fixture derivada nao fecha o item;
ele prova o codigo, e nao o dado.*

## Passo 4 - Contar o custo da montagem

Cada entidade a mais e uma chamada a mais por documento. Some antes de decidir.

**Exemplo real.** O desenho inicial custava cerca de 6 chamadas por nota, com uma cadeia de `or`
no filtro e duas fases. Depois de trocar o ponteiro polimorfico pela chave estrangeira declarada
e tirar as duas entidades que nao serviam, ficou em **4 chamadas com filtro de dois termos
fixos**. Menos chamada, menos superficie de erro, e um filtro que o F&O consegue indexar.

## Passo 5 - Escrever a decisao antes de escrever o XML

Cada entidade escolhida entra no `04-mapeamento-de-entidades.md` com:

- qual o papel dela na montagem;
- por que essa tabela e nao outra;
- quais campos e por que;
- o que foi deixado de fora de proposito.

**Esta e a parte que mais se perde.** Daqui a um mes ninguem lembra por que a
`FBTaxWithholdTrans_BR` ficou de fora, e alguem vai "consertar" isso acrescentando ela.

## Passo 6 - So entao criar

Com a decisao escrita, siga o `06-receita-criar-entidade-na-mao.md`.

---

## O resumo em uma frase

Ler o conector antigo diz **o que** o negocio precisa. Ler a documentacao diz **qual** o caminho
certo. Medir com dado real diz **se** o caminho certo serve. Escrever a decisao impede que alguem
a desfaca.

## O que nao fazer

**Nao copie a modelagem do conector antigo** so porque ela funciona. Ela foi escrita com o
conhecimento daquela epoca e, quase sempre, sem acesso a documentacao atual.

**Nao confie em nome de tabela.** `FBTaxWithholdTrans_BR` parece exatamente o que voce quer e nao
e. Leia o proposito.

**Nao feche o desenho sem medir.** Build verde e OData respondendo nao provam que o dado esta
certo - provam que a consulta e valida.

**Nao exponha campo "porque pode ser util".** Campo exposto e contrato; tirar depois quebra o
adapter.

---

## Referencias

- `06-receita-criar-entidade-na-mao.md` - como escrever o XML depois de decidir
- `04-mapeamento-de-entidades.md` - onde a decisao de cada entidade fica escrita
- `05-achados-de-metadata-e-ciclo-de-deploy.md` - o que da errado na exposicao
- `../docs/STATUS.md` - as lacunas conhecidas e a regra da evidencia