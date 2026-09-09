using FluentAssertions;
using WorkerTransfer.Applications.Infrastructure.Anschreiben;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Wie eine Ollama-Adresse den Behälter erreicht.</summary>
public sealed class KiZugangTests
{
    [Fact]
    public void Eine_nackte_Ollama_Adresse_bekommt_den_OpenAI_Pfad()
    {
        HttpKiZugang.Vervollstaendige("http://localhost:11434", "openai_compatible")
            .Should().Be("http://localhost:11434/v1/chat/completions");
    }

    [Fact]
    public void Ein_bereits_vollstaendiger_Pfad_bleibt()
    {
        HttpKiZugang.Vervollstaendige(
                "http://localhost:11434/v1/chat/completions", "openai_compatible")
            .Should().Be("http://localhost:11434/v1/chat/completions");
    }

    [Fact]
    public void Anthropic_bekommt_keinen_OpenAI_Pfad()
    {
        HttpKiZugang.Vervollstaendige(
                "https://api.anthropic.com/v1/messages", "anthropic")
            .Should().Be("https://api.anthropic.com/v1/messages");
    }
}
