# Progresso

Passo atual: **7. Organizador por conteúdo, consultas salvas, ficha da obra, exportações, extras de PDF, busca, juntar planilhas, duplicados** (seção 10 da arquitetura).

Passos 1 a 6 concluídos. O usuário autorizou seguir passo a passo até o fim, parando só quando for preciso algo dele.

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

### Passo 5: Dados (fontes A e B, base, filtros vinculados, telas Base de dados e Fontes de dados)

- `Core/Planilhas` e `Documentos/Planilhas/LeitorXlsx`: leitura de tabelas nomeadas e de abas por Open XML, sem abrir o Excel e sem escrever na origem. Arquivo aberto, ocupado ou só na nuvem vira mensagem específica.
- `Core/Base/Normalizacao`: dotação (pontos no lugar de espaços, original guardado), portaria (número e ano lidos do texto; "SEM PORTARIA - 96 Quadras" não vira portaria 96), busca sem acento/caixa, valores em reais.
- `Dados/Base`: importadores da mestra e das dotações (falham inteiros, sem gravar meia base), `BaseRepositorio` com troca atômica da base, filtros vinculados (cada lista só mostra o que combina com os outros filtros), medições, empenhos, vínculos portaria → contrato de receita → dotação e apelidos de portaria.
- `ServicoFontes`: lê ao abrir, vigia o arquivo na pasta sincronizada e relê sozinho quando ele é salvo (espera a gravação terminar; arquivo some por instantes durante o salvamento sem derrubar a base). A última base válida fica em uso se a leitura falhar.
- Telas: Base de dados (filtros por portaria, contrato, escola, contrato de receita, dotação, lote, situação, Data da OS e faixa de valor; ficha da obra ao lado com medições, empenhos e portarias; "Usar no relatório", "Usar no cronograma", "Usar em modelo Excel") e Fontes de dados (onde está cada planilha, quando foi lida, erro explicado, ler de novo).
- Leitura real feita em local (sem guardar nada no repositório): mestra lida em cerca de 3 s e gravada em 0,3 s; consultas em 10 a 16 ms.

### Passo 6: Cronograma e Modelos Excel

- `Core/Cronograma`: validação das obras (valor, datas, fim antes do início, mais de 20 meses, dotação que não está no modelo), divisão em partes de 15 obras sem deixar nenhuma de fora, leitura de linhas coladas do Excel (datas "11/2026", "fev/27").
- `Documentos/Modelos/EditorXlsx`: edição restrita de uma cópia: recusa escrever sobre fórmula, mantém o formato da célula, apaga resultados antigos das fórmulas e marca o arquivo para o Excel recalcular ao abrir.
- `ModeloCronograma`: reconhece o modelo (abas de cronograma, aba principal usada pela `Parametros`, dotações da aba `Dotacoes`) e preenche as entradas B–G, J e K das linhas 3 a 17. Linhas que sobram ficam limpas.
- Tela Cronograma padrão: modelo cadastrado uma vez (o app guarda cópia), linhas digitadas, coladas do Excel ou trazidas da Base de dados (início = Data da OS, identificado na linha; o valor fica em branco para o usuário informar), rascunho automático, nome do arquivo sugerido por portaria e lote.
- Conferência pelo Excel (`VerificadorCronogramaExcel`): abre o gerado na instância invisível do app, recalcula, compara total para obras, total geral e soma dos meses, conta células com erro, lê o resumo da `Parametros`, salva e, se pedido, exporta o PDF da aba. Sem Excel, o arquivo é gerado e a mensagem diz que não foi conferido.
- Início no meio do mês: o modelo só distribui valor a partir do mês seguinte quando o início não é dia 1º. A opção "Começar a contar no dia 1º do mês de início" (ligada por padrão) grava o dia 1º e cada linha mostra a data que vai para a planilha. Ver pendências.
- Tela Modelos Excel: cadastro de qualquer planilha (.xlsx/.xlsm) com campos → célula (tipo texto, número ou data, obrigatório, ligado ou não a um dado da Base de dados), nome dos arquivos com `{Campo}`. Preenche um ou vários arquivos (uma linha por arquivo, colar do Excel, obra da Base), PDF opcional pelo Excel, atualizar outro arquivo de mesma estrutura numa cópia. Bloqueia célula com fórmula e célula mesclada (indica a primeira do grupo). Se o arquivo do modelo mudar de estrutura, pede revisão antes de gerar.

## Testado (automático, Linux e CI Windows)

`dotnet test`: 235 testes passam em Linux, 1 pulado (Excel real); no CI Windows roda também o teste que abre as telas. Casos da especificação cobertos até aqui:

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
| T12 | `CronogramaTests`, `CronogramaVmTests` | 1, 15 e 16 obras (16 viram dois arquivos, nenhuma fica de fora), troca de ano, início no meio do mês. Cálculo do modelo conferido pelo Excel só no CI/PC com Excel. |
| T13 | `CronogramaTests`, `CronogramaVmTests` | Valor e datas inválidos, fim antes do início, dotação fora do modelo impedem gerar com o motivo na linha; conferência de totais que não batem é avisada. |
| T14 | `ModelosExcelTests`, `ModelosVmTests` | Preenche cópia de .xlsx mantendo as fórmulas; PDF pelo exportador. Falta .xlsm real com VBA (nenhum modelo do usuário tem macro). |
| T15 | `ModelosExcelTests`, `ModelosVmTests`, `CronogramaTests` | Escrita sobre fórmula e célula mesclada bloqueadas; modelo com estrutura mudada pede revisão; troca de versão. "Aba nova em arquivo existente" não foi feita (ver pendências). |
| T16 | `ServicoFontesTests`, `BaseTests` | Mestra salva (relê sozinha), ocupada, ausente por instantes, ausente de vez e com coluna faltando: base anterior mantida, erro específico. |
| T17, T18 | `BaseTests` | Contrato com várias escolas e portarias; várias medições e empenhos por contrato sem duplicar valores. |
| T27, T29, T31 | `BaseTests`, `BaseEFontesTests` | Filtros vinculados em qualquer ordem, limpar um filtro, Data da OS por período, faixa de valor com valor ausente. |
| T32 | `CronogramaVmTests` | Início vindo da OS identificado; início previsto identificado; data trocada à mão mostra divergência sem mexer na base. |
| T20 | `PdfTests`, `PdfVmTests` | Reduzir PDF com fotos (original preservado), PDF já otimizado (avisa e não cria cópia), protegido por senha (mensagem), assinado (aviso antes). |
| T24 | `TelasDeVerdadeTests` (CI) | Todas as telas a 1024x768 e 900x700, sem texto ou botão cortado, com nomes para leitor de tela. Falta conferir no monitor real e nas escalas 125% e 150%. |

## Decisões técnicas

- xUnit v3 com Microsoft.Testing.Platform (`global.json`); o `dotnet test` do .NET 10 exige.
- Automação do Excel por COM tardio (`dynamic`): funciona com Excel 32 ou 64 bits.
- Renomear: com zero à esquerda a largura mínima é 2 e cresce com o maior número do lote; colisão vira sugestão com sufixo (situação "ajustado", com o motivo); continuar numeração olha só nomes no padrão exato "nome + separador + número" e segue depois do maior.
- Zip por limite: a conta usa o pior caso (arquivo que não comprime), então o zip final nunca passa do limite. Para arquivos que comprimem muito (planilhas, texto) podem sair mais partes do que o mínimo possível. Os arquivos ficam na ordem recebida.
- Mover entre volumes: copia para `nome.spobras-parcial`, confere tamanho e hash, publica o nome final e só então tira a origem.
- Histórico: `ModificadoEm` é guardado em ticks para o Desfazer comparar com o disco sem erro de arredondamento.
- `{obra}`, `{lote}` e `{tarefa}` no destino das regras do organizador ainda não foram feitos: só `{ano}` e `{mes}`.
- Base: uma obra é o par (CONTRATO, EMPREENDIMENTO) da mestra. Os filtros mostram os nomes de coluna da própria planilha; o app não decide sozinho qual valor é "o valor do contrato".
- Modelos Excel: o app guarda uma cópia de cada modelo e grava a "assinatura" da estrutura (abas e número de fórmulas). Se mudar, pede revisão. Macros não são executadas.
- Cronograma: a conferência pelo Excel salva o arquivo gerado já calculado (para abrir com os valores certos mesmo em visualizadores).
- Fonte da interface: "Inter, Segoe UI". A Inter não vai no pacote; no PC sem ela o Windows usa Segoe UI.
- A prévia do relatório é desenhada pelo próprio plano de páginas (mesmas contas do gerador), não por uma imagem ilustrativa.
- Artefatos do CI não podem ser baixados do ambiente do Claude Code (rede bloqueada); por isso o teste das telas confere o layout por medidas e publica as imagens para o usuário ver no GitHub.

## Pendências com o usuário

- [ ] Reenviar `Cronograma_revisado.xlsx` e o relatório fotográfico de exemplo quando chegar a vez dos passos 4 e 6: os arquivos enviados na conversa não ficam guardados entre sessões e não podem ir para o repositório.
- [ ] Logo SPOBRAS (passo 4), a ser colocado pelo próprio usuário no app; confirmar se há versão em melhor qualidade.
- [ ] Confirmar o uso da aba `PORTARIAS` e o layout de `CONTRATADO_LIQUIDADO` na planilha mestra (passo 5; ver `docs/FONTES.md`).
- [ ] Decidir se o caminho da pasta e os códigos de contrato de receita que estão em `docs/ARQUITETURA.md` devem ser trocados por exemplos genéricos (repositório público).

- [ ] **Início no meio do mês (cronograma):** o modelo não distribui valor no mês de início quando a data não é dia 1º. O app grava o dia 1º por padrão (e mostra isso na linha). Confirmar se é isso que você quer ou se prefere a data exata (o valor daquele mês fica para o mês seguinte, como o modelo faz hoje).
- [ ] Algum modelo Excel precisa de macro depois de preenchido? Hoje nenhum modelo recebido tem VBA, então "rodar macro cadastrada" não foi feito.
- [ ] Cronograma em **aba nova de um arquivo já existente** (especificação T15): confirmar se você usa assim ou se um arquivo novo por cronograma basta.
- [ ] Editar portarias e dotações dentro do app e "Exportar para o Excel": hoje a Base de dados só lê; dá para dar apelido à portaria. Confirmar se precisa editar no app ou se a planilha de dotações continua sendo o lugar.
- [ ] Planilhas complementares (arquitetura 5.4, T30): não feitas. Dizer se há alguma outra planilha que deve entrar na base.

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
- Cronograma e Modelos Excel com o Excel real (recalcular, PDF da aba, conferir totais): o código existe, mas só roda com Excel; no Linux e no CI é usado um verificador de teste.
- Cronograma gerado conferido contra o `Cronograma_revisado.xlsx` real: feito localmente com LibreOffice (as fórmulas recalcularam igual); falta conferir no Excel do PC do trabalho.
- Duplicados por conteúdo e reconhecimento por conteúdo: passo 7.
- `Spobras.Excel.Tests`: só roda com Excel instalado (pulado no Linux e no CI).
