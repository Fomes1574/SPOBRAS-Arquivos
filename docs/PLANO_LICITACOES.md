# Plano: botão "Adicionar contrato" no cronograma e obras em licitação e a licitar

Fase de projeto (19/11/2026). Nada implementado.

## 1. Botão "Adicionar contrato" no Cronograma (PC)

**Hoje:** a função já existe. "Adicionar escolas de um contrato" é um campo de busca sem dica e sem botão, por isso passa despercebido.

**Proposta:**
- **Botão:** "Adicionar contrato" ao lado de "Adicionar obra", "Colar do Excel" e "Importar de uma planilha".
- **Busca:** abre uma busca com a dica "Digite o contrato ou o lote (ex.: 044/EX/2026)".
- **Resultado:** cada contrato aparece com a empresa, o lote e a quantidade de escolas ("12 escolas").
- **Ao tocar:**
  - todas as escolas do contrato entram no cronograma;
  - aparece o aviso "12 escolas adicionadas" ou "10 escolas adicionadas; 2 já estavam no cronograma";
  - há Desfazer.
- **Origem:** só contratos da base Em andamento.

## 2. O que a planilha mostra

As abas "Em licitação" e "A Licitar" não são tabelas nomeadas do Excel. São listas com título, uma linha de totais e o cabeçalho na 4ª linha. O app vai procurar o cabeçalho pelos nomes das colunas, e não pela posição.

| | Em andamento (contratos) | Em licitação | A licitar |
|---|---|---|---|
| Linhas (planilha de 18/11) | cerca de 1.570 | 115 | 218 |
| Contrato da obra | sim | não existe ainda | não existe |
| Empresa | sempre | falta em 19 (as "Publicada" ainda sem vencedora) | não tem |
| Situação | status do contrato, prazos | "Homologação" (89) ou "Publicada" (26) | sempre "A Licitar Obras" |
| Datas (OS, início, término) | sim | não | não |
| Medições, pago, saldo | sim | não | não |
| Valor | contratado, aditado, medido | "Valor do Contrato com ADM" e "Recursos para 2027" (= valor × 1,1) | "Valor do Contrato" (estimado) e "Valor com ADM" (= valor × 1,1) |
| Processo SEI | sim | sim | não |
| Lote | sim | sim | quase sempre; às vezes é um agrupamento ("PREVISÃO DE MUROS – EMEF'S – PORTARIA 03"), e 9 sem lote |
| Portaria, dotação, modalidade | sim | sim | sim |

**Pontos importantes:**
- **"Contrato Receita" não é o contrato da obra:** é o contrato de receita entre a SPOBRAS e a SIURB (ex.: 015/SIURB/2025), com valores como "Sem Contrato Receita" e "Não tem". O app não pode tratá-lo como contrato da obra.
- **A mesma escola em mais de uma base:**
  - 69 das 115 escolas em licitação já têm outro contrato em andamento, normalmente de outra modalidade;
  - em A licitar, isso vale para 171 de 218;
  - a mesma escola pode aparecer mais de uma vez na própria aba (20 vezes em A licitar), com obras diferentes.

  Por isso as bases não podem ser juntadas pela escola.

## 3. Proposta: três bases separadas que nunca se misturam

**Leitura**
- **Três conjuntos separados:** a leitura da planilha mestra passa a trazer, além das obras em andamento, "Em licitação" e "A licitar". Cada um tem a sua estrutura e os seus campos, sem nada copiado de um para o outro.
- **Banco:** cada conjunto fica numa tabela própria, em uma migração nova.
- **Configurações da base (engrenagem):** cada conjunto ganha sua aba, sua coluna e sua conferência, como já existe para as obras.
- **Aba renomeada ou sem uma coluna obrigatória:** o app avisa só sobre aquele conjunto, sem impedir a leitura das obras em andamento.

**Tela Base (PC e celular)**
- **Seletor no topo:** "Em andamento · Em licitação · A licitar". Cada um tem a sua lista, os seus filtros e a sua ficha:
  - **Em licitação:** filtros de portaria, lote, modalidade, dotação, situação (Publicada ou Homologação) e empresa, com "sem empresa ainda". A ficha mostra situação, empresa (ou "Sem vencedora ainda"), valores, processo SEI e lote.
  - **A licitar:** filtros de portaria, lote, modalidade e dotação. A ficha mostra o valor estimado, o valor com ADM e o lote ou agrupamento.
- **Mesma escola em mais de uma base:** a ficha mostra um aviso com atalho, por exemplo "Esta escola também está em: Em andamento (contrato 039/EX/2023) · A licitar (Muro de Arrimo)". Os dados não se juntam.
- **Selos visuais diferentes** para cada base, para não confundir uma obra contratada com uma prevista.

**Fora da Base:** o relatório fotográfico e o resumo por contrato continuam só com obras em andamento.

## 4. Decisões do usuário (19/11/2026)

- **Busca de contrato ao digitar:** a partir de 3 caracteres, a lista mostra os contratos parecidos ("012" mostra 012/SPOBRAS/2022, 012/SPOBRAS/2023…).
  - Hoje a busca procura o texto em qualquer parte e não ordena, então "012" também acha "LOTE 012" no meio.
  - Ordem proposta: primeiro os contratos cujo número começa com o que foi digitado, do ano mais novo para o mais antigo; depois os que contêm o texto.
- **Processo SEI em "A Licitar":** algumas linhas têm, numa coluna que a planilha de 18/11 ainda não tinha (na tela do usuário, a coluna G, antes de STATUS). O app procura essa coluna pelo nome, como opcional: sem a coluna, ou com a célula vazia, a ficha fica sem SEI.
- **Contrato SIURB:** é o contrato de receita (o pagamento para a SPOBRAS).
  - 015/SIURB/2025 e 164/SIURB/2025 são sempre da Portaria 3.
  - Só indica dentro de qual contrato de receita a obra está; quando a obra for contratada, o empenho vem por ele.
  - A ficha mostra "Contrato de receita: 015/SIURB/2025 (Portaria 3)", separado do contrato da obra.
- **Onde usar as duas bases novas:**
  - **Base de dados:** consulta com exportação, com um modelo de exportação para cada base, só com as colunas que ela tem.
  - **Cronograma:** dá para pedir recurso para uma escola antes de ela ser contratada.
  - **Relatório fotográfico:** por enquanto não.
- **Valores:** "Recursos para 2027" (Em licitação) e "Valor com ADM" (A licitar) já têm os 10% finais. No cronograma, o valor da obra é "Valor do Contrato com ADM" (Em licitação) ou "Valor do Contrato" (A licitar), e o próprio cronograma soma a remuneração, chegando ao mesmo total da planilha. Isso precisa ser confirmado antes de implementar.
- **Abas de resumo:** as abas de pressão são ignoradas.

## 5. Cronograma pelo lote ou pela modalidade, sem misturar

Na planilha de 18/11, 10 lotes de Em licitação e 9 de A licitar têm o mesmo número de um lote em andamento, mas com outra modalidade (ex.: LOTE 64 é "Manutenção" no contrato e "Manutenção CEU" na licitação; LOTE 09 tem "Pista de Skate" a licitar e contratos de manutenção). Por isso o lote nunca é usado sozinho.

**Busca no cronograma**
- **Mesma busca, resultados agrupados por base:** procura escola, contrato, lote ou modalidade e mostra o resultado separado por base, com selo. Exemplo:
  - **Em andamento:** LOTE 64 · Manutenção · contrato 216/EX/2023 · 3 escolas
  - **Em licitação:** LOTE 64 · Manutenção CEU · Homologação · 1 escola
  - **A licitar:** LOTE 09 · Pista de Skate · 1 escola
- **Grupo:** cada grupo é **base + lote + modalidade**. Escolher um grupo traz só as escolas daquele grupo.
- **Linha no cronograma:** cada linha mostra o selo da base. Obra em licitação ou a licitar não tem datas: a linha pede início e término, como uma obra digitada à mão.

**Ligação com a vista oculta "escola vwSGCOE"**
- **Quase todas aparecem lá:** 112 das 115 em licitação e 215 das 218 a licitar aparecem com o mesmo nome e lote, sem contrato, e com um código próprio da escola (`idEmpreendimento`).
- **Uso:** só para ligar a mesma escola entre as bases (o aviso "também está em…") e para separar escolas de nomes iguais.
- **O que não se usa dela:** a situação dessa vista ("Elaborando RD", "Elaborando Material Técnico"…) é diferente da situação das abas. As abas mandam, e nada da vista sobrescreve o que elas dizem.

## 6. Perguntas ao usuário

1. **Valores:** confirmar a regra do cronograma da seção 4 (valor da obra sem os 10%, e a remuneração somada pelo próprio cronograma).
2. **Planilha atual:** enviar a planilha atual, com a coluna de processo SEI em "A Licitar", para conferir o nome do cabeçalho dessa coluna.
3. **Liberação:** liberar a implementação.
