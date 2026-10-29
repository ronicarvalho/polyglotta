# ADR-0008 — Avalonia para a bandeja e a tela de configuração, em processo único

**Status:** Aceito · **Data:** 2026-10-28

## Contexto

O ADR-0007 define que o Polyglotta vira um agente residente da sessão do usuário com ícone na bandeja. Falta escolher com o que desenhar esse ícone e a tela de configuração — o M4 do PRD-001 já previa a escolha ("WinUI/Avalonia — exigirá ADR").

A pergunta levantada foi se dá para fazer essas telas em .NET para Linux/macOS, ou se sairia mais fácil manter o mecanismo em .NET e o controle em Electron.

O que a superfície de UI realmente é, hoje:

- Um ícone de bandeja com quatro itens de menu e um estado (parado / ativo / com erro).
- Uma janela de configuração com, no máximo: provedor, três dispositivos, par de idiomas, duas vozes, "ativar ao iniciar", e um botão de teste de áudio.
- Eventualmente, uma janela de monitoramento com transcrição corrente, nível de entrada e latência p95 (RF-06 — hoje é log no console).

Verificação feita para este ADR (2026-10-28):

- O `TrayIcon` do Avalonia é documentado com suporte **completo em Windows e macOS**. No Linux, funciona em ambientes que implementam **StatusNotifierItem/AppIndicator** (confirmado no Ubuntu); no GNOME o usuário pode precisar da extensão AppIndicator. O evento `Clicked` não é levantado no macOS — lá o menu é o caminho.
- Essa limitação do Linux **não é do Avalonia**: é do desktop. A documentação do Electron e a do Slint descrevem exatamente a mesma exigência de StatusNotifierItem. Trocar de framework não compra tray melhor no Linux.

## Decisão

Usaremos **Avalonia UI** para o ícone de bandeja e as janelas, no **mesmo processo** que hospeda os pipelines (`Polyglotta.Host`, ADR-0007). Não usaremos Electron.

- O `TrayIcon` é declarado no nível de `Application` e sobrevive a qualquer janela estar aberta ou fechada — é o que permite "fechar a janela" sem "fechar o app".
- O app sobe **sem janela** (`ShutdownMode.OnExplicitShutdown`); a janela de configuração é criada sob demanda pelo menu e apenas escondida ao fechar.
- As janelas falam com o host por injeção de dependência direta, não por IPC. Chamada de UI nunca bloqueia thread de áudio: os eventos do pipeline (`Transcript`, `Levels`, `Faulted`) chegam à UI por marshaling no dispatcher, e a UI nunca é chamada de dentro de um callback WASAPI.
- O projeto novo é `src/Polyglotta.Tray/`, dependendo de `Polyglotta.Host`. Nenhum código de UI entra no `Core` nem nos provedores.

A UI ser multiplataforma **não** torna o app multiplataforma: o que falta para Linux e macOS é a camada de áudio (`Polyglotta.Audio.Windows` é WASAPI puro) e um equivalente ao VB-Cable (BlackHole no macOS, *null sink* do PipeWire no Linux) — cada um com seu ADR, no seu tempo. A decisão aqui é só a de não criar um segundo porte para fazer depois.

## Alternativas consideradas

- **Electron (ou Tauri) para a UI + .NET para o mecanismo** — rejeitada. Paga um segundo runtime (Chromium + Node, ~150 MB), um segundo toolchain, uma ponte de IPC entre a UI e o host, e dois caminhos de atualização — tudo para desenhar um formulário de dez campos. Electron se paga quando a UI *é* o produto (layout rico, iteração de design, reaproveitamento de front-end web); aqui o produto é o pipeline de áudio e a UI é o painel de controle dele. E, como registrado acima, não resolve o tray do Linux, que é o único ponto onde o .NET realmente tem ressalva. Tauri cairia ainda no veto de linguagem do ADR-0001.
- **WinUI 3** — rejeitada. É a opção mais nativa no Windows e seria defensável se o projeto parasse aqui, mas é Windows-only: garantiria uma reescrita da UI no primeiro passo rumo a Linux/macOS, que é um objetivo declarado do PRD-001.
- **WinForms com `NotifyIcon`** — a alternativa honesta mais barata: para quatro itens de menu e um diálogo, é meia tarde de trabalho e zero dependência nova (já estamos em `net9.0-windows`). Rejeitada pelo mesmo motivo do WinUI — é descartável no primeiro porte — mas é o plano B natural caso o Avalonia traga algum atrito inesperado no Windows.
- **.NET MAUI** — rejeitada. Não tem ícone de bandeja e não suporta Linux; é um alvo de app, não de utilitário de desktop.
- **UI web servida pelo próprio host, aberta no navegador** — rejeitada como caminho principal. Resolve a configuração, mas não dá ícone de bandeja (que é metade do pedido) e levanta a questão de expor uma porta local. Continua interessante no futuro, se houver controle remoto — e o corte do ADR-0007 permite.

## Consequências

- Uma linguagem, um build, um processo, um instalador. `dotnet publish` continua sendo o ciclo de entrega.
- A tela de configuração passa a ser a dona do que hoje é linha de comando, inclusive das **guardas de realimentação** (fone = loopback, CABLE Input como fone, CABLE Output como mic). Validar isso num formulário, antes de ativar, é melhor que recusar no start — mas a validação do host continua valendo, porque o CLI permanece.
- No Linux, o ícone pode não aparecer em alguns ambientes (GNOME sem a extensão AppIndicator). Risco aceito e documentado: a saída é um fallback de janela principal quando o tray não inicializa. Não é problema hoje, já que não há camada de áudio para Linux.
- No macOS, o clique no ícone não dispara evento — todo o controle tem que estar no menu. Fica como restrição de design, não como coisa a descobrir depois.
- Perde-se o ecossistema de componentes web. Se um dia a UI crescer para algo com gráficos e layout denso, a decisão merece revisão.
