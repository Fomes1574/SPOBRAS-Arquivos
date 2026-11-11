# Projeto: Arquivos SPOBRAS

Documento consolidado de requisitos, decisões, interface, arquitetura e critérios de aceitação.

**Versão:** 2.3 — revisão em 11/11/2026 com data da Ordem de Serviço, consultas reutilizáveis e melhorias de desempenho e automação.

**Situação:** especificação de desenvolvimento. A validação do aplicativo, do protótipo visual e da planilha VBA no Windows do trabalho continua pendente.

Este documento substitui a especificação anterior. Reúne os requisitos e a descrição textual da interface. O logo, os arquivos de exemplo e o protótipo navegável não estão incorporados como arquivos neste Markdown.

## 1. Objetivo e modo de trabalhar

Aplicativo Windows de uso individual para organizar, renomear e compactar arquivos, montar relatórios fotográficos, gerar cronogramas e preencher modelos da SPOBRAS — São Paulo Obras.

O objetivo é reduzir digitação, decisões repetidas e etapas manuais, preservando os modelos oficiais e a integridade dos dados. A automação prepara o trabalho; o usuário confere o resultado numa prévia e executa a ação com um clique. Dúvidas e conflitos recebem atenção específica, sem transformar cada arquivo em uma sequência de perguntas.

### 1.1 Princípios de simplicidade e automação

1. **Lembrar para não perguntar de novo:** última pasta por módulo, perfil, modelo, opções e valores reutilizáveis ficam salvos. Dados sensíveis ao contexto, como escola e contrato, não são reaplicados silenciosamente em outro trabalho.
2. **Preencher com segurança.** O usuário pode começar pela portaria, pelo contrato ou pela escola. Cada escolha restringe as demais opções aos vínculos confirmados na base. Uma correspondência única permite preencher os dados vinculados. Quando houver mais de uma, o usuário escolhe entre as opções compatíveis.
3. **Um fluxo principal por tarefa:** selecionar ou soltar arquivos → conferir a prévia e as pendências → executar. Opções avançadas ficam recolhidas.
4. **Uma confirmação suficiente:** o botão da ação na própria prévia confirma a operação. Diálogo adicional somente quando houver um conflito ou uma consequência que ainda não apareceu na prévia.
5. **Tratar conjuntos:** escolher destino para vários arquivos, aplicar legenda comum e gerar arquivos em lote, mantendo exceções editáveis por linha.
6. **Interrupção mínima:** o monitoramento agrupa novidades. Não abrir uma janela para cada download nem roubar o foco de outro programa.
7. **Preservar antes de acelerar:** resultados novos não sobrescrevem originais por padrão; a planilha mestra nunca é alterada pelo app.
8. **Continuar depois:** relatórios e preenchimentos trabalhosos têm rascunho; operações em lote têm histórico por arquivo e recuperação após falha.
9. **Sem complexidade desnecessária:** aplicativo desktop modular, processamento local e um banco local. Não há necessidade inicial de servidor, conta de usuário, serviços em nuvem ou modelo de IA.

### 1.2 Exemplos de uso desejado

- **Fotos de obra.** Soltar as fotos e começar pelo dado que estiver à mão, seja portaria, contrato ou escola. Escolher entre as opções compatíveis quando necessário, revisar ordem e legendas e clicar em Gerar. Pasta, layout e qualidade vêm do último perfil usado.
- **Renomeação recorrente:** soltar arquivos, selecionar um perfil salvo e conferir a lista antigo → novo. Um clique executa o lote.
- **Downloads:** o app reúne os arquivos concluídos, sugere destinos e deixa os casos ambíguos em Você decide. O usuário pode mover os sugeridos em um único lote.
- **Cronograma:** colar as obras do Excel, completar os dados disponíveis pela base, revisar inconsistências e gerar uma cópia calculada do modelo oficial.

## 2. Ambiente, fontes e restrições

| Item | Situação e regra |
| --- | --- |
| Sistema | Windows; versão e arquitetura exatas serão registradas na preparação. |
| Office | Excel instalado. Registrar versão, canal de atualização e arquitetura de 32/64 bits; não presumir compatibilidade apenas por ser o pacote mais atual. |
| Instalação | Bloqueada por padrão; a TI vai liberar o app. Validar instalação, execução e automação do Excel no ambiente real. |
| Python | Não instalado e não exigido no PC do trabalho. |
| SharePoint | Biblioteca sincronizada pelo OneDrive. O usuário trabalha em Downloads e escolhe o que enviar à pasta compartilhada. |
| WhatsApp Web | O app trata somente arquivos já baixados, sem controlar o WhatsApp. |
| Arquivos | Muitas planilhas Excel, PDFs ocasionais e conjuntos grandes de fotos. |
| Operação | Processamento local, sem envio de documentos a serviços externos pelo aplicativo. |

Caminho sincronizado informado pelo usuário: `C:\Users\<usuario>\Planilhas Relacionadas\rede.sp\SPOBRASPlanilhas relacionadas - Documentos`.

Esse caminho é uma configuração inicial deste computador, nunca uma constante do código. Pastas podem ser trocadas sem reinstalar o app.

**Limite da operação offline:** renomear, compactar e processar arquivos já presentes no disco funciona sem internet. Arquivos disponíveis somente na nuvem precisam estar disponíveis localmente. O OneDrive continua podendo sincronizar os arquivos da pasta compartilhada; essa sincronização é externa ao app. A atualização das fontes do Power Query pertence ao Excel e pode depender de rede.

### 2.1 Arquivos de referência

O documento original registra que estes exemplos foram analisados na conversa com o Claude: Cronograma revisado (abas Joaninha, Palimercio, Parametros, Dotacoes), Relatório CEU Jardim Paulistano, Cronograma Lote 146, Obras em licitação, Escolas_Contratos_Com_Relatorio, Restos a Pagar 2025 (PDF), Anexo I Tabela SME 2022 (PDF) e logo SPOBRAS.

Esses arquivos precisam estar disponíveis para implementar e validar seus respectivos módulos. A revisão deste Markdown não comprova o conteúdo nem o funcionamento dos exemplos.

## 3. Regras gerais de operação

### 3.1 Campos opcionais e validação

Campos descritivos continuam opcionais. Campo vazio não imprime rótulo sem valor, espaço desnecessário ou texto fictício. O layout se ajusta à ausência do campo.

As condições mínimas para executar uma operação continuam sendo verificadas: deve haver entrada válida, destino utilizável e parâmetros coerentes. Exemplos: um relatório precisa de fotos; uma renomeação precisa produzir nomes válidos; uma data informada precisa existir. Um cronograma incompleto pode ser salvo como rascunho, mas não deve ser apresentado como cronograma validado quando não for possível calcular seus valores.

As mensagens distinguem **impede gerar**, **precisa conferir** e **informação**. Só o primeiro bloqueia a ação. A interface aponta o campo ou arquivo e oferece uma correção direta.

### 3.2 Seleção, nomes e destinos

- Arrastar e soltar nas áreas de entrada pertinentes; seleção de arquivos e pastas por botões equivalentes.
- Local de leitura e gravação livre: Downloads, pasta sincronizada, pen drive ou outra pasta autorizada pelo Windows.
- Lembrar o último destino de cada módulo e permitir perfis específicos. Mostrar sempre o destino efetivo antes da ação.
- Não misturar entradas de um trabalho anterior com um novo lote sem indicação visível.
- Nomes são validados para caracteres proibidos, nomes reservados, extensão, tamanho e limites do destino. A prévia evidencia qualquer ajuste sugerido.
- Resolver colisões dentro do próprio lote e com arquivos já existentes; nunca sobrescrever silenciosamente. Sufixos como (2), (3) são sugeridos na prévia.
- Datas de exibição em português brasileiro; nomes com data em formato ordenável, por exemplo `2026-11-10`, quando escolhido no perfil.
- Quantias monetárias usam precisão decimal e regra de arredondamento explícita; vírgula decimal, ponto de milhar e datas do Excel são tratados conforme o contexto da importação.

### 3.3 Originais, prévia e resultado

Gerar relatório, converter, reduzir, corrigir ou preencher produz uma cópia por padrão. Redução de fotos não modifica as fotografias de origem. Atualizar um arquivo existente é uma opção explícita, com cópia de segurança recuperável antes da mudança.

Mover significa transferir o arquivo para o destino escolhido; a origem deixa de conter esse arquivo após a conclusão. Isso não deve ser descrito como manter duas cópias. Em transferência entre volumes, copiar, verificar e só depois remover a entrada de origem; falha ou cancelamento não pode deixar o usuário sem uma cópia íntegra.

O app não oferece exclusão permanente de documentos nem remove duplicados automaticamente. A planilha mestra, os modelos ativos e os próprios arquivos internos do app ficam excluídos do organizador, inclusive quando houver sobreposição de pastas.

Prévia mostra efeito real: nomes, destinos, quantidade, arquivos ignorados, conflitos e, em documentos, paginação e campos preenchidos. Não prometer prévia integral de conteúdo para operações que mostram apenas uma lista de arquivos.

### 3.4 Histórico, recuperação e Desfazer

Cada lote e cada arquivo têm identificador e estado: pendente, em execução, concluído, ignorado, falhou ou cancelado. Registrar origem/destino, nome anterior/novo, horário e informações necessárias para verificar o arquivo antes de uma reversão.

- Renomeação em lote admite trocas como A → B e B → A por nomes temporários controlados, com recuperação se houver interrupção.
- Mover e renomear podem ser desfeitos quando as condições ainda forem seguras. Verificar se o arquivo mudou ou se o caminho anterior está ocupado; nesses casos mostrar o conflito e preservar o conteúdo atual.
- Gerar um documento não implica poder apagá-lo automaticamente para desfazer. Nesse caso, oferecer Abrir resultado e acesso à versão anterior, quando existir.
- Atualização de planilha pode restaurar a cópia anterior somente após verificar se houve edições posteriores e explicitar a substituição.
- Não apresentar um botão Desfazer funcional para uma ação sem reversão segura. A disponibilidade e o prazo de retenção das cópias devem ficar claros.
- Gravar o histórico por arquivo durante o lote. Após fechar inesperadamente, reconciliar o registro com o disco e oferecer continuar os pendentes ou revisar o que foi concluído; não repetir cegamente o lote inteiro.
- Usar arquivos temporários e publicar o nome final após conclusão e verificação. No cancelamento, preservar as saídas concluídas e informar seu número; remover apenas temporários reconhecidos como pertencentes à operação.
- Não prometer transação única envolvendo disco e SQLite: reconciliar falhas entre a ação no arquivo e a atualização do histórico.

### 3.5 Desempenho e acessibilidade

Trabalho pesado fora da interface, com fila de tarefas e progresso por etapa/arquivo. Operações simples com arquivos continuam disponíveis durante leitura de base ou geração de documentos, respeitando conflitos de uso do mesmo arquivo.

Não carregar todas as fotos em resolução máxima simultaneamente. Usar miniaturas, leitura sob demanda, processamento com concorrência limitada e descarte correto de recursos. Listas grandes usam virtualização. Fazer busca e releitura somente nos arquivos alterados, quando possível.

O botão Cancelar interrompe no próximo ponto seguro. A tela deve indicar quando aguarda uma chamada do Excel; não prometer cancelamento instantâneo de toda chamada externa. Uma falha de automação não pode encerrar o Excel em que o usuário está trabalhando.

Fonte legível, contraste alto, navegação por teclado, foco visível e suporte à escala do Windows. Status usa texto e ícone, além de cor. Testar no monitor e na escala reais do trabalho.

#### Ajustes de desempenho para a base e a automação

- **Abrir com os dados disponíveis.** Mostrar a base local da última importação válida enquanto as fontes são conferidas em segundo plano. Verificar quais arquivos mudaram e agrupar notificações repetidas do mesmo salvamento antes de iniciar uma leitura.
- **Ler apenas o necessário.** Importar as tabelas e colunas mapeadas em lotes, com leitura sequencial quando o formato permitir. Em fontes que exigem releitura completa da tabela, limitar o trabalho à fonte alterada. Atualizar o índice de busca de arquivos por itens modificados.
- **Filtrar na base local.** Indexar as chaves de vínculo e os filtros mais usados, incluindo data de início quando o volume justificar. Aplicar filtros, ordenação e paginação na consulta. Carregar detalhes e miniaturas quando forem abertos.
- **Responder à seleção mais recente.** Na busca por texto, aguardar uma pausa breve na digitação e descartar respostas de consultas anteriores que terminarem depois. Seleções de filtros disparam a atualização diretamente. Reaproveitar a consulta para a lista e para as opções compatíveis.
- **Limitar trabalho simultâneo.** Controlar concorrência por tipo de tarefa e concentrar operações do Excel na fila já prevista. Agrupar preenchimentos do mesmo lote quando puderem usar a mesma sessão isolada e o mesmo modelo. Priorizar a resposta da interface e evitar duplicar uma importação em andamento.
- **Recuperar sem refazer o lote.** Preparar e validar cada atualização antes de publicá-la. Repetir apenas as tarefas pendentes ou com falha. Registrar duração, tamanho e volume de registros para localizar gargalos com a planilha real.

## 4. Módulos e regras de automação

### 4.1 Organizador de arquivos

**Entrega inicial:** organização manual e sugestões por extensão, nome, pasta de origem e regras explícitas. **Após integração da base:** sugestões pelo conteúdo. **Evolução posterior:** lembrar escolhas confirmadas e propor regras reutilizáveis.

Funções preservadas:

1. Vigiar Downloads e outras pastas escolhidas, com botão Organizar agora e monitoramento opcional na bandeja do Windows.
2. Regras sem programação: parte do nome, extensão/tipo, chegada, destino por ano, mês, obra, lote ou tarefa.
3. Caixa de entrada para arquivos sem correspondência e casos ambíguos, com escolha de destino individual ou em lote.
4. Mostrar nome, conteúdo identificado, destino sugerido e motivo. Um botão move os sugeridos; Outra pasta permite corrigir.
5. Detectar duplicados iguais por conteúdo, após filtro por tamanho e comparação adequada. Nome parecido não comprova duplicidade. Apresentar os caminhos sem excluir nada.
6. Histórico e reversão conforme a seção 3.4.

**Modo automático significa vigiar, preparar e sugerir. Não mover sem confirmação.** Essa regra vale também para regras salvas: elas eliminam escolhas repetidas, mas a ação de mover continua confirmada na prévia. Não há modo de movimentação autônoma nesta versão.

#### Regras para reconhecer conteúdo

- Consultar apenas tipos suportados e limites definidos de leitura: nomes de tabelas/abas, cabeçalhos e regiões relevantes das planilhas; texto de PDFs quando disponível. Não abrir centenas de planilhas no Excel para classificá-las.
- Identificadores exatos e relações da base prevalecem sobre semelhança de nome. Se houver contratos ou escolas conflitantes, marcar Você decide.
- Mostrar a evidência encontrada e a origem da sugestão. Não exibir porcentagem de confiança sem método validado para calculá-la.
- Uma escolha isolada não vira regra geral. Oferecer Lembrar esta escolha com condição visível, ou sugerir uma regra após repetições confirmadas. Regras podem ser editadas e desativadas.
- Definir prioridade das regras, apresentar conflitos e impedir ciclos de movimentação entre pastas vigiadas. Ignorar eventos provocados pela própria operação quando apropriado.
- Arquivos desconhecidos, protegidos por senha ou sem texto continuam utilizáveis por nome/tipo; explicar por que a leitura de conteúdo não ocorreu.

#### Regras para vigiar pastas

- Esperar o término da gravação/download, ignorar extensões temporárias e arquivos temporários do Office, e repetir a tentativa em arquivos ocupados sem bloquear o app.
- Consolidar eventos repetidos numa única entrada e conferir pendências ao iniciar/retomar e após erro de monitoramento. Não depender exclusivamente de notificações do Windows.
- Registrar data de chegada observada pelo app; não confundir com a data de modificação do documento. Arquivos já existentes têm chegada desconhecida até serem catalogados.
- Não percorrer pontos de redirecionamento ou pastas recursivas sem controle. Aplicar exclusões e evitar analisar o próprio destino repetidamente.
- Caminhos sincronizados são aceitos, mas arquivos apenas na nuvem ou em sincronização aguardam disponibilidade local; falha de acesso é uma pendência, não um arquivo perdido.

O usuário informou que os arquivos atuais da pasta não têm vínculos entre si. Para novos lotes Excel, se forem identificados vínculos externos, avisar antes de mover/renomear; não presumir que a confirmação atual cobre todo arquivo futuro.

### 4.2 Renomear em lote

Três modos com seleção explícita e prévia editável:

1. **Nome + número:** Relatório 01, Relatório 02 etc.; início e separador configuráveis; zero à esquerda opcional. A largura se ajusta ao maior número previsto no lote, sem alterar silenciosamente arquivos antigos.
2. **Nome base + complemento por arquivo:** por exemplo Muros de arrimo + lote digitado em cada linha, sem exigir sequência.
3. **Nomes livres:** editar ou colar uma lista de nomes, sem numeração.

Selecionar pasta ou vários arquivos, arrastar e soltar, ordenar por nome natural (2 antes de 10), data selecionada ou arraste manual. Distinguir datas disponíveis; não tratar data de cópia como data de captura de foto.

Continuar numeração considera somente nomes que correspondam ao padrão selecionado, mostra o próximo número e trata lacunas/colisões sem reutilização silenciosa. Preservar extensão por padrão.

Adicionar data, substituir trecho, remover espaços excedentes ou acentos são opções de perfil. A remoção de acentos não deve ser aplicada por padrão a documentos oficiais.

Uma tabela mostra nome atual, nome novo, situação e motivo de impedimento. Executar arquivos válidos permite excluir explicitamente os inválidos do lote. Desfazer obedece à seção 3.4.

### 4.3 Compactar e descompactar

1. Um ZIP por pasta, com o nome correspondente.
2. Vários arquivos em um ZIP, com nome sugerido.
3. Distribuir os arquivos em **ZIPs independentes**, cada um dentro do limite informado para envio. Essa é a interpretação padrão de dividir por tamanho; cada ZIP abre sozinho.
4. Descompactar vários ZIPs, cada um numa pasta própria, com tratamento de colisões.
5. Reduzir cópias das fotos antes da compactação, com prévia de qualidade e tamanho; originais preservados.

O limite se aplica ao tamanho final do ZIP, incluindo sua estrutura. Se um único arquivo exceder o limite mesmo após uma redução escolhida, apontar esse arquivo e oferecer ajustar o limite ou a qualidade; não produzir uma parte acima do limite como se a operação tivesse sido cumprida.

Arquivo único dividido em volumes dependentes não faz parte da primeira entrega. Se necessário no futuro, será uma opção distinta, com explicação de como reunir os volumes e validação de compatibilidade.

Na extração, impedir caminhos que escapem da pasta de destino, sobrescrita silenciosa e consumo descontrolado por arquivos compactados muito grandes. Validar a integridade dos ZIPs gerados. Limites de e-mail/WhatsApp são escolhidos pelo usuário; não fixar no código uma cota presumida desses serviços.

### 4.4 Relatório fotográfico

Manter como referência o Relatório CEU Jardim Paulistano. O protótipo anterior em .xlsm serve como referência de layout e lógica; suas macros ainda não foram testadas no Excel do Windows.

#### Dados

Cabeçalho repetido: logo SPOBRAS, RELATÓRIO FOTOGRÁFICO, SÃO PAULO OBRAS - SPOBRAS, data e contratada, omitindo campos variáveis vazios.

Campos opcionais: contrato, escola/unidade educacional, contratada, lote, portaria, processo SEI, data do relatório, objeto/serviço e responsável/fiscal. **Não incluir medição nem Referência de medição.**

A seleção de uma obra com OS confirmada disponibiliza a **Data da OS (início da obra)** para preenchimento. Exibir essa data no documento quando o modelo tiver o campo correspondente. A data do relatório continua sendo um campo próprio.

Portaria, contrato e escola usam os filtros vinculados da seção 4.6. O formulário preenche os dados associados quando a seleção identifica um único registro válido. Se a mesma escola ou contrato tiver várias obras compatíveis, mostrar as opções para o usuário escolher. Edições manuais ficam no relatório em preparação.

#### Fotos e edição

- Selecionar uma pasta ou múltiplas fotos; trabalhar com 50, 100 ou mais sem carregar todas as imagens completas na memória.
- Ordenar por nome natural, data de captura disponível ou arraste. Foto sem data de captura fica identificada, sem inventar a data.
- Aplicar orientação EXIF de forma consistente. Ajustar horizontal, vertical e quadrada sem distorcer nem cortar.
- Usar redução de peso em cópias, com perfil de qualidade e amostra visual. Rotação manual disponível.
- Número automático Foto 01 — descrição, atualizado quando a ordem muda. Descrição e legenda editáveis por foto; permitir aplicação em lote quando fizer sentido.
- Detectar entradas repetidas e arquivos inválidos, sem removê-los silenciosamente da seleção.
- Definir formatos suportados na implementação; fotos de celular em formato não suportado recebem mensagem por arquivo. Não prometer suporte a todo codec do Windows sem teste.

#### Layout e paginação

Dois modos: **fotos com legenda curta** e **fotos com descrição do serviço**. Padrão de seis fotos por página; opções de uma, duas, quatro ou oito.

Prioridade do layout: legibilidade, preservação da proporção das fotos e ausência de cortes. A quantidade selecionada é a capacidade desejada por página. Descrição longa, foto ou assinatura que não couberem geram páginas adicionais; não reduzir a fonte abaixo de 11 pt para forçar o encaixe. Oito fotos só são acomodadas quando a área útil e os textos permitirem.

Campo de descrição cresce com paginação controlada. Manter foto e sua legenda juntas; quando um texto exceder o espaço previsto, aplicar uma regra de continuação indicada na prévia, sem texto oculto. Cabeçalho, rodapé e assinaturas nunca se sobrepõem.

Assinaturas opcionais da fiscalização SPOBRAS e da contratada; rodapé Página X de Y. Texto preto, fundo claro e fonte de pelo menos 11 pt no documento impresso. Considerar o público leitor de 60 a 70 anos.

#### Saídas, rascunho e fluxo

- Padrão: Excel .xlsx editável; opção PDF escolhida a cada geração. Nome sugerido Relatório Fotográfico - Escola - Data; componentes vazios são omitidos.
- PDF e Excel derivam do mesmo layout validado. Se for usado o Excel para paginar/exportar, a prévia deve refletir essa saída. Não usar uma prévia web meramente ilustrativa como garantia de impressão.
- A edição posterior no Excel pode mudar a paginação; o app não garante a fidelidade de um PDF antigo após essas edições.
- Rascunho automático preserva dados, referências às fotos, ordem, legendas e opções. Guardar miniaturas para navegação. Se uma foto original mudar de lugar ou desaparecer, mostrar quais faltam e permitir localizá-las; não gerar silenciosamente um relatório incompleto.
- Começar um novo relatório limpa o contexto do anterior. Perfil guarda layout/qualidade/pasta, sem transportar escola ou contrato sem indicação.
- Fluxo simples: **Dados → Fotos e legendas → Conferência e geração**, com navegação livre, salvamento automático e opção de voltar sem perder conteúdo.

### 4.5 Cronograma padrão

Usar o arquivo Cronograma revisado como modelo oficial, preservando suas fórmulas e sua estrutura:

- Uma aba por cronograma, com logo e título automático.
- Até 15 obras por aba, nas linhas 3 a 17: portaria, lote, dotação, nome da obra, breve escopo, valor para obras, remuneração, total, início e fim.
- Remuneração inicial de 10%, definida em Parametros; mudanças obedecem ao perfil/modelo e ficam visíveis.
- Distribuição mensal entre início e fim, linha de TOTAL e área Porcentagem mensal por obra.
- Resumo pedido com dotação completa buscada em Dotacoes e somas por dotação.
- Abas Parametros e Dotacoes; funções existentes, incluindo PROCX, ÚNICO e MÍNIMOSES.

Entrada por formulário, colagem do Excel ou importação, como Obras em licitação. Importação mostra correspondência das colunas, linhas ignoradas e conversões de valores/datas; mapeamentos podem ser salvos em perfil.

Quando a obra vinculada tiver uma **Data da OS confirmada**, preencher o campo **Início** do cronograma com essa data e indicar sua origem na prévia. Se o usuário informar outra data de início, mostrar a divergência antes de gerar e conservar o registro oficial na base. Se a OS estiver ausente, aceitar uma data planejada informada pelo usuário e identificá-la como **Início previsto** na interface. Essa data pode preencher o início do cenário planejado no modelo Excel, mas permanece distinta da OS na consulta e no cadastro da obra. Se nenhuma das duas datas estiver disponível, manter o campo em branco e sinalizar a pendência para o cálculo do cronograma.

Gerar arquivo novo ou aba nova em arquivo existente. Na segunda opção, trabalhar em cópia de preparação, validar referências entre abas e guardar versão anterior antes da substituição autorizada. Tratar nomes de abas repetidos e limites de nomes.

Acima de 15 obras, oferecer dividir explicitamente em novas abas/cronogramas; nunca descartar a 16ª obra ou ampliar linhas sem validar o modelo.

Conferir dotação inexistente/ambígua, datas inválidas, fim anterior ao início, valor ausente/inválido e erros calculados. Valores zero ou negativos seguem a regra validada do modelo, sem inventar uma regra financeira genérica. Rascunho pode conter pendências; geração validada exige entradas suficientes ao cálculo.

Distribuição, meses parciais, mudança de ano, remuneração e arredondamento seguem o modelo oficial, conferidos com casos de referência. Não reescrever a matemática do cronograma para facilitar o código.

O documento anterior registra modelo .xlsx sem VBA. Se um modelo futuro contiver VBA, usar .xlsm e preservar o projeto de macros; trocar a extensão não adiciona nem valida macros.

### 4.6 Base de contratos, escolas e dados financeiros

Uma base local central para consulta pelos módulos. **Uma base única não significa uma tabela única:** portarias, dotações, contratos, escolas/empreendimentos, vínculos contrato–obra e empenhos têm entidades e relações próprias, com visões combinadas na interface.

#### Dados editáveis e dados importados

- Portarias/dotações mantidas no app: consultar, incluir, editar e importar com conferência. Conservar histórico das alterações relevantes e origem dos valores.
- Dados da planilha mestra: somente leitura na base importada. Correção manual de um relatório não altera a fonte nem é sobrescrita em silêncio quando o usuário relê a base.
- Se forem necessários complementos locais aos dados importados, guardá-los separadamente, identificados como locais, com regra explícita de precedência. Não misturar duas origens sem indicação.
- Exportação para Excel em tabelas próprias para consultas por PROCX/SOMASES, com chaves consistentes.

#### Identificadores e padronização

- Portaria: órgão(s), número, ano e nome por extenso. Exemplo de exibição: PORTARIA CONJUNTA SME/SIURB Nº 03 (2021). Número sem ano não é identificação suficiente quando houver ambiguidade.
- Dotação: código curto, descrição, dotação completa de obra e remuneração e contexto aplicável, incluindo exercício quando a fonte exigir. Não presumir que 3362 sozinho resolve todas as combinações.
- Contratos e empreendimentos usam identificadores oficiais quando disponíveis; não unir registros apenas porque nomes se parecem. Um contrato pode ter várias unidades/obras e vários registros de medição/pagamento.
- Empenhos e relações usam a chave adequada da fonte, incluindo exercício quando necessário. Validar a cardinalidade antes de somar para não duplicar valores ao cruzar tabelas.
- Preservar códigos como texto, incluindo zeros iniciais. Guardar valor original, valor padronizado e origem; não converter códigos financeiros em números de ponto flutuante.
- Variantes conhecidas podem ser associadas a registros canônicos. Casos incompletos ou ambíguos pedem conferência; não inferir automaticamente que uma dotação abreviada determina toda a dotação completa.

Problemas já registrados nos exemplos: portarias escritas como Nº 03, Nº 03 (2021), 3 e 3 (2025); dotação com espaço no lugar do ponto em …4490.5100 00; formato abreviado 3.362.44905100 no Restos a Pagar. A conferência deve mostrar original → proposta e só corrigir uma cópia ou atualização explicitamente autorizada.

#### Filtros vinculados entre portaria, contrato e escola

O usuário pode começar por qualquer um dos três campos. Cada seleção atualiza imediatamente a lista de resultados e as opções dos outros campos, sem precisar clicar em Buscar.

| Primeira seleção | Opções apresentadas |
| --- | --- |
| Portaria nº 3 | Apenas as escolas e os contratos com vínculo confirmado à Portaria nº 3 selecionada. Se houver mais de uma Portaria nº 3, mostrar ano e órgão para distinguir as opções. |
| Contrato | Apenas as escolas e as portarias vinculadas a esse contrato. |
| Escola | Apenas os contratos e as portarias vinculados a essa escola. |

- **Filtros combinados.** Aplicar todos os filtros ativos ao mesmo vínculo válido. Se um contrato atender várias escolas e portarias, a seleção de uma portaria dentro desse contrato deve mostrar somente as escolas vinculadas àquela combinação. A mesma combinação de filtros produz os mesmos resultados em qualquer ordem de seleção.
- **Alterar e limpar.** Ao abrir um campo para trocar seu valor, mostrar as opções compatíveis com os demais filtros ativos. Limpar um campo amplia as opções conforme os filtros que restaram. Oferecer Limpar filtros para começar outra busca. Se uma seleção ficar incompatível após a atualização da base, identificar o campo e permitir sua correção.
- **Vínculo comprovado.** Ler a associação diretamente da planilha ou obtê-la por chaves confirmadas entre tabelas. Uma portaria associada a um contrato pode restringir suas escolas na granularidade comprovada pela fonte. Se o contrato tiver várias portarias e a fonte não indicar quais escolas pertencem a cada uma, mostrar que esse vínculo ainda precisa ser confirmado. Uma lista isolada de portarias, a coincidência de nomes ou um credor em comum não comprovam o vínculo.
- **Opções sem duplicatas.** Exibir cada escola, contrato e portaria uma vez na respectiva lista, mesmo que o vínculo apareça em várias medições ou empenhos. Usar identificadores da base para distinguir homônimos.
- **Desempenho.** Consultar os vínculos já importados e indexados na base local. Atualizar as listas ao mudar a seleção, sem reler a planilha ou recalcular suas fórmulas a cada interação.

Usar a mesma consulta na tela Base de dados e nos campos de seleção do Relatório fotográfico e dos Modelos Excel que usam esses dados. No Cronograma, aplicar a regra quando a linha for vinculada a uma obra da base. A consulta permite começar com qualquer vínculo já disponível e sinaliza quando uma relação necessária ainda não está na fonte.

#### Filtros de consulta e verificação financeira

Na tela Base de dados, o usuário pode começar por qualquer campo disponível, inclusive um valor financeiro, e combinar os filtros. As opções dos outros campos e os resultados acompanham a seleção na ordem em que ela for feita. A busca usa os dados já importados.

| Campos | Como filtrar |
| --- | --- |
| Contrato, contrato de receita, portaria, dotação e escola | Buscar pelo código ou nome aplicável e selecionar entre as opções compatíveis. Contrato e contrato de receita têm campos próprios e podem ser usados juntos quando a fonte comprovar o vínculo. |
| Valor de contrato, valor de medição, valor pago e saldo a pagar | Buscar valor exato, mínimo, máximo ou faixa, com limites inclusivos. Aceitar valores em reais no formato brasileiro e comparar com precisão decimal. |
| Data da OS (início da obra) | Buscar por dia ou período, com limites inclusivos. Oferecer **Sem data da OS** para verificar pendências e permitir ordenar as obras pela data de início. |
| Outros campos da base | Usar o tipo do dado. Texto por termo, códigos por identificação, datas por intervalo e categorias por seleção. Incluir novos campos conforme as fontes e os mapeamentos forem validados. |

- Mostrar os filtros ativos juntos. Os campos mais usados ficam à mão e os demais em **Mais filtros**. Exibir na lista os campos filtrados ou permitir adicioná-los como colunas para conferir o resultado.
- Por exemplo, selecionar a Portaria nº 3, informar saldo a pagar a partir de R$ 1.000 e escolher uma escola ou um contrato de receita entre os resultados compatíveis. O mesmo conjunto de filtros deve retornar as mesmas associações em qualquer ordem.
- Identificar o nível de cada valor conforme a fonte, como total do contrato, valor de uma medição ou pagamento individual. Quando uma escola estiver ligada ao contrato, indicar que um total do contrato pertence ao contrato. Só apresentar um valor como sendo da escola se a fonte comprovar essa atribuição.
- Manter os conceitos de contratado, medido, pago, liquidado e saldo a pagar conforme a definição de cada fonte. Usar o saldo informado pela fonte quando disponível. Se a fonte exigir saldo calculado, validar a regra e o nível de cálculo antes de disponibilizar o filtro.
- Distinguir valor ausente de valor zero. Oferecer **Sem informação** para consultar ausências. Ao cruzar várias medições, pagamentos ou empenhos, preservar a identidade de cada registro para evitar linhas ou valores duplicados.

#### Ordem de Serviço e data de início da obra

- **Data da OS significa a data da Ordem de Serviço usada como início da obra.** Registrar a data no vínculo da obra ou da unidade de execução indicado pela fonte. Um contrato com várias escolas pode ter inícios diferentes. Usar uma data em todo o contrato apenas quando o documento de origem confirmar que ela vale para todo aquele conjunto.
- No cadastro de cada planilha, mapear a coluna de data e a chave da obra vinculada. Cabeçalhos como **Data OS**, **Data da Ordem de Serviço** e **Data Início** podem ser sugeridos. Confirmar o significado da coluna no primeiro mapeamento, pois uma data de início também pode ser prevista.
- Importar a data preservando o dia informado, registrar arquivo, tabela e identificador de origem e exibir em `dd/MM/aaaa`. Distinguir data inválida, data ausente e data confirmada. Data de assinatura do contrato, data da primeira medição e data prevista continuam disponíveis em campos próprios quando constarem na fonte.
- Se houver várias OS para a mesma obra, usar a relação ou regra de vigência comprovada pela fonte. Se a OS aplicável estiver indefinida, listar as candidatas com suas origens e pedir a seleção na operação que depende da data.
- Combinar a Data da OS com contrato, contrato de receita, escola, portaria, dotação e valores. Exemplo: filtrar obras da Portaria nº 3 iniciadas em 2026 com saldo a pagar a partir de R$ 1.000. Aplicar a data ao mesmo vínculo de obra usado pelos filtros.
- A atualização da fonte deve atualizar a consulta e sugerir a data atual em novos preenchimentos. Se houver um rascunho em aberto que usava a data anterior, indicar a diferença e permitir atualizar o dado vinculado. Arquivos já gerados mantêm a referência que foi usada na geração.

#### Planilha mestra protegida

Fonte: `Escolas_Contratos_Com_Relatorio.xlsx`. O app **nunca escreve, renomeia, move nem exclui** essa fonte. Ela atualiza suas próprias tabelas por Power Query no Excel.

Leitura por nome de tabela, sem depender do nome ou visibilidade da aba:

| Fonte | Dados esperados |
| --- | --- |
| `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS` | Contratada, contrato, empreendimento, datas, valores, dotação, lote, processo SEI e status. |
| `escola_vwSGCOE` | Empreendimento, contrato, lote, dotação, descrição do serviço e processo SEI. |
| `contratos_VW_EMPENHOS_SOF` | Empenhos, credor e portaria. |
| Aba `CONTRATADO_LIQUIDADO` | Valores contratados e liquidados, quando necessários; por ser referência por aba, validar seu layout e avisar se mudar. |
| Aba oculta `PORTARIAS` | Possível fonte inicial, ainda a confirmar. |

A existência e os nomes exatos das colunas precisam ser conferidos nos arquivos reais. Tabela ausente, coluna renomeada, tipo inesperado ou falha de relação gera diagnóstico; não apagar a base válida nem substituir dados por vazios.

#### Atualização e disponibilidade

- Detectar alteração da planilha e reler após a gravação estabilizar; complementar com conferência ao iniciar/retomar e recuperação de eventos perdidos. Releitura em segundo plano, consolidando eventos repetidos.
- Ler o último estado salvo no disco. Edições ainda não salvas e resultados de consultas não carregados/salvos não pertencem a esse estado.
- A leitura pelo app não atualiza o Power Query. Não abrir/salvar a mestra automaticamente para forçar uma atualização.
- Mostrar separadamente **última leitura pelo app**, **última modificação do arquivo** e **última atualização da fonte**, esta última somente se houver informação confiável disponível. Quando não houver, mostrar Não informada.
- Manter uma cópia lógica local da última importação válida. Importar/validar em preparação e publicar a nova versão completa em transação; uma falha não deixa metade dos contratos atualizados.
- Planilha ocupada, indisponível ou sincronizando: usar o último estado válido e indicar que não se trata de leitura atual. Permitir Reler agora, sem insistir em loop.
- Rastrear caminho e, quando aplicável, identidade do arquivo no Windows como auxílio. Mudança de volume, sincronização ou substituição pode impedir esse rastreamento. Não garantir localização infalível.
- Se perder a fonte, procurar candidatos somente em locais pertinentes/autorizados ou oferecer Selecionar planilha. Validar tabelas, colunas e correspondência da fonte antes de trocar; nunca substituir silenciosamente por um arquivo parecido.

O Claude já recebeu uma das planilhas base, segundo o usuário. Começar pela identificação das colunas e dos vínculos que essa planilha comprova. A busca pode usar esses vínculos assim que forem validados, enquanto as fontes complementares são integradas. Identificar quais vínculos entre portaria, contrato e escola e quais dotações ainda precisam de outra fonte. Os módulos iniciais de arquivos podem avançar em paralelo.

#### Planilhas complementares e atualização automática

- Cadastrar cada planilha complementar uma vez em **Fontes de dados**. Identificar arquivo, tabela, colunas, tipos e chaves de vínculo. O app guarda esse mapeamento para reaproveitar nas próximas atualizações.
- Quando uma fonte cadastrada for atualizada e salva no disco ou terminar de sincronizar, o app deve ler os dados em segundo plano e refletir as mudanças nas consultas e nos filtros. Conferir alterações também ao iniciar ou retomar o app. O usuário acompanha o resultado sem precisar importar cada versão.
- Se a planilha se atualiza por Power Query ou outra automação, o app lê o resultado salvo por essa rotina. Cada fonte mostra seu estado e a data da última leitura bem-sucedida, com a data da atualização dos dados quando a própria fonte informar.
- Novas linhas e alterações em campos já mapeados entram na atualização normal. Novas colunas aparecem entre os campos detectados. Aproveitar as que correspondam a um mapeamento validado e permitir configurar uma vez o tipo e o significado das demais, inclusive a disponibilização como filtro. A estrutura deve permitir adicionar campos comuns e fontes por configuração do app.
- Para cruzar fontes, usar chaves comprovadas. Quando duas fontes informarem o mesmo campo, definir a origem de referência no mapeamento e sinalizar divergências relevantes. Publicar uma importação validada de cada fonte de forma consistente. Em caso de falha, manter a última versão válida daquela fonte e indicar sua data, enquanto as outras fontes podem continuar atualizando.
- Manter a consulta aberta e reaplicar os filtros ativos após uma atualização válida. Se um valor selecionado deixar de existir ou perder o vínculo, mostrar o efeito na consulta e permitir ajustar ou limpar o filtro.

### 4.7 Modelos Excel e preenchimento automático

Copiar modelo .xlsx ou .xlsm e preencher somente entradas mapeadas. Modelos iniciais: Cronograma revisado e Relatório fotográfico; outros podem ser cadastrados. A planilha Pressão Medições.xlsm continua fora do escopo atual.

Funções: um arquivo por formulário; vários arquivos a partir de lista (uma linha por obra); atualização explícita de áreas de dados em arquivo existente; macro cadastrada após o preenchimento quando necessária.

Cada modelo registra versão, identificação, formato, campos, tipos e destinos permitidos. Preferir intervalos nomeados/tabelas; coordenadas de célula continuam suportadas para os modelos existentes, com verificação de estrutura. Bloquear escrita sobre fórmulas fora das áreas de entrada.

A Data da OS pode ser mapeada como campo próprio ou para o campo de início efetivo de um modelo. Conferir a origem na prévia e gravar a data no formato esperado pelo Excel e pelo modelo.

Mudança de versão ou estrutura exige revisar o mapeamento antes de gerar. Não depender somente do nome do arquivo. Preservar imagens, tabelas, nomes, impressão, validações e projeto VBA aplicáveis, com testes de compatibilidade por modelo.

Cronograma e relatório têm suas telas próprias para o uso diário. A tela Modelos Excel concentra cadastro/mapeamento e usos genéricos, sem obrigar o usuário a configurar campos toda vez.

Arquivos recebidos para classificação/leitura não executam macros nem atualizam vínculos externos. Macro de geração só é executada quando cadastrada num modelo autorizado e selecionada no fluxo/perfil. Respeitar as políticas do Office/TI; não instruir o app a liberar globalmente macros ou remover proteções.

### 4.8 PDF e extração de dados

Funções preservadas:

1. Juntar PDFs na ordem escolhida.
2. Separar por páginas ou intervalos, com validação de intervalos.
3. Transformar fotos em PDF.
4. Reduzir tamanho com perfil de qualidade, comparação e preservação do original.
5. Extrair tabelas conhecidas: Restos a Pagar (data, NE, ano, SEI, valor a liquidar, dotação) e Anexo I Tabela SME (unidade, DRE, orçamento).
6. Conversor geral de um ou vários PDFs para Excel, com prévia de tabelas e um arquivo Excel por PDF como padrão. Várias tabelas do mesmo PDF ficam em abas identificadas; perfil pode reunir PDFs em um arquivo, sem misturar tabelas silenciosamente.
7. OCR de PDFs digitalizados e fotos em etapa posterior, com processamento local e indicação de necessidade de conferência.
8. Logo e numeração em cópias de PDFs prontos, com prévia de posição e páginas atingidas.

Não prometer redução significativa para todo PDF: arquivos já otimizados ou com predominância de texto podem reduzir pouco. Se o resultado ficar maior, informar e manter a alternativa original. Compactação com perdas exige opção de qualidade visível.

Extração por texto e por OCR são caminhos diferentes. Não apresentar o resultado como fiel ao documento sem conferência. Preservar identificação do arquivo, página e texto/célula de origem quando viável. Conferir valores monetários, datas, linhas quebradas, subtotais repetidos e dotações ambíguas antes de padronizar.

PDF protegido por senha ou restrição recebe mensagem específica; não tentar contornar a proteção. Detectar assinaturas digitais quando suportado e avisar antes de alterar o documento, pois a modificação pode afetar sua validade. Aplicar logo somente a uma cópia, sem cobrir conteúdo existente e sem tratar o documento externo como originalmente emitido pela SPOBRAS.

### 4.9 Funções complementares

- **Excel para PDF em lote:** usar a impressão do Excel, recálculo quando aplicável, definição das abas e falhas por arquivo. Não atualizar conexões externas sem necessidade/opção explícita.
- **Juntar planilhas:** prévia de correspondência das colunas, tipos e cabeçalhos; identificação do arquivo/aba de origem; não recalcular ou agregar valores silenciosamente. Confirmar como lidar com fórmulas versus valores.
- **Busca dentro dos arquivos:** contrato, escola ou outro texto em planilhas/PDFs suportados; indexação local incremental, escopo de pastas definido e resultados com caminho/trecho. PDFs sem texto dependem da etapa de OCR.
- **Perfis salvos:** disponíveis desde os primeiros módulos, com nome, opções, pasta e regra/modelo aplicáveis. Reutilizar sem repetir escolhas; sempre mostrar o efeito atual antes de executar.
- **Duplicados, OCR e reconhecimento por escolhas anteriores:** implementados após a base funcional e os testes dos módulos principais.

Comparação entre duas versões de uma planilha continua fora do escopo.

### 4.10 Novas funções para consultas e tarefas recorrentes

| Função | Comportamento esperado |
| --- | --- |
| **Consultas salvas** | Salvar filtros, colunas e ordenação com um nome, como **Portaria nº 3 com saldo** ou **Obras sem Data da OS**. Ao reabrir, executar sobre os dados locais mais recentes. Mostrar a data de referência de períodos relativos, se o app oferecer esse tipo de filtro. |
| **Exportar consulta para Excel** | Gerar um arquivo novo com todos os resultados filtrados ou só as linhas selecionadas, conforme a escolha do usuário. Preservar datas e números como tipos do Excel. Incluir uma aba simples com os filtros aplicados, data da consulta e fontes utilizadas. Quando houver totais, respeitar o nível de cada valor. |
| **Ficha da obra** | Abrir uma escola/obra a partir de qualquer resultado. Exibir os dados vinculados, incluindo contrato, contrato de receita, portaria, dotação, Data da OS e valores disponíveis. Mostrar os registros de origem quando houver várias medições, pagamentos ou OS. |
| **Preencher a partir da consulta** | Abrir um cronograma, relatório fotográfico ou modelo com a obra ou seleção já vinculada. Conferir os dados preenchidos e resolver pendências na prévia existente. Aplicar o limite e as regras do modelo escolhido. |
| **Pendências da base** | Reunir obras sem Data da OS, datas inválidas, vínculos ambíguos e fontes com falha de atualização. Abrir o registro ou a fonte correspondente e permitir salvar uma consulta para acompanhar cada pendência recorrente. |
| **Histórico de atualizações** | Registrar por fonte a última tentativa, resultado, duração e contagens de registros novos, alterados ou rejeitados quando a importação puder identificá-los. Permitir repetir apenas a leitura que falhou e acompanhar o estado na tela **Fontes de dados**. |

## 5. Preservação e cálculo de planilhas

Preservar a fórmula, recalcular seu resultado e reproduzir sua impressão são responsabilidades diferentes. A escolha de ferramenta depende da operação e do modelo, não apenas da existência de VBA.

| Operação | Caminho previsto |
| --- | --- |
| Ler tabelas/cabeçalhos .xlsx/.xlsm | Open XML, sem executar Excel/macros e sem escrever na origem. |
| Preencher entradas de modelo compatível | Cópia + edição restrita por Open XML ou Excel, conforme teste de fidelidade do modelo. |
| Recalcular fórmulas e validar resultados do modelo | Excel instalado quando o fluxo depender desses resultados; aguardar cálculo e conferir erros relevantes. |
| Executar macro cadastrada | Excel instalado, apenas no contexto autorizado do modelo. |
| PDF fiel à impressão da planilha | Exportação do Excel com áreas de impressão/paginação verificadas. |
| .xls, .xlsb ou arquivo com proteção incompatível | Fora da edição Open XML inicial; usar caminho Excel validado ou informar limitação e solicitar cópia compatível. |

Não assumir que valores armazenados em cache no arquivo representam o cálculo atual. Marcar recálculo para abertura futura não substitui calcular antes de validar totais ou exportar PDF.

Automação do Excel deve ficar isolada do Excel de trabalho do usuário, com uso serializado, recursos liberados, limite/diagnóstico para espera excessiva e encerramento apenas de instâncias comprovadamente criadas pelo app. Validar na preparação a integração e o isolamento possíveis no ambiente real. Avaliar processo auxiliar para impedir que uma chamada bloqueada congele toda a aplicação.

Nunca encerrar todos os processos EXCEL.EXE. Nunca alterar e deixar modificadas as configurações do Excel do usuário, como cálculo, alertas ou eventos. Não abrir documentos recebidos com execução automática de macros para descobrir seu conteúdo.

### 5.1 Verificação antes de publicar um resultado Excel

1. Identificar modelo/versão e criar cópia de preparação.
2. Validar entradas e preencher somente áreas permitidas.
3. Executar macro autorizada, se houver; recalcular quando necessário.
4. Conferir áreas críticas: erros, totais, remuneração, dotação, datas, referências e extensão adequada.
5. Conferir impressão/PDF quando solicitado; publicar resultado e registrar no histórico.

Por modelo, criar casos de referência com resultados esperados e verificar que fórmulas fora das entradas permanecem equivalentes, incluindo abas ocultas, nomes e vínculos necessários. Não exigir arquivos binariamente idênticos após o Excel salvar, pois metadados e caches podem mudar.

## 6. Tecnologia e arquitetura

**Escolha atualizada:** C# com **.NET 10 LTS e WPF**, sujeito à compatibilidade do Windows confirmado pela TI. .NET 8 foi retirado da escolha inicial por chegar ao fim do suporte em 10/11/2026. A versão LTS adotada deve receber atualizações pelo ciclo de manutenção do app.

Publicação autocontida para não exigir instalação manual de Python ou runtime .NET no PC. Isso não significa ausência de componentes internos: bibliotecas e, quando necessário, componentes nativos entram no pacote e são validados pela TI. Excel continua sendo dependência existente dos fluxos que o utilizam.

Instalador único, assinatura conforme disponibilidade/política da TI, atualização por instalador novo e preservação/migração dos dados do usuário. Registrar versão do app, do banco e dos modelos para diagnóstico. Não exigir internet para iniciar ou usar os módulos locais.

| Parte | Responsabilidade |
| --- | --- |
| Interface WPF | Fluxos curtos, prévias, estado e acessibilidade, sem executar trabalho pesado diretamente. |
| Serviços de tarefas | Filas, cancelamento, progresso, lotes e conflitos por arquivo. |
| Serviço de arquivos | Nomes, movimentação, ZIP, temporários, verificação e recuperação. |
| Serviço Excel/modelos | Leitura Open XML e integração isolada com o Excel, cálculo e exportação. |
| Serviço de relatórios/PDF/imagens | Layout, miniaturas, orientação, qualidade e extração. |
| Base e importadores | Entidades relacionadas, mapeamento de várias fontes, validação de esquema, atualização por versão de cada fonte e consultas com filtros combináveis. |
| Persistência SQLite | Configurações, perfis, rascunhos, regras, histórico e base local. |

Arquitetura modular dentro de um único aplicativo. Separar regras de negócio da interface para testar operações sem simular cliques. Processo auxiliar somente quando necessário para isolar automação instável, não criar serviços de rede.

Open XML para formatos compatíveis; ZIP nativo quando atender aos requisitos; biblioteca(s) de PDF/imagem escolhidas após prova com os exemplos reais. Verificar suporte a recursos, manutenção, tamanho do pacote e licenças antes de fixar uma dependência. Não assumir que uma biblioteca de PDF fará junção, renderização, compressão e extração com igual qualidade.

**OCR:** recurso posterior. Se adotada a API Windows.Media.Ocr, validar identidade de pacote exigida para desktop, empacotamento compatível e idiomas disponíveis no PC. Não considerar OCR resolvido apenas porque o Windows está instalado. O instalador e essa decisão precisam ser compatíveis desde a preparação.

### 6.1 Dados locais e continuidade

- Banco SQLite e arquivos de trabalho internos no perfil local do usuário, fora de Downloads e de pastas sincronizadas pelo OneDrive. Os documentos gerados podem ir para qualquer destino escolhido.
- Cache, miniaturas, backups, modelos e documentos finais são categorias distintas, com caminhos e retenção próprios. Limpar cache não apaga documentos nem rascunhos necessários.
- Backup automático da base/configurações antes de migrações e alterações importantes; retenção limitada e configurável. Usar mecanismo consistente de backup do SQLite, não copiar um banco em escrita sem considerar seus arquivos auxiliares.
- Exportar/restaurar configurações, base editável e perfis para troca de computador. Confirmar caminhos e disponibilidade dos modelos/fotos após restauração.
- Atualização não apaga perfis nem fonte mestra. Se a migração falhar, preservar a versão anterior e apresentar recuperação.
- Logs locais com erro e contexto suficientes para diagnóstico; não registrar conteúdo integral de documentos por padrão. Usuário escolhe exportar um diagnóstico quando necessário.

## 7. Interface simplificada

### 7.1 Estrutura comum

Português brasileiro. Cores SPOBRAS: preto #231F20, branco/cinzas e vermelho #C8102E para ação principal e seleção. Azul #1F5FA8 para informação/sugestão; laranja escuro #8A4300 para atenção. Mensagens positivas e negativas sempre têm texto/ícone, sem depender só da cor.

Fonte Inter se incluída no pacote; alternativa do sistema caso necessário. Base equivalente a 15 unidades independentes de dispositivo no WPF, botões de aproximadamente 44, ajustados nos testes de escala/acessibilidade. Fonte do documento impresso é tratada em pontos, conforme o relatório.

Menu lateral: Início, Organizar arquivos, Renomear em lote, Compactar, Relatório fotográfico, Cronograma padrão, Modelos Excel, PDF e Base de dados. Histórico e Configurações têm acesso fixo. Em janela estreita, menu recolhível; não comprimir tabelas e campos até ficarem ilegíveis.

Uma ação principal clara por etapa. Barra inferior mostra Salvar em e nome do resultado nas telas que geram arquivo; escolher outra pasta é uma ação secundária. Não pedir destino novamente na execução se ele já foi mostrado e continua válido.

Opções essenciais aparecem primeiro. Avançadas, regras e mapeamentos ficam recolhidos. Perfis são uma seleção simples, com opção Salvar estas opções, sem exigir configuração prévia para tarefas básicas.

Fila de tarefas e resumo persistente mostram concluídos, pendentes e falhas, com Abrir arquivo, Abrir pasta, Tentar novamente ou Desfazer somente quando aplicável.

### 7.2 Telas

| Tela | Conteúdo e comportamento |
| --- | --- |
| Início | Oito módulos acessíveis, com destaque opcional para os mais usados; continuar rascunho, últimas ações e estado discreto de Downloads/planilha mestra. Não duplicar todos os ajustes de cada módulo na página inicial. |
| Organizar arquivos | Lista de novidades com destino/motivo e grupo Você decide; escolher destino em lote; botão Mover N sugeridos. Estado do monitoramento, pastas e regras em área secundária. Duplicados disponíveis quando implementados. |
| Renomear em lote | Entrada por arraste; três modos; campos essenciais; prévia antigo → novo editável; contagem de válidos/conflitos; perfil e ação Renomear N arquivos. |
| Compactar | Escolher um ZIP por pasta, ZIP único ou ZIPs por limite; entradas e resultados previstos; redução de cópias de fotos em opção secundária; Descompactar como ferramenta própria no módulo. |
| Relatório fotográfico | Etapas Dados, Fotos e legendas, Conferência e geração. Filtros vinculados por portaria, contrato e escola na etapa Dados. Miniaturas, edição em lote, rascunho automático e prévia real. Saída Excel por padrão, com opção PDF. |
| Cronograma padrão | Colar/importar/adicionar obras; breve escopo incluído; dados da base e inconsistências junto das linhas; remuneração e destino claros; prévia do resultado e tratamento acima de 15 obras. |
| Modelos Excel | Modelos disponíveis, preencher um/vários/atualizar; cadastro e mapeamento em área avançada; informar versão e necessidade de Excel/macro sem exigir conhecimento técnico para preencher. |
| PDF | Escolher ferramenta primeiro e mostrar apenas seu painel. Juntar, separar, fotos em PDF e reduzir nas primeiras entregas; extração, Excel para PDF e logo conforme suas fases. |
| Base de dados | Busca e filtros combináveis por contrato, contrato de receita, portaria, dotação, escola, Data da OS e valores. **Mais filtros** reúne os demais campos. Os filtros continuam ativos ao mudar de visão. Ações para salvar a consulta, exportar resultados, abrir a ficha da obra e consultar pendências. **Fontes de dados** mostra o estado e o histórico de cada planilha cadastrada. |
| Novidades de Downloads | Indicador/notificação agrupada; painel aberto por escolha do usuário. Contagem, sugestões e pendências; Abrir app e Mover N sugeridos. Sem janela automática fixa de 420 × 600 a cada chegada. |
| Histórico e tarefas | Filtrar lotes/arquivos, acompanhar fila, ver erros, continuar pendentes e acessar reversões disponíveis. Mostrar limite da reversão quando houver. |
| Configurações | Pastas vigiadas/excluídas, monitoramento, logo, fonte mestra, modelos, perfis, dados/backup e diagnóstico. Não espalhar as mesmas opções em telas diferentes. |

As telas de busca e juntar planilhas entram quando os recursos forem implementados, reutilizando seleção, prévia e resumo existentes. Perfis não precisam de um módulo inteiro para serem usados.

### 7.3 Detalhamento das telas complementares

- **Configurações.** Agrupar pastas e monitoramento, fontes de dados, modelos e aparência, perfis, backup e diagnóstico. Permitir testar o acesso a uma pasta ou fonte e reaproveitar o mapeamento de uma planilha com estrutura equivalente.
- **Busca dentro dos arquivos.** Abrir pelo Início ou pela ficha da obra. Usar o contrato ou a escola selecionada como termo inicial, deixando visíveis o termo e as pastas consultadas. Mostrar nome, caminho e trecho encontrado quando disponível, com ação para abrir o arquivo.
- **Juntar planilhas.** Escolher os arquivos, conferir a correspondência entre colunas e selecionar um perfil de mapeamento. A prévia indica as linhas de origem, colunas incompatíveis e registros ignorados. Salvar o resultado em uma planilha nova.
- **Perfis e consultas salvas.** Manter a seleção de perfil perto da ação correspondente. Na Base de dados, permitir editar o nome da consulta e escolher as colunas sem sair dos resultados.
- **Histórico e Desfazer.** Filtrar por operação, período, arquivo ou resultado. Abrir os detalhes de um lote para ver os itens concluídos, ignorados e com falha. Usar as condições da seção 3.4 para mostrar a reversão disponível e permitir continuar os itens pendentes.

## 8. Logo e identidade dos documentos

Logo SPOBRAS nos documentos institucionais novos produzidos pelo app: relatórios, cronogramas, planilhas de saída quando compatível com o modelo e respectivos PDFs. Renomear, mover ou compactar não insere logo no conteúdo dos arquivos. Extrair dados de documento externo não deve atribuir sua autoria à SPOBRAS.

Modelos oficiais com logo mantêm seu posicionamento; não duplicar. Logo novo no canto superior esquerdo do cabeçalho quando esse for o padrão do documento. Ao atualizar planilha existente, não inserir imagem sobre células ou impressão sem mapeamento/layout aprovado.

O arquivo conhecido tem 195 × 138 pixels. Uma ampliação para 780 × 552 pode ajudar em alguns usos, mas não recupera detalhes originais nem equivale a obter um arquivo de alta resolução. Preferir fonte oficial vetorial ou imagem maior, se disponível. A ausência dessa fonte melhor não bloqueia a primeira fase de arquivos; validar impressão com a imagem disponível antes dos relatórios.

Um cadastro central de logo, com proporção preservada. Trocar a configuração afeta novas gerações; não modificar documentos já existentes automaticamente. Modelos com logo embutido precisam de atualização controlada se a identidade mudar.

## 9. Fases e dependências

Cada fase termina com evidências de teste no Windows do trabalho e avaliação do usuário. A autorização atual permite atualizar o projeto; a especificação não deve declarar aprovação da TI ou testes ainda não realizados.

| Fase | Entrega e limite | Dependências/saída verificável |
| --- | --- | --- |
| 0 — Preparação e provas | Confirmar Windows/Excel/TI; validar instalador e integração isolada com Excel; prova de cópia/preenchimento/recálculo do cronograma e de uma página de relatório/PDF. Escolher caminho de empacotamento considerando OCR futuro. | Arquivos de referência disponíveis para as provas; resultados registrados. Base inicial e logo melhor não bloqueiam os módulos simples. |
| 1 — Primeira versão utilizável | Início, renomear, ZIP/extrair, organização manual e regras simples, monitoramento de novidades com confirmação, perfis, histórico e recuperação. | Sem depender da base de contratos. Testes de colisão, interrupção, cancelamento e arquivos em gravação. |
| 2 — Relatório e PDF básico | Relatório fotográfico, rascunho, fotos/legendas em lote, prévia/paginação Excel e PDF; juntar/separar/reduzir PDF e fotos em PDF. | Validação do layout e impressão real, com fotos variadas e descrições longas. Preenchimento manual permitido sem base. |
| 3 — Base e planilhas | Importação das fontes, atualização automática, filtros vinculados, financeiros e por Data da OS, consultas salvas, exportação, cronograma, modelos, preenchimento em lote e Excel para PDF. Depois da consulta básica, incluir ficha da obra, preenchimento a partir da consulta e pendências. | Provar a busca com a planilha disponível. Validar a origem da Data da OS por obra. Conferir filtros, atualização, fórmulas, totais e preservação nos fluxos de saída. |
| 4 — Automação por conteúdo e extras | Organizador integrado à base, regras sugeridas por escolhas confirmadas, duplicados, busca incremental, juntar planilhas, extrações PDF conhecidas/gerais, logo/numeração em PDF e OCR. | Implementar por recurso, após teste de precisão e desempenho. OCR tem prova própria; cada função informa seus limites. |

Não construir de uma só vez todos os módulos para depois verificar se o Excel funciona. As provas da fase 0 reduzem risco; os módulos de cada fase continuam sujeitos a testes completos na sua entrega.

### 9.1 Disponibilidade progressiva sem confusão

Mostrar apenas ferramentas utilizáveis como ações ativas. Recurso futuro pode aparecer numa área de planejamento discreta, mas não como botão principal que leva a uma tela vazia. Não mostrar controles de OCR, base ou aprendizado como se já estivessem operacionais.

## 10. Critérios de aceitação e testes

Os casos abaixo são **testes a executar**, não resultados já obtidos. Usar cópias de referência; nenhum teste deve danificar a mestra ou os documentos de trabalho. Registrar versão do app/modelo, dados usados, resultado esperado/obtido e evidência pertinente.

| ID | Caso | Resultado exigido |
| --- | --- | --- |
| T01 | Renomear 100 arquivos com ordem natural, número inicial e três modos. | Prévia e resultado correspondem; extensões preservadas e ordem correta. |
| T02 | Colisões com destino, nomes inválidos e troca A ↔ B. | Nenhuma sobrescrita; conflito corrigível e troca recuperável. |
| T03 | Desfazer após edição posterior ou caminho original ocupado. | Conteúdo atual preservado; conflito explicado antes da reversão. |
| T04 | Fechamento inesperado durante mover/renomear lote, inclusive entre volumes. | Reconciliar disco/histórico; identificar concluídos e pendentes; manter cópia íntegra. |
| T05 | Cancelar em lote e executar novamente apenas pendentes. | Não repetir concluídos nem deixar saída incompleta com nome final. |
| T06 | Downloads em gravação, eventos repetidos/perdidos e reinício. | Um registro por arquivo pronto; pendências recuperadas sem movimentação não confirmada. |
| T07 | ZIP por pasta, único, independente por limite e arquivo individual muito grande. | ZIPs íntegros; limite final respeitado ou impedimento identificado; originais preservados. |
| T08 | ZIP com caminho externo, colisão e grande expansão. | Extração contida no destino, sem sobrescrita silenciosa ou consumo sem limite. |
| T09 | Relatório de 100 fotos horizontais, verticais, quadradas e com EXIF. | Sem distorção/corte; ordem correta, memória controlada e interface responsiva. |
| T10 | Descrições longas, campos vazios, 1/2/4/6/8 fotos, assinaturas e várias páginas. | Fonte mínima 11 pt; texto visível; páginas extras quando necessário; prévia fiel à saída. |
| T11 | Rascunho reaberto após fechar; foto original ausente. | Dados/ordem/legendas restaurados; foto faltante indicada e localizável. |
| T12 | Cronograma com 1, 15 e 16 obras, troca de ano e meses parciais. | Limite tratado explicitamente; cálculos seguem o modelo e não descartam obras. |
| T13 | Cronograma com valores/datas inválidos, dotação ausente e arredondamento. | Impedimentos claros; totais/remuneração/dotações conferidos contra referências. |
| T14 | Preencher modelos .xlsx/.xlsm e gerar PDF. | Fórmulas e recursos necessários preservados; cálculo atualizado; VBA preservado e executado apenas quando autorizado; impressão correta. |
| T15 | Modelo alterado, mapeamento sobre fórmula e nova aba em arquivo existente. | Mudança detectada; escrita indevida bloqueada; referências corretas e versão anterior recuperável. |
| T16 | Mestra salva, aberta/ocupada, sincronizando, ausente ou com esquema alterado. | Fonte intacta; última base válida mantida; datas distintas e erro específico; importação sem estado parcial. |
| T17 | Contrato com várias escolas e portarias/dotações ambíguas. | Escolha necessária; nenhum preenchimento ou consolidação por suposição. |
| T18 | Cruzamento de tabelas com várias medições/empenhos por contrato. | Não duplicar contratos/valores por junções inadequadas; relações conferidas. |
| T19 | PDF textual/digitalizado, múltiplas tabelas e valores quebrados entre linhas. | Origem identificável; prévia conferível; OCR separado; sem prometer conversão perfeita. |
| T20 | Reduzir PDF já otimizado, protegido ou assinado. | Original preservado; ganho/limitação ou impacto explicitado; nenhuma alteração silenciosa. |
| T21 | Pasta sincronizada/pen drive indisponível, permissão negada ou disco cheio. | Falha recuperável com arquivos afetados e destino claros; sem perda de fonte. |
| T22 | Excel do usuário aberto enquanto o app gera e cancela trabalho. | Não fechar a sessão do usuário, alterar seus arquivos ou deixar configurações modificadas. |
| T23 | Atualização do app, migração do banco e restauração. | Perfis/base editável preservados; backup consistente e recuperação em caso de falha. |
| T24 | Interface no monitor real, teclado e escalas 100%, 125% e 150%, se disponíveis. | Campos/botões acessíveis, sem texto cortado ou dependência exclusiva de cor. |
| T25 | Reconhecimento por conteúdo com casos conflitantes e escolha isolada. | Ambiguidades ficam em Você decide; nenhuma regra geral ou movimentação surge silenciosamente. |
| T26 | Pacote instalado sem Python/runtime instalado separadamente e sem internet. | Funções locais executam; dependências de Excel/arquivo online são identificadas por operação. |
| T27 | Começar pela Portaria nº 3, por um contrato ou por uma escola. Repetir a mesma combinação em ordens diferentes e limpar filtros individualmente. | Cada lista apresenta somente as opções compatíveis. A ordem de seleção produz o mesmo conjunto final e limpar um filtro amplia as opções conforme os filtros restantes. |
| T28 | Usar um contrato associado a duas escolas com portarias distintas, duas Portarias nº 3 de anos diferentes e linhas repetidas de medição. Incluir um vínculo cuja granularidade ainda não foi confirmada. | A combinação portaria + contrato retorna apenas as escolas daquele vínculo. O seletor distingue as portarias por ano e órgão, apresenta opções únicas e identifica a associação sem confirmação. |
| T29 | Combinar contrato de receita, dotação, escola e faixa de saldo a pagar, alternando a ordem dos filtros. Incluir saldo zero, saldo ausente e várias medições para um contrato. | A ordem produz os mesmos resultados. Zero e ausência são distinguíveis. A consulta apresenta valores no nível correto, com os limites da faixa respeitados e sem duplicar totais. |
| T30 | Cadastrar uma planilha complementar, atualizar e salvar novos dados, incluir uma coluna e deixar outra fonte temporariamente indisponível. | A consulta incorpora os dados salvos automaticamente, reaplica os filtros, identifica a nova coluna e informa a data da última leitura válida da fonte indisponível. |
| T31 | Filtrar a Data da OS em um período combinado com portaria, escola, contrato e saldo. Incluir duas obras do mesmo contrato com datas diferentes, uma data na fronteira e uma obra sem OS. | Cada data pertence à obra correta. O período inclui seus limites. A opção **Sem data da OS** encontra os registros ausentes. |
| T32 | Gerar um cronograma com OS confirmada, outro com data planejada e um terceiro com OS conflitantes. Atualizar a data na fonte com um rascunho já aberto. | O início da primeira obra vem da OS. O início planejado fica identificado. O conflito pode ser resolvido na prévia. A atualização da fonte aparece na consulta e a diferença do rascunho é sinalizada. As fórmulas do modelo são preservadas. |
| T33 | Salvar uma consulta, alterar e salvar a fonte, reabrir a consulta e exportar com a tabela da tela paginada. | A consulta usa os dados atualizados. A exportação corresponde a todos os resultados filtrados ou à seleção escolhida, com datas e valores editáveis no Excel, filtros e fontes registrados. |
| T34 | Abrir ficha da obra e iniciar preenchimento a partir de uma consulta com vínculos confirmados e ambíguos. | Os dados confirmados chegam ao formulário. As ambiguidades ficam disponíveis para escolha na prévia. O documento usa o modelo e o vínculo selecionados. |
| T35 | Alterar filtros rapidamente durante uma atualização e repetir uma leitura com falha. | A interface continua responsiva, a consulta mostra a seleção mais recente e o histórico permite repetir somente a leitura afetada. |

### 10.1 Metas de desempenho

Medir no PC do trabalho antes de fixar SLA de duração. Conjuntos iniciais: lote de 100 arquivos para renomear, relatório com 100 fotos originais e mestra de dezenas de MB, registrando tamanho real, tempo, pico de memória e comportamento do cancelamento.

Metas iniciais de experiência: manter navegação e feedback responsivos; exibir início/estado da tarefa em até cerca de um segundo, salvo limitação medida; informar progresso por etapa sem porcentagem fictícia; não executar cálculo pesado a cada tecla. No cancelamento, reconhecer o pedido imediatamente na interface e concluir a parada no próximo ponto seguro.

O custo esperado deve acompanhar o tamanho do trabalho, sem reler toda a base a cada arquivo e sem decodificar todas as fotos para mudar uma legenda. Repetir medições após mudanças que afetem esse comportamento, sem criar testes que apenas repitam detalhes do código.

Para a base, medir separadamente abertura com dados locais, primeira importação, atualização de uma fonte, troca de filtros e exportação. Testar com uma cópia representativa da planilha e informar quantidade de linhas e de vínculos. Usar como referência inicial uma resposta de até 500 ms para filtros usuais sobre a base local no PC do trabalho, ajustando a meta depois da medição real. Em consultas demoradas, manter a seleção atual e mostrar o estado da busca.

## 11. Fora do escopo

- Controlar WhatsApp Web ou usar automação não oficial do serviço.
- Acessar o SharePoint diretamente pela internet; usar pastas locais já sincronizadas.
- Uso simultâneo por várias pessoas ou SQLite compartilhado numa pasta OneDrive.
- Padronizar indiscriminadamente todas as planilhas; atuar nos modelos e campos cadastrados.
- Comparar versões de planilhas.
- Alterar a planilha Pressão Medições.xlsm nesta etapa.
- Excluir documentos/duplicados automaticamente, movimentar arquivos sem confirmação ou executar macros recebidas para classificar conteúdo.
- Garantir localização infalível de arquivo movido, conversão perfeita de qualquer PDF ou Desfazer irrestrito após qualquer edição externa.

## 12. Pendências e decisões de implementação

### Confirmadas pelo documento anterior e mantidas

- Uso individual Windows, Excel instalado, TI fará a liberação.
- Trabalho diário em Downloads com destino livre; WhatsApp não será controlado.
- Nome Arquivos SPOBRAS, interface em português, modelo cronograma sem VBA no estado informado.
- Relatório sem medição, campos descritivos opcionais, seis fotos como padrão e Excel editável como saída padrão.
- Base mestra somente leitura; portarias/dotações precisam ser editáveis localmente.
- Funções extras continuam previstas, com execução por fases.

### Incorporadas nesta revisão

- .NET 10 LTS; perfis desde o início; automação de sugestões sem mover sem confirmação; notificações agrupadas.
- Base estruturada por entidades; preenchimento sem suposições em casos ambíguos; atualização de dados com origem/data claras.
- Preservação, cálculo e impressão tratados separadamente; cópia/validação antes de publicar arquivos.
- Histórico recuperável por arquivo, limites de Desfazer e tratamento de interrupções.
- Relatório com etapas, rascunho e paginação que prioriza legibilidade.
- ZIPs por limite independentes; limites explícitos de PDF/OCR/logo e testes de aceitação.

### Pendências reais

- [ ] Registrar versão/arquitetura do Windows e Excel e validar o instalador com a TI (fase 0).
- [ ] Disponibilizar os arquivos de referência e registrar o resultado das provas de Excel/layout (fase 0).
- [ ] Confirmar fonte oficial de logo em melhor qualidade, se disponível; testar impressão com a fonte disponível (até fase 2).
- [ ] Validar os vínculos da planilha já recebida pelo Claude. Identificar e complementar os vínculos de portaria e as dotações que a fonte disponível não cobrir (fase 3).
- [ ] Conferir as colunas disponíveis, os vínculos do contrato de receita e a definição dos valores financeiros em cada fonte. Cadastrar as planilhas complementares à medida que forem disponibilizadas (fase 3).
- [ ] Confirmar em qual tabela e coluna vem a Data da OS, a qual obra ela se aplica e como a fonte identifica a OS vigente quando houver mais de uma (fase 3).
- [ ] Conferir nomes de colunas, chaves e relações das tabelas da mestra; confirmar o uso da aba PORTARIAS e o layout de CONTRATADO_LIQUIDADO (fase 3).
- [ ] Fixar bibliotecas e política de empacotamento após provas e validação de licenças/compatibilidade (nas fases correspondentes).
- [ ] Executar e registrar os testes de cada entrega no Windows do trabalho; os critérios deste documento ainda não equivalem a testes concluídos.

Não solicitar novamente aprovação do texto como condição para concluir esta revisão: o usuário já autorizou incorporar as melhorias. Pendências acima são insumos e validações do desenvolvimento, não evidência de rejeição do planejamento.

## 13. Orientações para a IA de programação

1. Ler este documento por inteiro e tratar cada fase como entrega verificável. Não declarar um módulo concluído com telas fictícias ou funções simuladas.
2. Antes de alterar código existente, ler as instruções do repositório e preservar funcionalidades já validadas. Este Markdown não contém repositório ou implementação prontos.
3. Priorizar seleção, prévia, perfis e automação de dados; evitar configurações obrigatórias e confirmações redundantes. Nenhuma simplificação pode ocultar perda de dados, ambiguidade ou falha de cálculo.
4. Implementar serviços comuns de arquivos/tarefas/histórico uma vez e reutilizá-los. Não duplicar lógica de renomeação, destinos ou tratamento de colisões em cada módulo.
5. Resolver escolhas técnicas rotineiras com base nos testes e requisitos. Pedir informação ao usuário somente quando faltar dado do trabalho ou quando uma ambiguidade afetar o resultado; registrar a decisão no projeto.
6. Não usar dados inventados como substitutos silenciosos das tabelas oficiais. Dados de demonstração/teste devem ser identificados e não misturados à base de trabalho.
7. Entregar evidência adequada: testes de lógica e recuperação, arquivos gerados e validação no Excel/Windows quando aplicável. Se não houver acesso ao ambiente, registrar exatamente o que não foi verificado.
8. Ao entregar cada fase, informar o que funciona, como foi conferido e o que permanece pendente. Não prometer ausência de todo bug futuro.

## 14. Referências técnicas da revisão

Consultadas em 10/11/2026 para fundamentar as decisões; revalidar compatibilidade no início do desenvolvimento:

- Microsoft, [fim do suporte do .NET 8 e 9 e migração para .NET 10 LTS](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).
- Microsoft, [fórmulas e valores calculados armazenados no Open XML](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/working-with-formulas).
- Microsoft, [atualização de conexões e consultas no Excel](https://support.microsoft.com/en-au/excel/refresh-an-external-data-connection-in-excel).
- Microsoft, [FileSystemWatcher: eventos repetidos, limitações e buffer](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher).
- Microsoft, [Windows.Media.Ocr e identidade de pacote para desktop](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr).

## 15. Registro das revisões

A revisão 2.3 inclui a Data da OS como início efetivo da obra, com uso na base, no cronograma e nos modelos aplicáveis. Acrescenta consultas salvas, exportação filtrada, ficha da obra, pendências e histórico de atualização. Detalha as telas complementares e os ajustes de desempenho.

A revisão 2.2 amplia as consultas com contrato de receita, dotação, valores financeiros e outros campos disponíveis. Define o cadastro de planilhas complementares e a atualização automática da base e dos filtros após cada atualização salva.

A revisão 2.1 detalha os filtros entre portaria, contrato e escola em qualquer ordem de seleção e inclui critérios de aceitação para os cruzamentos. A primeira prova usa a planilha já disponível ao Claude.

A revisão 2.0 reorganizou as fases, antecipou perfis e recuperação e simplificou a geração de relatórios e a confirmação de arquivos. Também especificou recálculo, proteção da mestra, desempenho e critérios de aceitação.

Esta é a versão consolidada para continuar o projeto com o Claude. A próxima atividade de desenvolvimento é a fase 0, seguida da primeira entrega utilizável da fase 1.

## 16. Dicas para a implementação e o uso diário

1. **Começar pela planilha já disponível.** Fazer uma prova pequena com contrato, escola e portaria. Acrescentar Data da OS, dotação e valores à medida que a fonte comprovar os campos. Conferir alguns resultados lado a lado com a planilha antes de ligar o preenchimento automático.
2. **Usar identificadores estáveis nas planilhas complementares.** Preferir tabelas com cabeçalhos consistentes e chaves de obra, escola ou contrato verificáveis. Registrar em qual nível cada data e valor se aplica. Guardar os mapeamentos no app para reaproveitar nas atualizações.
3. **Automatizar a consulta mais repetida primeiro.** Salvar um filtro como **Obras da Portaria nº 3 sem Data da OS** e outro como **Contratos com saldo a pagar**. Depois, usar o mesmo resultado para abrir a ficha da obra, exportar ou iniciar um documento.
4. **Aproveitar os perfis de trabalho.** Criar um perfil por rotina frequente, como **Relatório fotográfico da obra** ou **Juntar planilhas de contratos**. Salvar pasta, modelo, layout ou mapeamento. Escolher a obra atual na hora de executar.
5. **Conferir a data antes de gerar cronogramas.** Usar na prova uma obra com OS, uma com início planejado e um contrato com duas escolas em datas distintas. Conferir o início no Excel, a origem na prévia e o recálculo com as fórmulas do modelo.
6. **Medir antes de aumentar a automação.** Registrar tempo de abrir, filtrar e atualizar com a planilha real. Corrigir primeiro o gargalo que aparece na rotina e só então ampliar o lote ou adicionar mais fontes e tarefas em paralelo.
