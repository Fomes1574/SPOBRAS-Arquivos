# Arquivos SPOBRAS

App Windows de uso individual para organizar, renomear e compactar arquivos, gerar relatório fotográfico e cronograma, preencher modelos Excel e consultar uma base de contratos, portarias e dotações.

Estado atual e pendências: [docs/PROGRESSO.md](docs/PROGRESSO.md). Como é construído: [docs/ARQUITETURA.md](docs/ARQUITETURA.md).

## Baixar e conferir

O app é um arquivo só, `ArquivosSPOBRAS.exe` (Windows 10/11, 64 bits, sem instalação), publicado em cada execução verde de **build** na aba **Actions**, no artefato **ArquivosSPOBRAS**. O roteiro do primeiro teste está em [docs/ROTEIRO-CONFERENCIA.md](docs/ROTEIRO-CONFERENCIA.md).

O Teste de Ambiente (programa pequeno que confere se o PC tem o que o app precisa) continua disponível: [docs/TESTE-AMBIENTE.md](docs/TESTE-AMBIENTE.md).

## Desenvolvimento

```bash
dotnet build
```

```bash
dotnet test
```

Requer o SDK do .NET 10. Os projetos `-windows` compilam em Linux, mas só rodam em Windows.
