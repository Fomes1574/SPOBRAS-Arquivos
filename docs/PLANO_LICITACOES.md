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

## 4. Perguntas ao usuário

1. **Onde usar:** "Em licitação" e "A licitar" servem só para consulta na Base, ou também devem entrar no Cronograma (para a pressão orçamentária)? Para o cronograma, essas obras não têm datas; elas teriam de ser digitadas.
2. **Valores:** em "Em licitação", "Valor do Contrato com ADM" é o valor com os 10% e "Recursos para 2027" é esse valor × 1,1 de novo? Ou o primeiro é o valor sem ADM? (O app não pode deduzir regra financeira.)
3. **Celular:** também no celular, ou só no computador?
4. **Abas de resumo:** as abas "PRESSÃO EM LICITAÇÃO" e "PRESSÃO A LICITAR" são só resumos. O app pode ignorá-las e fazer as contas a partir das listas?
