using Polyglotta.Core.Audio;
using Polyglotta.Host;
using Xunit;

namespace Polyglotta.Host.Tests;

public class VoicePreviewTests
{
    [Theory]
    [InlineData("pt-BR", "Esta")]
    [InlineData("pt-PT", "Esta")]
    [InlineData("en-US", "This")]
    [InlineData("es-ES", "Esta es")]
    [InlineData("fr-FR", "Voici")]
    [InlineData("de-DE", "Das ist")]
    [InlineData("it-IT", "Questa")]
    public void Amostra_fala_a_lingua_da_voz(string locale, string start)
    {
        Assert.StartsWith(start, VoicePreview.SampleFor(locale));
    }

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("")]
    public void Locale_sem_frase_cai_no_ingles(string locale)
    {
        // Melhor uma frase em inglês do que nenhuma amostra: o objetivo é ouvir o timbre.
        Assert.StartsWith("This", VoicePreview.SampleFor(locale));
    }

    [Fact]
    public void ResolveOne_acha_o_fone_por_trecho_do_nome()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("{id-mic}", "Microphone (USB PnP Sound Device)", AudioDeviceKind.Capture, false),
            new("{id-fone}", "Speakers (USB PnP Sound Device)", AudioDeviceKind.Render, false),
            new("{id-hdmi}", "27HC5R (2- HD Audio Driver)", AudioDeviceKind.Render, true)
        };

        var found = SessionPlanner.ResolveOne(
            new DeviceSelection("Speakers", null), AudioDeviceKind.Render, devices);

        Assert.Equal("{id-fone}", found?.Id);
    }

    [Fact]
    public void ResolveOne_sem_selecao_usa_o_padrao_do_sistema()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("{id-fone}", "Speakers (USB PnP Sound Device)", AudioDeviceKind.Render, false),
            new("{id-hdmi}", "27HC5R (2- HD Audio Driver)", AudioDeviceKind.Render, true)
        };

        var found = SessionPlanner.ResolveOne(new DeviceSelection(), AudioDeviceKind.Render, devices);

        Assert.Equal("{id-hdmi}", found?.Id);
    }

    [Fact]
    public void ResolveOne_devolve_nulo_quando_o_dispositivo_sumiu()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("{id-hdmi}", "27HC5R (2- HD Audio Driver)", AudioDeviceKind.Render, true)
        };

        var found = SessionPlanner.ResolveOne(
            new DeviceSelection("Fone que foi desconectado", null), AudioDeviceKind.Render, devices);

        Assert.Null(found);
    }
}
