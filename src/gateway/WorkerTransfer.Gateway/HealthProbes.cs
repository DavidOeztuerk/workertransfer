namespace WorkerTransfer.Gateway;

/// <summary>The two probes the gateway answers itself.</summary>
/// <remarks>
/// <strong>Not as an endpoint but as middleware</strong>, and that is not a
/// preference: Ocelot ends the chain. A <c>MapGet</c> stands in the endpoint
/// layer that would come after it — that layer never runs, and the probe would
/// get the 404 of a missing route. Measured, not assumed.
/// <para>
/// And they are <em>not</em> passed on: an answer of "I am here" must not
/// depend on whether a service behind it answers. Otherwise a single sick
/// service tips the gateway out of the load balancer and takes the other ten
/// with it.
/// </para>
/// </remarks>
public static class HealthProbes
{
    /// <summary>Hangs the probes into the chain — before Ocelot.</summary>
    public static IApplicationBuilder UseHealthProbes(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;

            if (path is "/health/live" or "/health/ready")
            {
                await context.Response.WriteAsJsonAsync(
                    new { status = path["/health/".Length..] });
                return;
            }

            await next();
        });
    }
}
