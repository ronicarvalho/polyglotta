# Revisão de código e cobertura do PRD/ADRs — 2026-09-19

**Contexto:** revisão completa do código (~740 linhas C#) + análise de aderência ao PRD-001 e ADRs 0001–0004. Gerada por Claude Code a pedido do time. Nenhuma correção foi aplicada ainda — este documento é o ponto de retomada.

## Progresso

- **2026-09-21** — ADR-0005 aceito; migração para NAudio 3.x concluída. Resolvidos: **B1, B2, B3, C3, C4** (captura pede o formato canônico ao engine, `CanonicalFramer` fatia em 20 ms, default = `Role.Communications`). Novo projeto `tests/Polyglotta.Audio.Windows.Tests` com regressão do B3. M0 atingido.
- **2026-09-21** — **C1** resolvido: CLI recusa iniciar se fone == loopback ou se "CABLE Input" for usado como fone/loopback; novo flag `--loopback`; ids inválidos e variáveis de ambiente ausentes dão erro amigável. Pendentes: C2, C5, C6, menores, docs.
- **2026-09-21** — **C2, C5, C6** resolvidos no Core: `LatencyTracker` (fim da frase → primeira síntese, p95 nearest-rank); `DubbingPipeline` com handlers assinados uma vez, `StopAsync(ct)` idempotente que tenta todos os estágios (AggregateException), restart permitido, `Faulted` exposto (CLI encerra com código 2); `WasapiRecorderCapture` reconstrói o recorder a cada start (NAudio 3.1.0 não reinicia um `WasapiRecorder`) e só fecha o channel em erro; `AzureSpeechTranslator` Stop idempotente sem vazar recognizer. 20 testes no Core (+16). Pendentes: menores (Task.Delay em teste, M.E.Logging 10.x), docs (`Dubbing.*`, ADR-0001 filename), RF-07.

## Estado verificado

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
- README chama o produto "Realtime Dubbing" e cita `.claude/commands/` para `/new-adr` (é skill).

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
