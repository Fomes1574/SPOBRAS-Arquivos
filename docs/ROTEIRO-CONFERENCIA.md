# Roteiro de conferência do Arquivos SPOBRAS

Este é o primeiro teste do app completo no PC do trabalho. Leva uns 40 minutos se fizer tudo. Dá para parar no meio e voltar depois: o app guarda rascunhos e histórico.

O app não apaga arquivos seus, não escreve na planilha mestra e não move nada sem você confirmar. Mesmo assim, nos testes de **Organizar** e **Renomear**, use uma pasta com **cópias** de alguns arquivos, e não a pasta de verdade.

## 1. Baixar e abrir

1. No GitHub, abra a aba **Actions**, entre na execução mais recente de **build** com o sinal verde e baixe o artefato **ArquivosSPOBRAS** (precisa estar logado).
2. Extraia o zip. Há um arquivo só, `ArquivosSPOBRAS.exe`. Não precisa instalar: coloque numa pasta sua (por exemplo, Documentos) e abra com dois cliques.
   - Se aparecer "O Windows protegeu o computador", clique em **Mais informações** e **Executar assim mesmo**, como no Teste de Ambiente.
3. Na primeira vez, vá em **Configurações** e confira a **pasta de trabalho** (Downloads) e a **pasta das planilhas base** (a pasta sincronizada da rede).

## 2. O que conferir

Marque o que funcionou. Onde algo parecer estranho, tire um print e anote o que fez antes.

### Base de dados (comece por aqui: os outros módulos usam a base)

- [ ] Em **Fontes de dados**, a planilha mestra e a de dotações aparecem como lidas, com a data da leitura.
- [ ] Em **Base de dados**, escolha a Portaria 3: as listas de contrato e escola passam a mostrar só o que combina. Limpe um filtro pelo ✕ e veja as listas voltarem.
- [ ] Clique numa obra: a ficha ao lado mostra contrato, contrato de receita, portaria, dotação, **Data da OS** (a coluna INÍCIO EXECUÇÃO) e as medições do contrato. Confira com a planilha.
- [ ] **Salvar consulta** com um nome, mude os filtros, depois escolha a consulta salva: os filtros voltam.
- [ ] **Exportar para Excel**: abra a planilha gerada e veja se datas e valores estão como data e número, e se a aba "Sobre esta consulta" diz os filtros.
- [ ] Salve a planilha mestra na pasta da rede (pode ser sem mudar nada) e espere uns segundos: o app relê sozinho e a data da leitura muda.

### Relatório fotográfico

- [ ] Na Base, escolha uma obra e clique **Usar no relatório**: os dados entram na etapa Dados.
- [ ] Adicione fotos de celular (em pé e deitadas), escreva algumas legendas, gere em Excel e em PDF.
- [ ] Abra o Excel gerado e **imprima uma página** (ou veja a visualização de impressão): fotos inteiras, sem corte, cabeçalho em todas as páginas, "Página X de Y".
- [ ] O logo: cadastre em **Configurações → Logo dos documentos** e gere de novo.

### Cronograma padrão

- [ ] Cadastre o modelo (**Cronograma_revisado.xlsx**). O app guarda uma cópia; o seu arquivo não muda.
- [ ] Na Base, use **Usar no cronograma** em duas obras; preencha valor e fim.
- [ ] Gere com **Gerar também o PDF** marcado. A mensagem deve dizer "gerado e conferido no Excel" com o total.
- [ ] Abra o arquivo gerado no Excel: fórmulas intactas, meses distribuídos, e em **Parametros** a célula do resumo dizendo **RESUMO OK**.
- [ ] Teste a opção "Começar a contar no dia 1º do mês de início" ligada e desligada com uma OS no meio do mês, e diga qual comportamento é o certo (ver perguntas abaixo).

### Modelos Excel

- [ ] Cadastre uma planilha sua (pode ser uma ficha simples), com 2 ou 3 campos e a célula de cada um. Tente apontar uma célula com fórmula: o app recusa.
- [ ] Preencha duas linhas e gere: cada linha vira um arquivo.

### Organizar, renomear e compactar (na pasta de cópias)

- [ ] **Renomear**: 10 arquivos para "Relatório 01, 02…". Confira a prévia, renomeie e use **Desfazer**.
- [ ] **Organizar**: crie uma regra com destino `...\Obras\{contrato}` e copie para Downloads um arquivo com o número de um contrato da base no nome, trocando as barras por hífen (por exemplo, `Medição 123-SIURB-2024.pdf` para o contrato 123/SIURB/2024). A sugestão deve apontar a pasta do contrato e dizer onde achou o número.
- [ ] Deixe o **modo automático** ligado, feche a janela e baixe um arquivo: deve aparecer o aviso perto do relógio. Clique e veja a sugestão.
- [ ] **Compactar**: uma pasta com fotos em vários .zip com limite de tamanho.

### PDF e Ferramentas

- [ ] **PDF**: juntar dois PDFs, separar páginas, reduzir um PDF com fotos, e **Logo e numeração** a partir da página 2.
- [ ] **PDF para Excel** com um PDF de Restos a Pagar ou do Anexo I: veja o que veio certo e o que precisa de ajuste.
- [ ] **Ferramentas → Buscar nos arquivos**: procure um número de contrato na sua pasta de trabalho. A primeira busca demora mais; a segunda é rápida.
- [ ] **Arquivos repetidos** numa pasta com cópias. Use **Separar as cópias** e depois **Desfazer**.
- [ ] **Excel para PDF** com duas planilhas.

### Geral

- [ ] Telas sem texto cortado no seu monitor, inclusive com a janela menor (o menu se recolhe abaixo de certa largura).
- [ ] Com uma planilha sua aberta no Excel, gere um cronograma: o seu Excel não fecha nem muda.
- [ ] Em **Configurações → Cópias de segurança**, clique **Fazer cópia agora**.

## 3. Perguntas que só você responde

Estão também em `docs/PROGRESSO.md`, em "Pendências com o usuário":

1. **Cronograma com início no meio do mês**: gravar o dia 1º (padrão de agora, o valor entra naquele mês) ou a data exata (o modelo joga o valor do mês para o mês seguinte)?
2. Algum modelo Excel precisa rodar **macro** depois de preenchido?
3. Você faz cronograma em **aba nova de um arquivo que já existe**, ou um arquivo novo por cronograma basta?
4. Precisa **editar portarias e dotações dentro do app**, ou a planilha de dotações continua sendo o lugar disso?
5. Há outra planilha que deveria entrar na base, além da mestra e das dotações?
6. Pode mandar um PDF de **Restos a Pagar** e um do **Anexo I Tabela SME** (com valores trocados, se preferir) para eu fazer a leitura com as colunas certas?

## 4. Como devolver

Mande na conversa com o Claude:

- o que ficou marcado e o que não;
- prints dos problemas;
- o arquivo `erros.txt`, se existir: **Configurações → Abrir pasta de dados**. Ele tem só mensagens de erro, sem conteúdo dos seus documentos.

Não coloque prints, planilhas nem o `erros.txt` no repositório: ele é público.
