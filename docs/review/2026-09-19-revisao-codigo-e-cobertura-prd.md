# Revisão de código e cobertura do PRD/ADRs — 2026-09-19

**Contexto:** revisão completa do código (~740 linhas C#) + análise de aderência ao PRD-001 e ADRs 0001–0004. Gerada por Claude Code a pedido do time. Nenhuma correção foi aplicada ainda — este documento é o ponto de retomada.

## Progresso

- **2026-09-21** — ADR-0005 aceito; migração para NAudio 3.x concluída. Resolvidos: **B1, B2, B3, C3, C4** (captura pede o formato canônico ao engine, `CanonicalFramer` fatia em 20 ms, default = `Role.Communications`). Novo projeto `tests/Polyglotta.Audio.Windows.Tests` com regressão do B3. M0 atingido.
- **2026-09-21** — **C1** resolvido: CLI recusa iniciar se fone == loopback ou se "CABLE Input" for usado como fone/loopback; novo flag `--loopback`; ids inválidos e variáveis de ambiente ausentes dão erro amigável. Pendentes: C2, C5, C6, menores, docs.
- **2026-09-21** — **C2, C5, C6** resolvidos no Core: `LatencyTracker` (fim da frase → primeira síntese, p95 nearest-rank); `DubbingPipeline` com handlers assinados uma vez, `StopAsync(ct)` idempotente que tenta todos os estágios (AggregateException), restart permitido, `Faulted` exposto (CLI encerra com código 2); `WasapiRecorderCapture` reconstrói o recorder a cada start (NAudio 3.1.0 não reinicia um `WasapiRecorder`) e só fecha o channel em erro; `AzureSpeechTranslator` Stop idempotente sem vazar recognizer. 20 testes no Core (+16). Pendentes: menores (Task.Delay em teste, M.E.Logging 10.x), docs (`Dubbing.*`, ADR-0001 filename), RF-07.
- **2026-09-21** — docs: ADR-0004 com nomes `Polyglotta.*`, ADR-0001 renomeado para `…dotnet9.md`, nome do produto unificado. Itens menores restantes: `PushAudio` faz `ToArray()` porque o SDK Azure só aceita `byte[]` (não há o que fazer); pacotes M.E.Logging 10.x em net9.0 funcionam, mantidos. **Restante: RF-07 (reconexão) e teste M1 com Azure.**
- **Ponto de parada 2026-09-21** — aguardando chaves do Azure para o teste M1 (roteiro: `--list-devices` → `--loopback "<id Meet>" --phones "<id fone>"` → falar PT → observar `final:` e `latency` → Ctrl+C mostra p95). Depois: RF-07 como supervisor acima do `DubbingPipeline` (em `Faulted`: Stop → backoff → Start, com limite de tentativas), atualizando RF-07 no PRD no mesmo commit.
- **2026-09-22** — primeira execução real do M1 falhou por `AZURE_SPEECH_REGION` com aspas literais (`set VAR="eastus"` no cmd) → DNS inválido. Dispositivos e guardas estavam corretos, e o encerramento por `Faulted` funcionou como projetado. Adicionado `EnvironmentSettings` no Core (trim + remove aspas externas), usado pelo CLI e pelo `AzureSpeechTranslator`; CLI loga a região no startup; README documenta a armadilha. 36 testes no Core.
- **2026-09-22** — segunda execução do M1: conectou ao Azure, mas todos os finais vieram vazios. Causa: o microfone padrão de comunicação (`Microphone (High Definition Audio Device)`) captura só ruído de fundo (pico RMS 11/32767) — o mic USB que existia em 09-21 foi desconectado. Adicionados: `AudioLevelMeter` no Core (relatório de nível a cada 5 s, aviso quando a entrada é silêncio), flag `--mic` no CLI com guarda contra usar "CABLE Output", e supressão do log de finais vazios. 41 testes no Core. **Teste M1 ainda pendente: depende de conectar um microfone.**
- **2026-09-22** — terceira execução: avisos de "entrada sem sinal" nos dois pipelines, confirmando os diagnósticos. Enumeração completa mostrou `Microphone (2- USB Audio)` em estado **Disabled** no Windows (por isso ausente de `--list-devices`) e o onboard ativo porém morto (pico 2/32767). `--mic/--loopback/--phones` passaram a aceitar trecho do nome além do id, com erro explícito quando ambíguo. O mic desabilitado era o da câmera (desabilitado de propósito pelo usuário); o microfone alvo é o onboard `Microphone (High Definition Audio Device)`, que segue mudo — havia OBS em execução durante os testes anteriores. Adicionado `--check-audio [segundos]`: mede mic e loopback com barra de nível e toca tom nas saídas, sem exigir credenciais Azure.
- **2026-09-22 — M1 CONCLUÍDO.** Causa raiz do silêncio: o microfone em uso era o onboard sem nada conectado; com o mic USB (`Microphone (2- USB PnP Sound Device)`) o pipeline outbound funcionou ponta a ponta. 6 frases PT→EN, latência 0,85–1,46 s, **p95 1,46 s** (meta ≤ 3 s). RF-02, RF-04, RF-06 e o RNF de latência verificados em uso real. **Próximo: M2 (inbound) — exige áudio tocando no dispositivo de loopback; depois RF-07.**
- **2026-09-22** — feedback do primeiro uso real: vozes soavam mecânicas (eram `JennyNeural`/`FranciscaNeural`, neurais de 1a geração). Adicionado `AzureVoices`: apelidos `male`/`female` por locale resolvidos para vozes **HD** (`DragonHD` para en-US, `DragonHDOmni` para pt-BR, que não tem DragonHD), passagem de nomes completos, e `--list-voices` consultando o catálogo do recurso. Novo projeto `tests/Polyglotta.Translation.Azure.Tests` (16 testes). Padrão mudou para voz HD feminina. O plano F0 não afeta qualidade de voz (limita cota/concorrência).
- **2026-09-22** — `--list-voices` no recurso real desmentiu a documentação: **pt-BR tem DragonHD** (`pt-BR-Thalita`, `pt-BR-Macerio`), e os nomes Omni que eu havia deduzido da doc (`pt-BR-Francisca:DragonHDOmniLatestNeural`, `pt-BR-Antonio:…`) **não existem** — teriam falhado em runtime. A tabela fixa foi substituída por seleção a partir do catálogo (`AzureVoices.Select`, ranking HD > Omni > Flash > Dragon > Multilingual, com penalidade para `-Preview` e `Multitalker`), com fallback para padrões verificados quando a consulta falha. 27 testes no projeto Azure, usando um recorte do catálogo real como fixture.
- **2026-10-28** — decidida a forma de execução. O pedido era "serviço/daemon + bandeja"; levantamento mostrou que serviço do Windows roda na **sessão 0**, isolada da sessão interativa, onde os endpoints de áudio do usuário não existem (mesma razão no macOS, com TCC, e no Linux, com PipeWire por sessão). Aceitos **ADR-0007** (agente da sessão do usuário iniciado no logon, com bandeja), **ADR-0008** (Avalonia, em processo único; Electron recusado — e o tray do Linux depende de StatusNotifierItem nos dois) e **ADR-0009** (credenciais cifradas pelo SO, com a variável de ambiente mantendo precedência). PRD-001 ganhou RF-08/RF-09 e teve o RNF de segurança revisado; M4 redefinido.
- **2026-10-28** — implementação da fundação: novo `src/Polyglotta.Host/` (neutro de plataforma) com `PolyglottaSettings`, `SettingsStore` (JSON por usuário), `SessionPlanner` (resolução de dispositivos por nome→id + guardas de realimentação, lógica pura) e `DubbingSession` (ativar/desativar idempotente, estado, transcrição, falha). Novo `IAudioPlatform` no Core com `WindowsAudioPlatform` em `Audio.Windows`. Novo `src/Polyglotta.Security.Windows/` com `DpapiCredentialStore`; `ICredentialStore`/`EnvironmentCredentialStore`/`ChainedCredentialStore` no Core; os dois tradutores ganharam `FromCredentials`. O CLI virou frontend do Host, mantendo **todos os flags antigos** e ganhando `--show-config`, `--save-config`, `--set-credential`, `--forget-credential` e `--help`.
- **2026-10-28** — novo `src/Polyglotta.Tray/` (Avalonia 12.1.4): ícone de bandeja com Ativar/Desativar/Configurar/Fechar, janela de configuração (dispositivos, idiomas, vozes, credenciais, início automático), transcrição ao vivo, p95 no rodapé, log em arquivo (`%APPDATA%\Polyglotta\logs`) e início no logon via tarefa agendada. Sobe **desativado** por padrão. Verificado: build limpo, app sobe sem console e registra no log; **134 testes passando** (52 Core + 17 Host + 12 Security + 27 Azure + 20 Gemini + 6 Audio). Não verificado ainda: a bandeja numa sessão real de uso, o início automático (depende do primeiro clique do usuário) e o Gemini contra o serviço.

## Estado verificado (2026-09-19 — histórico, já corrigido)

- `dotnet build src/Polyglotta.sln` → **falha no restore** (NU1201).
- `Polyglotta.Audio.Windows` isolado → **3 erros de compilação** + 8 warnings de API obsoleta (NAudio 3.1.0).
- `Polyglotta.Core` e `Polyglotta.Translation.Azure` compilam.
- `dotnet test` (Core) → 4/4 passam.
- O diretório **não é um repositório git** (CLAUDE.md fala em commits convencionais, mas não há histórico).
- SDK instalado: .NET 10.0.401; projetos em `net9.0`; pacotes `Microsoft.Extensions.Logging.*` 10.0.12.
- NAudio 2.2.1 e 3.1.0 estão no cache NuGet (alguém já tentou o 2.2.1).

## Bloqueadores (impedem compilar/rodar)

**B1. TFM incompatível no CLI** — `src/Polyglotta.Cli/Polyglotta.Cli.csproj:5` usa `net9.0`, mas referencia `Polyglotta.Audio.Windows` (`net9.0-windows`). Fix: CLI → `net9.0-windows`.

**B2. NAudio 3.1.0 quebrou a API usada** (o "problema de versionamento" citado no README):
- `BufferedWaveProvider.BufferDuration` virou read-only; agora é parâmetro do construtor `new BufferedWaveProvider(fmt, TimeSpan)` — `CanonicalFormatConverter.cs:22`, `WasapiRenderDevice.cs:25`.
- `IWaveProvider.Read(byte[], int, int)` virou `Read(Span<byte>)` — `CanonicalFormatConverter.cs:40`.
- `WasapiCapture`, `WasapiLoopbackCapture`, `WasapiOut` estão `[Obsolete]`, substituídos por `WasapiRecorderBuilder` / `WasapiPlayerBuilder`.

Caminhos: fixar `NAudio 2.2.1` ou migrar para 3.x. **Recomendação: migrar para 3.x.** A API nova oferece `WithLoopbackCapture()`, `WithCommunicationsMode()`, `WithEchoCancellationReferenceEndpoint()`, `WithBufferLength()` e `CaptureAsync(ct)` (IAsyncEnumerable) — úteis para o eco do M2 e para frames de 20 ms. Troca de major de dependência → **ADR-0005** (ou adendo ao ADR-0004).

**B3. Loop infinito na thread de áudio** — `CanonicalFormatConverter.cs:35-46`. `BufferedWaveProvider.ReadFully` é `true` por padrão: `Read` sempre preenche o buffer com zeros quando vazio, então `read < _readBuffer.Length` nunca é verdadeiro e o `while` nunca termina. No primeiro `DataAvailable` a thread trava e o `MemoryStream` cresce até OOM. Fix: `ReadFully = false`. Evidência de que o pipeline de áudio nunca rodou.

## Bugs de comportamento

**C1. Feedback loop por padrão** — `Program.cs:42-45,59`. Sem `--phones`, o inbound toca no render default e `new WasapiLoopbackSystemCapture()` captura o loopback do mesmo dispositivo. A tradução PT é recapturada e retraduzida indefinidamente. O CLI deve recusar iniciar se fone == dispositivo de loopback.

**C2. Métrica de latência não mede nada** — `DubbingPipeline.cs:49-51,71`. `_lastSpeechAt` é atualizado com todo frame (inclusive silêncio); como a captura é contínua, a latência logada é ≈0. PRD exige "fim da frase → síntese (p95)". Precisa de VAD por energia ou usar offset/duration do resultado `Recognized` do Azure.

**C3. Frames não têm 20 ms** — o converter emite um frame por callback do driver (~100 ms). Skill: "não dependa do tamanho do buffer do driver". Fatiar em `BytesPer20Ms` (640 B).

**C4. Dispositivo default inconsistente** — `WindowsAudioDeviceEnumerator.cs:31` usa `Role.Communications`; `new WasapiCapture()` / `WasapiLoopbackCapture()` usam `Role.Console`. O `*` no `--list-devices` pode não ser o dispositivo realmente usado.

**C5. Stop/Dispose não idempotentes** — `Program.cs:82-83` chama `StopAsync()` e o `await using` chama de novo via `DisposeAsync()`. `StopRecording()` duas vezes; `StopContinuousRecognitionAsync()` após `_push.Close()`. Se `_pumpTask` falhar com exceção não-cancelamento, `StopAsync` propaga e os devices ficam abertos. `StopAsync()` não recebe `CancellationToken` (convenção do CLAUDE.md).

**C6. Dispositivos não reiniciáveis** — `WasapiMicCapture.cs:37` e loopback completam o `Channel` em `RecordingStopped`; `StartAsync` depois disso não entrega frames. `DubbingPipeline.StartAsync` re-subscreve eventos sem remover. Ambos quebram RF-07.

## Menores

- `WasapiRenderDevice.Enqueue` e `AzureSpeechTranslator.PushAudio` fazem `ToArray()` por frame; NAudio 3 tem `AddSamples(ReadOnlySpan<byte>)`.
- `CanonicalFormatConverter.Convert` aloca `MemoryStream` + `byte[]` a cada callback.
- `Program.cs:44` — `.First(d => d.Id == phonesId)` lança exceção genérica; `Console.KeyAvailable` lança com stdin redirecionado.
- Teste `MutedPipeline_DropsFrames` depende de `Task.Delay(100)`.
- Sem testes para `Faulted`, idempotência de Stop, métrica de latência.
- Pacotes M.E.Logging 10.x em projetos net9.0 — decidir: ficar no 9 (ADR-0001) ou subir para 10.

## Inconsistências de documentação

- `ADR-0001-linguagem-csharp-dotnet8.md` — nome diz .NET 8, conteúdo diz .NET 9.
- ADR-0004 e skill `audio-realtime` referem `Dubbing.Core` / `Dubbing.Audio.*`; projetos reais são `Polyglotta.*`.
- README chama o produto "Realtime Dubbing" enquanto solução/projetos são `Polyglotta` (unificado em 2026-09-21). ~~Cita `.claude/commands/` para `/new-adr`~~ — correto, o comando existe lá; erro da revisão.

## Cobertura do PRD-001

| ID | Requisito | Status |
|----|-----------|--------|
| RF-01 | Listar dispositivos | ⚠️ Código pronto, não executável (B1/B2, C4) |
| RF-02 | Outbound mic → mic virtual | ⚠️ Código pronto, nunca rodou (B3) |
| RF-03 | Inbound loopback → fone | ❌ Feedback loop na config padrão (C1) |
| RF-04 | Par de idiomas + voz | ✅ v0 (`--from/--to/--voice/--voice-in`) |
| RF-05 | Mute outbound | ✅ implementado e testado |
| RF-06 | Transcrição no log | ✅ v0 |
| RF-07 | Reconexão automática | ❌ não implementado (C5/C6 são pré-requisitos) |

| RNF | Status |
|-----|--------|
| Latência ≤ 3 s p95 medida e logada | ❌ métrica inválida (C2) |
| Estabilidade 60 min | ❌ não verificável; B3 causaria OOM |
| CPU leve | ➖ não avaliável |
| Chaves via env; sem áudio persistido | ✅ |

| Marco | Status |
|-------|--------|
| M0 — Esqueleto compilando | ❌ **não atingido** |
| M1 — Outbound | ❌ |
| M2 — Inbound | ❌ |
| M3 — Sessão completa | ❌ (mute ✅, métricas ❌) |
| M4 — UI mínima | ❌ (exige ADR) |

## ADRs

| ADR | Aderência |
|-----|-----------|
| 0001 C#/.NET 9 | ✅ (nome do arquivo errado) |
| 0002 VB-Cable | ✅ busca "CABLE Input" e falha com instrução |
| 0003 Azure Speech | ✅ `TranslationRecognizer` + Raw16Khz16BitMonoPcm |
| 0004 Abstração de áudio | ✅ Core sem NAudio/Azure (verificado nos .csproj); nomes `Dubbing.*` desatualizados |
| Faltante | ADR-0005: NAudio 3.x (builder API) |
| Faltante | ADR para UI (M4) |

## Ordem de trabalho sugerida

1. `git init` + commit do estado atual (baseline).
2. ADR-0005: NAudio 3.x.
3. Destravar o build: B1 + B2 (migrar para builders) + B3 (`ReadFully = false`), já fatiando em 20 ms (C3).
4. C1 (guarda loopback ≠ fone) e C4 (default `Communications`) — pré-requisitos do M2.
5. C2 (latência real) + C5/C6 (stop idempotente, restart) com testes no Core.
6. Marcar M0 concluído; testar M1 manualmente com VB-Cable.
7. Corrigir docs (`Dubbing.*` → `Polyglotta.*`, ADR-0001 filename, README).
