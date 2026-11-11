# Modelo de interface (referência visual)

Dez telas do protótipo feito na fase de projeto, mais o menu lateral. Servem de **referência de layout, textos, cores e hierarquia** para as telas WPF do passo 3. Não são código a reaproveitar.

## Como ler

- Cada arquivo `.dc.html` é uma tela. O HTML está dentro de `<x-dc>`, com estilos inline (cores, tamanhos, espaçamentos). O bloco `<script type="text/x-dc">` no fim traz os dados de exemplo e os estados da tela.
- Os arquivos dependem de um runtime externo (`support.js`) e **não abrem sozinhos no navegador**. Leia como texto.
- A imagem do logo (`/_blob/...`) não acompanha os arquivos: o logo não é versionado.
- Nomes de arquivos, escola e contratada são exemplos ou marcadores entre colchetes.

| Arquivo | Tela |
| --- | --- |
| Main.dc.html | Início |
| Organizar.dc.html | Organizar arquivos |
| Renomear.dc.html | Renomear em lote |
| Compactar.dc.html | Compactar |
| Relatorio.dc.html | Relatório fotográfico |
| Cronograma.dc.html | Cronograma padrão |
| Modelos.dc.html | Modelos Excel |
| Pdf.dc.html | PDF |
| Base.dc.html | Base de dados |
| Automatico.dc.html | Aviso do modo automático |
| Menu.dc.html | Menu lateral (usado em todas as telas) |

## Estilo

Preto `#231F20`, fundo `#F3F3F1`, painéis brancos com borda `#D6D6D2`, vermelho `#C8102E` só na ação principal e no item ativo do menu, azul `#1F5FA8` para sugestão, laranja escuro `#8A4300` para atenção, texto secundário `#5A5A5F`. Fonte Inter, base 15, botões com 44 de altura, cantos de 8 a 10. Menu lateral escuro à esquerda, conteúdo à direita, barra "Salvar em" no rodapé das telas que geram arquivo.

## O que mudou depois do protótipo (vale a especificação e a arquitetura)

- **Renomear:** três modos (nome + número; nome base + complemento por arquivo; nomes livres), com a coluna "Nome novo" editável. O número é opcional.
- **Relatório fotográfico:** remover o campo "Referência (ex.: Medição 05)". Todos os campos são opcionais. Fluxo em três etapas: Dados → Fotos e legendas → Conferência e geração, com rascunho automático.
- **Aviso do modo automático:** notificação agrupada e painel aberto por escolha do usuário, em vez de janela fixa a cada arquivo.
- **PDF:** acrescentar conversor geral de PDF para Excel, Excel para PDF em lote e logo/numeração em PDFs prontos.
- **Base de dados:** filtros vinculados (portaria, contrato, contrato de receita, escola, dotação, Data da OS, valores), consultas salvas, ficha da obra e tela Fontes de dados.
- **Ainda sem protótipo:** Configurações, Histórico e tarefas, Busca nos arquivos, Juntar planilhas. Seguir o mesmo estilo.
