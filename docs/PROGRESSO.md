# Progresso

Passo atual: **entrega única para o teste do usuário** (fim da seção 10 da arquitetura). Aguardando o resultado do [roteiro de conferência](ROTEIRO-CONFERENCIA.md).

Passos 1 a 8 concluídos. O usuário autorizou seguir passo a passo até o fim, parando só quando for preciso algo dele.

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

### Passo 7: organizador por conteúdo, consultas, exportações, extras de PDF, busca, juntar planilhas, repetidos

- Base de dados: **consultas salvas** (filtros com nome; ao reabrir, usam a base atual), **Exportar para Excel** (todas as obras do filtro, não só as da tela, com datas e números como tipos do Excel e uma aba "Sobre esta consulta" com filtros, data e de qual leitura das fontes os dados vieram), **pendências** (obras sem Data da OS e fontes com falha, com atalho para o filtro) e "Buscar o contrato nos arquivos" a partir da obra.
- Ficha da obra: já existia no passo 5 (painel ao lado dos resultados com contrato, receita, portaria, dotação, Data da OS, valores, medições e empenhos do contrato).
- PDF: **Logo e numeração** numa cópia (posição escolhida, a partir de uma página, prévia das páginas atingidas, aviso de PDF assinado) e **PDF para Excel** (texto do PDF em linhas e colunas, valores em reais e datas convertidos, página e linha de origem; PDF digitalizado é explicado, sem OCR).
- Ferramentas (módulo novo): **buscar nos arquivos** (nome e conteúdo de .xlsx/.xlsm, PDF com texto, .docx, .txt, .csv; índice local que só relê arquivo alterado), **juntar planilhas** (colunas casadas pelo título, arquivo e aba de origem em cada linha, nada somado), **arquivos repetidos** (pelo conteúdo; separar as cópias numa pasta "Cópias repetidas" com confirmação e Desfazer; nada é apagado) e **Excel para PDF em lote** (pelo Excel).
- Organizador por conteúdo: regra com "Conteúdo contém" e destino com `{contrato}`, `{escola}`, `{lote}`, `{portaria}`. A obra é reconhecida pelo número exato do contrato (no nome aceita `-`, `_` ou `.` no lugar da barra) ou pelo nome completo da escola, primeiro no nome e depois no conteúdo. Mais de uma obra possível, dado faltando ou base vazia vão para "Você decide", com a evidência.

### Passo 8: revisão geral, entrega e roteiro de conferência

- **Cópias de segurança** (especificação 6.1, T23): o banco é copiado pela API de backup do SQLite antes de toda atualização que muda a estrutura; em Configurações dá para fazer cópia na hora, abrir a pasta e voltar a uma cópia (a troca acontece na próxima abertura do app, e o estado de antes vira mais uma cópia). Ficam as 10 mais recentes.
- Revisão: o editor de regras deixou de descartar a condição "pasta de origem" ao salvar; contrato com uma escola só passa a resolver `{escola}`; exemplos e nomes de pessoas fora dos documentos que eu escrevi.
- Entrega: `ArquivosSPOBRAS.exe` único, autocontido, publicado pelo CI (artefato **ArquivosSPOBRAS**); versão 1.0.0. Não há instalador separado: o executável não precisa de instalação nem de administrador, como validado no Teste de Ambiente.
- [Roteiro de conferência](ROTEIRO-CONFERENCIA.md) com o que testar em cada módulo, as perguntas pendentes e como devolver.
- Compactar: **reduzir as fotos** (especificação 4.3, item 5). Cópias reduzidas das fotos JPG (qualidade alta, média ou leve) numa pasta temporária do app; a prévia mostra o tamanho antes e depois, o limite por zip considera o tamanho reduzido, e as originais não mudam.

### Revisão contra a conversa e o modelo de interface

Conferi as mensagens da conversa e cada tela de `docs/interface/` contra o app. Entraram as funções que faltavam e não dependiam de resposta:

- Relatório: digitar o contrato completa escola, contratada, lote, portaria, SEI e Data da OS pela Base; contrato com várias escolas pede a escolha.
- Início: quadro da planilha mestra (nome, última leitura, "Ver base de dados").
- Cronograma: "Importar de uma planilha" (acha as colunas pelos títulos) e "Nome do arquivo".
- Base de dados: "Conferir uma planilha" (portarias e dotações fora do padrão, original → proposta, ambíguo fica para conferir, correção só numa cópia). O padrão de portaria é o da planilha mestra.
- Organizar: atalho "Procurar duplicados". Modelos Excel: atalhos para Cronograma e Relatório.
- Excel para PDF em lote ficou em **Ferramentas** (o protótipo o punha na tela PDF; a especificação o lista entre as funções complementares).

### Depois das respostas do usuário (12/11/2026)

- Cronograma: início e fim só por mês ("nov/26"; sempre dia 1 na planilha, que já mostra "nov-26"); portaria "Nº 03 (2021)" lida da planilha mestra; campo de remuneração (padrão 10%). Como o modelo tem 10% escrito na fórmula (H = G*0,1), outro percentual troca essa fórmula na cópia, só se ela for exatamente a esperada. Conferido no modelo real com 12%: remuneração, total e meses batem.
- Relatório: conferido contra o exemplo do Jardim Paulistano; as fotos já tinham folga entre si. Um teste agora garante que nunca se encostam (1 a 8 por página).
- Edição no app: qualquer campo de uma obra (o valor da planilha fica guardado, é reaplicado a cada leitura, há aviso se a planilha mudar o campo editado e "Voltar ao da planilha"); incluir e tirar obra que não está na planilha; incluir, editar e tirar portarias e dotações, com exportação para o Excel. A planilha nunca é alterada.
- OCR: "PDF para Excel" lê páginas escaneadas e fotos de documento pelo OCR do Windows (Windows.Media.Ocr), local, sem instalar nada. O app passou a exigir Windows 10 versão 1809 ou mais nova (o PC do trabalho é Windows 11).

### Depois do primeiro teste no PC do trabalho (12/11/2026)

- Janela: abre do tamanho do monitor onde está o mouse (num monitor menor, abre maximizada); a barra de título nunca fica fora da tela.
- "Gerar a planilha" continua desativado depois de gerar (a lista é limpa, por desenho) e agora mostra "Pronto!" com o que foi criado. Resultados com selo: verde (deu certo), vermelho (problema) e azul (orientação).
- Datas: as barras entram sozinhas (10112026 vira 10/11/2026; 1011 completa o ano atual) em Relatório, Base e Modelos Excel.
- Logo: aparece no menu, abaixo de "Arquivos SPOBRAS"; pode ser tirado de um relatório já pronto (Configurações → "Tirar de um relatório…"); vai no topo de todas as planilhas exportadas (Base, vínculos, juntar planilhas, PDF para Excel).
- Relatório fotográfico: as fotos se sobrepunham no Excel porque a largura das colunas era gravada sem a margem interna da célula (cada coluna saía 5 px mais estreita). Agora a largura é gravada pela conta do próprio Excel (teste confere coluna a coluna), todas as fotos ficam no mesmo quadro, inteiras e centralizadas, com 12 px entre elas. Cabeçalho em grade como o do Jardim Paulistano: logo, título, caixa "SÃO PAULO OBRAS - SPOBRAS / DATA / CONTRATADA" e uma linha por campo (Unidade educacional, Contrato, Lote, Portaria, Processo SEI, Data da OS, Objeto, Responsável; vazio não aparece). Abre em "Visualização da quebra de página", escala de impressão 80% em A4.
- Relatório, etapa Dados: escola e contrato completam um ao outro. Contrato com várias escolas pede a escola (ou usa a já digitada, se for uma delas); escola em vários contratos pede o contrato; escola e contrato que não combinam na Base geram aviso e nada é trocado.
- **Cronograma próprio do app, sem modelo.** O app calcula: remuneração (padrão 10%, editável) sobre o valor da obra, total, valor de cada mês do início ao fim, total de cada ano (2026, 2027, 2028…), total solicitado e a tabela por dotação completa (obra e remuneração separadas, dotações iguais somadas). Ajustes por mês: percentual ou valor num mês, para todas as obras ou uma só; os outros meses dividem o resto. Tudo arredondado para cima no centavo, a sobra fica no último mês. A dotação completa vem da base de dotações pela resumida (3360) ou pode ser digitada; a da remuneração troca 5100 por 3900 (as que já terminam em 3900, como 2830, 2878 e 2879, servem às duas). A planilha tem logo, título, meses como "out/26" e as somas como fórmulas (com o resultado gravado). Um arquivo só, sem limite de 15 obras nem de 20 meses. O cadastro do modelo e a conferência pelo Excel saíram.

### Depois do segundo teste (13/11/2026)

- Relatório: foto sem legenda não leva texto nenhum ("Foto 01" saiu) e ganha o espaço. Cabeçalho mais compacto, na ordem do modelo do usuário: Unidade, Lote, Portaria, **Contrato e Data da OS na mesma linha**, Processo SEI, Objeto, **Fiscal** (era "Responsável") e **Descrição geral** (opcional). Campo vazio não aparece, nem o nome dele. O Processo SEI não vem mais da base (só se digitar). Margens estreitas já no arquivo (0,25" nas laterais) e escala de 88%: fotos maiores e a folha não passa de uma página. Botão **"Atualizar dados"**: busca de novo na base pelo contrato e pela escola e diz o que mudou (data, fiscal, descrição e fotos não mudam).
- Cronograma: **curva S** como distribuição (linear ou curva S, para todas as obras ou só para uma), com o pico sempre no meio (peso de cada mês = 0,3 + seno de 0 a 180°; em 6 meses: 9,9% · 17,8% · 22,4% · 22,4% · 17,8% · 9,9%); os ajustes por mês valem por cima. Mês do ajuste escolhido numa lista com os meses do cronograma. Valores em R$ (50000 vira R$ 50.000,00 ao sair do campo). Texto completo de qualquer caixa ao passar o mouse. Ao digitar o nome da escola, a base mostra todos os contratos dela (lote, portaria, dotação, situação) para escolher; escolas do mesmo lote aparecem para incluir com um clique. Planilha no estilo do modelo do usuário: título grande ao lado do logo, cabeçalho azul-marinho com letra branca, tudo em formato contábil, sem a frase de baixo.
- PDF para Excel: leitura pela **grade da tabela** (as linhas que o Excel ou o sistema desenham no PDF): colunas, mesclas, negrito, tamanho e cor da letra, fundos e alinhamento iguais ao PDF; páginas viram uma tabela só, sem repetir o cabeçalho; "Página X de Y" fica de fora; número que o PDF separou ("3 5.000,00") volta inteiro; reais em formato contábil, datas como data, NE, SEI, código e dotação como texto. Sem colunas Página/Linha, sem logo e sem a aba "Sobre". PDF sem grade: colunas pelo alinhamento da página inteira. Conferido localmente com os PDFs reais de Restos a Pagar e do Anexo I (não versionados): 80 de 80 e 739 de 739 linhas iguais ao PDF, célula por célula, e comparação visual lado a lado.
- Base de dados: ficha da obra mais larga (lista 40% e ficha 60%, com divisória que se arrasta e fica guardada), em blocos com duas colunas (Identificação, Prazo, Valores, Orçamento, Escopo), botão "Ampliar" e filtros que se recolhem numa linha. Novo filtro **Lote**, com uma faixa de totais do lote (contratado, medido, pago, saldo a medir e a pagar; as medições ficam fora porque são do contrato inteiro).

### Base: medições em trâmite e ficha nova (13/11/2026)

- Ficha da obra: blocos Identificação, **Valores** (valor original, aditado com o percentual sobre o original, contratado com aditamento, pago e saldo a pagar, como na planilha; pago + saldo = contratado), **Prazo** só com Data da OS e Término (sem assinatura), Orçamento e Escopo. Medido e a medir saíram.
- **Medições em trâmite** da obra (contrato + escola): toda medição que não está com "PAGAMENTO EFETUADO", inclusive "CADASTRADO PARA FATURAMENTO" e "DEVOLUÇÃO FISCAL", só em contrato em andamento, da mais recente para a mais antiga. Valor = VALOR CONSIDERADO + "VALOR REJUSTE" (sem remuneração). Migração 7 guarda o reajuste e o status do contrato da medição.
- Status do contrato: andamento = vigente, vencido, suspenso, em trâmite - aditivo, não iniciado, rescisão em trâmite; concluído = concluído, encerrado / TRD, TRP assinado, TRD em tramitação, em encerramento, rescindido; "SOB GESTÃO DA SIURB" não aparece; status desconhecido vale como andamento, com aviso.
- Totais do lote: original, aditado, contratado, pago, em trâmite (quantidade) e saldo a pagar. Lista em duas linhas por obra.
- Conferido localmente com a mestra real (não versionada): importação em 2 s, 759 medições em trâmite em 219 obras, nenhum status desconhecido.

### Celular (Android), em andamento

- `GeradorRelatorioPdf` (Documentos): o relatório fotográfico desenhado direto em PDF A4, a partir do mesmo plano de páginas do Excel (mesmas colunas, cabeçalho em grade, quadros e legendas, escala 88% e margens estreitas). É o que o celular usa, já que lá não há Excel. Testado (páginas, textos, imagens, nada fora da folha, nunca sobrescreve) e conferido visualmente.
- Fonte "Roboto" (a do Android) entrou na lista de fontes do PDF.

## Testado (automático, Linux e CI Windows)

`dotnet test`: 285 testes passam em Linux, 1 pulado (Excel real); no CI Windows roda também o teste que abre as telas. Casos da especificação cobertos até aqui:

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
| T12 | `CalculoCronogramaTests`, `GeradorCronogramaTests`, `CronogramaVmTests` | Remuneração e meses arredondados para cima com a sobra no último mês; 5%/7%/8% e o resto igual (como o Lote 239); valor num mês para uma obra e para todas; totais por ano; 16 obras num arquivo só; planilha válida com somas em fórmula. Visual conferido no LibreOffice. |
| T13 | `CalculoCronogramaTests`, `CronogramaVmTests` | Valor e meses inválidos, fim antes do início, ajustes acima do total e dotação fora da base aparecem na linha; com pendência não gera. |
| T14 | `ModelosExcelTests`, `ModelosVmTests` | Preenche cópia de .xlsx mantendo as fórmulas; PDF pelo exportador. Falta .xlsm real com VBA (nenhum modelo do usuário tem macro). |
| T15 | `ModelosExcelTests`, `ModelosVmTests`, `CronogramaTests` | Escrita sobre fórmula e célula mesclada bloqueadas; modelo com estrutura mudada pede revisão; troca de versão. "Aba nova em arquivo existente" não foi feita (ver pendências). |
| T16 | `ServicoFontesTests`, `BaseTests` | Mestra salva (relê sozinha), ocupada, ausente por instantes, ausente de vez e com coluna faltando: base anterior mantida, erro específico. |
| T17, T18 | `BaseTests` | Contrato com várias escolas e portarias; várias medições e empenhos por contrato sem duplicar valores. |
| T27, T29, T31 | `BaseTests`, `BaseEFontesTests` | Filtros vinculados em qualquer ordem, limpar um filtro, Data da OS por período, faixa de valor com valor ausente. |
| T32 | `CronogramaVmTests` | Início vindo da OS identificado; início previsto identificado; data trocada à mão mostra divergência sem mexer na base. |
| T19 | `PdfExtrasTests`, `PdfVmTests` | PDF com texto em várias colunas e páginas, valores e datas convertidos, origem por página e linha; PDF sem texto explicado. Falta testar com PDFs reais de Restos a Pagar e Anexo I. |
| T25 | `OrganizarConteudoTests`, `ComposicaoTests` | Escola em dois contratos, contrato com duas escolas, número parecido (1001 vs 001), PDF sem texto e base vazia: tudo em "Você decide" com o motivo. |
| T33 | `ConsultasTests` | Consulta salva reaberta depois de a fonte mudar mostra os dados novos; exportação com todas as linhas, tipos do Excel e aba de filtros. |
| T20 | `PdfTests`, `PdfVmTests` | Reduzir PDF com fotos (original preservado), PDF já otimizado (avisa e não cria cópia), protegido por senha (mensagem), assinado (aviso antes). |
| T23 | `BackupTests`, `CopiasDeSegurancaTests` | Banco da versão anterior: cópia feita antes de migrar, dados preservados; voltar a uma cópia na próxima abertura, com o estado atual guardado; retenção. Falta a troca real de versão do app no PC. |
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
- Busca e organizador por conteúdo usam o mesmo índice (tabela `IndiceArquivo`, migração 5), chaveado por caminho, tamanho e data do arquivo.
- PDF para Excel: colunas separadas por espaço largo entre palavras (cerca de dois caracteres). Funciona bem em relatórios de sistema alinhados; tabela com célula quebrada em duas linhas sai em duas linhas e precisa de conferência.
- Texto escrito em PDF (numeração) usa as fontes do computador pelo SkiaSharp (Segoe UI/Arial no Windows).
- Cronograma: portaria, lote e dotação só com algarismos são gravados como número (igual a digitar no Excel); com zero à esquerda, letras ou pontos ficam como texto.
- Cronograma: o app gera a planilha inteira (sem modelo). Os valores vêm calculados; as somas (total da obra, linha TOTAL, anos, total solicitado e tabela por dotação) vão como fórmulas com o resultado gravado, para continuarem certas se alguém mexer num mês no Excel.
- Relatório: largura de coluna gravada como Trunc(pixels × 256 / 7) / 256 (Calibri 11), a conta inversa da que o Excel usa para desenhar.
- Fonte da interface: "Inter, Segoe UI". A Inter não vai no pacote; no PC sem ela o Windows usa Segoe UI.
- A prévia do relatório é desenhada pelo próprio plano de páginas (mesmas contas do gerador), não por uma imagem ilustrativa.
- Artefatos do CI não podem ser baixados do ambiente do Claude Code (rede bloqueada); por isso o teste das telas confere o layout por medidas e publica as imagens para o usuário ver no GitHub.

## Pendências com o usuário

- [ ] Logo SPOBRAS (passo 4), a ser colocado pelo próprio usuário no app; confirmar se há versão em melhor qualidade.
- [ ] Decidir se o caminho da pasta e os códigos de contrato de receita que estão em `docs/ARQUITETURA.md` devem ser trocados por exemplos genéricos (repositório público).
- [x] Dotação 3363: o usuário confirmou que a obra é 5100 e a remuneração 3900 (sempre essa diferença). O app corrige pela regra e avisa na linha.
- [x] Extração de Restos a Pagar e Anexo I Tabela SME: o usuário mandou os PDFs; a leitura pela grade copia as colunas certas (conferido localmente, arquivos não versionados).

## Decidido pelo usuário

- **Cronograma:** só o mês importa (nada de dia); portaria no formato "Nº 03 (2021)"; remuneração com padrão de 10% e opção de mudar; um arquivo novo por cronograma basta. O app faz o cronograma inteiro (sem modelo): uma ou várias escolas, breve escopo digitado, total por ano, total solicitado, ajuste de valor ou percentual num mês com os seguintes recalculados, tabela por dotação completa. Arredondar sempre para cima no centavo; a sobra fica no último mês.
- **Dotações:** remuneração termina em 3900 e obra em 5100, sempre, menos 2830, 2878 e 2879, em que a 3900 serve às duas. Vale também para a 3363, que na planilha veio com 3900 na obra.
- **Relatório:** foto inteira (sem cortar), mas sem uma encostar na outra.
- **Base (13/11/2026):** em trâmite = tudo que não foi pago (contrato em andamento, por escola), com valor considerado + reajuste e sem remuneração; saldo = saldo a pagar da planilha; só Data da OS e término; percentual do aditado sem aviso de limite.
- **Celular:** Android primeiro, APK gratuito sem loja (iPhone talvez depois). Base, relatório fotográfico (prioridade) e ferramentas de PDF; sem cronograma. Só a planilha mestra (Dotações não é usada no celular), escolhida uma vez, só leitura, relida ao abrir. Fotos ficam no rascunho; câmera com 4:3, 16:9, tela cheia, 1:1 e flash; salva Excel e/ou PDF numa pasta escolhida (opção de uma pasta por escola); mesmo logo do PC.
- **Edição no app:** qualquer dado pode ser mudado no app; o dado da planilha vem por padrão e o usuário pode mudar.
- **Macros:** nenhum modelo precisa rodar macro depois de preenchido.
- **Bases:** a planilha mestra (Escolas_Contratos_Com_Relatorio, com as tabelas do SGCOE e de contratos) e a de Dotações Completas. Nenhuma planilha complementar. O relatório do Jardim Paulistano (fotos que nunca se encostam) e o Cronograma revisado são os modelos de referência.
- **OCR:** quer converter PDF escaneado em Excel (feito com o OCR do Windows).

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
- Modelos Excel e o PDF do cronograma pelo Excel real: o código existe, mas só roda com Excel; no Linux e no CI é usado um exportador de teste.
- Cronograma novo aberto no Excel real (formato "out/26" em português e as fórmulas recalculando): conferido só no LibreOffice.
- Busca, repetidos e organizador por conteúdo em pastas grandes da rede/OneDrive (arquivos só na nuvem são baixados ao ler o conteúdo): medir no PC do trabalho.
- OCR no PC do trabalho: no CI o teste usa o OCR do Windows Server; no PC, o resultado depende do pacote de idioma português do Windows (sem ele, o app usa o idioma do usuário e avisa qual). Conferir com um PDF escaneado real.
- `Spobras.Excel.Tests`: só roda com Excel instalado (pulado no Linux e no CI).
