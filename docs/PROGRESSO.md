# Progresso

Passo atual: **5. Dados: fontes A e B, base, filtros vinculados, telas Base de dados e Fontes de dados** (seção 10 da arquitetura).

Passos 1 a 4 concluídos. O usuário autorizou seguir passo a passo até o fim, parando só quando for preciso algo dele.

## Feito

### Passo 1: solução, CI e Teste de Ambiente

- Solução com os projetos da seção 3 e CI em `windows-latest` (compila, testa e publica o `TesteAmbienteSPOBRAS.exe`).
- Teste de Ambiente rodado pelo usuário no PC do trabalho em 11/11/2026: **9 de 9 itens passaram**; a arquitetura não precisou de ajuste.
  - Windows 11 x64, escala 100%, dois monitores (1920x1080 e 1600x900). Executável único abriu sem bloqueio da TI.
  - Excel do Microsoft 365, **64 bits**, versão 16.0. Instância invisível abre em cerca de 4,5 s e não afeta o Excel do usuário.
  - Modelo do cronograma (`.xlsx`, 4 abas): abrir cópia, recalcular e exportar PDF em cerca de 6 s.
  - OneDrive e a biblioteca das planilhas base encontrados pelo registro. **Quase todos os arquivos estão só na nuvem** (24 de 27 na pasta das planilhas base): o importador (passo 5) deve contar com o tempo de download e com falha sem internet.
  - SQLite em `%LOCALAPPDATA%\ArquivosSPOBRAS`, bandeja, notificação e vigia de Downloads: ok.

### Passo 2: Core (tarefas, histórico, arquivos, renomear, zip, organizador)

- `Spobras.Core`
  - `Abstracoes`: `IArquivos` (disco, nunca sobrescreve), `IRelogio`.
  - `Nomes`: ordem natural, validação de nomes do Windows, correção sugerida, sufixo (2), (3).
  - `Renomear`: prévia dos três modos, ordem natural/data/manual, continuar numeração, opções de perfil, nome editado na prévia, colisões no lote e com a pasta.
  - `Operacoes`: lote e itens com os seis estados, `ExecutorLote` (item a item, troca A↔B por nome temporário registrado, mover entre volumes com cópia verificada, cancelar, retomar só pendentes, reconciliar após fechamento inesperado), `Desfazer` com conflitos explicados.
  - `Organizar`: regras sem programação (nome, extensão, pasta de origem, prioridade, `{ano}`/`{mes}`), sugestão com motivo, "Você decide", protegidos e temporários fora, validação de regras (ciclo com pasta vigiada), `FilaChegada` (espera a gravação terminar, um registro por arquivo).
  - `Compactar`: plano de zip por pasta, único e por limite (zips independentes).
  - `Tarefas`: fila de trabalhos em segundo plano, um por vez, com progresso e cancelamento.
- `Spobras.Documentos/Zip`: `Compactador` (temporário, conferência de integridade e limite, publica o nome final só no fim) e `Descompactador` (pasta própria, sem sair do destino, sem sobrescrever, com limites).
- `Spobras.Dados`: `BancoApp` (SQLite com migrações) e `HistoricoSqlite` (histórico por arquivo gravado a cada mudança).
- Documentos: `docs/ESPECIFICACAO.md`, `docs/interface/` e `docs/FONTES.md` (tabelas e colunas reais; só nomes).

### Passo 3: App (casca, telas dos módulos de arquivos, bandeja)

- `Spobras.App` em WPF + MVVM, seguindo `docs/interface/` (cores, medidas, textos): menu lateral, Início, Organizar arquivos, Renomear em lote, Compactar, Histórico e tarefas, Configurações. Só aparecem no menu os módulos que já funcionam.
- Lógica das telas em `ViewModels/` e `Servicos/`, sem WPF, testada em Linux pelo projeto `tests/Spobras.App.Tests` (os arquivos são compilados lá por vínculo).
- Fluxo único de operações (`ServicoOperacoes`): prévia confirmada vira lote, roda na fila de tarefas, grava histórico, permite Desfazer e continuar pendentes. Lotes interrompidos são reconciliados ao abrir.
- Modo automático (`ServicoNovidades` + `VigiaPasta`): vigia a pasta de trabalho, espera o download terminar, avisa agrupado pela bandeja e só sugere. Regras do organizador editáveis na tela; "Lembrar esta escolha" só cria regra quando o usuário pede.
- Bandeja: ícone, aviso agrupado, menu Abrir / Ver arquivos novos / Sair; com o modo automático ligado, fechar a janela deixa o app na bandeja (avisa na primeira vez). Instância única.
- Perfis ("Salvar estas opções") no Renomear; último destino lembrado por módulo; preferências, regras e perfis no SQLite.
- Janela estreita (menos de 1180 px): o menu se recolhe e aparece o botão Menu.
- `tests/Spobras.App.Telas.Tests` (só Windows/CI): abre a janela de verdade, passa por todas as telas com dados de exemplo, falha se houver erro de XAML, erro de ligação de dados, elemento cortado ou fora da janela, e guarda uma imagem de cada tela no artefato `telas` do CI.

### Passo 4: Documentos, relatório fotográfico e PDF básico

- Bibliotecas escolhidas (licença conferida): **SkiaSharp 3.119.1 (MIT)** para imagens, **PDFsharp 6.2.4 (MIT)** para PDF. `System.Security.Cryptography.Pkcs` fixado em 10.0.15 por causa de alerta de segurança na versão que o PDFsharp puxava. PdfPig (Apache-2.0) reservado para a extração de texto do passo 7.
- `Core/Relatorio`: grade da página tirada do modelo de referência (A4 em pé, cabeçalho repetido, duas colunas iguais de fotos) e paginador para 1, 2, 4, 6 ou 8 fotos por página. Texto longo tira fotos da página em vez de diminuir a letra; descrição maior que a página continua na seguinte; assinaturas no pé ou em página própria; dados longos no cabeçalho tomam linhas do corpo.
- `Documentos/Imagens`: leitura de fotos com orientação e data de captura EXIF (leitor próprio), cópia reduzida em JPEG, miniaturas. Formatos aceitos: JPG, PNG, BMP, WEBP, GIF. HEIC recebe mensagem por arquivo.
- `Documentos/Relatorio/GeradorRelatorio`: gera o `.xlsx` por Open XML com cabeçalho, logo, fotos, legendas, assinaturas, quebras de página manuais, área de impressão e rodapé "Página X de Y". Escala fixa de 85% (não usa "ajustar à página" para o Excel respeitar as quebras).
- `Documentos/Pdf/FerramentasPdf`: juntar, separar por intervalos ou por página, fotos em PDF (uma por página A4), reduzir (recomprime fotos JPEG; se não diminuir, não cria cópia). PDF protegido por senha e PDF assinado têm tratamento próprio.
- Telas: Relatório fotográfico em três etapas (Dados, Fotos e legendas, Conferência e geração) com rascunho automático, prévia das páginas desenhada com as mesmas medidas do arquivo, fotos faltando indicadas e localizáveis; PDF com a ferramenta escolhida primeiro. Logo cadastrado em Configurações (cópia em `%LOCALAPPDATA%\ArquivosSPOBRAS\modelos`).
- PDF do relatório: feito pelo Excel instalado (`ExportadorPdfExcel`, instância invisível, planilha aberta só para leitura).

## Testado (automático, Linux e CI Windows)

`dotnet test`: 171 testes passam em Linux, 1 pulado (Excel real); no CI Windows roda também o teste que abre as telas. Casos da especificação cobertos até aqui:

| Caso | Onde | Situação |
| --- | --- | --- |
| T01 | `RenomearTests` | 100 arquivos, ordem natural, número inicial, três modos, extensão preservada; resultado no disco igual à prévia. |
| T02 | `RenomearTests` | Colisão com a pasta e dentro do lote (sufixo, nada sobrescrito), nomes inválidos, troca A↔B, ciclo de três, troca interrompida e recuperada. |
| T03 | `OperacoesTests` | Desfazer após edição posterior, com caminho original ocupado e com arquivo levado: conteúdo atual preservado e motivo explicado. |
| T04 | `OperacoesTests`, `HistoricoSqliteTests` | Queda no meio de renomear e de mover entre volumes (antes de copiar, antes de publicar, antes de tirar a origem); reconcilia pelo histórico do banco; sempre há cópia íntegra. |
| T05 | `OperacoesTests` | Cancelar no meio e executar de novo só os pendentes; nenhuma saída pela metade. |
| T06 | `OrganizarTests` | Só a lógica (`FilaChegada`): arquivo em gravação, eventos repetidos, temporários, ocupado, varredura ao iniciar. A vigia real do Windows entra no passo 3. |
| T07 | `ZipTests` | Zip por pasta, único, independentes por limite com tamanho final conferido, arquivo grande demais apontado, originais preservados, cancelamento sem zip parcial. |
| T08 | `ZipTests` | Caminho que sai do destino recusado sem extrair nada, colisão de pasta, grande expansão barrada, nomes repetidos no zip. |
| T09 | `RelatorioTests`, `RelatorioFotograficoTests`, `RelatorioVmTests` | 100 fotos em ordem, horizontais/verticais/quadradas sem distorção nem corte (proporção conferida no arquivo), orientação EXIF aplicada, uma foto por vez na memória. Falta conferir com fotos reais de celular. |
| T10 | `RelatorioTests`, `RelatorioFotograficoTests` | 1/2/4/6/8 fotos, descrições longas, campos vazios sem rótulo, assinaturas, várias páginas, fonte fixa em 11 pt. Prova visual no LibreOffice: páginas do PDF iguais ao plano. Falta a impressão real pelo Excel. |
| T11 | `RelatorioVmTests` | Rascunho restaurado (dados, ordem, legendas, rotação, opções); foto que mudou de lugar é indicada, impede gerar e pode ser localizada. |
| T20 | `PdfTests`, `PdfVmTests` | Reduzir PDF com fotos (original preservado), PDF já otimizado (avisa e não cria cópia), protegido por senha (mensagem), assinado (aviso antes). |
| T24 | `TelasDeVerdadeTests` (CI) | Todas as telas a 1024x768 e 900x700, sem texto ou botão cortado, com nomes para leitor de tela. Falta conferir no monitor real e nas escalas 125% e 150%. |

## Decisões técnicas

- xUnit v3 com Microsoft.Testing.Platform (`global.json`); o `dotnet test` do .NET 10 exige.
- Automação do Excel por COM tardio (`dynamic`): funciona com Excel 32 ou 64 bits.
- Renomear: com zero à esquerda a largura mínima é 2 e cresce com o maior número do lote; colisão vira sugestão com sufixo (situação "ajustado", com o motivo); continuar numeração olha só nomes no padrão exato "nome + separador + número" e segue depois do maior.
- Zip por limite: a conta usa o pior caso (arquivo que não comprime), então o zip final nunca passa do limite. Para arquivos que comprimem muito (planilhas, texto) podem sair mais partes do que o mínimo possível. Os arquivos ficam na ordem recebida.
- Mover entre volumes: copia para `nome.spobras-parcial`, confere tamanho e hash, publica o nome final e só então tira a origem.
- Histórico: `ModificadoEm` é guardado em ticks para o Desfazer comparar com o disco sem erro de arredondamento.
- `{obra}`, `{lote}` e `{tarefa}` no destino das regras dependem da base (passo 5); por enquanto só `{ano}` e `{mes}`.
- Fonte da interface: "Inter, Segoe UI". A Inter não vai no pacote; no PC sem ela o Windows usa Segoe UI.
- A prévia do relatório é desenhada pelo próprio plano de páginas (mesmas contas do gerador), não por uma imagem ilustrativa.
- Artefatos do CI não podem ser baixados do ambiente do Claude Code (rede bloqueada); por isso o teste das telas confere o layout por medidas e publica as imagens para o usuário ver no GitHub.

## Pendências com o usuário

- [ ] Reenviar `Cronograma_revisado.xlsx` e o relatório fotográfico de exemplo quando chegar a vez dos passos 4 e 6: os arquivos enviados na conversa não ficam guardados entre sessões e não podem ir para o repositório.
- [ ] Logo SPOBRAS (passo 4), a ser colocado pelo próprio usuário no app; confirmar se há versão em melhor qualidade.
- [ ] Confirmar o uso da aba `PORTARIAS` e o layout de `CONTRATADO_LIQUIDADO` na planilha mestra (passo 5; ver `docs/FONTES.md`).
- [ ] Decidir se o caminho da pasta e os códigos de contrato de receita que estão em `docs/ARQUITETURA.md` devem ser trocados por exemplos genéricos (repositório público).

## Decidido pelo usuário

- **Data da Ordem de Serviço:** coluna `INÍCIO EXECUÇÃO` da tabela `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS`.
- A base fica na pasta sincronizada da rede; quando a planilha for salva lá, o app relê sozinho (arquitetura 5.1, passo 5).
- Dotação completa sempre com pontos; espaço é erro de digitação e deve ser corrigido na importação.
- Portaria 3 tem dois contratos de receita: 015/SIURB/2025 (manutenções e reformas) e 164/SIURB/2025 (construções novas).
- A mesma dotação se repetir em várias portarias é normal.
- Repositório público: https://github.com/Fomes1574/SPOBRAS-Arquivos

## Não testado (e por quê)

- O app nunca rodou num Windows com gente usando: o CI abre as telas e confere medidas, mas ninguém viu o visual ainda. As imagens estão no artefato `telas` de cada build.
- Bandeja, notificação agrupada, arrastar e soltar, janelas de escolher pasta/arquivo, instância única e "continua na bandeja ao fechar": só existem no Windows real, sem teste automático. Entram no roteiro de conferência.
- PDF do relatório pelo Excel real e impressão em papel: dependem do PC do trabalho (T10 e T22 parciais).
- Relatório com fotos reais de celular (EXIF variado, HEIC) e 100 fotos grandes: testado só com fotos sintéticas.
- T04 "entre volumes" é simulado; pen drive e pasta sincronizada reais ficam para o roteiro de conferência.
- Reduzir fotos antes de compactar (especificação 4.3, item 5): ainda não ligado à tela Compactar.
- Filtros vinculados por portaria, contrato e escola na etapa Dados do relatório: dependem da base (passo 5).
- Duplicados por conteúdo e reconhecimento por conteúdo: passo 7.
- `Spobras.Excel.Tests`: só roda com Excel instalado (pulado no Linux e no CI).
