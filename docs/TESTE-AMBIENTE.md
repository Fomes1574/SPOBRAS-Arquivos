# Teste de Ambiente: como rodar

Leva cerca de 10 minutos. O teste só cria e remove arquivos dele mesmo, não pede administrador e não mexe no Excel que estiver aberto.

## 1. Baixar

1. No GitHub, abra a aba **Actions** do repositório e entre na execução mais recente de **build** com o sinal verde.
2. No fim da página, em **Artifacts**, baixe **TesteAmbienteSPOBRAS** (é preciso estar logado no GitHub).
3. Extraia o zip. Dentro há um único arquivo: `TesteAmbienteSPOBRAS.exe` (não precisa instalar nada).

## 2. Rodar no PC do trabalho

1. Leve o `TesteAmbienteSPOBRAS.exe` para o PC do trabalho e abra com dois cliques.
   - Se o Windows mostrar "O Windows protegeu o computador", clique em **Mais informações** e **Executar assim mesmo**. Se não houver essa opção ou a TI bloquear, anote a mensagem: isso já é o resultado do item 1.
2. Opcional: clique em **Escolher arquivo…** e aponte o modelo do cronograma. O teste abre uma **cópia**; o original não é alterado.
3. Clique em **Rodar teste** e aguarde. No item 8 o programa pergunta se você viu o ícone na bandeja e a notificação.
4. Para o item 6 conferir a leitura "só na nuvem", deixe pelo menos um arquivo pequeno do OneDrive com o ícone de nuvem (botão direito no arquivo, **Liberar espaço**) antes de rodar.

## 3. Devolver o resultado

O programa salva `relatorio-ambiente.txt` em **Downloads**. Envie esse arquivo (ou cole o texto) na conversa com o Claude. O relatório troca a pasta do seu usuário por `%USERPROFILE%` e não lista nomes de arquivos seus.

**Não coloque o relatório no repositório**: ele é público.
