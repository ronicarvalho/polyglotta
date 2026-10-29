# ADR-0007 — Execução contínua como agente da sessão do usuário (bandeja), não como serviço do sistema

**Status:** Aceito · **Data:** 2026-10-28

## Contexto

Hoje o Polyglotta é um CLI: o usuário monta uma linha de comando com provedor, dispositivos, idiomas e vozes, e a dublagem vive enquanto o console estiver aberto. O pedido é transformar isso em algo que fique residente, com um ícone na bandeja oferecendo **ativar**, **desativar**, **configurar** e **fechar** — e que o mesmo desenho sirva para Linux e macOS depois.

O pedido usa a palavra "serviço / daemon". Existe uma restrição de plataforma que contraria a leitura literal dela:

- **Windows:** desde o Vista, serviços rodam na **sessão 0**, isolada da sessão interativa do usuário. A documentação da Microsoft descreve serviços da sessão 0 como não-interativos e sem acesso aos recursos interativos do usuário. O stack de áudio do Windows é por sessão: os *endpoints* que o usuário vê (o headset USB, o CABLE Input, o monitor HDMI) pertencem à sessão interativa. Um serviço na sessão 0 não é o lugar de onde se captura microfone nem de onde se toca áudio.
- **macOS:** a permissão de microfone (TCC) é concedida a um app com contexto de usuário. Um `LaunchDaemon` (nível de sistema) não tem a quem pedir o consentimento; um `LaunchAgent` (por usuário, iniciado no login) tem.
- **Linux:** PipeWire/PulseAudio sobem por sessão de usuário. O equivalente correto é uma unidade `systemd --user`, não `systemd` de sistema.

Ou seja: nos três sistemas, o processo que fala com o áudio precisa viver **na sessão do usuário**. "Daemon" no sentido de "roda em segundo plano, sem console, sobe sozinho" continua valendo e é o que se quer; "serviço do sistema" no sentido técnico não serve.

Há ainda razões de produto que apontam para o mesmo lado: a configuração (dispositivos, idiomas, vozes, chaves) é por usuário, e o usuário precisa ver o que está acontecendo — nível de áudio, transcrição, latência.

## Decisão

O Polyglotta passará a ser um **agente de segundo plano da sessão do usuário**, iniciado no logon, sem janela de console, com ícone na bandeja. Não será um serviço do Windows nem um `LaunchDaemon`.

Autostart por plataforma:

| Plataforma | Mecanismo |
|---|---|
| Windows | Tarefa Agendada com gatilho "ao fazer logon" (preferida à chave `Run`: sobrevive a reinício, permite atraso e reexecução) |
| macOS | `LaunchAgent` em `~/Library/LaunchAgents` |
| Linux | unidade `systemd --user` com `WantedBy=default.target` |

O ciclo de vida fica assim, e é exatamente o que a bandeja expõe:

- **Ativar** — monta os dois pipelines com a configuração salva e dá `StartAsync`. É o equivalente exato à linha de comando que rodamos hoje.
- **Desativar** — `StopAsync` idempotente (já existe em `DubbingPipeline`); o processo continua vivo, os dispositivos são liberados, nada é capturado nem enviado para a nuvem.
- **Configurar** — abre a janela de configuração (ADR-0008).
- **Fechar** — `StopAsync` e encerra o processo. Só volta no próximo logon ou manualmente.

O estado inicial ao subir no logon é **desativado**, com a opção "ativar ao iniciar" na configuração. Microfone aberto sem pedir é o tipo de comportamento que queima a confiança no app.

Para isso, a orquestração que hoje mora no `Program.cs` do CLI sobe para um projeto novo `src/Polyglotta.Host/`, como `IHostedService` do Generic Host: resolução de dispositivos, guardas de realimentação, escolha de provedor, montagem dos pipelines e métricas. O CLI e a bandeja viram dois *frontends* finos do mesmo host — o CLI continua existindo, porque é o que permite diagnosticar sem UI (`--list-devices`, `--check-audio`, `--list-voices`).

A configuração passa a ser persistida em arquivo JSON por usuário:

| Plataforma | Caminho |
|---|---|
| Windows | `%APPDATA%\Polyglotta\config.json` |
| macOS | `~/Library/Application Support/Polyglotta/config.json` |
| Linux | `$XDG_CONFIG_HOME/polyglotta/config.json` |

Os dispositivos são gravados **por nome e por id**, com o nome como critério primário na hora de resolver. Já perdemos um id de dispositivo numa troca de hardware e outro numa atualização do Windows; o nome sobreviveu às duas.

## Alternativas consideradas

- **Serviço do Windows de verdade (`sc.exe` / `ServiceBase`) com um app de bandeja conversando por IPC** — rejeitada. É a leitura literal do pedido, mas coloca a captura e a reprodução de áudio no lado errado da fronteira da sessão 0, e paga um protocolo de IPC para resolver um problema criado por ela mesma. Se um dia existir um motivo real para separar (p.ex. uma UI web, ou dublagem seguindo troca de usuário), a fronteira volta — e o `Polyglotta.Host` já é o ponto de corte.
- **Manter só o CLI e documentar um atalho/.bat na inicialização** — rejeitada. Resolve o "sobe sozinho" e nada mais: não dá ativar/desativar sem matar processo, não tem tela de configuração, e a linha de comando continua sendo montada à mão (foi, literalmente, a dor que originou este pedido).
- **Subir já com dois processos, host e UI, separados desde o começo** — rejeitada por ora. Para um usuário numa máquina, o ganho (UI pode cair sem parar a dublagem) não paga o custo (protocolo, versionamento entre os dois lados, dois instaladores). Revisitar se aparecer um segundo *frontend*.

## Consequências

- O que o app faz **não muda**: a bandeja aciona o mesmo pipeline, com os mesmos dispositivos. Nenhuma mudança no `Polyglotta.Core` (ADR-0004 segue intacto).
- O CLI deixa de ser o dono da orquestração e vira cliente do `Polyglotta.Host`. É refatoração com rede de proteção: os 94 testes atuais cobrem o que está sendo movido.
- "Serviço do Windows" sai do vocabulário do projeto. Vale corrigir onde a expectativa já foi registrada, para não voltar daqui a seis meses.
- Fica mais difícil rodar sem ninguém logado — por construção, e é o preço de ter áudio.
- O RF-07 (reconexão automática) passa a ser mais visível: um app residente que falha em silêncio é pior que um CLI que morre na cara do usuário. O supervisor de reconexão deve entrar junto, e a bandeja precisa de um estado de erro.

## Pontos resolvidos na aceitação (2026-10-28)

1. **Onde ficam as chaves.** Decidido guardar as credenciais salvas pela aplicação, em vez de exigir variável de ambiente. O RNF de segurança do PRD-001 muda; o mecanismo está no **ADR-0009**.
2. **Serviço vs. sessão do usuário.** Decidido: *background worker* na sessão do usuário logado. O probe da sessão 0 fica dispensado — a pergunta original era justamente qual dos dois modelos se aplicava, e o raciocínio de isolamento responde sem custo de verificação.
