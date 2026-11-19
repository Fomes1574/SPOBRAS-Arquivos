# Plano: cronograma mais claro (tela e planilha)

Fase de projeto (19/11/2026). Nada implementado.

## 1. Defeito: "035" não mostra os contratos

- **A busca acha os contratos.** Testado com a planilha atual: "035" encontra os 4 contratos 035/SPOBRAS.
- **A tela não mostra.** O estilo `BotaoLink` desenha só texto (`TextBlock Text="{TemplateBinding Content}"`). Cada resultado é um bloco com título e detalhe, então o botão fica vazio e invisível: é o espaço em branco abaixo do campo.
- **Correção:** um estilo de item de lista que aceite conteúdo, mais o botão "Adicionar contrato" e a ordem por início do número (ver `PLANO_LICITACOES.md`, seções 1 e 4).
- **Teste:** um teste de tela de verdade (Windows) que digita "035" e confere que os itens aparecem com texto visível.

## 2. O que o modelo de fórmulas fazia (planilha "Cronograma revisado" e os dois scripts)

- **Remuneração:** `=G*0.1` em cada linha. A aba Parametros tinha 10%, mas a fórmula não usava. O app já usa o percentual do campo Remuneração.
- **Meses:** começam no menor início e seguem por 20 colunas; os totais só cobriam 2 anos. O app gera do primeiro ao último mês e um total por ano, quantos anos forem.
- **Distribuição:** linear. Cada obra tinha uma linha "PORCENTAGEM MENSAL" onde se fixava um mês: número até 1 era percentual do total; acima de 1, valor em reais. O restante era dividido igualmente pelos outros meses. O app faz o mesmo nos "Ajustes por mês" (% do total ou R$), e ainda tem curva S.
- **Arredondamento:** o modelo deixava dízimas (R$ 359.858,1311…). O app arredonda no centavo e acerta a diferença no último mês.
- **Título automático:** uma obra: "Cronograma SPOBRAS - nome da obra"; várias: "Cronograma SPOBRAS - Portaria X / Lote Y - N unidades".
- **Resumo por dotação:** até 5 dotações, cada uma com o par Obra (…5100) e Remuneração (…3900), somando as obras da mesma dotação. Havia a conferência "RESUMO OK" (resumo = total). O app já soma por dotação e sem limite.
- **Script "Novo cronograma":** limpava só os campos digitados. No app: "Limpar tudo".
- **Script "Cronograma pronto":** ocultava linhas sem obra, pares vazios do resumo e meses sem valor. O app já gera só o que tem valor.

## 3. Planilha gerada: proposta

Comparando com o cronograma do lote 161 (feito pelo modelo) e o gerado pelo app (Safra + Redondinhos):

- **Título e subtítulo:** "Cronograma SPOBRAS" e, embaixo, o resumo automático ("Lote 161 · Portaria Nº 06 · 4 unidades" ou "2 lotes · 5 unidades"), como o título do modelo.
- **Lote numa faixa, e não numa coluna estreita:** as obras ficam agrupadas por lote. Cada grupo abre com uma faixa clara na largura da tabela: "Lote 161 · Portaria Nº 06 (2023) · 4 unidades". Nome comprido ("CONSTRUÇÃO CENTRO EDUCACIONAL – …") cabe na faixa sem espremer a linha. Lote curto fica igual de limpo.
- **Subtotal por lote** na própria faixa, quando houver mais de um lote.
- **Linhas com a mesma altura** e nome da obra em até 2 linhas.
- **Mantém:** meses, total por ano, total solicitado, resumo por dotação, paisagem em uma página de largura, cabeçalho repetido na impressão.
- **Pergunta ao usuário:** tirar as colunas Portaria e Lote das linhas (já estão na faixa) ou mantê-las com o número curto.

Exemplo com dados fictícios gerado fora do repositório para o usuário comparar.

## 4. Tela do cronograma: proposta

- **Valor da obra formatado enquanto digita:** "2500000" aparece como "2.500.000" na hora, com R$ fixo ao lado; ao sair do campo vira "2.500.000,00". Hoje só formata ao sair.
- **Sem o aviso de início diferente da OS:** o início é livre. Continua só a informação discreta de onde o início veio, quando veio da base.
- **Distribuição só na obra:** sai a "Distribuição" geral do rodapé. Hoje ela é o padrão das obras marcadas como "Como o cronograma", o que duplica a escolha. Cada obra passa a ter Linear ou Curva S (padrão Linear), com um atalho "Usar em todas".
- **Obras em cartões:** cada obra em duas linhas. Em cima, o nome e o contrato de origem; embaixo, Portaria · Lote · Dotação · Escopo · Valor · Início · Fim · Distribuição. Assim o nome deixa de ser cortado.
- **"Como fica" como miniatura do cronograma:** uma grade igual à planilha (meses por obra e totais), em vez de só texto.
- **Testes:** de unidade da máscara de dinheiro (digitação, colar, apagar, centavos) e testes visuais das telas.
