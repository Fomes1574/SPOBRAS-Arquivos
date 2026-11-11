# Progresso

Passo atual: **2. Core: tarefas, histórico, arquivos, renomear, zip, organizador** (seção 10 da arquitetura). **Bloqueado**: falta `docs/ESPECIFICACAO.md`.

Passo 1 concluído: o usuário rodou o Teste de Ambiente no PC do trabalho em 11/11/2026 e os 9 itens passaram. A arquitetura não precisou de ajuste.

## Resultado do Teste de Ambiente (PC do trabalho)

- O executável único autocontido abriu sem bloqueio da TI (processo de 64 bits, .NET 10 embutido).
- Windows 11 x64 (compilação 26200), escala 100%, dois monitores (1920x1080 e 1600x900).
- Excel do Microsoft 365, **64 bits**, versão 16.0, registrado para automação.
- Excel invisível: abre em cerca de 4,5 s; escrever, recalcular, ler total, exportar PDF, salvar e fechar funcionaram com o Excel do usuário aberto, sem afetá-lo.
- Modelo do cronograma (`.xlsx`, 4 abas): abrir cópia, recalcular e exportar PDF levou cerca de 6 s.
- Downloads: criar, renomear (com acento), mover e remover funcionam; a vigia detecta arquivo novo em milissegundos.
- OneDrive e a biblioteca sincronizada das planilhas base foram encontrados pelo registro. **Quase todos os arquivos estão só na nuvem** (24 de 27 na pasta das planilhas base): a leitura sob demanda funciona, mas o importador (passo 5) deve contar com o tempo de download e com falha sem internet.
- SQLite em `%LOCALAPPDATA%\ArquivosSPOBRAS`, ícone na bandeja e notificação: ok.

## Feito

- Solução `ArquivosSpobras.sln` com os projetos da seção 3 (Core, Dados, Documentos, Excel, App, TesteAmbiente) e os 4 projetos de teste. `Spobras.App` é só uma janela vazia "em construção" (telas entram no passo 3).
- `Spobras.TesteAmbiente` (WPF, executável único autocontido win-x64, ~73 MB) com os 9 itens da seção 9, botão **Rodar teste**, modelo do cronograma opcional e `relatorio-ambiente.txt` em Downloads. Roteiro em `docs/TESTE-AMBIENTE.md`.
  - Core: resultado/relatório, máscara de caminhos, leitura de 32/64 bits do executável, teste de arquivos, vigia de pasta.
  - Dados: gravar e ler SQLite. Documentos: planilha de teste com fórmulas (Open XML).
  - Excel: `SessaoExcel` (instância invisível própria, repete chamada quando o Excel está ocupado, só encerra o processo que criou) e `LocalizadorExcel`.
- CI `.github/workflows/build.yml` (windows-latest): compila, testa e publica o executável como artefato `TesteAmbienteSPOBRAS`.

## Testado (Linux, automático)

- `dotnet build` de toda a solução (incluindo os projetos `-windows`) sem avisos.
- `dotnet test`: 16 testes passam (Core 12, Dados 2, Documentos 2); 1 pulado (Excel, sem Excel instalado).
- Planilha de teste aberta no LibreOffice: fórmulas calculam (total 400, dobro 800) e exporta PDF.
- Publicação cruzada do executável único win-x64.

## Decisões técnicas

- xUnit v3 com Microsoft.Testing.Platform (`global.json`), porque o `dotnet test` do .NET 10 exige; permite pular o teste do Excel quando não há Excel.
- Automação do Excel por COM tardio (`dynamic`), sem biblioteca de interoperabilidade: funciona com Excel 32 ou 64 bits.
- Além de passou/falhou, o relatório tem o estado **não verificado** (ex.: nenhum arquivo só na nuvem para ler).
- O item 6 descobre as pastas sincronizadas pelo registro do OneDrive (inclui bibliotecas do SharePoint), sem caminho fixo no código.

## Pendências com o usuário

- [ ] **Enviar `docs/ESPECIFICACAO.md` (revisão 2.3)**: não veio junto com os outros arquivos. Sem ela o passo 2 não começa (modos de renomeação, regras do organizador e casos T01 a T35 estão lá).
- [ ] Enviar o modelo de front-end criado pelo Claude (telas): não veio junto. Necessário no passo 3.
- [ ] Qual coluna é a data oficial da Ordem de Serviço: `INÍCIO EXECUÇÃO`, `INICIO`, `DATA DE INÍCIO` ou a coluna `OS` da aba STATUS?

## Decidido pelo usuário

- Dotação completa sempre com pontos; espaço é erro de digitação e deve ser corrigido na importação.
- Portaria 3 tem dois contratos de receita: 015/SIURB/2025 (manutenções e reformas) e 164/SIURB/2025 (construções novas).
- A mesma dotação se repetir em várias portarias é normal.
- Repositório público: https://github.com/Fomes1574/SPOBRAS-Arquivos

## Não testado (e por quê)

- Execução do `TesteAmbienteSPOBRAS.exe` em Windows não é testável pelo Claude Code (Linux; o CI não tem Excel). Coberta pelo relatório do usuário acima.
- `Spobras.Excel.Tests`: só roda com Excel instalado (pulado no Linux e no CI).
