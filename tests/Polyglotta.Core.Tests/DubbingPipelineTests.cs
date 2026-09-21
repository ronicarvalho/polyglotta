using System.Threading.Channels;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Pipeline;
using Polyglotta.Core.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Polyglotta.Core.Tests;

public class DubbingPipelineTests
{
    [Fact]
    public async Task Frames_FlowFromCaptureToTranslator_AndSynthesisToRender()
    {
        var capture = new FakeCapture();
        var translator = new FakeTranslator();
        var render = new FakeRender();

        await using var pipeline = new DubbingPipeline(
            "test", capture, translator, render, NullLogger<DubbingPipeline>.Instance);

        await pipeline.StartAsync(new TranslatorOptions("pt-BR", "en-US", null));

        capture.Emit(new byte[AudioFrame.BytesPer20Ms]);
        await translator.WaitForPushAsync();

        translator.EmitSynthesis(new byte[128]);
        Assert.Equal(128, render.BytesEnqueued);

        await pipeline.StopAsync();
        Assert.True(translator.PushedBytes > 0);
    }

    [Fact]
    public async Task MutedPipeline_DropsFrames()
    {
        var capture = new FakeCapture();
        var translator = new FakeTranslator();

        await using var pipeline = new DubbingPipeline(
            "test", capture, translator, new FakeRender(), NullLogger<DubbingPipeline>.Instance)
        { Muted = true };

        await pipeline.StartAsync(new TranslatorOptions("pt-BR", "en-US", null));
        capture.Emit(new byte[AudioFrame.BytesPer20Ms]);
        await Task.Delay(100);

        Assert.Equal(0, translator.PushedBytes);
        await pipeline.StopAsync();
    }

    private sealed class FakeCapture : IAudioCaptureDevice
    {
        private readonly Channel<AudioFrame> _ch = Channel.CreateUnbounded<AudioFrame>();
        public ChannelReader<AudioFrame> Frames => _ch.Reader;
        public void Emit(byte[] pcm) => _ch.Writer.TryWrite(new AudioFrame(pcm, DateTimeOffset.UtcNow));
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) { _ch.Writer.TryComplete(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTranslator : ISpeechTranslator
    {
        private readonly TaskCompletionSource _pushed = new();
        public long PushedBytes { get; private set; }
        public event EventHandler<TranscriptEvent>? Transcript { add { } remove { } }
        public event EventHandler<ReadOnlyMemory<byte>>? SynthesizedAudio;
        public event EventHandler<Exception>? Faulted { add { } remove { } }
        public Task StartAsync(TranslatorOptions o, CancellationToken ct = default) => Task.CompletedTask;
        public void PushAudio(ReadOnlySpan<byte> pcm) { PushedBytes += pcm.Length; _pushed.TrySetResult(); }
        public Task WaitForPushAsync() => _pushed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        public void EmitSynthesis(byte[] pcm) => SynthesizedAudio?.Invoke(this, pcm);
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRender : IAudioRenderDevice
    {
        public int BytesEnqueued { get; private set; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Enqueue(ReadOnlyMemory<byte> pcm) => BytesEnqueued += pcm.Length;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
