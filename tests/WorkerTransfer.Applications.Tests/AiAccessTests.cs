using FluentAssertions;
using WorkerTransfer.Applications.Infrastructure.CoverLetter;

namespace WorkerTransfer.Applications.Tests;

/// <summary>How an Ollama address reaches the container.</summary>
public sealed class AiAccessTests
{
    [Fact]
    public void A_bare_Ollama_address_gets_the_OpenAI_path()
    {
        HttpAiAccessLookup.Complete("http://localhost:11434", "openai_compatible")
            .Should().Be("http://localhost:11434/v1/chat/completions");
    }

    [Fact]
    public void An_already_complete_path_stays()
    {
        HttpAiAccessLookup.Complete(
                "http://localhost:11434/v1/chat/completions", "openai_compatible")
            .Should().Be("http://localhost:11434/v1/chat/completions");
    }

    [Fact]
    public void Anthropic_gets_no_OpenAI_path()
    {
        HttpAiAccessLookup.Complete(
                "https://api.anthropic.com/v1/messages", "anthropic")
            .Should().Be("https://api.anthropic.com/v1/messages");
    }
}
