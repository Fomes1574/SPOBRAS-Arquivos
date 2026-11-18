# Plano: carimbo nas fotos, endereço pelo GPS e leveza do app

Projetado e liberado em 18/11/2026; **implementado em 18/11/2026** (ver `PROGRESSO.md`). Onde a implementação ficou diferente do projeto, o texto abaixo já está corrigido.

## 0. Regra de ouro das otimizações: não perder arquivos

Nenhuma melhoria abaixo pode mexer no que guarda o trabalho do fiscal. O comportamento atual fica como está:

- A opção "Guardar também na galeria" continua existindo e podendo ser desligada.
- Fechar o app ou sair sem gerar não apaga nada: fotos, legendas e descrições voltam iguais.
- "Novo relatório" continua apagando o rascunho anterior (decisão do usuário).
- A foto original nunca é alterada. O carimbo só aparece no relatório gerado.

Só pode ser limpo o que o app refaz sozinho: miniaturas, cópias reduzidas (preparadas) e arquivos temporários.

**Testes de segurança dos arquivos**, obrigatórios em toda otimização. Cada teste guarda a impressão (hash) das fotos e o texto do rascunho antes, faz a ação e confere depois:

- fechar e reabrir;
- gerar o relatório;
- app fechado no meio do preparo das fotos;
- limpeza das miniaturas;
- trocar o carimbo;
- ler o endereço;
- atualizar o app por cima, com a pasta de dados intacta.

A única ação que muda o conteúdo é "Novo relatório".

## 1. Endereço pelo GPS

### Como funciona

- **Quando lê:** na primeira vez que a câmera abre no relatório, o app pede a localização uma vez, em segundo plano, sem travar a câmera. Ele espera até 15 s por uma leitura com precisão de 30 m ou melhor e fica com a melhor que tiver.
- **Converter em endereço:** usa o serviço do próprio sistema (Geocoder no Android, CLGeocoder no iPhone). Não entra nenhuma biblioteca nova nem mapa no APK. Precisa de internet só nesse momento.
- **Texto curto:** "Rua Exemplo, 123 – Vila Exemplo". Ficam de fora CEP, cidade, estado e país.
- **Confirmar:** o fiscal confirma ou corrige o texto. O mesmo endereço vale para todas as fotos do relatório.
- **Atualizar:** o botão "Ler de novo" roda o GPS outra vez e troca o endereço guardado.

### Guardado por escola **e** contrato

Na planilha, duas escolas diferentes podem ter exatamente o mesmo nome e até o mesmo código interno. Exemplo: uma escola existente com contrato de manutenção, e uma construção nova com contrato próprio. Só o nome não basta para separar.

- **Chave:** nome da unidade normalizado (sem acento, sem diferença de maiúsculas e espaços) mais o número do contrato.
- **Mesma chave:** o endereço aparece direto, mesmo sem internet.
- **Mesmo nome, contrato diferente:** o app nunca reaproveita sozinho. Ele pergunta: "Já existe um endereço guardado para uma unidade com este nome (contrato X): Rua Exemplo, 123. É o mesmo local?", com os botões "Usar este" e "Ler pelo GPS".
- **Relatório sem contrato** (unidade digitada à mão): a chave é só o nome, e o app sempre pede para confirmar.
- **Distância (só aviso):** cada endereço guarda o ponto do GPS. O aviso aparece quando uma leitura nova fica mais longe que **300 m + a margem de erro das duas leituras**: "Você está a 1,2 km do endereço guardado desta escola. Atualizar?", com "Atualizar" e "Manter". "Manter" não pergunta de novo naquele relatório.
- **A distância não escolhe a escola.** Quem separa as escolas é a chave escola + contrato, escolhida pelo fiscal:
  - Num CEU do tamanho de um quarteirão, o endereço guardado não muda com o fiscal andando pelo terreno. Os 300 m mais a margem cobrem andar pelo terreno sem aviso falso.
  - Escolas pequenas vizinhas podem receber o mesmo endereço do GPS. O fiscal corrige uma vez, e a correção fica guardada para aquele contrato.

### Armazenamento

- **Arquivo:** um JSON pequeno na pasta do app (`enderecos.json`). Cada linha tem chave, nome, contrato, endereço, latitude, longitude, precisão e data da leitura, cerca de 250 bytes.
- **Só o que foi visitado:** entram apenas as escolas onde aquele celular já leu o endereço. A planilha não é copiada.
- **Tamanho:** a planilha tem cerca de 1.250 unidades. Mesmo se todas fossem visitadas com dois contratos cada, seriam uns 600 KB. Um fiscal com 300 escolas fica com uns 75 KB.
- **Por celular:** cada celular guarda o seu arquivo. Não há troca entre fiscais nem com o PC.
- **Proteção:** o arquivo é gravado do mesmo jeito seguro do rascunho (arquivo novo e depois troca). Se o arquivo se perder, só some o atalho: o GPS lê de novo.

### Quando falta alguma coisa

| Situação | O que o app faz |
|---|---|
| Sem permissão de localização | O carimbo sai sem endereço. Aviso com "Permitir" e opção de digitar o endereço. |
| Sem internet e sem endereço guardado | O ponto do GPS fica guardado. Aviso "Sem internet: o endereço será buscado quando houver sinal" e botão "Tentar de novo". |
| Sinal fraco (dentro de prédio) | Mostra "precisão de 80 m" e sugere ler perto da entrada. |
| Celular sem serviço de endereço (sem Google) | Digitar à mão. |
| PC | O PC não tem GPS: o endereço é digitado no passo 1. |

## 2. Carimbo nas fotos

### O que entra (cada item liga e desliga)

- **Escola:** nome da unidade do relatório. Entra na primeira linha, em negrito.
- **Endereço:** o do item 1.
- **Data**, **hora** ou as duas. Vêm do momento da captura: na câmera do app, a hora do toque; na foto da galeria, a data gravada na própria foto (EXIF).
- Foto da galeria sem data gravada: o carimbo sai sem data, e o passo 3 avisa "3 fotos sem data de captura: 04, 07 e 12". O app nunca usa a data do arquivo sem avisar.

**Exemplo:**

```
EMEF Exemplo
Rua Exemplo, 123 – Vila Exemplo
18/11/2026  14:32
```

**Visual:** texto branco sobre faixa preta a 55%, cantos arredondados, fonte do relatório (Carlito). Não entra fonte nova.

### Posição e tamanho, como o controle de jogo no celular

- **Editor:** botão "Carimbo" no passo 2. A foto de exemplo é a primeira deitada do relatório. Os botões "Ver em pé" e "Ver deitada" trocam a foto.
- **Posição:** o fiscal arrasta o carimbo. Ao soltar, ele gruda no canto mais próximo.
- **Tamanho:** pinça com dois dedos, ou a barra "Tamanho" (no PC, só a barra).
- **Como fica guardado:** canto, distância até o canto e tamanho, tudo como fração do lado menor da foto. Por isso o carimbo fica no mesmo lugar visual em foto em pé, deitada e girada.
- **Alcance:** a configuração vale para o relatório inteiro e fica como padrão do próximo. O botão "Padrão" volta para o canto inferior esquerdo, no tamanho padrão.
- **Exceção:** cada foto pode ter "Sem carimbo nesta foto" na ficha da foto.

### Sempre legível e nunca fora da foto

- **Letra mínima:** 5 pt no papel. O tamanho padrão (6% do lado menor da foto) dá pelo menos isso até 6 fotos por página, mesmo com o cabeçalho completo e as assinaturas (teste). Se o tamanho escolhido der menos que isso no formato de página escolhido (por exemplo, 8 por página), o passo 3 avisa: "O carimbo ficou pequeno com 8 fotos por página (letra de 4,6 pt). Aumente o tamanho…". O app não aumenta sozinho, para a cópia carimbada não ser refeita a cada troca do número de fotos por página.
- **Tamanho máximo:** 60% da largura e 35% da altura da foto. O endereço quebra em até 2 linhas; o que passar disso termina em "…".
- **Limites:** a faixa nunca sai da área da foto nem encosta na borda (margem mínima de 2% do lado menor).

### Gravado na cópia da foto, sem atrasar a geração (decidido em 18/11/2026)

O carimbo é **gravado na cópia reduzida** (1.600 px) que vai para o relatório. Essa mesma cópia serve ao Excel e ao PDF, então os dois saem iguais e ninguém consegue mover ou apagar o carimbo no Excel. A original no rascunho continua sem carimbo.

- **Na tela:** enquanto o fiscal arrasta e muda o tamanho, a prévia desenha o carimbo por cima da foto, sem gravar nada. Por isso a resposta ao dedo é instantânea. A conta da posição e a fonte são as mesmas da gravação, então o que se vê é o que sai.
- **Sem passo a mais na geração:** a cópia carimbada é feita junto com a cópia reduzida, em segundo plano, assim que a foto entra, e no mesmo passo, enquanto a imagem já está na memória. Na hora de gerar, ela já está pronta.
- **Mudou o carimbo:** a cópia carimbada é refeita a partir da original (lida já reduzida), uma foto por vez, em segundo plano. Fica só uma cópia por foto (não guarda a sem carimbo junto), para não dobrar o espaço ocupado. A versão antiga sai: é arquivo interno, e o app refaz quando precisar.
- **Custo medido (computador, 300 fotos de 2400 px):** 73 ms por foto sem carimbo e 73 a 75 ms com carimbo, a mesma memória (cerca de 93 MB). A conta da posição leva 2 microssegundos. A cópia carimbada em tamanho cheio para a galeria levou cerca de 120 ms por foto. No celular simples os tempos são maiores, mas a diferença com e sem carimbo continua pequena.

### Galeria com carimbo (decidido em 18/11/2026)

- **Carimbo ligado:** com "Guardar também na galeria" ligado, a galeria recebe a foto **com carimbo**. Com o carimbo desligado, a galeria recebe a original, como hoje.
- **Original sem carimbo:** fica só no rascunho, até o "Novo relatório".
- **Configuração usada:** a do momento em que a foto foi tirada. Mudar o carimbo depois não altera o que já foi para a galeria.
- **Em tamanho cheio:** a foto da galeria não é reduzida. Para não travar a câmera, a gravação na galeria entra numa fila separada, uma foto por vez, em segundo plano. A foto aparece na galeria alguns segundos depois, enquanto a câmera continua livre.
- **Endereço ainda em leitura:** a fila espera o endereço até 30 s. Sem endereço até lá, a foto vai para a galeria com o carimbo sem a linha do endereço.
- **Memória:** decodificar uma foto de 12 MP ocupa cerca de 48 MB. Com uma foto por vez, o pico fica controlado mesmo num celular de 2 GB.

## 3. Leveza do app

| # | Melhoria | Ganho esperado | Risco para os arquivos | Como testar |
|---|---|---|---|---|
| 1 | Um APK para cada tipo de processador (64 e 32 bits). O de 32 bits fica para celular simples com Android Go. | Era 84 MB com os dois juntos; ficou com 46,6 MB (64 bits) e 45,7 MB (32 bits) no CI 92. | Nenhum, se o nome do pacote e a chave forem os mesmos: instala por cima e mantém os dados. | O CI confere o pacote, a chave e o versionCode de cada APK. No celular, instalar por cima com um rascunho aberto. |
| 2 | Fonte do relatório numa cópia só, com apenas as letras que o relatório aceita. A licença exige outro nome na versão reduzida, e o app já usa "SPOBRAS Relatorio". | Eram 2,6 MB (duas cópias de 1,3 MB); ficaram 0,3 MB (duas cópias de cerca de 145 KB). | Nenhum. | Os testes do PDF conferem que toda letra aceita pelo `TextoMedido` existe na fonte reduzida e que as larguras não mudaram. |
| 3 | Preparar 1 ou 2 fotos por vez em celular com pouca memória (Android "low RAM" ou menos de 3 GB). Hoje são até 4. | Menos risco de o app fechar sozinho. Fica um pouco mais lento só nesses aparelhos. | Nenhum: só muda a velocidade. | Estresse com 300 fotos de 12 MP com 1, 2 e 4 por vez, medindo tempo e pico de memória. |
| 4 | Limpeza: confirmar com testes que só miniaturas, preparadas e temporários saem. | Pouco: o app já limpa isso hoje. | É o item mais sensível, por isso só ganha testes, sem mudar a regra. | Testes de segurança dos arquivos (seção 0). |

Fica de fora o corte de código: ele pode quebrar Excel e PDF só no celular, sem aviso.

Já existe e continua:

- leitura da foto já reduzida (o app não abre os 12 MP inteiros na memória);
- foto preparada uma vez e reaproveitada;
- miniaturas sem uso há 30 dias saem.

## 4. Telas

### Celular

- **Passo 1, Dados:** cartão "Endereço das fotos", com estes estados:
  - vazio: "Ler pelo GPS";
  - lendo: "Procurando sinal… precisão 40 m";
  - achado: endereço, precisão e data, com "Ler de novo" e "Editar";
  - guardado de outro contrato: "É o mesmo local?";
  - sem internet;
  - sem permissão.
- **Câmera:** um selo pequeno no alto, "📍 lendo endereço…" e depois "📍 Rua Exemplo, 123". O selo não cobre o visor nem os botões, e tocar nele abre o cartão.
- **Passo 2, Fotos:** botão "Carimbo" na barra, que abre o editor:
  - foto de exemplo;
  - chips Escola, Endereço, Data e Hora;
  - barra Tamanho;
  - "Ver em pé" e "Ver deitada";
  - "Padrão" e "Pronto".

  Nas miniaturas o carimbo não aparece, para não pesar a lista.
- **Passo 3, Conferir:** o carimbo aparece na prévia das páginas, com o aviso das fotos sem data.

### PC

- **Passo 1:** campo "Endereço das fotos", digitado.
- **Passo 2:** botão "Carimbo…" abre um painel lateral. O carimbo é arrastado com o mouse, e o painel tem a barra Tamanho e os mesmos chips.
- **Passo 3:** prévia com carimbo.

## 5. Testes

- **Lógica (Core):**
  - posição no canto mais próximo, com giro de 0, 90, 180 e 270 graus e foto quadrada;
  - letra mínima em cada formato de página;
  - corte do endereço longo;
  - carimbo sempre dentro da imagem;
  - chave escola + contrato: mesmo nome com contratos diferentes nunca se misturam; nome com acento e espaço duplo acha a mesma escola;
  - aviso de distância.
- **Endereço (com GPS e Geocoder simulados):**
  - sem permissão;
  - sem internet;
  - tempo esgotado;
  - precisão ruim;
  - endereço sem número;
  - arquivo `enderecos.json` danificado;
  - mil escolas guardadas (tempo de busca e tamanho).
- **PDF:** com PdfPig, as palavras do carimbo ficam dentro da área da foto e com letra de pelo menos 5,5 pt.
- **Excel:** a caixa de texto fica dentro da âncora da foto. A abertura no LibreOffice continua com o mesmo número de páginas.
- **Visual, celular (Playwright, 360 e 412 px):**
  - editor com o carimbo nos quatro cantos, nos tamanhos mínimo e máximo;
  - endereço longo, foto em pé e deitada;
  - todos os estados do cartão de endereço e o selo da câmera.

  O que se confere: carimbo dentro da foto, texto sem corte, contraste, alvos de toque de pelo menos 44 px e nada coberto.
- **Visual, PC (CI Windows):** painel do carimbo e campo de endereço com a conferência automática de layout.
- **Estresse:** 300 fotos com e sem carimbo. O tempo e a memória devem ficar praticamente iguais, porque o carimbo não toca nas fotos. Repetir com 1 foto por vez (celular simples).
- **Segurança dos arquivos:** a lista da seção 0, em toda mudança.
- **Celular de verdade (roteiro para o usuário):** um Android simples (8 ou 9, 2 GB) e um novo. Anotar tempo e travamentos em:
  - abrir o app;
  - tirar 30 fotos;
  - ler o endereço com e sem internet;
  - gerar com e sem carimbo;
  - fechar no meio e reabrir.

## 6. Plataformas

| Aparelho | Situação | Cuidados |
|---|---|---|
| Android 8 e 9 (antigos, muitas vezes 32 bits e 2 GB) | Funciona hoje. | APK de 32 bits; preparar 1 foto por vez; permissão de localização simples. Já anotado: nessas versões, "Guardar também na galeria" não grava (falta a permissão de armazenamento). |
| Android 10 a 12 | Funciona hoje. | Permissão de localização "só durante o uso". |
| Android 13 em diante | Funciona hoje. | A localização pode ser concedida só como "aproximada". Nesse caso o app avisa que o endereço pode sair errado e pede a precisa. |
| iPhone | Fora por enquanto (18/11/2026: sem conta de desenvolvedor da Apple). | Sem a conta não há como instalar no iPhone de outras pessoas. Se um dia houver conta, as telas e a câmera podem ser reaproveitadas; galeria, arquivos, compartilhar e GPS precisam de código próprio. |

## 7. Câmera: várias fotos seguidas

### O que acontece hoje

Já é possível tirar várias fotos seguidas. Com o flash desligado, cada toque pega a foto na hora. Com o flash ligado ou automático, aparece "Segure firme" enquanto o sensor ajusta a luz.

O problema relatado em 18/11/2026: depois de 3 fotos seguidas, o relatório apareceu vazio e as fotos surgiram uns 2 segundos depois. **Nenhuma foto se perde**, mas a tela assusta. A causa está na ordem dos passos:

1. **No toque:** a imagem do visor é pega na hora.
2. **Depois do toque:** ela é transformada em JPEG em tamanho cheio (até 4096 × 3072), o passo mais lento num celular simples. Em seguida vai da câmera para o app, é gravada no rascunho e só então entra na lista.
3. **Ao fechar a câmera:** a lista abre na hora e mostra só as fotos que já terminaram o passo 2. As outras aparecem quando terminam.

### Melhorias

1. **A foto entra na lista no toque.** O lugar dela é reservado na hora, com a imagem do visor e "Salvando…" (nunca "gravando", que lembra vídeo). Detalhes em "Como fica o Salvando" abaixo.
2. **Contador na câmera:** "Salvando 2…" embaixo da miniatura da última foto.
3. **Fila na câmera:** os toques nunca esperam a foto anterior terminar de salvar. A câmera só pede para esperar se 6 fotos estiverem salvando ao mesmo tempo (3 num celular com pouca memória), para não estourar a memória do celular simples.
4. **Medir antes de otimizar:** uma tela de diagnóstico (escondida nas Configurações) mostra o tempo de cada passo nas últimas fotos: pegar a imagem, transformar em JPEG, passar para o app, gravar e miniatura. Com os números de um celular de verdade, otimizar o passo mais lento. Candidatos:
   - transformar em JPEG fora da tela principal (worker), sem travar os toques;
   - passar a foto da câmera para o app sem a conversão em texto que a ponte entre página e app faz hoje (verificar no .NET 10).
5. **Carimbo e galeria fora do caminho da câmera:** a cópia carimbada e a gravação na galeria (seção 2) correm em filas separadas, depois que a foto já está segura no rascunho. Elas nunca atrasam o próximo toque.

### Como fica o "Salvando"

**Na câmera**
- **Ao tocar:** a tela pisca e a miniatura no canto mostra a foto nova, como hoje. Embaixo da miniatura aparece "Salvando 1…".
- **Fotos seguidas:** o número sobe: "Salvando 3…". O botão de tirar continua livre.
- **Quando terminam:** aparece "✓ Salvas" por 1 segundo e o texto some.
- **Fila cheia (6 salvando):** o botão fica cinza com "Aguarde…" e libera sozinho quando uma termina.
- **Fechar (✕) enquanto salva:** a lista abre na hora, sem esperar. As fotos que ainda estão salvando já aparecem nela.

**Na lista de fotos (passo 2)**
- **Lugar reservado:** a foto aparece no lugar certo, na ordem dos toques, com a imagem do visor um pouco apagada, um círculo girando no canto e "Salvando…" embaixo.
- **Bloqueio rápido:** enquanto salva, que costuma levar até 2 segundos, a foto não pode ser girada, arrastada, marcada nem receber legenda. Tocar nela mostra "Aguarde, a foto está sendo salva".
- **Quando termina:** a foto fica normal e o círculo some, sem mensagem.
- **Topo da lista:** "12 fotos · 2 salvando". O "· 2 salvando" some quando acabam.
- **Falha ao salvar:** a foto fica com borda vermelha e "Não salva · toque". O toque abre "Esta foto não foi salva", com o motivo e dois botões: "Tentar de novo" (funciona enquanto a imagem ainda está na memória; se não estiver, o app avisa para tirar de novo) e "Tirar da lista". A foto nunca some sem aviso.
- **Gerar com fotos salvando:** o app mostra "Esperando 2 fotos terminarem de salvar…" e continua sozinho.

**Se o app for fechado à força durante o "Salvando"**
Fica só o que já terminou de salvar. A foto que ainda estava sendo convertida se perde, como acontece hoje; a janela é de até 2 segundos. O lugar reservado existe só na memória, então nunca sobra uma foto fantasma ao reabrir.

### Testes

- **Visual (Playwright):** 3, 10 e 30 toques seguidos, fechando a câmera logo depois. A lista deve mostrar o número certo de fotos desde o primeiro instante, com "Salvando…" nas que ainda não terminaram e as fotos na ordem dos toques.
- **Robustez:**
  - app fechado com fotos na fila: as que já tinham virado arquivo entram, nenhuma pela metade;
  - falha ao salvar uma foto;
  - fila cheia.
- **Segurança dos arquivos (seção 0):** depois de toques seguidos, o número de fotos no rascunho é igual ao número de toques.

## 9. Revisão do GPS (implementada em 18/11/2026)

**Decisões do usuário:** só o primeiro endereço, o mais próximo.

**Na implementação:** o motor ficou o `LocationManager` do próprio Android ouvindo GPS, rede e a fonte combinada, em vez da biblioteca do Google. Essa biblioteca trazia versões mais novas das bibliotecas AndroidX do MAUI, um risco de quebrar o app. Por isso também não há a janela do Google para ligar a localização precisa: o app abre a tela de localização do Android.

### O que aconteceu no teste do usuário

Com a permissão dada, a localização ligada e o Wi-Fi da empresa, o app ficou procurando e terminou em "sem sinal do GPS".

### Causa (no código do app, não no celular)

- **Leituras jogadas fora:** a leitura usa o `Geolocation` do .NET MAUI com precisão "Best". No Android, esse modo descarta em silêncio toda leitura com margem de erro maior que 50 m e só entrega as que ficam abaixo disso.
- **Fontes que não chegam lá:** dentro de prédio o GPS quase não vê os satélites. A posição pelo Wi-Fi e pelas antenas costuma ter margem de 20 a 100 m ou mais, então quase tudo era descartado.
- **Android 14 em diante:** quando o aparelho não tem o provedor "fused" ligado, o MAUI escolhe um provedor só (o GPS). Isso piora o caso dentro de prédio.
- **Sem atalho:** o app também não aproveitava a última localização conhecida pelo celular (a que o Google Maps e outros apps já obtiveram), que sai na hora.

Resultado: 15 s sem nenhuma leitura aceita, e a mensagem "sem sinal".

### O que fazem os apps de foto com localização (GPS Map Camera, Timemark, Solocator, NoteCam)

- Usam o provedor combinado do Google (GPS + Wi-Fi + antenas), o mesmo do Google Maps.
- Mostram a precisão de cada leitura.
- Deixam escolher entre endereço automático e manual, e entre endereço curto e longo.
- Assumem que dentro de prédio o endereço pode sair aproximado: o manual é a saída segura.

### Proposta

**1. Endereço opcional, sem pedir permissão de cara**
- **Interruptor no passo 1:** cartão "Endereço nas fotos" com "Usar o endereço pelo GPS", desligado no começo.
- **Permissão só quando ligar:** o app mostra uma frase curta ("Para pôr o endereço nas fotos, o app usa a localização só agora, uma vez") e depois o pedido do Android.
- **Câmera sem pedidos:** a câmera nunca pede permissão.
- **Desligado:** o item "Endereço" do carimbo fica apagado com "ligue nos dados do relatório", e nenhuma localização é usada.

**2. Leitura em 3 etapas, sem jogar leitura fora**
1. **Na hora (menos de 1 s):** a última localização conhecida do celular, se tiver menos de 2 minutos e margem de até 100 m. Aparece como "aproximada" e já permite buscar o endereço.
2. **Leitura atual (até 20 s):** GPS, Wi-Fi e antenas juntos. Cada leitura melhor substitui a anterior e a tela mostra "precisão 35 m… 18 m…". Nenhuma leitura é descartada pela margem.
3. **Para cedo:** com 20 m ou menos, ou no fim dos 20 s, o app fica com a melhor leitura e mostra a precisão real. Só sem nenhuma leitura aparece "sem sinal".

**3. Motor de localização**
- **Recomendado:** provedor combinado do Google (`FusedLocationProviderClient`, pacote `Xamarin.GooglePlayServices.Location`), o mesmo do Google Maps. Dentro de prédio com Wi-Fi a leitura costuma sair em poucos segundos.
  - Ele também abre a janela do próprio Android "Ativar a localização precisa", quando estiver desligada.
  - Custo: um pacote a mais no APK, estimado em 1 a 2 MB (medir no CI).
- **Reserva (celular sem os serviços do Google, por exemplo Huawei):** o `LocationManager` do próprio Android, ouvindo GPS e rede ao mesmo tempo (e o provedor "fused" do sistema no Android 12+), sem filtro de margem.
- **Bateria:** liga só quando o fiscal pede e desliga assim que termina. Nada fica rodando em segundo plano.

**4. Endereço a partir do ponto**
- **Serviço:** o Geocoder do Android, gratuito e sem chave, com internet.
- **Falhas:** ele às vezes falha com erro de rede ("grpc failed") mesmo com internet. O app tenta de novo 2 vezes, com intervalo, antes de dizer que não achou.
- **Escolher entre opções:** o Geocoder devolve até 5 endereços para o mesmo ponto. O app mostra os 2 ou 3 primeiros para o fiscal tocar no certo; numa escola de esquina, pode ser "Rua A" ou "Avenida B".
- **Sem serviço pago:** fica de fora o Google Geocoding API (precisa de chave e é pago) e o Nominatim (o uso por app é restrito). Editar à mão continua sempre disponível.

**5. Mensagens que dizem o que fazer**
- **Localização do celular desligada:** botão "Ligar a localização", que abre a janela do Android.
- **Só localização aproximada liberada (Android 12+):** botão "Permitir localização precisa".
- **Sem internet:** o ponto fica guardado, com "Buscar o endereço depois".
- **Precisão ruim (mais de 100 m):** aviso "Endereço pode estar errado: confira", com a opção de editar.

**6. Diagnóstico da localização**
Em Configurações, ao lado do diagnóstico da câmera, ficam os dados das últimas leituras: fonte (GPS, Wi-Fi ou rede), precisão, tempo até a primeira leitura e se o Geocoder respondeu. Serve para o teste de campo.

**7. Precisão esperada**

| Situação | Margem de erro |
|---|---|
| Céu aberto (GPS) | 3 a 10 m |
| Dentro de prédio com Wi-Fi | 10 a 50 m |
| Só antenas de celular | 100 a 1.000 m |

Para o endereço, até 50 m costuma acertar a rua; o número pode sair aproximado e o fiscal confirma.

**8. Testes**
- **Simulados:**
  - última localização na hora;
  - Wi-Fi com 40 m e depois GPS com 8 m;
  - nenhuma leitura;
  - localização desligada;
  - permissão só aproximada;
  - Geocoder falhando uma vez e depois respondendo;
  - Geocoder com 3 endereços.
- **Visuais:** cada estado do cartão.
- **Roteiro de campo para o usuário:**
  - dentro do escritório com Wi-Fi;
  - na rua;
  - no pátio de uma escola;
  - sem internet;
  - com a localização desligada.

  Em cada caso, anotar o tempo e a precisão mostrados no diagnóstico.

## 10. Segundo teste do GPS (fase de projeto, 18/11/2026)

### O que o usuário viu

- **Endereço errado:**
  - No prédio da SIURB (Rua XV de Novembro, 165, Centro Histórico), o app deu "Rua da Quitanda, 64 – Centro Histórico de São Paulo", com precisão de 100 m.
  - No mesmo momento, o Google Maps também não soube dizer onde ele estava: respondeu só "região do Triângulo Histórico".
- **Endereço que não sai:** depois de desligar "Usar o endereço pelo GPS", o endereço continuou aparecendo na câmera e iria para as fotos.

### Diagnóstico

1. **A leitura funcionou, mas veio imprecisa.**
   - Dentro de um prédio, num centro de prédios altos, o GPS não vê os satélites, e a posição sai do Wi-Fi e das antenas.
   - Nesse caso o próprio celular só sabe a região. Por isso o Google Maps respondeu só "Triângulo Histórico".
   - Uma margem de 100 m quer dizer que o ponto real pode estar a 100 m ou mais. A Rua da Quitanda fica justamente a essa distância da XV de Novembro.
   - Estudos com celulares dão erros típicos de 3 a 30 m na rua entre prédios altos, e mediana de dezenas de metros para a posição só por Wi-Fi.
2. **O app aceitou um endereço impreciso sem perguntar.** Com 100 m de margem, o endereço do ponto é só um palpite e não deveria ir para as fotos sem o fiscal confirmar.
3. **Erro de regra no app:** o interruptor controlava só o GPS, não o endereço. Com ele desligado, o endereço já lido continuava valendo no carimbo.

### O que outros apps fazem

- **Timemark:** grava o ponto do GPS no toque e avisa que o endereço pode sair errado se o celular ainda não tiver um sinal forte. Recomenda esperar alguns segundos e ir para área aberta.
- **Apps de câmera com mapa (GPS Map Camera, MapCam, GeoMap):** oferecem localização automática ou manual. No modo manual, o usuário marca o lugar num mapa ou procura o endereço.
- **Google Maps:** mostra um círculo de margem em volta do ponto, em vez de fingir precisão.

### Métodos estudados

| Método | Resultado esperado | Avaliação |
|---|---|---|
| GPS na rua | 3 a 30 m | Ótimo fora do prédio. Dentro, quase nada |
| Wi-Fi e antenas (o que o app usa hoje junto com o GPS) | dezenas a centenas de metros | Funciona dentro do prédio, mas impreciso no centro |
| Wi-Fi RTT (802.11mc) | 1 a 2 m | Precisa de roteadores especiais, que quase não existem: fora |
| Marcar no mapa | exato | Precisa de mapa: OpenStreetMap restringe o uso por apps e Google Maps exige chave paga. Fica para depois, se for necessário |
| Endereço de cada escola por cadastro (dados abertos da Prefeitura) | exato, sem GPS | O conjunto "Cadastro de escolas municipais, conveniadas e privadas" existe no portal de dados abertos. Não foi possível abrir daqui (endereço bloqueado nesta sessão). Precisa do arquivo ou da liberação do endereço `dados.prefeitura.sp.gov.br` |
| Confirmar uma vez e guardar por escola + contrato | exato a partir da 2ª visita | Já existe. É o caminho mais seguro junto com a confirmação |

### Proposta

**A. Um controle só para o endereço**
- **"Endereço nas fotos", ligado ou desligado:** é o mesmo controle do item "Endereço" do carimbo; os dois mudam juntos.
- **Desligado:** nenhum endereço aparece na câmera, no carimbo, na galeria, nas prévias e no relatório. O texto fica guardado, sem apagar, para quando for religado.
- **Ligado, de onde vem o endereço:** "Digitar" (padrão) ou "Pelo GPS".

**B. GPS que não engana**
1. **Margem até 30 m:** o endereço vale direto, como hoje.
2. **Margem acima de 30 m:** o endereço vira sugestão e só entra nas fotos depois de um toque.
   - Texto: "Perto de: Rua da Quitanda, 64 (margem de 100 m). Está certo?"
   - Botões: "Usar este", "Editar" e "Ler de novo".
   - Até o fiscal confirmar, o carimbo sai sem endereço e o passo 3 avisa.
3. **Mais tempo quando a leitura é ruim:** se depois de 20 s a margem ainda passar de 30 m, o app continua ouvindo o GPS até 45 s e mostra "Para mais precisão, vá para perto de uma janela ou para fora do prédio".
4. **Satélites no diagnóstico:** quantos satélites o celular vê e quantos usa. Com 0, fica claro que é falta de céu, não defeito do app.
5. **Confirmou uma vez, fica:** a confirmação vale para a escola + contrato. Nas próximas visitas não precisa de GPS, que só confere a distância.

**C. Cadastro de endereços das escolas (opcional)**
- **Como funcionaria:** com o arquivo de escolas da Prefeitura, o app preenche o endereço pelo nome da escola, sem GPS e sem internet.
- **Peso:** o arquivo reduzido ficaria em poucas centenas de KB.
- **O que falta:** o arquivo ou a liberação do endereço `dados.prefeitura.sp.gov.br` para estudar as colunas.

**Decisões do usuário (18/11/2026), que substituem os itens B.1 a B.3 acima**
- **Confirmação sempre:** com qualquer margem, acima ou abaixo de 30 m, o endereço lido vira sugestão. Ele só entra nas fotos depois de "Usar este". Sempre há "Editar" e "Marcar no mapa".
- **30 m é a meta da leitura:** ao chegar a 30 m, a leitura para.
- **Leitura comprometida:** depois de 30 s procurando sem chegar a 30 m, aparece um aviso só: "Leitura comprometida: vá para perto de uma janela ou ao ar livre". A leitura continua até 45 s.
- **Sem frases soltas:** nenhuma frase dizendo que o endereço "pode não ser preciso". A margem aparece só como número na linha pequena e no diagnóstico.
- **Satélites no diagnóstico:** aprovado (item B.4).
- **Marcar no mapa:**
  - mapa com o ponto lido;
  - o fiscal arrasta o mapa até o lugar exato, com um alfinete fixo no centro;
  - "Usar este ponto" busca o endereço desse ponto.
  - O provedor do mapa ainda precisa ser escolhido (ver abaixo).

**Provedor do mapa (a decidir)**

| Opção | Custo | Peso no app | Observação |
|---|---|---|---|
| Leaflet (biblioteca de mapa de cerca de 40 KB, dentro da tela do app) + mapa do OpenStreetMap | grátis | cerca de 40 KB | Exige crédito "© OpenStreetMap" na tela. A política de uso proíbe uso pesado por apps sem autorização; o uso aqui é pequeno (algumas consultas por relatório, poucos fiscais), mas não há garantia de serviço |
| Leaflet + provedor com plano gratuito (ex.: MapTiler) | grátis até um limite mensal, com chave | cerca de 40 KB | Precisa criar uma conta e uma chave (a chave fica nos segredos do CI, nunca no repositório) |
| Google Maps (SDK para Android) | mapa no app sem custo por carregamento, mas exige chave e conta de cobrança no Google Cloud | biblioteca do Google | Traz bibliotecas do Google que entram em conflito com as do MAUI (o mesmo problema visto na localização): risco de quebrar o app |

Recomendação: Leaflet + OpenStreetMap para começar, com o crédito na tela e cache dos pedaços do mapa já vistos, trocando para um provedor com chave se o uso crescer.

**D. Testes**
- Desligado não aparece em lugar nenhum: câmera, cópia carimbada, galeria, prévia, Excel e PDF.
- Margem de 100 m vira sugestão, e só entra depois de "Usar este".
- 45 s com o GPS melhorando.
- Diagnóstico com satélites.
- Teste visual dos estados.
- Roteiro de campo no prédio da SIURB: dentro, na janela e na calçada.

## 8. Decisões do usuário (18/11/2026)

1. **Carimbo no Excel:** gravado na cópia da foto (seção 2).
2. **Distância do aviso:** 300 m mais a margem de erro do GPS, só como aviso (seção 1).
3. **iPhone:** sem conta de desenvolvedor da Apple, fica fora por enquanto.
4. **Galeria:** recebe a foto com carimbo quando o carimbo está ligado (seção 2).
