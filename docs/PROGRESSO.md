# Progresso

## GPS revisado em 18/11/2026

No teste do usuário, dentro do escritório com Wi-Fi, a leitura terminava em "sem sinal". A causa estava no app: o modo de localização "mais preciso" do MAUI descarta em silêncio toda leitura com mais de 50 m de margem, e dentro de prédio quase todas ficam acima disso. Revisado como na seção 9 de `docs/PLANO_CARIMBO_E_LEVEZA.md`.

- **Opcional e sem pedir de cara:**
  - O cartão "Endereço nas fotos" (passo 1) tem "Usar o endereço pelo GPS", **desligado** no começo.
  - Desligado, o app nunca pede nem usa a localização; o endereço pode ser digitado.
  - Ao ligar, uma folha explica antes e só então aparece a janela do Android.
  - A câmera nunca pede permissão.
- **Leitura em 3 etapas, sem descartar leitura:**
  1. A última posição que o celular já conhece, se tiver até 2 min e até 50 m, vale na hora.
  2. Senão, o app ouve GPS, Wi-Fi e rede e a fonte combinada (Android 12+) ao mesmo tempo, até 20 s. Para antes ao chegar a 20 m, fica com a melhor leitura e mostra cada melhora ("Procurando a posição… 35 m").
  3. Sem leitura atual, ainda serve uma posição conhecida de até 10 min.
  - Leitura com mais de 100 m pede para conferir o endereço.
- **Motor:** o `LocationManager` do próprio Android, sem biblioteca a mais.
  - A biblioteca do Google foi testada: puxava versões mais novas das bibliotecas AndroidX que o MAUI usa (risco de quebrar o app) e não baixa neste ambiente.
  - A fonte "rede" do Android já usa o Wi-Fi e as antenas (nos celulares com Google, é o mesmo serviço do Google Maps).
- **Endereço:** só o primeiro, o mais próximo, em português.
  - Quando o serviço do Android falha de passagem, o app tenta de novo até 3 vezes. Sem internet, guarda o ponto na hora.
  - Se o serviço não separar rua, número e bairro, a linha completa vira o texto curto (sem CEP, cidade e país).
- **Situações com botão:**
  - localização do celular desligada: "Ligar a localização" e "Tentar de novo";
  - permissão só aproximada: aviso e "Permitir localização precisa";
  - localização por Wi-Fi desligada: aviso;
  - sem permissão: "Permitir e ler".
- **Diagnóstico da localização** em Configurações: as últimas 10 leituras, com fonte, precisão, tempo e resultado.
- **Testes:**
  - Novos:
    - desligado nunca pede nem lê;
    - ligar pede uma vez;
    - última posição boa vale na hora, e a velha ou imprecisa não vale;
    - 80 m dentro do prédio é aceito;
    - para em 20 m;
    - posição de reserva até 10 min;
    - localização desligada;
    - só aproximada;
    - serviço de endereço falhando 2 vezes e respondendo na 3ª;
    - sem internet;
    - diagnóstico;
    - linha de endereço encurtada.
  - Visuais: interruptor desligado, folha que explica, procurando, lido.
  - Total: 616 testes, todos passando.
- **Só no celular de verdade dá para conferir:** o tempo e a precisão dentro do escritório e da escola. O diagnóstico da localização mostra isso.

## Carimbo nas fotos, endereço pelo GPS, fotos "Salvando…" e app mais leve em 18/11/2026

Implementado o plano `docs/PLANO_CARIMBO_E_LEVEZA.md` (decisões do usuário na seção 8). Carimbo e GPS só no celular.

- **Fotos seguidas sem susto:**
  - Cada foto entra na lista no toque, com a imagem do visor e "Salvando…". A câmera mostra "Salvando 3…" e depois "✓ Salvas", e o topo da lista mostra "12 fotos · 2 salvando".
  - A câmera salva uma foto por vez, na ordem dos toques. Com 6 salvando (3 em celular com pouca memória), o botão fica em "Aguarde…".
  - Fechar a câmera não espera a fila: as fotos chegam à lista sozinhas.
  - Foto que não salvou fica com borda vermelha. O toque abre "Tentar de novo" ou "Tirar da lista"; ela nunca some sem aviso.
  - "Gerar" e "Começar um relatório novo" esperam as que estão salvando.
  - Em Configurações › "Diagnóstico da câmera" ficam os tempos de cada passo das últimas 20 fotos, para medir num celular de verdade.
- **Carimbo (escola, endereço, data e hora):**
  - O cartão "Carimbo nas fotos" no passo 2 abre o editor: itens que ligam e desligam, arrastar com um dedo (gruda no canto mais perto), dois dedos ou a barra para o tamanho, "Ver em pé" e "Padrão".
  - Posição e tamanho são frações do lado menor da foto, então o carimbo fica igual em foto em pé, deitada e girada.
  - Vai **gravado na cópia** que entra no Excel e no PDF. A cópia é feita em segundo plano junto com a cópia reduzida, então a geração não fica mais lenta. A original nunca muda.
  - Cada foto pode sair sem carimbo ("Carimbo nesta foto").
  - O passo 3 avisa foto sem data de captura, carimbo pequeno para o número de fotos por página (menos de 5 pt) e carimbo sem endereço.
  - As prévias das páginas mostram o carimbo.
  - Com "Guardar também na galeria" e o carimbo ligado, a galeria recebe a foto carimbada em tamanho cheio, por uma fila em segundo plano. A fila espera até 30 s pelo endereço que ainda está sendo lido.
- **Endereço pelo GPS:**
  - Lido uma vez por relatório, quando a câmera abre e só com o carimbo usando o endereço. A espera vai até 15 s ou até uma leitura de 30 m; depois, o serviço do próprio Android transforma o ponto em endereço.
  - Fica num texto curto ("Rua, nº – Bairro") e é guardado em `enderecos.json` por **escola + contrato**, porque na planilha duas escolas diferentes podem ter o mesmo nome e até o mesmo código. Mesmo nome com outro contrato nunca é usado sozinho: o app pergunta "É o mesmo local?".
  - Aviso de distância só acima de 300 m mais a margem de erro das duas leituras, com "Atualizar" e "Manter".
  - Sem permissão, sem sinal, sem internet (o ponto fica guardado e "Tentar de novo" busca só o endereço) e sem serviço (digitar): cada caso tem a sua mensagem.
  - Os dados do relatório têm o cartão "Endereço das fotos", e a câmera mostra o selo "📍 Rua…".
- **Leveza:**
  - Um APK para cada tipo de processador, `ArquivosSPOBRAS-64bits.apk` e `ArquivosSPOBRAS-32bits.apk`, no lugar do APK único de 84 MB. Cada um ficou com cerca de 45 MB (CI 92: 46,6 MB e 45,7 MB). O CI confere que cada APK leva só o código do seu processador.
  - A fonte do relatório agora é um subconjunto da Carlito com outro nome, como a licença pede (`Fontes/reduzir-fonte.py`): passou de 2,6 MB para 0,15 MB, com as mesmas larguras de letra (teste).
  - Celular com pouca memória prepara 1 ou 2 fotos por vez.
- **Testes:**
  - Unidade: conta do carimbo em 7 formatos × 4 cantos × 3 tamanhos, chave da escola, distância, endereço.
  - Gravação com conferência de pixels e giro, cache, Excel e PDF com a foto carimbada, fonte reduzida.
  - Endereço com GPS e serviço simulados: mesma escola, mesmo nome com outro contrato, longe, sem permissão, sem sinal, sem internet, troca de escola durante a leitura, arquivo danificado e mil escolas.
  - Salvando, galeria e segurança dos arquivos: impressão das fotos e legendas antes e depois de cada ação.
  - Visual do celular em 412 e 360 px: Salvando, fila cheia, foto não salva, editor (arrastar, tamanho, em pé), ficha da foto, endereço, selo na câmera, páginas e diagnóstico.
  - Estresse com 300 fotos chegando juntas, carimbo, galeria e troca do canto: as originais ficaram idênticas e sobrou uma cópia por foto. Gerar Excel e PDF com as cópias prontas levou 2,2 s.
  - Custo do carimbo medido em 300 fotos: o mesmo tempo e a mesma memória que sem carimbo (cerca de 73 ms por foto).
- **Beta tester:** achou e corrigiu:
  - a fileira de botões do passo 2 passava da tela em 360 px (o carimbo virou cartão próprio);
  - endereço e data largos estouravam a faixa em foto estreita (a letra agora diminui até caber);
  - trocar de escola não fazia a conferência de distância na escola nova;
  - o tamanho padrão do carimbo dava letra de 4,5 pt com 6 fotos por página e cabeçalho completo (o padrão subiu para 6% do lado menor).

## Cabeçalho em todas as páginas e mover fotos em 18/11/2026

- **Cabeçalho completo em todas as páginas** (saiu o cabeçalho reduzido), no PDF, no Excel e nas prévias. Os nomes dos campos ficam sem fundo cinza, separados do valor por uma linha fina, e os dados saem em letra normal, como antes.
- **Assinatura:** se a última página estiver cheia, todas as fotos do relatório diminuem um pouco, igual em todas, e as páginas continuam cheias (opção 2, aprovada).
  - Só se precisassem ficar pequenas demais, a última linha desce junto com a assinatura.
  - Com cabeçalho enorme, as fotos diminuem até o menor quadro; se nem assim couber, o app explica o motivo e não gera.
- **Mover as marcadas para o início ou para o fim** (PC e celular): vão juntas, na ordem em que estavam, continuam marcadas e há Desfazer de 5 s.
- **"Deitadas primeiro, em pé no fim"** junto das ordenações: vale o formato depois do giro, a foto quadrada fica com as deitadas e dentro de cada grupo a ordem fica igual. Também tem Desfazer.
- A regra fica em `OrdemDasFotos` (Core), a mesma no PC e no celular.
- **Testes:**
  - unidade e 500 listas aleatórias sem perder nem repetir foto;
  - reordenar enquanto a câmera grava;
  - Desfazer com foto nova no meio do caminho;
  - teste visual do celular com mover e desfazer;
  - 1.500 relatórios aleatórios com o cabeçalho em todas as páginas;
  - PDF e Excel com o mesmo número de páginas em 13 casos;
  - estresse de 300 fotos: Excel e PDF em 15,9 s sem preparo e 1,8 s com as fotos preparadas.

## Relatório fotográfico novo, tela 2 e proposta A em 17/11/2026

Implementado o que o usuário aprovou nos modelos `modelo-relatorio.pdf` e `modelo-tela2-e-assinatura.pdf`, no computador e no celular.

**Página do relatório (PDF e Excel):**
- Medidas em pontos da A4 (`GradeRelatorio`): margens de 12 mm dos lados, 11 mm em cima e 9 mm embaixo.
- Cabeçalho em faixa: logo, título com a data por extenso e SPOBRAS. Abaixo, os campos em grade dois a dois; campo vazio não aparece. Da página 2 em diante, um cabeçalho reduzido com a escola e o contrato.
- Rodapé com a escola, a data e "Página X de Y".
- Todas as fotos no mesmo quadro, inteiras e centralizadas.
- Legenda colada no quadro, com espaço de pelo menos 2 linhas; a descrição do serviço vem embaixo.
- Um desenho único (`DesenhoRelatorio`) é usado pelo PDF e pelas prévias do PC (WPF) e do celular (SVG): a tela mostra exatamente o papel.
- A fonte Carlito vai embutida no app (licença OFL, `Fontes/OFL.txt`) e tem as medidas da Calibri do Excel. A quebra de linha é calculada com as larguras da própria fonte (`TextoMedido`) e sai igual no PDF.

**Limites e textos:**
- Legenda até 100 caracteres, descrição do serviço até 300 e descrição geral até 300, com contador. Acima disso, o relatório não é gerado e a mensagem diz qual foto encurtar.
- Quebras de linha viram espaço, para o texto sempre caber.
- Emoji e letras de outros alfabetos saem do texto com aviso, em vez de virar quadradinho no PDF.
- Foto sem legenda ou sem descrição: aviso, sem impedir a geração.

**Assinatura:**
- Fica sempre na página da última foto, nunca sozinha.
- Página única cheia: os quadros diminuem só o necessário, até 75%.
- Mais de uma página: o cabeçalho reduzido deixa espaço. Se ainda faltar, a última linha de fotos desce junto com a assinatura.

**PDF montado pelo app no PC** (antes era pelo Excel): sempre A4, com as opções Excel e PDF, Só PDF ou Só Excel, lembradas para os próximos relatórios. Excel e PDF saem com o mesmo nome e o mesmo número.

**Excel:**
- Mesmo layout do PDF. As linhas da planilha seguem o plano, com alturas em pixels inteiros para a soma não passar da página.
- Os quadros cinza são desenhos.
- "Ajustar todas as colunas em uma página", com quebra manual no fim de cada página.
- Conferido no LibreOffice. **Falta conferir no Excel do PC do trabalho.**

**Tela 2 "Fotos e textos":**
- O modo ("Só legenda, até 6 por página" ou "Legenda e descrição do serviço, até 4") saiu da conferência e fica no topo.
- Cada campo diz o que é e onde sai, com contador.
- Mesmo texto para várias fotos (legenda e descrição): vai para as marcadas ou, sem marcadas, só para os campos em branco; campo vazio não apaga nada.
- Tirar as marcadas com confirmação e Desfazer de 5 s; o ✕ tira na hora, também com Desfazer.
- Trocar de modo guarda as descrições e avisa.
- Na conferência, 6 e 8 por página ficam apagados no modo com descrição.
- PC: corretor ortográfico pt-BR nos campos; arrastar para reordenar continua; em janela baixa a etapa rola.
- Celular: a descrição do serviço passou a existir, a grade marca "Sem legenda" e "Falta descrição", e a conferência ganhou as assinaturas.

**Base (proposta A):**
- PDF "Resumo da obra" e "Resumo do contrato" com:
  - composição (original + aditado com o percentual sobre o original = contratado);
  - execução em pizza, com o percentual ao lado do nome e o valor embaixo;
  - saldo a pagar em destaque;
  - sem medido nem saldo a medir.
- Medições em trâmite com "Medido" e "Protocolo".
- A data de protocolo é importada da tabela de medições (coluna opcional) e guardada no banco (versão 8). Sem ela, aparece "sem protocolo" (análise fiscal). A ficha do PC e a do celular têm a coluna Protocolo.
- Corrigido: valores na casa dos milhões se sobrepunham na tabela das escolas do contrato.

**Testes:**
- Pior caso dos limites em todas as capacidades, com e sem assinatura.
- 1.500 relatórios aleatórios (nada sai da folha, nenhuma letra se perde).
- Cada palavra do PDF conferida dentro do espaço planejado, com a fonte embutida.
- Excel válido, com a altura de cada página dentro da folha.
- Tirar e Desfazer, mesmo texto, formatos.
- Teste visual do celular em 360 e 412 px com o modo com descrição.
- Telas reais do Windows no CI.

**Beta tester:** além do que os testes acima pegaram, encontrei e corrigi:
- Descrição cheia de Enter travava a geração com mensagem errada.
- Emoji e outros alfabetos saíam como quadradinho ou sumiam sem aviso.
- O Desfazer de uma foto tirada podia devolvê-la para um "Novo relatório".
- Valores milionários se sobrepunham no resumo do contrato.
- Corrida só de teste entre miniaturas e prévia.

## Resumo por contrato em 16/11/2026

Implementação de `docs/codex/RESUMO-POR-CONTRATO.md` com dados sintéticos. O filtro Contrato pesquisa por número, lote ou modalidade. Cada contrato selecionado tem resumo e tabela próprios. As consultas antigas com Lote são convertidas para os contratos daquele lote e, quando não há correspondência, o lote vai para a pesquisa. O cronograma do Windows inclui as escolas de um contrato e guarda a origem no rascunho para sugerir as demais escolas daquele contrato.

Windows e Android usam o mesmo gerador PDF para contrato e escola, com prazo calculado em horário de Brasília, logo opcional, percentuais e medições em trâmite. Foi conferida a equivalência de texto e paginação dos dois caminhos com PdfPig. Os testes também cobrem uma, seis e treze escolas, dois contratos, encerrado, não iniciado, logo, nome de arquivo, preservação das consultas e tentativa de exportação simultânea ou saída durante a gravação.

**Verificação visual e beta:** as oito telas de referência do celular foram abertas em 360 e 412 px. O roteiro cobre tamanho dos alvos, contraste, texto, elementos cobertos e valores de R$ 99.999.999,99 com nomes extensos. Também foi testado um contrato com todas as escolas ocultas por outro filtro. A primeira execução WPF encontrou controles cortados na Base e no Cronograma em janela menor. O resumo passou a dividir a altura com a ficha, e a área de obras do Cronograma passou a rolar com a busca, a tabela, as sugestões e os avisos.

**Regressão encontrada na CI:** Excel e PDF podiam tentar substituir uma foto preparada que outro leitor mantinha aberta no Windows. A primeira cópia completa passa a ficar no cache, com teste para o caso em que já está aberta para leitura.

## Desempenho, histórico e exclusão em 16/11/2026

**Gerar o relatório (celular e computador):** cada foto agora é aberta já reduzida (perto do tamanho do relatório) e girada e reduzida num desenho só; é preparada uma vez para o Excel e o PDF e as fotos são preparadas em paralelo (até 4). No celular, depois das miniaturas, o app vai preparando as fotos no tamanho do relatório em segundo plano (um núcleo, enquanto o usuário escreve as legendas). Medido com 300 fotos de 12 MP neste computador: Excel + PDF de ~137 s para **16 s** sem nada preparado e **1,9 s** com as fotos já preparadas. As 8 orientações de câmera × 4 giros conferidas canto a canto; o PDF sai com as mesmas imagens com ou sem preparo.
**Outros ganhos:** PC monta a prévia das páginas só na Conferência (onde ela aparece) e faz até 3 miniaturas ao mesmo tempo; celular só copia a mestra se o tamanho ou a data mudaram (Reler força); Duplicados compara o começo dos arquivos antes de ler inteiros. Banco: abrir a conexão custa 0,4 ms, então ficou como está (reaproveitar traria risco de arquivo preso nas cópias de segurança).
**Histórico:** 20 últimas ações no PC (ação interrompida fica para poder ser retomada); 5 últimas leituras da planilha, mostradas no horário de Brasília ("16/11/2026 às 14:32"); registro de erros do PC no máximo 1 MB; miniaturas do PC sem uso há 30 dias saem; a cópia do relatório no celular sai ao deixar a tela de pronto.
**Excluir arquivo gerado:** celular, lixeira em "Últimos gerados" com Desfazer de 5 s (se o local não deixa apagar, avisa e o arquivo fica na lista); PC, "Excluir arquivo" nas ações de arquivo gerado do Histórico, com confirmação, para a Lixeira do Windows. Vale mesmo se o arquivo foi alterado; fotos da galeria nunca.
**Compilação antecipada do APK (testada e desfeita):** no Android do .NET 10 ela só funciona junto com o corte de código, que fica desligado porque as bibliotecas de Excel e PDF usam reflexão (o corte pode quebrá-las sem aviso e isso só apareceria no celular). O build voltou ao normal.
**Câmera:** engrenagem no topo abre "Configurações da câmera" (proporção, flash, galeria); o flash atual continua visível ao lado.
**Beta tester:** dois toques na lixeira, excluir e sair na hora, vários seguidos com Desfazer, flash guardado, entrar e sair rápido da câmera; girar e legendar durante a geração (achou versões preparadas de giros antigos sobrando até a próxima abertura: agora saem ao fim de cada geração).

## Revisão das fotos do relatório em 16/11/2026

Causa da foto que não aparecia: sair da câmera (Pronto, fechar ou voltar) logo depois de tirar desligava tudo antes de a foto terminar de gravar. Com flash Auto (o padrão de antes), o "Segure firme…" de 1 a 3 s quase sempre perdia a foto. O teste visual não pegava porque usava só o flash Desligado e sempre esperava a foto gravar.

**Corrigido (cada item com teste que falhava antes):**
- Câmera: sair espera as fotos já pegas gravarem; trocar de câmera também. Em cada toque fica guardado o quadro do visor: vale se o sensor do flash falhar ou passar de 4 s. Durante o "Segure firme" o botão mostra que está esperando. Flash padrão **Desligado** (foto na hora); quem nunca escolheu passa para Desligado, quem escolheu mantém. Celular deitado com a rotação automática desligada: a foto é girada pelo sensor (conferir no aparelho).
- Fotos perdidas ao ordenar ou usar "Atualizar dados" enquanto uma foto gravava (150 tiradas, ficavam 52): agora toda mudança parte do rascunho atual.
- Rascunho estragado apagava todas as fotos: agora há cópia da versão anterior; se nenhuma der para ler, as fotos da pasta voltam (sem as legendas) e a tela avisa. Foto que está na pasta mas fora da lista (app fechou no meio) volta para o fim do relatório; gravações pela metade e arquivos que não são foto saem. Campos vazios ou estranhos no arquivo não quebram mais.
- Legenda ou descrição longa travava a geração (celular e computador, o paginador é o mesmo): legenda longa continua na página seguinte como a descrição; texto maior que uma página inteira segue página a página (antes era cortado sem aviso); cabeçalho grande demais vira pendência explicada em vez de erro. Legenda limitada a 300 caracteres, com contador (celular) e limite no campo (computador).
- Nome de escola enorme estourava o nome do arquivo e a pasta da escola: os dois limitados.
- Progresso de gerar Excel + PDF voltava para trás: agora conta de 1 até o total.
- Dois toques rápidos em "Gerar relatório" faziam duas gerações e as duas falhavam: a segunda é ignorada (em todas as telas, não só nesta).
- Digitar e sair sem fechar o campo (voltar, menu) perdia o texto: agora grava ao sair, e a legenda grava também enquanto digita.
- Qualquer erro inesperado (inclusive do Android) vira aviso na tela em vez de fechar o app.
- Miniatura que falhou por um instante é feita de novo; arquivo que não é foto mostra "Sem prévia". Girar não acumula miniaturas; descartar o rascunho com miniaturas em andamento não deixa sobras; nomes parecidos (IMG e IMG-0001) não apagam a miniatura um do outro.
- Mesma foto escolhida de novo na galeria entra assim mesmo, com um aviso só (com os nomes). Preferências do celular lidas uma vez (antes, do disco a cada botão) e com cópia de segurança. Cópia da galeria no Android vai para um temporário e troca de uma vez.

**Testes novos:** 20 testes de robustez do rascunho (incluindo tudo ao mesmo tempo: câmera, galeria, girar, mover, tirar, desfazer e ordenar), estresse do paginador com 1.500 relatórios aleatórios (legendas até 5.000 caracteres, cabeçalhos enormes, todas as capacidades), teste visual da câmera (sair no meio, flash lento, sensor travado, botão esperando), contador da legenda, fotos repetidas, digitar e sair, dois toques em Gerar. Rodados 8 vezes seguidas sem falha intermitente.

## Melhorias fechadas no planejamento de 14 e 15/11/2026

**Celular, fotos e câmera**
- A foto é pega no instante do toque, direto no navegador do app, sem esperar o .NET: com flash Desligado ou Lanterna é o quadro que estava no visor (medido no teste: menos de 150 ms do toque à imagem). Com flash Automático ou Ligado aparece "Segure firme…" até o Android registrar. O flash branco só pisca depois que a foto foi pega; o contador e a miniatura da última foto mudam quando ela foi salva. Dá para tirar fotos seguidas (o rascunho grava uma mudança por vez; 20 fotos simultâneas no teste, nenhuma perdida).
- Miniaturas feitas em segundo plano e guardadas em arquivo: a tela de fotos abre na hora; ao reabrir o app, 60 miniaturas carregam em menos de 0,5 s. Tamanho de cada foto guardado ao entrar: conferir as páginas não relê as fotos.
- Tirar uma foto: sem confirmação, com aviso "Foto removida · Desfazer" e barra vermelha de 5 s. Remoções seguidas se juntam ("3 fotos removidas"); Desfazer devolve cada foto para a posição de onde saiu. Depois do tempo, a cópia do app é apagada e não volta. Se o app fechar no meio, a remoção vale na próxima abertura; sobras de cópias são limpas ao abrir.
- Modo seleção: contagem de marcadas, "Marcar todas" e "Desmarcar todas", legenda em lotes sem sair do modo (marcações e campo limpos para o próximo grupo), "Tirar do relatório" para as marcadas com uma confirmação (e Desfazer). O botão "Tirar foto" some no modo seleção e sobe quando o aviso de desfazer aparece. O aviso nunca cobre a folha da foto.

**Base de dados (celular e computador)**
- Só consulta: editar obra, incluir obra e editar vínculos de dotação saíram (os comandos ficam travados).
- Configurações da base (engrenagem discreta na Base): aviso "Antes de mexer aqui"; aba Planilha com arquivo, tabelas e as 53 colunas usadas pelo app, cada uma com ✓ ou ⚠ e a lista de colunas que existem de verdade na planilha (com exemplo); Testar leitura (total e 3 primeiras linhas), Salvar só depois do teste, Voltar ao padrão com confirmação. Aba Status: andamento, concluído ou não mostrar para cada status da planilha (status novo com selo NOVO). Aba Diagnóstico: totais, ajustes feitos e últimas leituras. Vale só no aparelho; a planilha nunca muda.
- Empresa sem opções repetidas por grafia (espaço sobrando, maiúscula); obras ocultas (sob gestão da SIURB) filtradas no próprio banco; listas de opções guardadas por leitura (sugestões e ficha não refazem 9 consultas a cada letra); totais do lote numa consulta só.
- Celular: filtros grandes mostram as 100 primeiras opções (e o resto pela pesquisa), sem nada pular de lugar ao marcar. Computador: o filtro não volta ao topo ao marcar; a sugestão do relatório não reabre depois do Enter.

**Testes**
- 392 testes (1 pulado, precisa do Excel), incluindo desempenho com base inventada de 2.000 obras e 8.000 medições (cada consulta com tempo máximo) e uso intenso: 300 combinações de filtros conferidas contra a conta direta, 50 rodadas de remover e desfazer com 60 fotos.
- Teste visual do celular em 6 cenários (pronto, primeira vez, planilha movida, sem internet, coluna renomeada e uso intenso), dois tamanhos de tela: remover, desfazer, seleção, câmera, configurações da base (escolher a coluna nova, testar, salvar, voltar ao padrão) e tempos reais no navegador (Base com 2.000 obras, filtro de 2.000 escolas, 60 fotos). Botões de folhas e janelas cobertos por outro elemento agora são acusados automaticamente.
- Telas reais do Windows (CI): 3 telas novas das Configurações da base.

## Melhorias de consulta e relatório em 13/11/2026

- Windows e Android: Processo SEI preenchido e restaurado pela Base, rótulos curtos, sugestões de escola e contrato ao digitar e borracha nos campos do relatório. A data do relatório começa vazia no Windows.
- Base: filtro Empresa e seleção múltipla pesquisável. Os valores de um filtro são combinados por OU, e filtros diferentes por E. As consultas salvas no formato anterior continuam abrindo. A faixa de totais soma os lotes marcados. No Windows, limpar os filtros ou perder a seleção restaura a lista quando a ficha está ampliada.
- Fotos: reordenação por arraste no Windows e por toque prolongado no Android, com os botões de mover disponíveis. No Android há legenda em lote e ordenação por nome original ou data de captura, com leitura de EXIF para fotos de rascunhos antigos.
- Verificação local: build sem erros e 372 testes executados pelo xUnit direto. A execução de `dotnet test` e os testes WPF em janela real ficam a cargo do CI. O teste visual do celular passou nos quatro cenários em 360 × 760 e 412 × 915, com dados sintéticos. Foram gerados e conferidos PDFs com o lote apagado e com o lote restaurado pela Base e duas legendas aplicadas em lote.
- Conferência no aparelho: validar toque prolongado, vibração e rolagem durante o arraste no Android real. Validar o arraste com mouse no Windows.

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

### Celular (Android), primeira versão (13/11/2026)

- Arquitetura na seção 12 da `ARQUITETURA.md`. Telas no estilo do protótipo aprovado: Início (situação da base, atalhos, rascunho e últimos gerados), Primeira vez (só a planilha mestra), Base (busca, filtros de lote, portaria, status e contrato, totais do lote), Ficha (valores com aditado e percentual, pago e saldo a pagar, prazo da OS ao término, medições em trâmite, orçamento, escopo, a escola em outros contratos), Relatório (dados com busca por escola ou contrato e escolha quando há mais de um, Atualizar dados, fotos da galeria e da câmera, legenda, girar, mudar de lugar, tirar; conferir com miniatura fiel da página; gerar Excel e/ou PDF), Câmera (formatos e flash), PDF (juntar, separar, reduzir, fotos em PDF, logo e numeração, PDF para Excel) e Configurações (planilha, pasta, pasta por escola, logo).
- `GeradorRelatorioPdf` (Documentos): o relatório desenhado direto em PDF A4 a partir do mesmo plano do Excel. Fonte "Roboto" (a do Android) entrou na lista de fontes do PDF.
- Buscas por contrato, escola e lote passaram para `Spobras.Dados.Base.ProcuraObras`, usadas pelo computador e pelo celular (o computador só chama).
- Testes: 12 de lógica (`Spobras.Celular.Tests`) e o teste visual em dois tamanhos de tela, 4 cenários. O teste visual achou e já corrigiu: a folha de opções da câmera ficava por baixo da câmera, a ferramenta de PDF guardava o resultado da anterior e o ícone de busca saía enorme.
- O C# do app Android compila aqui (com um SDK falso só para compilar); o APK de verdade sai no CI (job `celular`, artefato `ArquivosSPOBRAS-Android`). CI 54: tudo verde (Windows, lógica e visual do celular, APK de 83 MB sem corte de código).
- **Primeiro teste no aparelho (13/11/2026): o app ficava parado em "Abrindo…".** Causa: a página do app chamava `Blazor.start()` e o BlazorWebView do Android também chama, então as telas nunca se ligavam (no navegador de teste isso não aparece, a página é outra). Corrigido, com teste que impede voltar. Na mesma revisão: respeita a barra de status e o teclado (Android 15 desenha atrás deles), ícones da barra de status claros, erro antes de abrir aparece na tela em vez de travar, erro numa tela mostra a mensagem e "Voltar ao início", trocar a planilha durante uma leitura não perde a troca, voltar ao app em menos de 2 minutos não baixa a planilha de novo, seletor de arquivos sempre aberto na linha principal do Android, e negrito do PDF simulado quando a fonte do aparelho (Roboto variável) não tem arquivo próprio de negrito.
- Instalar no celular: baixar o artefato `ArquivosSPOBRAS-Android` da última execução do CI, abrir o `ArquivosSPOBRAS-64bits.apk` no Android (se o celular recusar, é um aparelho de 32 bits: use o `ArquivosSPOBRAS-32bits.apk`) e permitir "instalar apps desconhecidos" para o navegador ou o gerenciador de arquivos. No primeiro uso: escolher a planilha mestra no OneDrive/SharePoint, a pasta de saída e o logo (Configurações).
- Conferência no celular (pelo usuário): 1) escolher a planilha e ver o total de obras; 2) fechar e abrir o app, ver "relida"; 3) ficha de uma obra em andamento e de uma concluída; 4) relatório: escola com dois contratos, 3 fotos pela câmera (testar 16:9 e flash), 2 da galeria, legenda, girar, fechar o app no meio e voltar; 5) gerar Excel e PDF numa pasta do OneDrive com pasta por escola; 6) abrir o PDF e compartilhar no WhatsApp; 7) juntar dois PDFs; 8) modo avião: abrir o app e ver o aviso de sem internet.

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

- **GPS, segundo teste (18/11/2026):**
  - No prédio da SIURB o endereço saiu na rua vizinha, com margem de 100 m (o Google Maps também só achou a região).
  - O interruptor desligado não tirava o endereço das fotos.
  - Proposta na seção 10 de `docs/PLANO_CARIMBO_E_LEVEZA.md`, aguardando as decisões do usuário.

- **Relatório, Excel:** conferir no Excel do PC do trabalho que "ajustar todas as colunas em uma página" respeita a quebra no fim de cada página (no LibreOffice respeita). O PDF não depende disso.

- **Resumo por contrato:** confirmar o nome sugerido para vários contratos, `Resumo dos contratos - AAAA-MM-DD.pdf`.
- **Resumo por contrato:** confirmar o texto do selo quando há data da OS, mas falta o término. Neste caso aparece “Sem término informado”.
- **Resumo por contrato:** confirmar como exibir a pizza caso os percentuais calculados ultrapassem 100% por inconsistência nos dados. A legenda usa o valor contratado como denominador, e as fatias exibem a proporção entre os valores disponíveis.


- ~~Celular, chave do APK~~: resolvido em 13/11/2026. A chave foi criada pelo workflow "criar chave do APK" e está nos segredos; o CI 57 já assinou o APK com ela. As próximas versões instalam por cima.
- **Celular, ícone:** o ícone do app é genérico (o logo oficial não pode ir para o repositório público). O logo dos relatórios é escolhido nas Configurações do app, uma vez.

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

- **Celular, no aparelho:** foto girada pelo sensor com a rotação automática desligada (direção do giro); foto no toque e "Segure firme…" com flash (o tempo do Android varia por aparelho), arrastar fotos, vibração e copiar diagnóstico só dá para conferir no Android real.
- **App do celular num aparelho de verdade:** a lógica e as telas foram testadas em Linux (navegador simulando o celular). Seletor de arquivos do Android, permissão guardada da planilha no OneDrive/SharePoint, câmera real (flash e lanterna variam por aparelho), salvar na pasta escolhida, galeria e compartilhar só existem no Android: entram no roteiro de conferência do celular.

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
