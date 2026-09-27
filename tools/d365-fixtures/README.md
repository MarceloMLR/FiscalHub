# Fixtures do D365 F&O

`Record-D365Fixtures.ps1` grava as respostas OData que os testes da montagem do D365 usam (design D15 da
change `add-d365-document-assembly`). Os testes rodam sobre esses arquivos, sem rede.

## Como rodar

```powershell
az login
./tools/d365-fixtures/Record-D365Fixtures.ps1 -EnvironmentUrl https://fiscosysdev.operations.dynamics.com -Company brmf
```

Destino padrão: `tests/Adapters/Ingress/FiscalHub.Adapters.Ingress.D365Poll.Tests/Fixtures/d365/`.
Regravar sobrescreve; confira o `git diff` antes de versionar — um diff grande quer dizer que a base
mudou, e os testes que afirmam valores gravados precisam acompanhar.

## Só o fiscosysdev

O fiscosysdev tem dado de **demonstração**. As respostas vão para o repositório: **nunca** rode este
script contra ambiente de cliente.

## Gravadas e derivadas

- **Gravada** — a resposta exatamente como o F&O devolveu (`-OutFile`, sem reformatar).
- **Derivada** — o caso que a base não tem (imposto apontando para encargo, IBS/CBS, divergência
  fiscal × contábil…). É uma cópia de uma gravada com a edição mínima, nome terminado em
  `.derived.json`, e a edição descrita no `README.md` da pasta de fixtures: qual arquivo de origem, qual
  campo de qual RecId, de quê para quê, e por quê.

O teste que usa uma derivada diz isso no nome ou num comentário. Derivada não prova que o caminho já
passou dado real — ver "O que a base não exercita" no design da change.
