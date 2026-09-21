# ADR-0005 — NAudio 3.x com a API de builders (WasapiRecorder/WasapiPlayer)

**Status:** Aceito · **Data:** 2026-09-21

## Contexto

ADR-0004 fixou NAudio como biblioteca WASAPI de `Polyglotta.Audio.Windows`, mas não fixou a versão. O código do esqueleto foi escrito contra a API do NAudio 2.x (`WasapiCapture`, `WasapiLoopbackCapture`, `WasapiOut`, `BufferedWaveProvider.BufferDuration` gravável, `IWaveProvider.Read(byte[], int, int)`), enquanto o `.csproj` referencia **NAudio 3.1.0**, que é uma major com quebras:

- `IWaveProvider.Read` e `ISampleProvider.Read` passaram a receber `Span<T>`.
- `BufferedWaveProvider.BufferDuration` virou somente-leitura (parâmetro do construtor).
- `WasapiCapture`, `WasapiLoopbackCapture` e `WasapiOut` estão `[Obsolete]`, substituídos por `WasapiRecorderBuilder`/`WasapiRecorder` e `WasapiPlayerBuilder`/`WasapiPlayer`.
- O meta-pacote exige `net9.0` ou superior e só inclui `NAudio.Wasapi` em TFMs `-windows`.

Resultado: a solution não compila (ver `docs/review/2026-09-19-revisao-codigo-e-cobertura-prd.md`, B2). É preciso decidir entre voltar ao 2.x ou adotar o 3.x — e a decisão afeta diretamente o M2 (eco/feedback) do PRD-001.

## Decisão

Usaremos **NAudio 3.x (≥ 3.1.0)** e migraremos `Polyglotta.Audio.Windows` para a API de builders:

- Captura de microfone: `WasapiRecorderBuilder().WithDevice(...).WithCommunicationsMode().WithBufferLength(20)`.
- Loopback do sistema: `WasapiRecorderBuilder().WithDevice(renderDevice).WithLoopbackCapture()`.
- Render (VB-Cable e fone): `WasapiPlayerBuilder().WithDevice(...).WithSharedMode().WithEventSync().WithLatency(...)`.
- APIs `[Obsolete]` do 2.x não devem ser usadas; warnings CS0618 vindos do NAudio são tratados como erro no projeto de áudio.

Todo projeto que referencia `Polyglotta.Audio.Windows` (hoje só o CLI) passa a usar TFM `net9.0-windows`. O Core e o projeto Azure continuam `net9.0`.

## Alternativas consideradas

- **Fixar NAudio 2.2.1** — faz o código atual compilar com mudanças mínimas, mas fica numa linha sem evolução, com API baseada em `byte[]` (mais cópias no caminho de áudio) e sem os recursos abaixo. Adiar a migração só aumenta o custo quando o 2.x deixar de receber correções.
- **Trocar de biblioteca (miniaudio/PortAudio via bindings)** — já rejeitada no ADR-0004 para a v1: loopback WASAPI e dispositivos virtuais variam por plataforma; NAudio segue sendo a opção mais madura no Windows.
- **Manter as classes obsoletas do 3.x** (`WasapiCapture` etc. ainda existem) — compila hoje, mas acumula dívida garantida e os warnings mascaram problemas reais.

## Consequências

Positivas:
- `WithLoopbackCapture()` e `WithEchoCancellationReferenceEndpoint()` atacam diretamente o risco de eco do M2; `WithCommunicationsMode()` alinha captura com o dispositivo default de comunicação (o mesmo que o enumerador já usa).
- `WithBufferLength(ms)` permite pedir callbacks de ~20 ms ao driver, aproximando-se do frame canônico sem depender do buffer default de 100 ms.
- `Read(Span<T>)` e `AddSamples(ReadOnlySpan<byte>)` eliminam cópias `ToArray()` no caminho quente.
- `WasapiRecorder.CaptureAsync(ct)` (`IAsyncEnumerable`) é uma alternativa natural ao `DataAvailable` + `Channel`, se quisermos simplificar no futuro.

Negativas / riscos aceitos:
- Reescrever as quatro classes de `Polyglotta.Audio.Windows` (`CanonicalFormatConverter`, `WasapiMicCapture`, `WasapiLoopbackSystemCapture`, `WasapiRenderDevice`) — trabalho pequeno, mas exige roteiro de teste manual (skill `audio-realtime`).
- NAudio 3.x é recente: menos exemplos na web e nos manuais em `docs/man/naudio`; o guia oficial de migração (`Docs/MigratingFromNAudio2.md` no repositório do NAudio) passa a ser a referência.
- O CLI deixa de ser "portável" no TFM (`net9.0-windows`). É aceitável: o CLI da v0 é Windows-only por definição; a portabilidade real vem da separação Core/plataforma (ADR-0004).
