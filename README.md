# ClipHistorico

Área de transferência e prints para Windows, para quem usa uma instalação sem o `Win + V` e sem ferramenta de captura. Leve, em um único arquivo C#, sem nada para instalar além do que já vem no Windows.

## O que faz

- **Histórico:** guarda até 50 textos e 10 imagens copiados, com busca e prévia. Apaga sozinho depois do prazo que você escolher (1 hora a 30 dias, ou nunca; padrão de 1 dia).
- **Salvos:** textos fixos para colar quando quiser.
- **Emotes coloridos:** 1.715 emojis com busca em português.
- **Prints:** janela ativa ou tela inteira, inclusive em jogos. Vão para `Imagens\PRINTS` e para a área de transferência.
- **Salvar imagem como arquivo:** PNG, JPG ou BMP.
- **Atalhos configuráveis**, início junto com o Windows e opção de colar sozinho.

| Ação | Atalho padrão |
|---|---|
| Abrir o histórico | `Win + V` |
| Abrir os emotes | `Win + .` |
| Print da janela ativa | `Ctrl + Alt + J` |
| Print da tela inteira | `Ctrl + Alt + K` |

No Histórico e nos Salvos, um clique mostra o item e o duplo clique (ou Enter) copia, cola e fecha.

## Instalar

Baixe o zip da versão mais recente em [Releases](../../releases), extraia em uma pasta e abra o `ClipHistorico.exe`. Requer Windows 10 ou mais novo com .NET Framework 4.

## Compilar

Rode o `compilar.bat`. Ele usa o compilador que já vem no Windows, fecha o programa se estiver aberto, compila e abre a versão nova.

## Onde ficam os dados

| O quê | Pasta |
|---|---|
| Histórico | `%USERPROFILE%\ClipHistorico` |
| Prints | `Imagens\PRINTS` |
| Salvos e configurações | `%APPDATA%\ClipHistorico` |

O histórico fica em arquivos comuns, sem criptografia. Tudo o que você copiar, inclusive senhas, é gravado nessa pasta até vencer o prazo, sair da lista ou ser apagado. Use **Pausar captura** no ícone perto do relógio para evitar isso.

## Testes

`testes/Teste.cs` testa o programa sem abrir nada na tela e sem tocar no seu histórico. Só entra na compilação com `/define:TESTE`:

```
csc /target:exe /platform:x86 /define:TESTE /resource:emojis.gz,emojis.gz /out:teste.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll ClipHistorico.cs testes\Teste.cs
teste.exe <pasta para os arquivos de teste>
```
