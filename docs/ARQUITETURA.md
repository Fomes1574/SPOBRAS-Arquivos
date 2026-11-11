# Arquivos SPOBRAS: arquitetura final

Versão 3.1, 11/11/2026. Este arquivo diz **como construir**. O que o app faz está em `docs/ESPECIFICACAO.md` (revisão 2.3). Onde os dois divergirem, vale este.

## 1. Decisões que mudam a especificação 2.3

1. **Aprovação:** nada na especificação está "já autorizado". O usuário aprova cada entrega.
2. **Ordem de entrega:** primeiro o **Teste de Ambiente** (seção 9), validado pelo usuário no PC do trabalho. Depois o app é construído **inteiro**, e o usuário testa só o app completo. As "fases" da especificação viram **ordem interna de construção** (seção 10), não entregas.
3. **Testes:** quem testa durante a construção é o Claude Code, de forma automática (seção 8). O usuário não testa fase por fase.
4. **Proporção:** app de uso individual. Aplicar as regras de segurança da especificação (prévia, histórico, nunca apagar, mestra só leitura), sem criar infraestrutura além do que a seção 3 descreve.
5. **Fontes de dados:** definidas na seção 5 deste arquivo, com os nomes reais das planilhas.

## 2. Plataforma

- C# 13, **.NET 10 (LTS)**, WPF, publicação **autocontida win-x64** (o PC não tem .NET nem Python instalados).
- Excel instalado no PC de destino (Microsoft 365). Arquitetura 32/64 bits confirmada pelo Teste de Ambiente.
- Banco local **SQLite** em `%LOCALAPPDATA%\ArquivosSPOBRAS\` (fora de Downloads e do OneDrive).
- Sem servidor, sem conta, sem internet, sem IA dentro do app.

## 3. Estrutura da solução

```
ArquivosSpobras.sln
├─ src/
│  ├─ Spobras.Core/        net10.0          regras de negócio puras (sem Windows, sem Excel)
│  ├─ Spobras.Dados/       net10.0          SQLite, importadores Open XML, consultas
│  ├─ Spobras.Documentos/  net10.0          gerar/ler xlsx (Open XML), PDF, imagens, zip
│  ├─ Spobras.Excel/       net10.0-windows  automação do Excel instalado (COM), isolada
│  ├─ Spobras.App/         net10.0-windows  WPF (MVVM): telas, bandeja, fila de tarefas
│  └─ Spobras.TesteAmbiente/ net10.0-windows  programa mínimo da seção 9
├─ tests/
│  ├─ Spobras.Core.Tests/        net10.0   roda em Linux e Windows
│  ├─ Spobras.Dados.Tests/       net10.0   roda em Linux e Windows
│  ├─ Spobras.Documentos.Tests/  net10.0   roda em Linux e Windows
│  └─ Spobras.Excel.Tests/       net10.0-windows  só no runner Windows com Excel (ou manual)
├─ modelos/        NÃO versionado (.gitignore): modelos oficiais e logo ficam só no PC do usuário
├─ amostras/       dados de teste SINTÉTICOS com a mesma estrutura das planilhas reais
├─ docs/           ESPECIFICACAO.md, ARQUITETURA.md, PROGRESSO.md
└─ .github/workflows/build.yml
```

**Regra central:** tudo que puder ser lógica pura fica em `Core`, `Dados` e `Documentos` (multiplataforma). Assim o Claude Code compila e testa quase tudo no ambiente dele (Linux). `Excel` e `App` ficam finos: só chamam as outras camadas.

Dependências permitidas: `App → Excel, Documentos, Dados, Core` · `Excel → Core` · `Documentos → Core` · `Dados → Core`. `Core` não depende de ninguém.

## 4. Responsabilidade de cada projeto

| Projeto | Contém |
| --- | --- |
| Core | Modelos de domínio; renomeação (3 modos, ordem natural, colisões, troca A↔B); plano de organização (regras, sugestão, motivo); plano de zip por limite; paginação do relatório fotográfico (encaixe horizontal/vertical/quadrada, 1/2/4/6/8 por página, fonte mínima 11 pt); validação do cronograma; normalização de portaria e dotação; fila de tarefas e estados; histórico e regras de Desfazer. Interfaces `IArquivos`, `IRelogio`, `IExcel` para testar sem disco nem Excel. |
| Dados | Esquema SQLite e migrações; importadores das fontes (seção 5); detecção de mudança das fontes; consultas com filtros vinculados; consultas salvas; perfis, regras, rascunhos, configurações. |
| Documentos | Leitura de tabelas xlsx por nome de tabela (Open XML SDK, sem abrir o Excel); geração do relatório fotográfico em xlsx; preenchimento de modelo por cópia + edição restrita; PDF (juntar, separar, fotos em PDF, reduzir, carimbo de logo/numeração, extração de texto e tabelas); imagens (EXIF, orientação, miniaturas, redução); zip. |
| Excel | Uma instância do Excel **criada pelo app e invisível**, usada em fila (uma chamada por vez): recalcular, exportar PDF, rodar macro cadastrada. Nunca toca na instância do usuário, nunca mata `EXCEL.EXE` que não criou, sempre restaura o que alterar. |
| App | WPF + MVVM (CommunityToolkit.Mvvm). Telas da especificação seção 7. Ícone na bandeja para o modo automático. Trabalho pesado sempre fora da thread da interface. |

Bibliotecas: Open XML SDK (xlsx), Microsoft.Data.Sqlite, CommunityToolkit.Mvvm, xUnit. Para PDF e imagem, escolher após prova com os arquivos de `amostras/` e conferir a licença antes de fixar (registrar a escolha em `docs/PROGRESSO.md`).

## 5. Fontes de dados

O app tem uma tela **Fontes de dados** com uma linha por fonte. Cada fonte é um arquivo Excel que o usuário mantém **fora do app**; o app só lê.

### 5.0 Duas pastas, dois papéis

| Pasta | Papel | Valor inicial |
| --- | --- | --- |
| **Pasta de trabalho** | Onde o usuário organiza, renomeia, compacta e onde os arquivos gerados são salvos por padrão | `%USERPROFILE%\Downloads` |
| **Pasta das planilhas base** | Onde o app procura as fontes de dados (planilha mestra, Dotações Completas e complementares). O app só lê desta pasta | `%USERPROFILE%\Planilhas Relacionadas\rede.sp\SPOBRASPlanilhas relacionadas - Documentos` |

Na primeira execução o app sugere esses dois valores e procura as fontes pelo **nome do arquivo** na pasta das planilhas base (`Escolas_Contratos_Com_Relatorio.xlsx`, `Dotações Completas.xlsx`). Ambos são configurações, nunca constantes no código, e podem ser trocados em Configurações. A pasta das planilhas base fica fora do organizador: o app nunca move, renomeia ou altera o que está nela.

### 5.1 Como a atualização funciona

1. O usuário aponta o arquivo uma vez. O app guarda o caminho e o mapeamento das colunas.
2. O usuário edita a planilha no Excel, como já faz hoje, e salva.
3. O app percebe a mudança (vigia o arquivo e confere também ao abrir), espera a gravação terminar e **relê sozinho em segundo plano**.
4. A importação é validada inteira antes de substituir a anterior. Se falhar (arquivo aberto, coluna faltando), o app mantém a última versão válida e mostra a data dela e o motivo.
5. Botão **Reler agora** em cada fonte. Se o arquivo sumir, o app procura pelo **nome do arquivo** nas pastas configuradas e pede confirmação antes de trocar.

Edição dentro do app: só para a base de portarias e dotações (5.3), com o botão **Exportar para o Excel** para devolver as mudanças à planilha. As outras fontes são somente leitura.

### 5.2 Fonte A: planilha mestra `Escolas_Contratos_Com_Relatorio.xlsx`

Somente leitura, sempre. Atualizada pelo próprio Excel (Power Query). Ler por **nome de tabela**, nunca por nome de aba.

| Tabela ou aba | Uso | Colunas já vistas no arquivo |
| --- | --- | --- |
| `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS` | Contratos, escolas/empreendimentos, valores | INÍCIO EXECUÇÃO, INICIO, VALOR MEDIDO, VALOR PAGO, SALDO A MEDIR, SALDO A PAGAR, DOTACAO, FONTE DE RECURSOS, PORTARIA |
| `escola_vwSGCOE` | Escola, lote, serviço, SEI | Fonte de Recursos, Valor Portaria, Dotacao_Adao, saldo contrato |
| `contratos_VW_EMPENHOS_SOF` | Empenhos | Vl.Pago no Exercício, Vl.Pago Restos, pago total, saldo real, SALDO FINAL, PORTARIA |
| tabela da aba `preCadastro VW_RELATORIO_MEDICO…` | Medições | DATA DE INÍCIO, VALOR PAGO, ATIVIDADE DOTAÇÃO, CONTRATO RECEITA, portaria |
| aba `STATUS` | Situação das obras | coluna OS |
| aba `CONTRATADO_LIQUIDADO` | Contratado e liquidado | conferir layout |

A lista acima veio de uma busca por palavras-chave, não é o esquema completo. **Primeira tarefa do importador:** listar todas as tabelas e colunas do arquivo real e gravar em `docs/FONTES.md`.

**Decidido pelo usuário (11/11/2026):** a data oficial da Ordem de Serviço é a coluna `INÍCIO EXECUÇÃO` da tabela `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS`. As tabelas e colunas reais estão em `docs/FONTES.md` (a aba `STATUS` não tem coluna `OS`).

### 5.3 Fonte B: `Dotações Completas.xlsx` (base de portarias e dotações)

Arquivo pequeno, sem tabelas nomeadas. Ler por **posição dos cabeçalhos** (localizar o texto do cabeçalho, não fixar a célula).

- **Aba `Planilha2`** (fonte principal, 1 linha por vínculo). Cabeçalhos na linha 2, colunas B a F:
  `Portarias` · `Contratos de receita (Por onde vem o dinheiro para obra)` · `Dotações Completas` · `Dotação Resumida` · `Dotação Resumida + Descrição`
- **Aba `Planilha1`**: lista das dotações completas com "resumida + descrição" (cabeçalho na linha 1) e, mais abaixo, a tabela `Portarias` ↔ `Contratos de receita`. Usar só para conferir a `Planilha2`.

Conteúdo atual: 6 portarias, cada uma com 1 contrato de receita, e 12 dotações.

| Portaria | Contrato de receita |
| --- | --- |
| Portaria 2 (2024) | 016/SIURB/2026 |
| Portaria 2 (2025) | 019/SIURB/2026 |
| Portaria 3 | 015/SIURB/2025 |
| Portaria 3 (Construção Nova) | 164/SIURB/2025 |
| Portaria 4 | 301/SIURB/2022 |
| Portaria 6 | 303/SIURB/2022 |

Regras que decorrem do arquivo:

- **Uma portaria pode ter mais de um contrato de receita.** A Portaria 3 tem dois: `015/SIURB/2025` (manutenções e reformas) e `164/SIURB/2025` (construções novas). O rótulo "Portaria 3 (Construção Nova)" da planilha é a Portaria 3 com o contrato 164. Guardar a finalidade junto do vínculo portaria ↔ contrato de receita.
- **A mesma dotação se repete em várias portarias**, e isso é normal (ex.: 3362 nas Portarias 3, 4 e 6). A chave do vínculo é **portaria + contrato de receita + dotação**. A dotação resumida sozinha não identifica a portaria.
- **Forma oficial da dotação completa: sempre com pontos** (confirmado pelo usuário). Espaço no lugar do ponto (ex.: `…3390.3900 00`, `…4490 5100.00`) é erro de digitação. Na importação, trocar o espaço por ponto, guardar o texto original para conferência e avisar na tela Fontes de dados quais linhas foram corrigidas. Nos documentos gerados, usar sempre a forma com pontos.
- Códigos são **texto** (preservar zeros e pontuação).
- A variante escrita em outras planilhas ("Nº 03", "3", "3 (2025)") é ligada à portaria canônica por uma tabela de **apelidos** editável no app; ambiguidade pede escolha do usuário.

### 5.4 Outras fontes

Qualquer planilha complementar entra pela mesma tela: escolher arquivo, tabela ou aba, mapear colunas e chave uma vez. Depois atualiza sozinha como as outras.

## 6. Modelo de dados (SQLite)

Entidades principais e chaves:

- `Portaria(id, rotulo, numero, ano, complemento)` · `PortariaApelido(texto, portaria_id)`
- `ContratoReceita(id, codigo)` · `PortariaContratoReceita(id, portaria_id, contrato_receita_id, finalidade)`
- `Dotacao(id, completa, completa_original, resumida, descricao)` · `VinculoDotacao(portaria_contrato_receita_id, dotacao_id)`
- `Contrato(id, codigo, contratada, sei, …)` · `Escola(id, nome, …)`
- `Obra(id, contrato_id, escola_id, lote, portaria_id?, dotacao_id?, data_os?, origem_data_os, valores…)`: é a unidade onde contrato, escola, portaria e datas se encontram
- `Medicao`, `Empenho` (sempre ligados por chave da fonte; nunca somar após junção sem conferir a cardinalidade)
- `Fonte(id, nome, caminho, mapeamento_json, ultima_leitura, ultima_modificacao, estado)` · `ImportacaoLog`
- `Perfil`, `Regra`, `Rascunho`, `ConsultaSalva`, `Lote`, `LoteItem` (histórico por arquivo), `Config`

Toda linha importada guarda `fonte_id` e a versão da importação. Dinheiro em `decimal`, nunca ponto flutuante.

## 7. Como os módulos usam as camadas

| Módulo | Core | Dados | Documentos | Excel |
| --- | --- | --- | --- | --- |
| Organizar, Renomear, Compactar | plano + prévia + execução + histórico | regras, perfis, histórico | zip, leitura leve de conteúdo | não usa |
| Relatório fotográfico | paginação e encaixe das fotos | rascunho, dados da obra | gera o xlsx, miniaturas, EXIF | só para exportar PDF fiel |
| Cronograma | validação das linhas | portaria, dotação, data da OS | copia o modelo e preenche entradas | recalcula e confere totais |
| Modelos Excel | validação do mapeamento | mapeamentos, listas | cópia + preenchimento | recálculo, macro cadastrada |
| PDF | regras de intervalo, nomes | perfis | todas as operações | Excel → PDF em lote |
| Base de dados | filtros vinculados | importação e consultas | exportar consulta | não usa |

Fluxo único de toda operação em arquivos: **selecionar → montar plano (Core) → mostrar prévia → executar item a item gravando o histórico → resumo**. O mesmo serviço serve a todos os módulos; não duplicar lógica de nomes, colisões e destinos.

## 8. Testes (feitos pelo Claude Code)

- **Unitários em Core** para cada regra, incluindo os casos T01 a T35 da especificação que não dependem de Windows.
- **Dados:** importar as planilhas de `amostras/` e conferir contagens, vínculos, releitura e falha parcial.
- **Documentos:** gerar relatório e cronograma, reabrir o arquivo gerado e conferir células, imagens, fórmulas preservadas e número de páginas; operações de PDF e zip com arquivos reais.
- **CI:** `.github/workflows/build.yml` em `windows-latest` compila tudo, roda todos os testes multiplataforma e publica o instalador como artefato.
- **Não testável pelo Claude Code:** automação do Excel real, bandeja, OneDrive, instalação liberada pela TI. Cobrir com o Teste de Ambiente (seção 9) e com um roteiro curto de conferência entregue junto do app.
- Nenhum módulo é dado como pronto com tela fictícia ou função simulada. `docs/PROGRESSO.md` registra o que foi testado e o que não pôde ser.

## 9. Teste de Ambiente (primeira entrega, 10 minutos do usuário)

Programa `Spobras.TesteAmbiente`, autocontido, com um botão **Rodar teste**. Ele verifica e mostra **passou / falhou** para cada item, e salva `relatorio-ambiente.txt` em Downloads:

1. O programa abre no PC do trabalho (instalação liberada pela TI).
2. Versão e arquitetura do Windows; escala do monitor.
3. Excel encontrado: versão e 32/64 bits.
4. Criar uma instância invisível do Excel, abrir uma planilha de teste com fórmulas criada pelo próprio programa (e, se o usuário apontar, uma cópia do modelo do cronograma), escrever numa célula de entrada, recalcular, ler um total, exportar PDF, salvar e fechar **sem afetar o Excel do usuário**.
5. Criar, renomear, mover e remover um arquivo de teste em Downloads.
6. Ler a pasta sincronizada do OneDrive (listar, e ler um arquivo que esteja só na nuvem).
7. Gravar e ler o SQLite em `%LOCALAPPDATA%`.
8. Mostrar o ícone na bandeja e uma notificação.
9. Vigiar Downloads: detectar um arquivo novo criado pelo próprio teste.

O usuário devolve o `relatorio-ambiente.txt`. Qualquer falha ajusta esta arquitetura **antes** de construir o app.

## 10. Ordem interna de construção

1. Solução, CI e Teste de Ambiente → **parar e aguardar o relatório do usuário**.
2. Core: tarefas, histórico, arquivos, renomear, zip, organizador.
3. App: casca (menu, início, fila de tarefas, histórico, configurações, perfis) + telas dos módulos de arquivos + bandeja.
4. Documentos e tela do relatório fotográfico; PDF básico.
5. Dados: fontes A e B, base, filtros vinculados, tela Base de dados e Fontes de dados.
6. Cronograma e Modelos Excel (com `Spobras.Excel`).
7. Organizador por conteúdo, consultas salvas, ficha da obra, exportações, extras de PDF, busca, juntar planilhas, duplicados.
8. Revisão geral, instalador final e roteiro de conferência → **entrega única para o teste do usuário**.

OCR fica fora desta entrega (evolução posterior).

## 11. Regras para o Claude Code

- Ler `CLAUDE.md` sempre; ler deste arquivo e da especificação **só as seções do passo em andamento**.
- Um passo da seção 10 por sessão. Ao terminar: atualizar `docs/PROGRESSO.md`, fazer commit e parar.
- **O repositório é público.** Nunca versionar planilhas reais, modelos oficiais, o logo, nomes de usuário, caminhos do PC do trabalho, nomes de pessoas, contratos ou valores reais. Usar `amostras/` com dados sintéticos de mesma estrutura.
- Modelos e logo entram no app pelo próprio usuário (Configurações → Modelos e logo) e ficam em `%LOCALAPPDATA%\ArquivosSPOBRAS\modelos`. O instalador não os contém.
- Não inventar nomes de colunas: o que não estiver em `docs/FONTES.md` é pendência a perguntar.
- Interface e mensagens em português do Brasil. Código e nomes de arquivos sem acentos.
