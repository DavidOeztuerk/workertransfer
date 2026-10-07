using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.Applications.Contracts;
using WorkerTransfer.Messages.Infrastructure.Process;

namespace WorkerTransfer.Messages.Tests;

/// <summary>
/// What applications-service writes is what this service reads — with the
/// sender's own type on one side and the receiver's on the other.
/// </summary>
/// <remarks>
/// The pattern of <c>NotificationWireTests</c>: comparing the two names would
/// be one string checked twice. A renamed <c>tenant_id</c> would otherwise
/// arrive as <c>Guid.Empty</c>, and every company would be refused.
/// </remarks>
public sealed class ApplicationWireTests
{
    [Fact]
    public void The_parties_and_the_name_survive_the_wire()
    {
        var subject = Guid.NewGuid();
        var tenant = Guid.NewGuid();

        var written = JsonSerializer.Serialize(new ApplicationV1(
            Guid.NewGuid(), Guid.NewGuid(), tenant, subject, "Hallo", false, false, [], "submitted",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new ApplicantContactV1("Anna Muster", "", "", "", "", "", "", "")));

        var read = JsonSerializer.Deserialize<ApplicationPartiesV1>(written)!;

        read.SubjectId.Should().Be(subject);
        read.TenantId.Should().Be(tenant);
        read.ApplicantContact!.FullName.Should().Be("Anna Muster");
    }
}
