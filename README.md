# Trajado Tools

Área de transferência, prints, clipes e gravação de tela para Windows, para quem usa uma instalação sem o `Win + V`, sem ferramenta de captura e sem a sobreposição da NVIDIA. Leve, em um único arquivo C#, sem nada para instalar além do que já vem no Windows (e do ffmpeg, para os vídeos).

Até a versão 1.6 se chamava **ClipHistorico**. Ao abrir a versão nova, as pastas antigas são renomeadas sozinhas e nada se perde.

## O que faz

- **Histórico:** guarda até 50 textos e 10 imagens copiados, com busca e prévia. Apaga sozinho depois do prazo que você escolher (1 hora a 30 dias, ou nunca; padrão de 1 dia).
- **Salvos:** textos e imagens fixos. Um salvo pode ter imagem e texto juntos: o duplo clique cola a imagem e, com a prévia do WhatsApp aberta, cola o texto na legenda. Nunca aperta Enter, e salvos não têm prazo.
- **Emotes coloridos:** 1.715 emojis com busca em português.
- **Prints:** janela ativa ou tela inteira, inclusive em jogos. Vão para `Imagens\PRINTS` e para a área de transferência.
- **Recorte de tela:** `Win + Shift + S` congela a tela (todos os monitores), você arrasta a área e ela vai para a área de transferência e para o histórico. Funciona em cima de jogos e programas em tela cheia.
- **Salvar imagem como arquivo:** PNG, JPG ou BMP.
- **Editor de imagem:** tela cheia, com lápis, marca-texto, borracha, borrar, setas, retângulos, texto, recorte e outra imagem por cima. Abra com o botão **Editar**, `Ctrl + E` ou dois cliques na prévia. O que for borrado é substituído na própria imagem, sem como recuperar no arquivo final. `Ctrl + scroll` aproxima e afasta, `Shift + scroll` anda para os lados e o scroll sozinho anda para cima e para baixo.
- **Clipes (replay instantâneo):** como o da NVIDIA. Grava a tela o tempo todo e `Alt + F10` salva os últimos segundos (15 s a 10 min) em `Vídeos\CLIPES`, com o som do PC e, se quiser, o microfone. A tela é capturada e codificada na placa de vídeo (NVENC, AMD ou Intel, com o processador como último recurso). Numa RTX 3060, gravando 1080p a 60 FPS, usa cerca de 2% de um núcleo do processador, e salvar leva menos de 1 segundo.
- **Gravação:** `Alt + F9` começa e para. Grava até você parar, com as mesmas configurações dos clipes, e salva em `Vídeos\CLIPES` como `gravacao-....mp4`. Um "● REC" com o tempo aparece no canto, mas não sai no vídeo. Se o programa fechar no meio, o que foi gravado vira vídeo na pasta na próxima vez que ele abrir.
- **Jogos em tela cheia (BlueStacks e outros):** quando um jogo em tela cheia ganha ou perde o foco (`Alt + Tab`, tecla Windows, um aviso), o Windows tira a tela da captura por um instante (erro `887a0026`). O programa percebe na hora e volta a gravar em menos de 0,1 segundo, **sem perder o que já estava no replay**: o clipe passa pelo ponto da queda com um salto de menos de 1 segundo, e o segundo logo antes da queda também entra. A gravação (`Alt + F9`) continua no mesmo vídeo.
- **Vigia da captura:** se a captura travar sem nenhum erro, o replay reinicia sozinho em cerca de 2 segundos, também sem perder o que já tinha. Um clipe nunca sai com imagem de minutos atrás.
- **Galeria (`Alt + Z`):** prints, clipes e gravações numa tela só, separados por dia, com o replay, a gravação e as configurações no topo. Um clique abre (vídeo no player, print no editor).
  - **Selecionar vários:** arraste um retângulo com o mouse (a tela rola sozinha perto da borda), clique no círculo do canto, use `Ctrl + clique`, `Shift + clique` ou `Ctrl + A`.
  - **Com a seleção:** copiar (cola como arquivos no WhatsApp, Discord ou numa pasta), mostrar na pasta, mandar para a Lixeira (`Delete`) ou **excluir permanentemente** (`Shift + Delete`, pede confirmação).
  - **Configurações:** duração, qualidade, resolução, FPS, taxa de bits, formato (H.264, HEVC ou AV1, conforme a placa), monitor, som, cursor, pasta e os **atalhos** dos vídeos.
- A galeria e os avisos não aparecem nos clipes nem nas gravações.
- **Atalhos configuráveis**, início junto com o Windows e opção de colar sozinho.

| Ação | Atalho padrão |
|---|---|
| Abrir o histórico | `Win + V` |
| Abrir os emotes | `Win + .` |
| Print da janela ativa | `Ctrl + Alt + J` |
| Print da tela inteira | `Ctrl + Alt + K` |
| Recortar parte da tela | `Win + Shift + S` |
| Galeria de prints e clipes | `Alt + Z` |
| Salvar clipe (replay) | `Alt + F10` |
| Iniciar ou parar a gravação | `Alt + F9` |
| Ligar ou desligar o replay | `Alt + Shift + F10` |

No Histórico e nos Salvos, um clique mostra o item e o duplo clique (ou Enter) copia, cola e fecha.

## Instalar

Baixe o zip da versão mais recente em [Releases](../../releases), extraia em uma pasta e abra o `TrajadoTools.exe`. Requer Windows 10 ou mais novo com .NET Framework 4.5.

Os clipes e as gravações precisam do [ffmpeg](https://ffmpeg.org) (versão 7 ou mais nova). Se ele não estiver no computador, use **Baixar o ffmpeg** nas configurações da galeria (`Alt + Z`). O programa baixa o pacote oficial do [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), com cerca de 115 MB, e guarda só o `ffmpeg.exe` em `%LOCALAPPDATA%\TrajadoTools\ffmpeg`. Ele também é encontrado se estiver ao lado do `.exe` ou no `PATH`.

## Compilar

Rode o `compilar.bat`. Ele usa o compilador que já vem no Windows, fecha o programa se estiver aberto, compila e abre a versão nova.

## Onde ficam os dados

| O quê | Pasta |
|---|---|
| Histórico | `%USERPROFILE%\TrajadoTools` |
| Prints | `Imagens\PRINTS` |
| Clipes e gravações | `Vídeos\CLIPES` (dá para mudar) |
| Replay, gravação em andamento e miniaturas | `%LOCALAPPDATA%\TrajadoTools` |
| Salvos e configurações | `%APPDATA%\TrajadoTools` |

O histórico fica em arquivos comuns, sem criptografia. Tudo o que você copiar, inclusive senhas, é gravado nessa pasta até vencer o prazo, sair da lista ou ser apagado. Use **Pausar captura** no ícone perto do relógio para evitar isso.

## Testes

`testes/Teste.cs` testa o programa sem tocar no seu histórico. Só entra na compilação com `/define:TESTE`:

```
csc /target:exe /platform:x86 /define:TESTE /resource:emojis.gz,emojis.gz /out:teste.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:Microsoft.VisualBasic.dll TrajadoTools.cs testes\Teste.cs
teste.exe <pasta para os arquivos de teste>
```

Deixe o mouse no monitor principal: o editor e a galeria abrem na tela do mouse, e os testes do editor contam com uma tela larga.

Os testes de vídeo gravam a tela de verdade por alguns segundos (com o ffmpeg) e congelam o ffmpeg de propósito para testar o vigia. `teste.exe <pasta> clipes` roda só eles. `teste.exe <pasta> sincronia` pisca um quadrado branco no canto da tela e toca um bipe ao mesmo tempo, para medir se o som está no compasso do vídeo.
