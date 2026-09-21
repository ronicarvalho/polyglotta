using System.Threading.Channels;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Pipeline;
using Polyglotta.Core.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Polyglotta.Core.Tests;

public class DubbingPipelineTests
{
    private static readonly TranslatorOptions Options = new("pt-BR", "en-US", null);

    private static DubbingPipeline Create(FakeCapture capture, FakeTranslator translator, FakeRender render) =>
        new("test", capture, translator, render, NullLogger<DubbingPipeline>.Instance);

    [Fact]
    public async Task Frames_FlowFromCaptureToTranslator_AndSynthesisToRender()
    {
        var capture = new FakeCapture();
        var translator = new FakeTranslator();
        var render = new FakeRender();
        await using var pipeline = Create(capture, translator, render);

        await pipeline.StartAsync(Options);

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
        await using var pipeline = Create(capture, translator, new FakeRender());
        pipeline.Muted = true;

        await pipeline.StartAsync(Options);
        capture.Emit(new byte[AudioFrame.BytesPer20Ms]);
        await capture.WaitDrainedAsync();

        Assert.Equal(0, translator.PushedBytes);
        await pipeline.StopAsync();
    }

    [Fact]
    public async Task StopAsync_IsIdempotent_AndStopsEveryStageOnce()
    {
        var capture = new FakeCapture();
        var translator = new FakeTranslator();
        var render = new FakeRender();
        var pipeline = Create(capture, translator, render);

        await pipeline.StartAsync(Options);
        await pipeline.StopAsync();
        await pipeline.StopAsync();
        await pipeline.DisposeAsync(); // calls StopAsync again internally

        Assert.Equal(1, capture.StopCalls);
        Assert.Equal(1, translator.StopCalls);
        Assert.Equal(1, render.StopCalls);
        Assert.False(pipeline.IsRunning);
    }

    [Fact]
    public async Task StopAsync_StopsRemainingStages_WhenOneFails()
    {
        var capture = new FakeCapture { FailOnStop = true };
        var translator = new FakeTranslator();
        var render = new FakeRender();
        await using var pipeline = Create(capture, translator, render);

        await pipeline.StartAsync(Options);
        var ex = await Assert.ThrowsAsync<AggregateException>(() => pipeline.StopAsync());

        Assert.Single(ex.InnerExceptions);
        Assert.Equal(1, translator.StopCalls);
        Assert.Equal(1, render.StopCalls);
    }

    [Fact]
    public async Task CanRestart_AfterStop_WithoutDuplicatingEventHandlers()
    {
        var capture = new FakeCapture();
        var translator = new FakeTranslator();
        var render = new FakeRender();
        await using var pipeline = Create(capture, translator, render);

        await pipeline.StartAsync(Options);
        await pipeline.StopAsync();
        await pipeline.StartAsync(Options);
        Assert.True(pipeline.IsRunning);

        capture.Emit(new byte[AudioFrame.BytesPer20Ms]);
        await translator.WaitForPushAsync();
        Assert.Equal(AudioFrame.BytesPer20Ms, translator.PushedBytes);

        // One handler -> one enqueue. A re-subscribed handler would double it.
        translator.EmitSynthesis(new byte[100]);
        Assert.Equal(100, render.BytesEnqueued);
        Assert.Equal(2, translator.StartCalls);
    }

    [Fact]
    public async Task StartAsync_WhileRunning_Throws()
    {
        await using var pipeline = Create(new FakeCapture(), new FakeTranslator(), new FakeRender());
        await pipeline.StartAsync(Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.StartAsync(Options));
    }

    [Fact]
    public async Task TranslatorFault_IsSurfacedThroughFaulted()
    {
        var translator = new FakeTranslator();
        await using var pipeline = Create(new FakeCapture(), translator, new FakeRender());
        Exception? seen = null;
        pipeline.Faulted += (_, e) => seen = e;

        await pipeline.StartAsync(Options);
        translator.EmitFault(new InvalidOperationException("boom"));

        Assert.NotNull(seen);
        Assert.Equal("boom", seen!.Message);
    }

    [Fact]
    public async Task CaptureError_IsSurfacedThroughFaulted_AndPumpEnds()
    {
        var capture = new FakeCapture();
        await using var pipeline = Create(capture, new FakeTranslator(), new FakeRender());
        var faulted = new TaskCompletionSource<Exception>();
        pipeline.Faulted += (_, e) => faulted.TrySetResult(e);

        await pipeline.StartAsync(Options);
        capture.Fail(new IOException("device unplugged"));

        var ex = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<IOException>(ex);
        await Task.Delay(50); // pump task observes the completion
        Assert.False(pipeline.IsRunning);
    }

    [Fact]
    public async Task Latency_IsMeasured_FromLastVoicedFrame_ToFirstSynthesis()
    {
        var capture = new FakeCapture();
        var translator = new FakeTranslator();
        await using var pipeline = Create(capture, translator, new FakeRender());
        await pipeline.StartAsync(Options);

        var spokeAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1.5);
        capture.Emit(Voiced(), spokeAt);
        capture.Emit(new byte[AudioFrame.BytesPer20Ms], spokeAt + TimeSpan.FromSeconds(1)); // trailing silence
        await capture.WaitDrainedAsync();

        translator.EmitTranscript("olá", "hello", isFinal: true);
        translator.EmitSynthesis(new byte[64]);

        Assert.Equal(1, pipeline.Latency.Count);
        Assert.InRange(pipeline.Latency.Last!.Value.TotalSeconds, 1.4, 2.5);
    }

    private static byte[] Voiced()
    {
        // Full-scale square wave: RMS = 32767, well above the threshold.
        var pcm = new byte[AudioFrame.BytesPer20Ms];
        for (var i = 0; i < pcm.Length; i += 2)
            BitConverter.TryWriteBytes(pcm.AsSpan(i), (short)((i / 2) % 2 == 0 ? 32767 : -32767));
        return pcm;
    }

    private sealed class FakeCapture : IAudioCaptureDevice
    {
        private readonly Channel<AudioFrame> _ch = Channel.CreateUnbounded<AudioFrame>();
        public int StopCalls { get; private set; }
        public bool FailOnStop { get; init; }
        public ChannelReader<AudioFrame> Frames => _ch.Reader;

        public void Emit(byte[] pcm, DateTimeOffset? at = null) =>
            _ch.Writer.TryWrite(new AudioFrame(pcm, at ?? DateTimeOffset.UtcNow));

        /// <summary>Simulates a device error: the frame channel ends with an exception.</summary>
        public void Fail(Exception ex) => _ch.Writer.TryComplete(ex);

        public async Task WaitDrainedAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (_ch.Reader.Count > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(5);
            await Task.Delay(20); // let the pump process the last frame
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            // Like the WASAPI implementation: stopping keeps the channel open for a restart.
            return FailOnStop ? throw new InvalidOperationException("stop failed") : Task.CompletedTask;
        }

        public ValueTask DisposeAsync() { _ch.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    private sealed class FakeTranslator : ISpeechTranslator
    {
        private TaskCompletionSource _pushed = new();
        public long PushedBytes { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public event EventHandler<TranscriptEvent>? Transcript;
        public event EventHandler<ReadOnlyMemory<byte>>? SynthesizedAudio;
        public event EventHandler<Exception>? Faulted;

        public Task StartAsync(TranslatorOptions o, CancellationToken ct = default)
        {
            StartCalls++;
            _pushed = new TaskCompletionSource();
            return Task.CompletedTask;
        }

        public void PushAudio(ReadOnlySpan<byte> pcm) { PushedBytes += pcm.Length; _pushed.TrySetResult(); }
        public Task WaitForPushAsync() => _pushed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void EmitTranscript(string src, string dst, bool isFinal) =>
            Transcript?.Invoke(this, new TranscriptEvent(src, dst, isFinal));
        public void EmitSynthesis(byte[] pcm) => SynthesizedAudio?.Invoke(this, pcm);
        public void EmitFault(Exception ex) => Faulted?.Invoke(this, ex);

        public Task StopAsync(CancellationToken ct = default) { StopCalls++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRender : IAudioRenderDevice
    {
        public int BytesEnqueued { get; private set; }
        public int StopCalls { get; private set; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Enqueue(ReadOnlyMemory<byte> pcm) => BytesEnqueued += pcm.Length;
        public Task StopAsync(CancellationToken ct = default) { StopCalls++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
