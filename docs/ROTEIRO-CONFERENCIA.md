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
- [ ] **Editar dados** numa obra (por exemplo, o lote): a lista mostra "campo editado no app" e a ficha mostra o valor da planilha, com "Voltar ao da planilha". Teste também **Incluir obra** e, em Fontes de dados, editar uma dotação.
- [ ] **Conferir uma planilha**: escolha uma planilha sua com portarias e dotações e veja as propostas. A correção sai numa cópia "(padronizada)".
- [ ] Escolha um **Lote**: aparecem os totais do lote acima da lista. Arraste a divisória entre a lista e a ficha, use **Ampliar** e **Recolher** os filtros.
- [ ] **Exportar para Excel**: abra a planilha gerada e veja se datas e valores estão como data e número, e se a aba "Sobre esta consulta" diz os filtros.
- [ ] Salve a planilha mestra na pasta da rede (pode ser sem mudar nada) e espere uns segundos: o app relê sozinho e a data da leitura muda.

### Relatório fotográfico

- [ ] Digite um número de contrato na etapa Dados (pode usar hífen no lugar da barra): o resto se completa. Se o contrato tiver várias escolas, o app pede para escolher (se você já digitou a escola e ela é uma delas, usa essa).
- [ ] Comece pela escola: se ela estiver em mais de um contrato, o app lista os contratos para escolher. Escola e contrato que não combinam na Base geram aviso, sem trocar nada.
- [ ] Na Base, escolha uma obra e clique **Usar no relatório**: os dados entram na etapa Dados.
- [ ] Adicione fotos de celular (em pé e deitadas), escreva legenda só em algumas, gere em Excel e em PDF. Foto sem legenda fica sem texto embaixo.
- [ ] Apague o Fiscal ou a Data da OS: a linha some do cabeçalho. Use **Atualizar dados** e veja o que voltou da base.
- [ ] Abra o Excel gerado: ele já abre mostrando as folhas A4. Fotos inteiras, todas do mesmo tamanho e sem encostar uma na outra; cabeçalho em grade em todas as páginas; "Página X de Y".
- [ ] O logo: em **Configurações → Logo dos documentos**, use **Tirar de um relatório…** com o relatório do Jardim Paulistano. Ele aparece no menu, no relatório e nas planilhas exportadas.

### Cronograma padrão

- [ ] Na Base, use **Usar no cronograma** em duas obras (ou cole linhas do Excel); preencha valor e fim. Embaixo de cada linha aparecem a remuneração, o total e a dotação completa achada na base.
- [ ] Digite o nome de uma escola com mais de um contrato: escolha o contrato certo na lista e veja lote, portaria e dotação entrarem. Inclua uma escola do mesmo lote pela lista de sugestões.
- [ ] Troque a **Distribuição** para **Curva S** e confira em **Como fica** (começa baixo, sobe até o meio, desce). Depois, em **Ajustes por mês**, escolha um mês na lista e coloque 5%: os outros meses seguem a curva.
- [ ] Digite 50000 no valor e saia do campo: vira R$ 50.000,00.
- [ ] Gere com **Gerar também o PDF** marcado. Abra o Excel: logo, título, meses como "out/26", total de cada ano, total solicitado e, embaixo, a tabela por dotação (obra e remuneração).
- [ ] Mude a **Remuneração** para outro percentual e gere de novo.

### Modelos Excel

- [ ] Cadastre uma planilha sua (pode ser uma ficha simples), com 2 ou 3 campos e a célula de cada um. Tente apontar uma célula com fórmula: o app recusa.
- [ ] Preencha duas linhas e gere: cada linha vira um arquivo.

### Organizar, renomear e compactar (na pasta de cópias)

- [ ] **Renomear**: 10 arquivos para "Relatório 01, 02…". Confira a prévia, renomeie e use **Desfazer**.
- [ ] **Organizar**: crie uma regra com destino `...\Obras\{contrato}` e copie para Downloads um arquivo com o número de um contrato da base no nome, trocando as barras por hífen (por exemplo, `Medição 123-SIURB-2024.pdf` para o contrato 123/SIURB/2024). A sugestão deve apontar a pasta do contrato e dizer onde achou o número.
- [ ] Deixe o **modo automático** ligado, feche a janela e baixe um arquivo: deve aparecer o aviso perto do relógio. Clique e veja a sugestão.
- [ ] **Compactar**: uma pasta com fotos em vários .zip com limite de tamanho. Marque **Reduzir as fotos** e veja a prévia mostrar o tamanho novo.

### PDF e Ferramentas

- [ ] **PDF**: juntar dois PDFs, separar páginas, reduzir um PDF com fotos, e **Logo e numeração** a partir da página 2.
- [ ] **PDF para Excel** com o PDF de Restos a Pagar e o do Anexo I: abra a planilha ao lado do PDF e compare (colunas, negrito, valores em R$, cabeçalho uma vez só). Teste também um **PDF escaneado** e uma **foto de documento**.
- [ ] **Ferramentas → Buscar nos arquivos**: procure um número de contrato na sua pasta de trabalho. A primeira busca demora mais; a segunda é rápida.
- [ ] **Arquivos repetidos** numa pasta com cópias. Use **Separar as cópias** e depois **Desfazer**.
- [ ] **Excel para PDF** com duas planilhas.

### Geral

- [ ] Telas sem texto cortado no seu monitor, inclusive com a janela menor (o menu se recolhe abaixo de certa largura).
- [ ] Com uma planilha sua aberta no Excel, gere um cronograma com PDF: o seu Excel não fecha nem muda.
- [ ] Datas: digite 10112026 num campo de data e veja virar 10/11/2026.
- [ ] Em **Configurações → Cópias de segurança**, clique **Fazer cópia agora**.

## 3. O que ainda depende de você

- Nada no momento.

## 4. Como devolver

Mande na conversa com o Claude:

- o que ficou marcado e o que não;
- prints dos problemas;
- o arquivo `erros.txt`, se existir: **Configurações → Abrir pasta de dados**. Ele tem só mensagens de erro, sem conteúdo dos seus documentos.

Não coloque prints, planilhas nem o `erros.txt` no repositório: ele é público.
