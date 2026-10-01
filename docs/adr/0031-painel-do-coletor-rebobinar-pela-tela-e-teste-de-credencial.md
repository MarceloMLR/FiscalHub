# ADR-0031: O painel do coletor, o rebobinamento pela tela, o teste de credencial com o freio no botão, e os módulos como apresentação

- **Status:** Aceito
- **Data:** 2026-09-29
- **Complementa:** o ADR-0024. O coletor continua sem fazer a marca regredir. A tela ganha um segundo caminho de
  backfill, sob o mesmo lease, ao lado de apagar o cursor.
- **Change OpenSpec:** `openspec/changes/module-navigation-and-integration-panel`. As capacidades são
  `module-navigation`, `automatic-integration-panel`, `connector-credential-test`, `change-feed-polling`,
  `connector-secret-references` e `avalara-tenant-authentication`.

## Contexto

O coletor do D365 guarda no cursor tudo o que é preciso para diagnosticá-lo: o último poll, as falhas seguidas, o último
erro e a marca. Nada disso aparecia na tela.

- **O diagnóstico:** saber por que nenhuma nota entrou exigia abrir o banco.
- **O rebobinamento:** voltar a marca exigia SQL (RUNNING §6).
- **A credencial:** saber se ela funciona exigia esperar uma nota falhar.
- **O ponto de partida:** o `startFrom` que some não dá erro nenhum. A investigação de 29/09 foi causada por ele: nenhum
  erro, a marca avançando e zero documento.
- **A navegação:** o produto vai ter integração contábil e de inventário, e cada cliente contrata um conjunto diferente de
  módulos.

## Decisão

**O Admin vê e corrige o coletor pela tela, pelos mesmos mecanismos que o coletor usa, e testa a credencial gravada com
um token novo, com um freio no botão. Os módulos montam a barra lateral, e não dão nem tiram permissão.**

1. **O painel é leitura do cursor.** Ele mostra a última verificação, as falhas seguidas, o último erro, a marca, o
   throttling e o `startFrom`, este só como leitura.
   - **Quem vê:** só o Admin, assim que o interruptor é ligado na tela, sem esperar o salvar (revisão da prova manual,
     2026-10-01).
   - **Os textos:** na língua de quem usa ("última busca", "sincronizado até", "buscar novamente"), e não na do código.
   - **Sem marca:** o painel diz de onde a primeira passada vai começar.
2. **Rebobinar pela tela é o mesmo mecanismo do coletor, e não um segundo.**
   - **O lease e o fencing:** o rebobinamento toma o lease `changefeed:{origem}:{tenant}` do coletor e grava a marca
     condicionada a ele na mesma instrução. É o fencing do avanço, com a comparação invertida.
   - **O registro de publicações:** não é esquecido pelo rebobinamento. A passada seguinte vê a marca menor que a última
     vista e o zera pela regra que já existia (a do rebobinamento por SQL), na réplica que faz o poll.
   - **Os limites:** só para trás, e só num cursor com marca. A marca que sobe continua sendo só do coletor.
   - **A confirmação:** diz a verdade sobre o efeito e o custo. A leitura é das notas **alteradas** desde a data. O que
     foi enviado e não mudou não volta à plataforma. Cada NF-e relida faz pelo menos 4 consultas ao ERP.
3. **O teste de credencial usa a credencial gravada e um token novo.**
   - **A porta:** `IConnectorCredentialTest` fica na Application, e cada adapter a implementa.
   - **O token novo é requisito.** Um teste que reusasse o token em cache passaria com um segredo já revogado. Isso não
     seria limite, e sim um teste que mente, exatamente para quem está decidindo se o problema é a credencial.
     - **No D365:** uma instância nova da credencial do Entra ID por teste. O Azure.Identity guarda o token na
       instância, e a nova começa sem cache.
     - **Na Avalara:** uma troca que não lê o cache nem a recusa lembrada.
   - **O D365 não para no token:** faz uma leitura `$top=1` da `FSFiscalDocumentBRs`, porque o token prova a
     credencial, e não a permissão.
   - **A resposta:** traz uma mensagem curta, igual para qualquer adapter, e, com o freio, o horário do próximo teste.
     As mensagens são três (revisão da prova manual, 2026-10-01):
     - "Credenciais e conexão válidas";
     - "Credenciais ou ambiente inválidos";
     - "Não foi possível conectar agora. Tente novamente em instantes", para a indisponibilidade, para não mandar trocar
       um segredo certo.

     O motivo detalhado (o código AADSTS, o status HTTP, o campo que falta) vai para o log do host.
4. **O freio fica no endpoint de teste, e não no provedor de token do D365.** É um limite de 5 minutos para um teste
   recusado, por tenant e por adapter, e na Avalara também por ambiente. Salvar o perfil o esquece.
   - **Por que as proteções são diferentes por dentro:**
     - **o endpoint de token da Avalara:** é proprietário, e o risco dele é desconhecido. Por isso a recusa lembrada no
       provedor, para cada nota em voo não virar uma tentativa de login;
     - **o endpoint do D365:** é o Entra ID, cujo comportamento com credencial de cliente é conhecido. O coletor já tenta
       uma vez por intervalo, o token bom fica em cache, e o bloqueio inteligente do Entra ID vale para conta de
       usuário, e não para credencial de cliente. Uma recusa lembrada ali só atrasaria a recuperação.
   - **Por que a proteção é a mesma no botão:**
     - o botão é o caminho sem limite: uma pessoa clica quantas vezes quiser;
     - sem cache, cada clique é um pedido real ao emissor do token.

     Projetar para o risco real, e não para a simetria.
   - **Por que o sucesso não entra no freio:** um "funcionou" lembrado seria o mesmo teste que mente.
   - **Um teste da Avalara que dá certo esquece a recusa lembrada do envio,** como o salvar do perfil.
5. **Os módulos são apresentação, e não permissão.**
   - **Onde ficam:** no perfil de conector, numa coluna própria, porque as settings são por adapter e a tela as zera na
     troca de ERP.
   - **O padrão:** sem módulos gravados, só o Fiscal.
   - **O limite:** a API continua respondendo para um módulo escondido. Restringir de fato é outra fatia.
   - **Contábil e Inventário:** são lugares reservados, de outro domínio, e não filtros do Fiscal. A integração
     automática continua só Fiscal, por decisão de produto.

## Alternativas consideradas

- **Rebobinar chamando o `Forget` do registro de publicações.** Descartada. O registro está em memória, por réplica, e o
  `Forget` só alcançaria a réplica que atendeu o HTTP, e não a que faz o poll.
- **Criar o cursor pela tela quando ele não existe.** Descartada. Seria um segundo jeito de a marca nascer, competindo
  com o `startFrom`.
- **Editar o `startFrom` pela tela.** Fica fora. Definir o ponto de partida é outra operação, que não é rebobinar, e o
  painel o mostra.
- **Uma recusa lembrada no provedor de token do D365, pela simetria com a Avalara.** Descartada pelo risco real (4).
- **O teste reusar o token do coletor.** Descartada: é o teste que mente (3).
- **O teste do D365 montar o próprio `ClientSecretCredential`, fora do provedor.** Descartada. Duplicaria a regra da
  referência do próprio tenant (ADR-0027), que tem de morar num lugar só.
- **Uma leitura de verificação na Avalara.** Descartada. O contrato verificado só tem o envio e a consulta, e os dois
  são sobre um documento. Uma chamada inventada daria um "funciona" que ninguém verificou.
- **Os módulos dentro das settings de entrada.** Descartada. A tela zera as settings na troca de adapter.

## Consequências

- **Na tela:** o diagnóstico do coletor e a correção mais comum (rebobinar, testar o segredo) saem do banco e vão para
  ela.
- **O segredo errado:** um segredo errado do D365 fica visível no painel em cerca de um minuto. Isso reduz o valor de
  proteger contra a sondagem continuada.
- **Com mais de uma réplica,** duas coisas mudam:
  - **o rebobinamento:** a regra de regressão vale por réplica. Uma réplica cuja última marca vista é anterior ao alvo
    não vê a regressão. Precisa de uma geração persistida no cursor, e está no STATUS. O rebobinamento por SQL tem o
    mesmo limite;
  - **o freio:** fica em memória, por réplica, e vira N testes por 5 minutos.
- **O `startFrom`:** continua exigindo SQL para ser editado.
- **A API por módulo:** continua aberta. Quem precisar de restrição por módulo abre outra fatia.
