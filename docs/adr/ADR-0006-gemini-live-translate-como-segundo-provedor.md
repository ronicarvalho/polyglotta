# ADR-0006 — Gemini Live (modo tradução) como segundo provedor de `ISpeechTranslator`

**Status:** Aceito · **Data:** 2026-10-18

## Contexto

O ADR-0003 escolheu o Azure Speech Translation como provedor da v1 e previu explicitamente manter uma segunda implementação de `ISpeechTranslator` para benchmark de latência e custo (na época, a candidata era a OpenAI Realtime API).

O M1 foi concluído em 2026-09-22 com o Azure: p95 de **1,46 s** (meta do PRD-001 é ≤ 3 s). O interlocutor, porém, relatou que a voz soava **mecânica, "tipo uma IA"**. Isso tem duas causas possíveis, e elas não foram separadas ainda:

1. As vozes em uso eram `en-US-JennyNeural` e `pt-BR-FranciscaNeural`, neurais de primeira geração. Em 2026-09-22 o padrão passou a ser voz HD (`DragonHD`), **mas essa mudança nunca foi ouvida numa chamada real**.
2. A arquitetura do Azure é STT → tradução → TTS: a entonação da fala original se perde no texto intermediário e a síntese recria uma prosódia neutra.

A hipótese levantada é que um modelo **speech-to-speech nativo** preserva entonação, ritmo e ênfase, soando como interpretação simultânea em vez de narração.

Levantamento feito para este ADR (2026-10-18):

- A Gemini Live API tem um **modo de tradução dedicado**: modelo `gemini-3.5-live-translate-preview` com `translationConfig` (`targetLanguageCode`, `echoTargetLanguage`). Não é tradução por *system instruction* — nesse modo, instruções e ferramentas ficam indisponíveis.
- Formato de entrada: **PCM 16-bit, 16 kHz, mono, little-endian** — idêntico ao formato canônico do pipeline (ADR-0003/ADR-0004). Saída: mesmo formato a **24 kHz**.
- Transcrições de entrada e de saída disponíveis (`inputAudioTranscription` / `outputAudioTranscription`), o que preserva o RF-06.
- Existe SDK .NET oficial: **`Google.GenAI` 1.26.1**. Verificado por reflexão que expõe `Client.Live.ConnectAsync(model, LiveConnectConfig, ct)`, `LiveConnectConfig.TranslationConfig`, `AsyncSession.SendRealtimeInputAsync(...)`, `AsyncSession.ReceiveAsync(ct)`, `LiveServerContent.InputTranscription/OutputTranscription` e `Blob.MimeType`. Ou seja, não é preciso implementar o WebSocket na mão.

Dois pontos do material que motivou a proposta não se confirmam na documentação e precisam ficar registrados:

- **Latência "de milissegundos"**: a documentação do modo tradução não publica número nenhum. Interrupção (*barge-in*) é descrita para o modo *Live Agent*, não para o modo tradução. A comparação honesta é contra o p95 de 1,46 s que já medimos.
- **Escolha de voz**: o modo tradução **não permite selecionar a voz**. A documentação ainda alerta que a replicação de voz é inconsistente — a voz pode mudar após pausas longas ou se fixar numa só em trocas rápidas entre interlocutores. Isso conflita diretamente com o RF-04 e com o pedido explícito do usuário, atendido em 2026-09-22, de escolher voz masculina ou feminina.

## Decisão

Implementaremos o Gemini Live em modo tradução como **segunda implementação de `ISpeechTranslator`**, num projeto novo `src/Polyglotta.Translation.Gemini/`, mantendo o Azure como padrão. O CLI ganha `--provider azure|gemini` para alternar.

Detalhes que a implementação deve respeitar:

- `Polyglotta.Core` permanece intocado: a troca de provedor é injeção de outra implementação da mesma interface (ADR-0004).
- O áudio sintetizado volta a 24 kHz e precisa ser convertido para o formato canônico de 16 kHz **dentro do projeto do provedor**, na borda, usando `NAudio.Core` (que é multiplataforma e não arrasta dependência de Windows). O Core continua sem referência a NAudio.
- Autenticação por `GEMINI_API_KEY` (Google AI) ou por projeto/região com credencial do Google Cloud (Vertex AI), lidas via `EnvironmentSettings`.
- O objetivo da primeira entrega é **comparação**, não substituição: medir com o `LatencyTracker` já existente e ouvir as duas saídas na mesma máquina.

A decisão de qual provedor fica como padrão na v1 será tomada **depois** da comparação, num ADR que substitua ou complemente o ADR-0003.

## Alternativas consideradas

- **Substituir o Azure de imediato** — rejeitada. O modelo de tradução do Gemini está em *preview* (sem SLA, sujeito a mudança ou remoção), não deixa escolher voz, e o Azure já entrega o M1 dentro da meta. Trocar antes de comparar jogaria fora um caminho que funciona.
- **Manter só o Azure e resolver a voz mecânica com vozes HD** — é a alternativa mais barata e ainda **não foi testada**: o padrão já mudou para `DragonHD`, só falta ouvir numa chamada. Não é excludente: continua valendo como a primeira coisa a fazer, e vira a linha de base da comparação. Rejeitada apenas como resposta única, porque não endereça a perda de prosódia inerente ao STT → TTS.
- **OpenAI Realtime API** — candidata original do ADR-0003, também speech-to-speech. Fica para depois: diferente do Gemini, não tem modo de tradução dedicado (seria por *system instruction*, com risco de o modelo responder em vez de traduzir).
- **Pipeline próprio (Whisper + LLM + TTS)** — já rejeitado no ADR-0003 por latência e número de partes móveis. Nada mudou.

## Consequências

Positivas:

- O formato de entrada coincide exatamente com o formato canônico do pipeline: nenhuma conversão na ida.
- Modo de tradução dedicado elimina o risco de o modelo "conversar" em vez de traduzir.
- Transcrições de entrada e saída preservam o RF-06 sem trabalho extra.
- Passamos a ter dois provedores atrás da mesma interface, que é exatamente o que o ADR-0003 previu — a arquitetura é exercitada de verdade pela primeira vez.

Negativas e riscos aceitos:

- **Perda de controle de voz** (RF-04): no modo tradução não se escolhe gênero nem timbre, e a voz pode variar durante a sessão. Se a comparação favorecer o Gemini, isso vira uma mudança de escopo a registrar no PRD-001.
- **Preview**: modelo sem SLA, sujeito a mudanças sem aviso. Aceitável para comparação; não aceitável como único provedor sem reavaliação.
- **Conversão 24 kHz → 16 kHz** na volta, que adiciona um resample que o Azure não exige (ele entrega 16 kHz direto). Custo de CPU pequeno, mas é mais uma etapa no caminho quente.
- **Segunda credencial e segundo modelo de custo** para configurar, documentar e manter. O Azure tem nível gratuito F0; o custo do Gemini Live é por áudio e ainda não foi levantado.
- Limites de duração de sessão não estão documentados para o modo tradução, o que torna o **RF-07 (reconexão automática)** mais urgente do que é hoje com o Azure.
- Mais um projeto, mais um conjunto de testes e mais uma superfície para manter enquanto o `--provider` existir.
