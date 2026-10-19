using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.ServiceDefaults.Ai;
using CoverLetterSettings = WorkerTransfer.Applications.Infrastructure.CoverLetter.CoverLetterSettings;
using JobsDraft = WorkerTransfer.Jobs.Infrastructure.Draft.DraftSettings;
using ProfileDraft = WorkerTransfer.Profile.Infrastructure.Draft.DraftSettings;
using ScoutDraft = WorkerTransfer.Scout.Infrastructure.Draft.DraftSettings;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// The operator's local Ollama is the platform's default until the start
/// (ADR-0051), and both wire formats are spoken correctly.
/// </summary>
public sealed class ModelWireTests
{
    [Fact]
    public void Every_seam_defaults_to_the_local_model()
    {
        // Four settings types, four chances to keep a default abroad.
        var seams = new (string Provider, string Address, string Model)[]
        {
            (new ProfileDraft().Provider, new ProfileDraft().BaseUrl, new ProfileDraft().Model),
            (new JobsDraft().Provider, new JobsDraft().BaseUrl, new JobsDraft().Model),
            (new ScoutDraft().Provider, new ScoutDraft().BaseUrl, new ScoutDraft().Model),
            (new CoverLetterSettings().Provider, new CoverLetterSettings().BaseUrl, new CoverLetterSettings().Model)
        };

        seams.Should().AllSatisfy(seam =>
        {
            seam.Provider.Should().Be(ModelWire.OpenAiCompatible);
            new Uri(seam.Address).Host.Should().Be("host.docker.internal");
            seam.Model.Should().Be(ModelWire.DefaultModel);
        });
    }

    [Fact]
    public async Task OpenAI_compatible_sends_the_rules_as_system_message_and_a_bearer_key()
    {
        using var request = ModelWire.Request(
            ModelWire.OpenAiCompatible, ModelWire.DefaultAddress, "m", "k", "RULES", "PROMPT", 400);

        request.Headers.Authorization!.ToString().Should().Be("Bearer k");
        request.Headers.Contains("x-api-key").Should().BeFalse();

        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var messages = body.RootElement.GetProperty("messages");
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("RULES");
        messages[1].GetProperty("content").GetString().Should().Be("PROMPT");
        body.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(400);
    }

    [Fact]
    public void A_local_model_without_key_gets_no_authorization_header()
    {
        using var request = ModelWire.Request(
            ModelWire.OpenAiCompatible, "http://localhost:11434/v1/chat/completions",
            "m", string.Empty, "R", "P", 10);

        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Anthropic_keeps_its_own_shape()
    {
        using var request = ModelWire.Request(
            ModelWire.Anthropic, "https://api.anthropic.com/v1/messages", "m", "k", "RULES", "PROMPT", 400);

        request.Headers.GetValues("x-api-key").Should().Equal("k");
        request.Headers.GetValues("anthropic-version").Should().ContainSingle();

        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        body.RootElement.GetProperty("system").GetString().Should().Be("RULES");

        // Current models think first, and the thinking counts against
        // max_tokens: 400 would leave no text.
        body.RootElement.GetProperty("max_tokens").GetInt32().Should().BeGreaterThanOrEqualTo(ModelWire.AnthropicRoom);
        body.RootElement.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("low");
    }

    [Fact]
    public void An_unknown_label_is_refused_instead_of_guessed()
    {
        var act = () => ModelWire.Request("minimax", "http://x", "m", "k", "R", "P", 10);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(ModelWire.OpenAiCompatible, """{"choices":[{"message":{"content":"Hallo"}}]}""", "Hallo")]
    [InlineData(ModelWire.Anthropic, """{"content":[{"type":"text","text":"Hallo"}]}""", "Hallo")]
    [InlineData(ModelWire.OpenAiCompatible, """{"content":[{"text":"wrong shape"}]}""", null)]
    [InlineData(ModelWire.Anthropic, """{"choices":[]}""", null)]
    [InlineData(ModelWire.Anthropic, """{"content":[{"type":"thinking","thinking":""},{"type":"text","text":"Hallo"}]}""", "Hallo")]
    [InlineData(ModelWire.Anthropic, """{"stop_reason":"refusal","content":[{"type":"text","text":"no"}]}""", null)]
    public void The_text_is_read_in_the_providers_own_shape(string provider, string json, string? expected)
    {
        ModelWire.Text(provider, json).Should().Be(expected);
    }
}
