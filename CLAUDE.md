# Arquivos SPOBRAS

App Windows (C# / .NET 10 / WPF) de uso individual para organizar, renomear e compactar arquivos, gerar relatório fotográfico e cronograma, preencher modelos Excel e consultar uma base de contratos, portarias e dotações.

## Onde está cada coisa

- `docs/ARQUITETURA.md`: como construir. **Manda em caso de conflito.**
- `docs/ESPECIFICACAO.md`: o que o app faz (revisão 2.3).
- `docs/PROGRESSO.md`: o que já foi feito, testado e o que falta. Atualize ao fim de cada sessão.
- `docs/FONTES.md`: tabelas e colunas reais das planilhas (criado no passo 5).

## Como trabalhar

1. Leia `docs/PROGRESSO.md` e descubra o passo atual (seção 10 da arquitetura).
2. Leia só as seções da arquitetura e da especificação ligadas a esse passo. Não releia os documentos inteiros.
3. Faça um passo por sessão. Teste, atualize `docs/PROGRESSO.md`, faça commit e pare.
4. Depois do passo 1 (Teste de Ambiente), pare e aguarde o relatório do usuário.

## Regras fixas

- Lógica em `Spobras.Core`, `Spobras.Dados` e `Spobras.Documentos` (net10.0, testáveis em Linux). `Spobras.Excel` e `Spobras.App` ficam finos.
- Comandos: `dotnet build` e `dotnet test` (os projetos `-windows` só compilam e rodam no CI Windows; use `-p:EnableWindowsTargeting=true` para compilar em Linux).
- O app nunca apaga arquivos do usuário, nunca escreve na planilha mestra, nunca move sem confirmação na prévia.
- **Repositório público.** Não versione planilhas reais, modelos oficiais, logo, caminhos ou nomes de usuário do PC do trabalho, nem contratos e valores reais. Use `amostras/` com dados sintéticos. `modelos/` fica no `.gitignore`.
- Não invente colunas, valores ou regras financeiras. O que faltar vira pergunta ao usuário, registrada em `docs/PROGRESSO.md`.
- Nada de tela fictícia ou função simulada marcada como pronta.
- Interface em português do Brasil. Código e nomes de arquivos sem acentos.
- Responda ao usuário em português do Brasil, de forma curta.
