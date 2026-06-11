<h1 align="center">FiscalHub</h1>

<p align="center">
  Middleware de integração fiscal plugável, em .NET, já preparado para a Reforma Tributária (IBS/CBS/IS).
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white" alt="C# 14">
  <img src="https://img.shields.io/badge/Azure-serverless-0078D4?logo=microsoftazure&logoColor=white" alt="Azure">
  <img src="https://img.shields.io/badge/status-em%20desenvolvimento-orange" alt="status">
</p>

---

Recebe notas fiscais já emitidas — de um ERP ou de arquivos XML — e as despacha para uma
plataforma de compliance tributário (Avalara e afins), traduzindo cada documento para um
modelo de domínio interno no caminho.

## O problema

A NT 2025.002 da NF-e introduziu IBS, CBS e Imposto Seletivo por item, com obrigatoriedade a
partir de 2026. Quem integra ERPs a plataformas de compliance precisa carregar esses campos
novos sem perder nada no caminho — e ainda lidar com origens e destinos que mudam de cliente
para cliente. O FiscalHub é a camada do meio: uma entrada agnóstica de origem, uma saída
agnóstica de destino, e um núcleo estável entre as duas.

## Arquitetura

Ports & adapters (hexagonal). No centro, um modelo de domínio por tipo de documento — a
representação interna que não muda quando se troca o ERP de origem ou a plataforma de destino.
Nas bordas, adapters plugáveis traduzem de e para os formatos externos. Entre origem e
destino, uma esteira assíncrona (fila + claim-check) move os documentos com idempotência,
retry e dead queue.

```mermaid
flowchart LR
    XML[XML NF-e]:::src --> IN
    ERP[ERP / D365]:::src --> IN

    subgraph HUB [FiscalHub]
        direction LR
        IN[Porta de<br/>entrada] --> Q[(Fila<br/>claim-check)]
        Q --> CORE[Núcleo de domínio<br/>Mercadoria · IBS/CBS/IS]
        CORE --> OUT[Porta de<br/>saída]
    end

    OUT --> AV[Avalara]:::dst
    OUT --> EXP[Export / outros]:::dst

    classDef src fill:#e7f0ff,stroke:#5b8def,color:#1a1a1a;
    classDef dst fill:#eafaf1,stroke:#43b581,color:#1a1a1a;
```

O raciocínio por trás de cada decisão está em [`docs/adr`](docs/adr).

## Escopo

Recebe, traduz e despacha. **Não** emite notas, **não** assina com certificado, **não**
transmite à SEFAZ e **não** calcula imposto — isso é responsabilidade de quem emite e de quem
apura. O hub assume que a nota já foi autorizada (cStat 100) e cuida do envio confiável
dela até o destino.

## Estado atual

Implementado: o núcleo de domínio da NF-e de mercadoria (modelo 55), com o grupo de tributos
da Reforma por item, e o envelope comum da esteira.

Em sequência: as portas de entrada e saída, a esteira resiliente, o adapter de XML, o adapter
para o formato da Avalara e o painel de acompanhamento. O histórico de commits acompanha essa
evolução.

## Limites conscientes

Conectores reais de SAP/D365, integração real com a Avalara, emissão e transmissão à SEFAZ,
certificado digital e multi-tenant real estão fora do escopo desta implementação de
referência — ficam anotados como evolução. A meta é demonstrar a arquitetura funcionando de
ponta a ponta num caminho vertical real, não cobrir toda a superfície fiscal.

## Stack

| Camada | Tecnologia |
|--------|-----------|
| Backend | .NET 10 (LTS), C# |
| Painel | React |
| Mensageria | Azure Service Bus |
| Armazenamento | Blob (payload) + Azure SQL serverless (metadados) |
| Publicação | Static Web Apps · Functions / Container Apps |
| Dev local | Docker + Azurite (sem custo) |

## Rodando localmente

Pré-requisito: SDK do .NET 10.

```bash
dotnet build FiscalHub.slnx
```

Instruções completas serão adicionadas conforme os módulos forem entrando.

## Reforma Tributária

O modelo de mercadoria carrega o grupo de IBS, CBS e Imposto Seletivo (com CST e cClassTrib)
por item, conforme a NT 2025.002. O objetivo é enviar esses campos sem perda da origem
até o destino — a apuração em si fica com a plataforma de compliance.
