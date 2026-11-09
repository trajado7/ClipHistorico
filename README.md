# ClipHistorico

Área de transferência e prints para Windows, para quem usa uma instalação sem o `Win + V` e sem ferramenta de captura. Leve, em um único arquivo C#, sem nada para instalar além do que já vem no Windows.

## O que faz

- **Histórico:** guarda até 50 textos e 10 imagens copiados, com busca e prévia. Apaga sozinho depois do prazo que você escolher (1 hora a 30 dias, ou nunca; padrão de 1 dia).
- **Salvos:** textos e imagens fixos. Um salvo pode ter imagem e texto juntos: o duplo clique cola a imagem e, com a prévia do WhatsApp aberta, cola o texto na legenda. Nunca aperta Enter, e salvos não têm prazo.
- **Emotes coloridos:** 1.715 emojis com busca em português.
- **Prints:** janela ativa ou tela inteira, inclusive em jogos. Vão para `Imagens\PRINTS` e para a área de transferência.
- **Recorte de tela:** `Win + Shift + S` congela a tela (todos os monitores), você arrasta a área e ela vai para a área de transferência e para o histórico. Funciona em cima de jogos e programas em tela cheia.
- **Salvar imagem como arquivo:** PNG, JPG ou BMP.
- **Editor de imagem:** tela cheia, com lápis, marca-texto, borracha, borrar, setas, retângulos, texto, recorte e outra imagem por cima. Abra com o botão **Editar**, `Ctrl + E` ou dois cliques na prévia. O que for borrado é substituído na própria imagem, sem como recuperar no arquivo final. `Ctrl + scroll` aproxima e afasta, `Shift + scroll` anda para os lados e o scroll sozinho anda para cima e para baixo.
- **Clipes (replay instantâneo):** como o da NVIDIA. Grava a tela o tempo todo e `Alt + F10` salva os últimos segundos (15 s a 10 min) em `Vídeos\CLIPES`, com o som do PC e, se quiser, o microfone. A tela é capturada e codificada na placa de vídeo (NVENC, AMD ou Intel, com o processador como último recurso). Numa RTX 3060, gravando 1080p a 60 FPS, usa cerca de 2% de um núcleo do processador, e salvar leva menos de 1 segundo. Dá para configurar duração, qualidade, resolução, FPS, taxa de bits, formato (H.264, HEVC ou AV1, conforme a placa), monitor e cursor.
- **Galeria (`Alt + Z`):** prints e clipes numa tela só, separados por dia, com o replay e as configurações dos clipes no topo. Um clique abre (clipe no player, print no editor). O botão direito copia, mostra na pasta ou manda para a Lixeira.
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
| Ligar ou desligar o replay | `Alt + Shift + F10` |

No Histórico e nos Salvos, um clique mostra o item e o duplo clique (ou Enter) copia, cola e fecha.

## Instalar

Baixe o zip da versão mais recente em [Releases](../../releases), extraia em uma pasta e abra o `ClipHistorico.exe`. Requer Windows 10 ou mais novo com .NET Framework 4.5.

Os clipes precisam do [ffmpeg](https://ffmpeg.org) (versão 7 ou mais nova). Se ele não estiver no computador, use **Baixar o ffmpeg** nas configurações da galeria (`Alt + Z`). O programa baixa o pacote oficial do [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), com cerca de 115 MB, e guarda só o `ffmpeg.exe` em `%LOCALAPPDATA%\ClipHistorico\ffmpeg`. Ele também é encontrado se estiver ao lado do `.exe` ou no `PATH`.

## Compilar

Rode o `compilar.bat`. Ele usa o compilador que já vem no Windows, fecha o programa se estiver aberto, compila e abre a versão nova.

## Onde ficam os dados

| O quê | Pasta |
|---|---|
| Histórico | `%USERPROFILE%\ClipHistorico` |
| Prints | `Imagens\PRINTS` |
| Clipes | `Vídeos\CLIPES` (dá para mudar) |
| Gravação do replay e miniaturas | `%LOCALAPPDATA%\ClipHistorico` |
| Salvos e configurações | `%APPDATA%\ClipHistorico` |

O histórico fica em arquivos comuns, sem criptografia. Tudo o que você copiar, inclusive senhas, é gravado nessa pasta até vencer o prazo, sair da lista ou ser apagado. Use **Pausar captura** no ícone perto do relógio para evitar isso.

## Testes

`testes/Teste.cs` testa o programa sem abrir nada na tela e sem tocar no seu histórico. Só entra na compilação com `/define:TESTE`:

```
csc /target:exe /platform:x86 /define:TESTE /resource:emojis.gz,emojis.gz /out:teste.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:Microsoft.VisualBasic.dll ClipHistorico.cs testes\Teste.cs
teste.exe <pasta para os arquivos de teste>
```

Os testes de clipe gravam a tela de verdade por alguns segundos (com o ffmpeg). `teste.exe <pasta> clipes` roda só eles. `teste.exe <pasta> sincronia` pisca um quadrado branco no canto da tela e toca um bipe ao mesmo tempo, para medir se o som está no compasso do vídeo.
