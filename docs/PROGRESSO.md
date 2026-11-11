# Progresso

Passo atual: **3. App: casca + telas dos módulos de arquivos + bandeja** (seção 10 da arquitetura). Ainda não iniciado.

Passos 1 e 2 concluídos. Seguir o modelo visual de `docs/interface/` (ler o `LEIAME.md` de lá, que lista o que mudou depois do protótipo).

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

## Testado (automático, Linux e CI Windows)

`dotnet test`: 97 testes passam, 1 pulado (Excel real). Casos da especificação cobertos neste passo:

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

## Decisões técnicas

- xUnit v3 com Microsoft.Testing.Platform (`global.json`); o `dotnet test` do .NET 10 exige.
- Automação do Excel por COM tardio (`dynamic`): funciona com Excel 32 ou 64 bits.
- Renomear: com zero à esquerda a largura mínima é 2 e cresce com o maior número do lote; colisão vira sugestão com sufixo (situação "ajustado", com o motivo); continuar numeração olha só nomes no padrão exato "nome + separador + número" e segue depois do maior.
- Zip por limite: a conta usa o pior caso (arquivo que não comprime), então o zip final nunca passa do limite. Para arquivos que comprimem muito (planilhas, texto) podem sair mais partes do que o mínimo possível. Os arquivos ficam na ordem recebida.
- Mover entre volumes: copia para `nome.spobras-parcial`, confere tamanho e hash, publica o nome final e só então tira a origem.
- Histórico: `ModificadoEm` é guardado em ticks para o Desfazer comparar com o disco sem erro de arredondamento.
- `{obra}`, `{lote}` e `{tarefa}` no destino das regras dependem da base (passo 5); por enquanto só `{ano}` e `{mes}`.

## Pendências com o usuário

- [ ] Reenviar `Cronograma_revisado.xlsx` e o relatório fotográfico de exemplo quando chegar a vez dos passos 4 e 6: os arquivos enviados na conversa não ficam guardados entre sessões e não podem ir para o repositório.
- [ ] Logo SPOBRAS (passo 4), a ser colocado pelo próprio usuário no app; confirmar se há versão em melhor qualidade.
- [ ] Confirmar o uso da aba `PORTARIAS` e o layout de `CONTRATADO_LIQUIDADO` na planilha mestra (passo 5; ver `docs/FONTES.md`).
- [ ] Decidir se o caminho da pasta e os códigos de contrato de receita que estão em `docs/ARQUITETURA.md` devem ser trocados por exemplos genéricos (repositório público).

## Decidido pelo usuário

- **Data da Ordem de Serviço:** coluna `INÍCIO EXECUÇÃO` da tabela `contratos_VW_ESCOLAS_CONTRATOS_MEDIDOS_PAGOS`.
- A base fica na pasta sincronizada da rede; quando a planilha for salva lá, o app relê sozinho (arquitetura 5.1, passo 5).
- Dotação completa sempre com pontos; espaço é erro de digitação e deve ser corrigido na importação.
- Portaria 3 tem dois contratos de receita: 015/SIURB/2025 (manutenções e reformas) e 164/SIURB/2025 (construções novas).
- A mesma dotação se repetir em várias portarias é normal.
- Repositório público: https://github.com/Fomes1574/SPOBRAS-Arquivos

## Não testado (e por quê)

- Nada do passo 2 rodou ainda no PC do trabalho: é lógica sem tela. O uso real começa com as telas do passo 3.
- T04 "entre volumes" é simulado (o teste finge que origem e destino estão em volumes diferentes); pen drive e pasta sincronizada reais ficam para o roteiro de conferência.
- T06 com a vigia real do Windows (`FileSystemWatcher`), bandeja e notificação agrupada: passo 3.
- Reduzir fotos antes de compactar (especificação 4.3, item 5): depende da biblioteca de imagem, escolhida no passo 4.
- Duplicados por conteúdo e reconhecimento por conteúdo: passo 7.
- `Spobras.Excel.Tests`: só roda com Excel instalado (pulado no Linux e no CI).
