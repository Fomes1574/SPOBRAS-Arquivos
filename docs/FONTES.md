# Fontes de dados: tabelas e colunas reais

Levantado em 11/11/2026 a partir dos arquivos enviados pelo usuário. Aqui ficam **só nomes** de abas, tabelas e colunas, nunca dados. Os arquivos não são versionados.

O que não estiver neste arquivo é pendência a perguntar (arquitetura, seção 11).

## Decisões do usuário sobre as fontes

- **Data da Ordem de Serviço:** coluna `INÍCIO EXECUÇÃO` da tabela `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS`.
- A planilha fica na pasta sincronizada da rede; quando ela for salva lá, o app relê sozinho (arquitetura, seção 5.1).

## Fonte A: `Escolas_Contratos_Com_Relatorio.xlsx`

Ler por nome de tabela. Linhas contadas no arquivo de 11/11/2026 (só para dar a ordem de grandeza).

### Tabelas nomeadas

#### `contratos_VW_EMPENHOS_SOF`

Aba `contratos VW_EMPENHOS_SOF`, cerca de 3087 linhas, 40 colunas:

`Nr.Contrato` · `Nr.Proposta` · `Nr.Empenho` · `Data do Empenho` · `Ano do Empenho` · `Processo SEI` · `Nr.Reserva` · `Vl.Total Empenhado` · `Vl.Empenhado Liquido` · `Vl. Liquidado` · `Vl.Anulado` · `Vl.Pago no Exercício` · `Vl.Pago Restos` · `Cnpj Credor` · `Credor` · `codCategoria` · `codElemento` · `codFonteRecurso` · `codFuncao` · `codGrupo` · `codItemDespesa` · `codModalidade` · `codOrgao` · `codPrograma` · `codProjetoAtividade` · `codSubElemento` · `codSubFuncao` · `codUnidade` · `idContrato` · `pago total` · `cancelados` · `empenho liquido` · `saldo real` · `anulados/cancelados` · `tem saldo` · `EMPREENDIMENTO` · `CONTRATANTE` · `PROGRAMA` · `SALDO FINAL` · `PORTARIA`

#### `preCadastro_VW_RELATORIO_MEDICOES`

Aba `preCadastro VW_RELATORIO_MEDICO` (oculta), cerca de 13893 linhas, 29 colunas:

`DATA DE PROTOCOLO` · `CONTRATANTE` · `TIPO DE CONTRATO` · `STATUS DO CONTRATO` · `NR. DO CONTRATO` · `OBJETO` · `CONTRATADA` · `PROCESSO SEI PGTO` · `NR. DA MEDIÇÃO` · `DATA DE INÍCIO` · `DATA FIM` · `VALOR PROTOCOLADO` · `VALOR LIBERADO` · `VALOR CONSIDERADO` · `VALOR PAGO` · `VALOR REAJUSTE PROTOCOLADO` · `VALOR REJUSTE` · `EMPREENDIMENTO` · `TIPO DE EMPREENDIMENTO` · `ATIVIDADE DOTAÇÃO` · `ATIVIDADE CONSIDERADA` · `DATA DE VENCIMENTO` · `DATA DE PAGAMENTO` · `STATUS DA MEDIÇÃO` · `CONTRATO RECEITA` · `portaria` · `Ano_Medicao` · `Coluna1` · `Recurso 26`

#### `RelSelecoes`

Aba `SELEÇÃO`, cerca de 30 linhas, 3 colunas:

`Empresa` · `Contrato` · `Obra`

#### `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS`

Aba `contratos VW_ESCOLAS_CONTRATOS_`, cerca de 1568 linhas, 50 colunas:

`CONTRATADA` · `CONTRATO` · `DATA DE ASSINATURA` · `INÍCIO EXECUÇÃO` · `TÉRMINO EXECUÇÃO` · `EMPREENDIMENTO` · `VALOR EMPREENDIMENTO` · `VALOR ADITADO` · `VALOR MEDIDO` · `VALOR PAGO` · `DATA LTIMA MEDIÇÃO` · `STATUS` · `AVANÇO FÍSICO` · `ATIVIDADE` · `DOTACAO` · `nmLote` · `MODALIDADE` · `TIPO` · `PROCESSO SEI` · `STATUS CONTRATO` · `SITUAÇÃO` · `VALOR TOTAL` · `DEVERIA` · `CONSIDERADO` · `EXECUTADO` · `TERMINO` · `DESCOMPASSO` · `Observação` · `DATA DO DOCUMENTO (OBSERVAÇÃO)` · `INICIO` · `Coluna1` · `Coluna2` · `FONTE DE RECURSOS` · `VALOR CONTRATADO COM ADITAMENTO` · `SALDO A MEDIR` · `SALDO A PAGAR` · `ESCOPO` · `ANEXOS` · `TIPO DE OBRA` · `0` · `ATIVIDADE 2` · `TIPO2` · `ATIVIDADE2` · `PORTARIA` · `anexo` · `A LIQUIDAR` · `ano termino` · `mês termino` · `Coluna4` · `CONC`

#### `escola_vwSGCOE`

Aba `escola vwSGCOE` (oculta), cerca de 2847 linhas, 33 colunas:

`Empreendimento` · `Fonte de Recursos` · `Anexo` · `Modalidade` · `Status` · `Valor Portaria` · `Valor Estimado` · `Valor Orçado` · `Valor Contratado` · `Valor Aditado` · `Atividade` · `Status Contrato` · `Andamento/Concluído` · `Contrato` · `Lote` · `Dotacao_Adao` · `Descrição do Serviço` · `Processo SEI` · `idEmpreendimento` · `idContrato` · `idModalidade` · `idStatus` · `Atividade_Adao` · `valor com aditivo` · `a considerar` · `considerado` · `STATUS2` · `ATIVIDADE2` · `ATIVIDADE3` · `saldo contrato` · `TIPO` · `termino` · `ano`

#### `Tabela13`

Aba `PORTARIAS` (oculta), cerca de 35 linhas, 6 colunas:

`PORTARIA` · `EMPENHO` · `DESCRIÇÃO` · `VALOR SEM GERENCIAMENTO` · `LICITAÇÃO` · `HOMOLOGADO`

#### `Tabela3`

Aba `PORTARIAS` (oculta), cerca de 375 linhas, 3 colunas:

`Coluna1` · `Coluna2` · `Coluna3`

#### `Tabela14`

Aba `PORTARIAS` (oculta), cerca de 265 linhas, 6 colunas:

`Coluna1` · `Coluna2` · `Coluna3` · `Coluna4` · `Coluna5` · `Coluna6`

#### `Tabela6`

Aba `STATUS` (oculta), cerca de 401 linhas, 4 colunas:

`CONTRATO` · `STATUS` · `OBSERVAÇÃO` · `DATA`

### Abas sem tabela nomeada

Resumos, relatórios e tabelas dinâmicas montados pelo próprio Excel. Não são fonte de importação enquanto não houver decisão em contrário.

- `PRESSÃO EM ANDAMENTO RESUMO`
- `PRESSÃO EM LICITAÇÃO`
- `Em licitação`
- `PRESSÃO A LICITAR`
- `A Licitar`
- `Empenhos`
- `Medição`
- `CONTRATADO_LIQUIDADO`
- `6 NOVOS CEUS`
- `REDONDINHOS`
- `CONSTRUÇÃO`
- `MANUTENÇÃO`
- `ESPECÍFICOS`
- `RECURSOS ESPECÍFICOS`
- `RELATÓRIO`
- `REPASSES` (oculta)
- `_RELATORIO` (oculta)

### Observações para o importador (passo 5)

- `INICIO` (sem acento) e `INÍCIO EXECUÇÃO` existem as duas em `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS`; a data da OS é `INÍCIO EXECUÇÃO`.
- Há colunas com nomes genéricos (`Coluna1`, `Coluna2`, `Coluna4`, `0`, `CONC`): não usar sem perguntar.
- A tabela `Tabela6` (aba `STATUS`) tem `CONTRATO`, `STATUS`, `OBSERVAÇÃO`, `DATA`; não tem coluna `OS`.
- A aba `PORTARIAS` tem três tabelas (`Tabela13`, `Tabela3`, `Tabela14`), duas delas com colunas genéricas: uso a confirmar.
- `CONTRATADO_LIQUIDADO` não tem tabela nomeada: cabeçalhos na linha 3 (colunas B a O) ao lado de tabelas dinâmicas; layout a confirmar antes de importar.
- O arquivo tem segmentações de dados e mais de 200 nomes definidos (`Rel…`), usados pelas abas de relatório.
- Granularidade (conferida na leitura real): uma linha por par (`CONTRATO`, `EMPREENDIMENTO`); um contrato pode ter várias escolas. Cada contrato tem uma só portaria e uma só data em `INÍCIO EXECUÇÃO`.
- O contrato de receita vem das medições, por contrato, e pode vir combinado ("015/SIURB/2025, 128/SIURB/2021"); o app mostra como está.

## Fonte B: `Dotações Completas.xlsx`

Sem tabelas nomeadas. Ler pela posição dos cabeçalhos (localizar o texto, não fixar a célula).

- Aba `Planilha2` (principal): cabeçalhos na linha 2, colunas B a F: `Portarias` · `Contratos de receita (Por onde vem o dinheiro para obra)` · `Dotações Completas` · `Dotação Resumida` · `Dotação Resumida + Descrição`. Dados até a linha 27.
- Aba `Planilha1` (conferência): cabeçalhos na linha 1 (`Dotações Completas` · `Dotação Resumida + Descrição`), e na linha 15 a tabela `Portarias` · `Contratos de receita (…)`.
- Na `Planilha2` a coluna `Dotação Resumida + Descrição` traz só a descrição; na `Planilha1` traz "resumida - descrição".

## Modelos recebidos (não são fontes; ficam só no PC do usuário)

- `Cronograma_revisado.xlsx`: abas `Cronograma Joaninha`, `Cronograma Palimercio`, `Parametros`, `Dotacoes` (tabela `Tabela1`: `Dotação Completa Remuneração` · `Dotação Completa` · `Descrição` · `Dotação Resumida`). Obras nas linhas 3 a 17, total na 18; cabeçalhos na linha 2 (`Portaria`, `Lote`, `Dotação`, `Nome da Obra`, `Breve escopo`, `Valores para Obras`, `Valores de Remuneração`, `Valores Total`, `Início`, `Fim`, meses de L a AE, totais por ano em AF e AG). Entradas preenchidas pelo app: B a G, J e K das linhas 3 a 17; o resto é fórmula. A aba principal é a que a `Parametros` usa para o resumo (`Parametros!B20`). Quando `Início` não é dia 1º, o modelo não distribui valor naquele mês.
- Relatório fotográfico de exemplo: uma aba por bloco de páginas, área de impressão `C2:L58`, retrato, cabeçalho repetido a cada 57 linhas. Detalhar no passo 4.

