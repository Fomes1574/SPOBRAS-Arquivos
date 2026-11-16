# Resumo por contrato, exportar PDF e filtros: o que implementar

Planejado com o usuário em 16/11/2026. Tudo aqui já foi decidido; o que não estiver escrito é pergunta ao usuário (registre em `docs/PROGRESSO.md`, seção "Pendências com o usuário"). Os modelos visuais estão em `docs/codex/modelos/` e usam **dados inventados**.

## Como usar este documento

1. Leia `CLAUDE.md` e as regras fixas (repositório público, nada de dados reais, interface em pt-BR, código sem acentos).
2. Leia este documento inteiro e abra os dois cadernos de modelo:
   - `modelos/caderno-resumo-por-contrato.pdf`: o PDF exportado (todos os casos) e a tela da Base no computador.
   - `modelos/caderno-base-celular.pdf`: as telas do celular em 360 e 412 px, com o resultado dos testes visuais.
3. Implemente na ordem da seção 10. **Tudo vale para o computador e para o celular**, exceto o cronograma (só existe no computador).
4. Teste como pede a seção 9, atualize `docs/PROGRESSO.md` e faça commit.

---

## 1. Por que "por contrato" e não "por lote" (estudado na planilha real)

Levantamento feito na planilha mestra do usuário (só números agregados; a planilha não é versionada):

| O que foi conferido | Resultado |
|---|---|
| Contratos / escolas | 515 contratos, 1.569 escolas; 285 contratos têm uma escola só; o maior tem 13 |
| Campos sempre iguais dentro de um contrato (515 de 515) | portaria, lote (`nmLote`), modalidade, contratada, processo SEI, data da OS (`INÍCIO EXECUÇÃO`), término (`TÉRMINO EXECUÇÃO`), status do contrato, situação (no prazo / fora do prazo) |
| Campos que mudam de escola para escola | valores, tipo de obra, dotação, atividade, medições em trâmite |
| Lotes (portaria + nome) com mais de um contrato | 38. **Em todos**, os contratos são de **modalidades diferentes** (Manutenção, Reforma e Manutenção de CEUs, Muro de Arrimo, Quadras...). Em 173 pares de contratos, só 3 têm alguma escola em comum |
| (portaria, modalidade, lote) com mais de um contrato | 0 de 510 |
| Nomes de lote repetidos em portarias diferentes | 21 |
| Valores | contratado c/ aditamento = original + aditado; saldo a pagar = contratado − pago; confere nas 1.569 linhas |
| Contratos sem lote | 5 |
| Contratos sem OS e sem término | 16 (não iniciados, suspensos, rescindidos) |
| Contratos com espaço sobrando no nome | 10 (usar sempre `Trim`) |

**Conclusão (confirmada pelo usuário):** o nome do lote **não** identifica um contrato. Não existe "contrato anterior" num lote; dois contratos com o mesmo nome de lote são programas diferentes. **A unidade é o contrato** (chave: texto do contrato sem espaços nas pontas). Nunca some contratos diferentes num resumo só.

---

## 2. Regras comuns (PDF, computador e celular)

### 2.1 Selos de prazo

Calculados na hora (data de hoje no horário de Brasília, `Spobras.Core.HoraDeBrasilia`) a partir do término e do status do contrato. Ver página 2 do caderno do PDF.

| Selo | Cor | Quando |
|---|---|---|
| "Vence em 4 meses e 21 dias" | verde (`#E7F3EA` / `#1E7B34`) | status **VIGENTE, VENCIDO, SUSPENSO, EM TRÂMITE - ADITIVO** e mais de 90 dias até o término |
| "Vence em 1 mês e 29 dias" | laranja (`#FBF0E4` / `#8A4300`) | os mesmos status, 90 dias ou menos (inclui "Vence hoje") |
| "Venceu há 1 mês e 10 dias" | vermelho (`#FBE9EC` / `#A50D26`) | término já passou e o status não é de encerramento |
| "Encerrado em 10/09/2024" | cinza (`#EDEDEA` / `#3D3D42`) | **ENCERRADO / TRD, CONCLUÍDO, TRP ASSINADO, RESCINDIDO, RESCISÃO EM TRÂMITE, EM ENCERRAMENTO, TRD EM TRAMITAÇÃO**: só a data do término, sem contagem |
| "OS não emitida" | cinza | sem data da OS (ex.: NÃO INICIADO) |

- Status desconhecido (novo na planilha) com término: trate como os de contagem.
- Texto do tempo: meses e dias de calendário ("4 meses e 21 dias", "1 mês e 1 dia", "12 dias"). No singular: "1 mês", "1 dia".
- O status do contrato aparece num selo ao lado, com o texto da planilha em caixa de título ("Vigente", "Encerrado / TRD"): verde para Vigente, vermelho para Vencido, cinza para os demais.
- A coluna `SITUAÇÃO` da planilha ("no prazo" / "fora do prazo") aparece como texto em "Prazo (planilha)"; não muda a cor dos selos.
- Coloque o cálculo no Core (ex.: `Spobras.Core.Base.PrazoContrato`), testável, usado pelo PDF, pelo computador e pelo celular.

### 2.2 Valores, percentuais e pizza

- Cartões: **Contratado c/ aditamento**, **Pago**, **Em trâmite (n medições)**, **Saldo a pagar** (este em destaque, fundo escuro).
- Em trâmite: o que já existe em `MedicoesEmTramite` (considerado + reajuste; sem as de gestão da SIURB).
- Pizza (gráfico de pizza, não rosca) com três fatias e legenda com percentual e valor:
  - **Pago** = pago / contratado c/ aditamento (verde `#1E7B34`);
  - **Em trâmite** = em trâmite / contratado (laranja `#E3A23B`);
  - **A pagar, sem medição em trâmite** = (saldo a pagar − em trâmite) / contratado (cinza `#D9D9D5`). Se a conta der negativa (dado inconsistente), use 0.
- Legenda com o título "Do valor contratado com aditamento (R$ ...)". **Não** escreva a frase solta "do contratado com aditamento" embaixo do gráfico (o usuário pediu para tirar).
- Sem pagamento nem medição (ex.: não iniciado): não desenhe a pizza; mostre "Sem pagamento nem medição ainda: o gráfico aparece quando houver."
- Tabela de escolas: coluna **% pago** (pago / contratado da escola, inteiro).

### 2.3 O que **não** entra

Data de assinatura, escopo, avanço físico (a coluna existe na planilha, mas o usuário não quer), OS e término por escola (são do contrato), "resumo por lote", "contrato anterior", soma de contratos diferentes.

### 2.4 Nome do arquivo

- Contrato: `Resumo do contrato 029-SPOBRAS-2026 - Lote 207 - 2026-11-16.pdf` (barras viram hífens; sem lote: omitir a parte do lote).
- Escola: `Resumo da obra - EMEI Exemplo Alfa - 2026-11-16.pdf`.
- Vários contratos num PDF: sugestão `Resumo dos contratos - 2026-11-16.pdf` (não confirmado; registre como pendência se mudar).
- Nunca sobrescrever: `(2)`, `(3)`... Usar os limites de tamanho de nome que já existem (`CabecalhoRelatorio.MaximoDaUnidadeNoNome`).

### 2.5 Logo e rodapé

- Logo: o mesmo já cadastrado no app (computador: `ServicoModelos.Logo`; celular: `Preferencias.Logo`). Nos modelos ele aparece como um espaço tracejado; sem logo cadastrado, o cabeçalho sai sem a caixa (nada de placeholder no PDF real).
- Rodapé: "Arquivos SPOBRAS · planilha mestra lida em dd/mm/aaaa às hh:mm" à esquerda e "Gerado em dd/mm/aaaa às hh:mm · Página X de Y" à direita, horário de Brasília.

---

## 3. PDF "Resumo da obra" (uma escola)

Modelo: página 3 do caderno do PDF. A4 retrato, uma página. Público: fiscais e chefes de 30 a 70 anos, reunião formal (letra grande, sóbrio, nada espremido).

Ordem:
1. Cabeçalho: logo à esquerda; à direita "BASE DE CONTRATOS · SPOBRAS" e o título "Resumo da obra"; linha vermelha (`#C8102E`).
2. Nome da escola (grande) e, embaixo, "Tipo de obra · Situação em dd/mm/aaaa".
3. **Situação contratual** (bloco com borda): contrato (grande), "Lote · Modalidade", selos de status e prazo; grade com Portaria, Contratada, Processo SEI, Data da OS, Término, Prazo (planilha).
4. **Situação financeira**: os 4 cartões e a pizza com legenda.
5. **Valores**: valor original, aditado, contratado com aditamento, medido, saldo a medir, pago (grade de 3 colunas).
6. **Medições em trâmite**: tabela (medição, situação, início, considerado, reajuste, total) com total. Sem medições: a seção não aparece.

## 4. PDF "Resumo do contrato"

Modelos: páginas 4 a 8 do caderno do PDF.

1. Cabeçalho igual, título "Resumo do contrato".
2. "029/SPOBRAS/2026 · Lote 207" (grande) e "Modalidade · n escolas · Situação em dd/mm/aaaa".
3. **Situação contratual** igual à da escola.
4. **Situação financeira** (cartões + pizza) do contrato inteiro.
5. **Escolas do contrato**: escola (com o tipo de obra embaixo), contratado c/ adit., pago, em trâmite, saldo a pagar, % pago; linha de total "Total do contrato (n escolas)".
6. Observação: "Valores em reais. "Em trâmite": medições apresentadas e ainda não pagas (sem as de gestão da SIURB)."

- **Vários contratos marcados: uma página (ou mais) para cada contrato**, nunca somados (decidido).
- Tabela longa (até 13 escolas na planilha atual, mas pode crescer): continua na página seguinte com cabeçalho da tabela repetido e "(continuação)" no título; os totais só no fim (página 8 do caderno).
- Contrato de uma escola só (metade dos contratos): a tabela sai com uma linha e o total, igual (página 5).
- Use PDFsharp em `Spobras.Documentos` (como `GeradorRelatorioPdf`), com `FontesPdf.Garantir()`. O mesmo gerador é chamado pelo computador e pelo celular.

---

## 5. Computador (WPF)

Modelos: página 9 e 10 do caderno do PDF (`modelos/computador/tela-filtros.png`, `tela-resumo.png`).

### 5.1 Filtros da Base

- **Sai o filtro "Lote".** Ficam Portaria, Contrato, Escola, Empresa (e os "mais filtros" de hoje).
- Cada filtro vira uma caixa larga com o nome em cima (rótulo vermelho sobre a borda). Vazia: "Todas"/"Todos" com ▾. Com seleção: o valor em negrito e **o ✕ dentro da caixa**, à direita (como a borracha do relatório fotográfico). Vários: "029/SPOBRAS/2026 + 1".
- Lista do filtro **Contrato**: cada opção em duas linhas:
  - "029/SPOBRAS/2026 · Lote 207" (negrito);
  - "Manutenção · 6 escolas · Portaria Conjunta nº 12 (2026)".
  - Sem lote: "029/SPOBRAS/2026 · sem lote".
- A pesquisa dentro do filtro Contrato e a caixa "Procurar" acham pelo número do lote ("207"), pelo contrato e pela modalidade (decidido: é assim que se procura por lote agora).
- Consultas salvas antigas que usavam o filtro Lote: ao abrir, converter para os contratos daquele lote (avisando "O filtro Lote foi trocado pelos contratos do lote") ou, se não der, aplicar o lote como texto na pesquisa. Não pode quebrar ao abrir.
- Hoje o `FiltroMultiplo` já não volta ao topo ao marcar: manter.

### 5.2 Resumo do contrato na tela

Com um ou mais contratos filtrados, o painel de totais (hoje "LOTE 207 · 6 obras") vira o **resumo de cada contrato**:
- título "Contrato 029/SPOBRAS/2026" com os selos de status e prazo;
- linha "Lote · Modalidade · Contratada · OS dd/mm/aaaa · término dd/mm/aaaa · Portaria";
- os valores (valor original, aditado, contratado c/ adit., pago com %, em trâmite com %, saldo a pagar);
- tabela com **todas as escolas** do contrato e os valores (contratado, pago, em trâmite, saldo, % pago) e o total; clicar numa escola abre a ficha ao lado, como hoje;
- botão **Exportar PDF** (gera o PDF da seção 4; com vários contratos, uma página por contrato).
- Vários contratos: um bloco por contrato, um embaixo do outro.

### 5.3 Ficha da escola

Botão **Exportar PDF** na ficha (gera o PDF da seção 3).

---

## 6. Celular (Blazor, `Spobras.Celular.Telas`)

Modelos: `modelos/caderno-base-celular.pdf` e `modelos/celular/*.png`. Fontes HTML/CSS dos modelos em `modelos/fontes-dos-modelos/celular/` (use como referência de medidas e cores, não copie o HTML).

1. **Filtros** (etiquetas roláveis de hoje): sai "Lote"; ficam Contrato, Portaria, Empresa, Status. A etiqueta ativa já tem o ✕ dentro: aumentar o ✕ para **44 × 44 px** (hoje 36). A folha do filtro Contrato mostra as opções em duas linhas, como no computador, com pesquisa ("207" → "3 contratos do lote 207"). Tela 1.
2. **Cartão do contrato** no alto da lista (substitui o cartão "Lotes 216 e 239" de hoje): "Contrato" + número, selos, "Lote · Modalidade · n escolas", "Contratada · Portaria", datas da OS e término, 4 valores (saldo em destaque), pizza com legenda (Pago, Em trâmite, A pagar) e botão vermelho **Exportar PDF do contrato**. Tela 2.
3. **Escolas do contrato**: cada escola com o tipo de obra, "% pago", barra pago/em trâmite e os quatro valores; total do contrato no fim. Tela 3.
4. **Dois contratos**: um cartão por contrato e um botão só, "Exportar PDF dos 2 contratos" (texto curto: o longo quebrava em 360 px). Tela 4.
5. **Vencido / encerrado / não iniciado**: telas 5 e 6.
6. **Ficha da escola**: botão "Exportar PDF desta escola" logo abaixo de "Relatório fotográfico", com os selos e a pizza. Tela 7.
7. **PDF salvo**: tela no padrão da de "Pronto" do relatório: aviso verde, arquivo, **Compartilhar (WhatsApp, e-mail…)**, **Abrir o PDF**, **Voltar à Base**. Tela 8. O arquivo entra em "Últimos gerados" (com a lixeira e o Desfazer que já existem).
8. Salvar pela pasta escolhida (`IPlataforma.SalvarAsync`), gerar numa pasta temporária do app e apagar a cópia ao sair da tela (como `RascunhoRelatorio.LimparGerado`).

Problemas que os testes visuais acharam no modelo e **não podem voltar**: texto branco sobre fundo cinza no cartão de saldo (regra CSS que vazava para os filhos: use `>`); valores com margem extra; ✕ com 36 px; placeholder claro demais (3,4 : 1); letra de 11 px; botão com texto em duas linhas.

---

## 7. Cronograma (só no computador)

Hoje as "escolas do mesmo lote" (`CronogramaVm.AtualizarSugestoesDoLote` → `ProcuraObras.DoLote`) comparam **só os números do lote** (`ChaveDoLote`). Na planilha real isso mistura programas: 70 chaves de lote reúnem mais de um contrato (68 com modalidades diferentes), "LOTE 14" e "AGRUPAMENTO 14" viram o mesmo, e em média 8,6 escolas de outros contratos aparecem como sugestão (no pior caso, 15 contratos numa chave).

Fazer:
1. **"Adicionar escolas de um contrato"**: campo com sugestões no mesmo formato do filtro Contrato (contrato · lote / modalidade · n escolas · portaria), pesquisando por contrato ou número de lote. Dois "Lote 14": cada um aparece com seu contrato e modalidade. Escolher um contrato adiciona **todas** as escolas dele (cada uma pelo caminho que já existe, `AdicionarDaBase`), sem repetir as que já estão.
2. **Sugestões abaixo da tabela**: trocar "do mesmo lote" por "do mesmo contrato" das escolas já incluídas (guarde o contrato de origem na linha; ele não precisa aparecer na tabela nem no Excel gerado).
3. Escola sem contrato ou sem lote na planilha: continua entrando pela busca por nome.

---

## 8. Onde mexer (ponto de partida)

- Core: novo cálculo de prazo (seção 2.1); percentuais; nada de regra financeira nova além das contas da seção 2.2.
- Dados: `BaseRepositorio.OpcoesGerais()` precisa das opções de contrato com lote, modalidade, portaria e quantidade de escolas; `FiltroObras` (tirar o uso de `Lote` nas telas, manter compatibilidade das consultas salvas); `ProcuraObras` (cronograma).
- Documentos: novo gerador do resumo (PDFsharp), ao lado de `Relatorio/GeradorRelatorioPdf.cs`.
- Computador: `ViewModels/BaseVm.cs` (`TotaisDoLote`, `ConsultaSalva`), `Telas/BaseTela.xaml`, `Telas/FiltroMultiplo.xaml(.cs)`, `ViewModels/CronogramaVm.cs`.
- Celular: `Telas/Base.razor`, `Telas/Ficha.razor`, `Servicos/BaseCelular.cs` (`Totais`), `wwwroot/app.css`.

## 9. Testes obrigatórios

- **Unidade (Core/Dados/Documentos)**: selos para cada status e para término a 91, 90, 1, 0 e −1 dias; percentuais (incluindo em trâmite maior que o saldo); opções de contrato com lote/modalidade/escolas; contrato sem lote; consulta salva antiga com Lote; "Adicionar escolas de um contrato" e sugestões só do mesmo contrato (com a base sintética tendo dois "Lote 14" de modalidades diferentes e um "AGRUPAMENTO 14"); nome do arquivo.
- **PDF**: abrir com PdfPig e conferir textos e páginas: uma escola; contrato com 1, 6 e 13 escolas (13 em duas páginas, cabeçalho repetido, total só no fim); dois contratos = páginas separadas; encerrado sem contagem; não iniciado sem pizza; logo presente e ausente. **Gerar o mesmo contrato pelo caminho do computador e pelo do celular e comparar** (mesmo texto e mesmas páginas).
- **Visual do celular** (`tests/celular-visual/telas.js`, 360 e 412 px): telas 1 a 8 do caderno, com as verificações que o caderno lista na página 9 (nada vaza, nada cortado, toque ≥ 44 px, nada coberto, letra ≥ 12 px, contraste ≥ 4,5 : 1, botão/etiqueta/selo sem texto em duas linhas). O script `modelos/fontes-dos-modelos/celular/testar.js` mostra como cada verificação foi feita; leve as que ainda não existem para o `telas.js`.
- **Telas do computador** (`Spobras.App.Telas.Tests`, CI Windows): filtros com o ✕ dentro, lista de contratos com lote, resumo do contrato com escolas, exportar.
- **Beta tester**: dois toques em Exportar, exportar e sair da tela na hora, contrato sem escolas visíveis por outro filtro, nomes muito longos de escola e de contratada, valores grandes (R$ 99.999.999,99) sem quebrar linha em 360 px.

## 10. Ordem sugerida

1. Core: prazo, percentuais. 2. Dados: opções de contrato e compatibilidade das consultas salvas. 3. Documentos: PDF da escola e do contrato. 4. Computador: filtros, resumo, exportar, ficha. 5. Celular: filtros, cartão, escolas, ficha, PDF salvo. 6. Cronograma. 7. Testes da seção 9, CI verde (Windows e celular), `docs/PROGRESSO.md`.

## 11. Ideias para depois (não implementar agora)

- Resumo por empresa (mencionado pelo usuário como ideia).
