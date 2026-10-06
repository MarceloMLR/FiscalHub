# Fixtures do D365 F&O

`Record-D365Fixtures.ps1` grava as respostas OData que os testes da montagem do D365 usam (design D15 da
change `add-d365-document-assembly`). Os testes rodam sobre esses arquivos, sem rede.

## Como rodar

O script autentica como o conector: client credentials, com o app do conector, e nunca com a sessão do Azure CLI (change
`explicit-credential-and-execution-cnpj`). A resposta gravada é a que o conector recebe, com a role
`FSFiscalHubIntegration`. A credencial vem de três variáveis de ambiente, as mesmas dos testes contra o F&O real:

```powershell
$env:FISCALHUB_D365_ENTRA_TENANT_ID = '<tenant do Entra do app>'
$env:FISCALHUB_D365_CLIENT_ID = '<client id do app>'
$env:FISCALHUB_D365_CLIENT_SECRET = '<client secret do app>'
./tools/d365-fixtures/Record-D365Fixtures.ps1 -EnvironmentUrl https://fiscosysdev.operations.dynamics.com -Company brmf
```

- **Onde as variáveis ficam:** só na sessão (`$env:`). Nunca em arquivo versionado. O segredo não vai por parâmetro,
  para não ficar no histórico do shell, e o script nunca imprime o segredo nem o token.
- **Sem alguma delas:** o script para antes de qualquer requisição, nomeando as que faltam.
- **Os mesmos valores** que a tela grava em Configurações → Conectores → Entrada: Tenant do Entra ID, Client ID e Client
  Secret.

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

## O estabelecimento na `brmf`

Regravado em 2026-09-27 (change `establishment-and-readable-dashboard`, design D3), com o `FiscalEstablishment` no
`$select` do cabeçalho. A regravação só acrescentou esse campo: nenhum outro valor mudou. Na `brmf`, os 83 cabeçalhos
têm três estabelecimentos:

| `FiscalEstablishment` | `FiscalEstablishmentCNPJCPF` | Cabeçalhos |
|---|---|---|
| `Matriz` | `442782250001-80` | 61 |
| `SP-01` | `442782250002-60` | 7 |
| `SAL-01` | `442782250003-41` | 15 |

O CNPJ vem formatado (`NNNNNNNNNNNN-NN`). O hub tira a pontuação e preserva as letras do CNPJ alfanumérico (change
`erp-company-directory-and-card-filters`, `tax-identifier-normalization`). O código vem preenchido em todos. O tamanho máximo
do campo não aparece no `$metadata` do OData (a propriedade é só `Edm.String`) nem no esquema CDM da Microsoft. Conferir
no AOT (EDT do `FiscalEstablishmentId`, tabela `FiscalEstablishment_BR`).

## O diretório e a descoberta por período

Gravados em 2026-10-01 (change `erp-company-directory-and-card-filters`, design D1 e D4), com `-DirectoryOnly`, que grava só
a pasta `directory/` e não regrava o resto:

```powershell
./tools/d365-fixtures/Record-D365Fixtures.ps1 -EnvironmentUrl https://fiscosysdev.operations.dynamics.com -Company brmf -DirectoryOnly
```

- **`directory/establishments.json`:** a `FiscalEstablishments`, a entidade padrão da Microsoft, com os quatro campos que o
  hub lê (`dataAreaId`, `FiscalEstablishmentId`, `CNPJ`, `Name`). A `brmf` tem quatro estabelecimentos. O `RJ-01`
  (`442782250034-48`, "Filial Rio de Janeiro") não tem nenhum cabeçalho: é o que prova que o diretório vem do cadastro, e
  não das notas.
- **`directory/period-<estabelecimento>-<dia>.json`:** a consulta da descoberta por período de cada estabelecimento com
  nota, no dia fiscal mais recente dele nos cabeçalhos do `snapshot/`. O `$select` é o do feed, com
  `$orderby=FiscalDocumentRecId` e `$top=500`, e o filtro vai de `T00:00:00Z` a `T23:59:59Z` do dia.

| Arquivo | Notas |
|---|---|
| `period-Matriz-2017-01-15.json` | 1 nota de modelo `01` (`BRMF06-110000030`), fora do mapa de modelos padrão |
| `period-SP-01-2026-08-07.json` | 2 NFS-e (`BRMF06-110000034` e `BRMF06-110000035`) |
| `period-SAL-01-2016-02-05.json` | 1 nota de modelo `01` (`BRMF28-14021`), fora do mapa de modelos padrão |

**Em 2026-10-01, a data fiscal mais recente da `brmf` é 2026-08-07.** Nenhuma nota cabe na janela de 30 dias dos cards.
