# ADR-0010 — Avalonia headless para testar a janela de configuração

**Status:** Proposto · **Data:** 2026-10-29

## Contexto

Em 2026-10-29 o primeiro uso real da bandeja terminou com o agente morrendo ao clicar em "Configurar". A causa foi um `InitializeComponent()` escrito à mão no code-behind: o gerador do Avalonia produz `InitializeComponent(bool loadXaml = true)`, que carrega o XAML **e** atribui os campos `x:Name`. A sobrecarga sem parâmetros ganhava a resolução, os campos ficavam todos `null`, e a primeira linha do construtor que tocava num deles levantava `NullReferenceException` dentro do handler do clique — derrubando o processo.

O que isso diz sobre a cobertura atual:

- O build passou **sem um único aviso**. As duas sobrecargas são C# legal.
- Os **134 testes passaram**. Nenhum instancia a janela; a classe inteira nunca foi executada por teste nenhum.
- O agente morreu **sem deixar linha no log**, porque a exceção escapou pela thread de UI (já corrigido com guardas nos handlers da bandeja, mas o guarda transforma a falha em log, não em falha pega antes de entregar).

A convenção vigente (CLAUDE.md, ADR-0004) divide o código em duas categorias: lógica portável, com teste automatizado obrigatório, e código de plataforma (WASAPI), testado manualmente com roteiro documentado. A UI não se encaixa em nenhuma das duas: ela não toca hardware, mas também não é lógica pura — é fiação entre XAML e code-behind, e é exatamente aí que o erro aconteceu.

Verificação feita para este ADR (2026-10-29):

- `Avalonia.Headless.XUnit` **12.1.4** existe no NuGet, na mesma versão do `Avalonia` que o `Polyglotta.Tray` usa.
- Ele é construído sobre **xunit v2**, que é o que o repositório já usa (`xunit` 2.9.3 em todos os projetos de teste). Não existe variante `XUnit3` publicada, então não há decisão de runner a tomar.
- A plataforma headless roda sem display: `[AvaloniaTest]` executa o corpo do teste na thread de UI, com uma `Application` real e janelas que podem ser abertas, sem compositor nem GPU.

## Decisão

Criaremos `tests/Polyglotta.Tray.Tests/` com **`Avalonia.Headless.XUnit` 12.1.4**, cobrindo a construção e a fiação da janela de configuração.

O alvo não é aparência — é comportamento verificável sem olhar:

- A janela **constrói** com a configuração salva e com uma configuração vazia (o caso de primeiro uso).
- Os campos `x:Name` que o code-behind usa estão **preenchidos** — o teste que teria pego o bug de hoje.
- As combos de dispositivo são populadas a partir do `IAudioPlatform` e a seleção salva é reencontrada por nome e por id.
- Trocar o provedor para `gemini` desabilita as vozes e mostra a nota do ADR-0006.
- "Validar" com dispositivos conflitantes (fone = loopback) mostra a pendência vinda do `SessionPlanner`.
- O status da credencial diz "variável de ambiente" quando ela vem do ambiente, e nunca imprime o segredo inteiro.

Para isso, `ConfigWindow` deixa de depender da classe concreta `App` e passa a depender de uma **interface do host** (os membros que ela já usa: `Transcripts`, `SettingsPath`, `Settings`, `ListDevices()`, `Credentials`, `Vault`, `Session`, `ActivateAsync()`, `DeactivateAsync()`). Isso não é preferência de estilo: a plataforma headless não oferece `IClassicDesktopStyleApplicationLifetime`, que é justamente o `if` dentro do qual o `App` inicializa tudo hoje — sem a interface, o teste receberia um `App` com todos os campos nulos e mediria o próprio bug ao contrário.

O `DubbingSession` já é testável assim: ele recebe `IAudioPlatform` e `ICredentialStore`, e `Polyglotta.Host.Tests` já tem uma implementação falsa dos dois.

Isso **não** revoga a divisão do ADR-0004: a camada de áudio continua sendo teste manual. A UI passa a ser uma terceira categoria — testada headless no que é fiação e lógica, verificada à mão no que é aparência e no que depende do shell do sistema (ícone na bandeja, menu nativo, tarefa agendada).

## Alternativas consideradas

- **Continuar só com teste manual, e tratar o de hoje como azar** — rejeitada. Foi a política vigente e ela falhou do pior jeito possível: eu entreguei a janela dizendo "build limpo, 134 testes passando", e ela não abria uma única vez. O erro não era de lógica difícil, era de fiação trivial, e sobreviveu a todas as verificações que eu tinha. Um agente residente piora o custo disso: não há console na frente do usuário mostrando a pilha.
- **Extrair tudo para um ViewModel e testar sem UI nenhuma (MVVM)** — rejeitada como solução deste problema, embora seja boa prática em geral. Pegaria a lógica de validação e de credencial, mas **não** pegaria o bug de hoje: campos `x:Name` nulos são precisamente a fronteira que um ViewModel deixa de fora. Além disso, o ADR-0008 decidiu code-behind direto para um formulário de dez campos, e trocar isso por MVVM é uma reescrita que não compra a cobertura que estamos procurando.
- **Teste de UI de ponta a ponta (FlaUI/WinAppDriver/Appium)** — rejeitada. Dirige a janela real pela árvore de acessibilidade, então pegaria o bug — mas exige sessão interativa com desktop destravado, é lento, é notoriamente instável, e não roda no mesmo `dotnet test` dos outros. Custo alto para cobrir uma janela de dez campos.
- **Proibir por análise estática que o code-behind declare `InitializeComponent`** — não é alternativa, é complemento barato. O caso específico de hoje virou comentário no construtor explicando por que a sobrecarga não deve existir; uma regra de analisador cobriria só esta armadilha, enquanto o teste headless cobre a classe.

## Consequências

- Uma dependência nova, **só em projeto de teste**. Nada entra em `Polyglotta.Tray` nem em produção.
- `Avalonia.Headless.XUnit` fica **amarrado à versão do Avalonia**: subir o Avalonia passa a exigir subir o pacote de teste na mesma leva, senão o `dotnet test` quebra. É um acoplamento aceito e previsível.
- O headless **não renderiza nada de verdade**: ele não pega layout quebrado, texto cortado, contraste ruim nem ícone errado. A verificação visual continua sendo manual, e o ADR não deve ser lido como "a UI está testada".
- `tests/Polyglotta.Tray.Tests/` é `net9.0-windows`, porque `Polyglotta.Tray` é. Qualquer CI que rode esses testes tem que ser Windows — o que já valia para `Polyglotta.Audio.Windows.Tests`.
- A interface do host é ganho de desenho que vem de graça: hoje `ConfigWindow` conhece a classe `App` inteira, inclusive o que não usa. Depois dela, a janela declara exatamente o que precisa do agente — e fica possível abrir a mesma janela de outro hospedeiro (um modo de diagnóstico do CLI, por exemplo) sem arrastar a bandeja.
- O custo é modesto e pontual: um projeto de teste, uma interface e a troca de um parâmetro de construtor. Não há migração de código existente.
