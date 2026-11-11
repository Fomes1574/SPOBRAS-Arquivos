# Arquivos SPOBRAS

App Windows de uso individual para organizar, renomear e compactar arquivos, gerar relatório fotográfico e cronograma, preencher modelos Excel e consultar uma base de contratos, portarias e dotações.

Em construção. Estado atual e pendências: [docs/PROGRESSO.md](docs/PROGRESSO.md). Como é construído: [docs/ARQUITETURA.md](docs/ARQUITETURA.md).

## Primeira entrega: Teste de Ambiente

Programa pequeno que confere se o PC de destino tem o que o app precisa. Como baixar e rodar: [docs/TESTE-AMBIENTE.md](docs/TESTE-AMBIENTE.md).

## Desenvolvimento

```bash
dotnet build
```

```bash
dotnet test
```

Requer o SDK do .NET 10. Os projetos `-windows` compilam em Linux, mas só rodam em Windows.
