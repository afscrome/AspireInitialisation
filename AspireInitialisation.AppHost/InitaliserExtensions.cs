using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AspireInitialisation.AppHost;

public static class InitialiserExtensions
{
    public static IDistributedApplicationBuilder WithInitialisationHealthChecks(this IDistributedApplicationBuilder builder)
    {
        // TODO: Work out a way to convert InitialiserAnnotations to health checks in BeforeStartEvent
        // Right now this method has some "magic" properties in that it has to be the last method called before the app host is built
        // the `builder.ApplicationBuilder.Services.AddHealthChecks()` call is the problematic bit

        foreach (var resource in builder.Resources)
        {
            if (!resource.TryGetAnnotationsOfType<InitialiserAnnotation>(out var Initialisers))
            {
                continue;
            }

            foreach (var Initialiser in Initialisers)
            {
                // Add a health check that only passes once the Initialiser has completed
                var tcs = new TaskCompletionSource();
                var checkName = $"Initialiser-{resource.Name}-name";
                resource.Annotations.Add(new HealthCheckAnnotation(checkName));

                var statusMessage = "Waiting for other health checks to pass";

                builder.Services.AddHealthChecks()
                    .AddCheck(checkName, (ct) => tcs.Task switch
                    {
                        { IsCompletedSuccessfully: true } => HealthCheckResult.Healthy(),
                        { IsFaulted: true } => HealthCheckResult.Unhealthy(exception: tcs.Task.Exception),
                        _ => HealthCheckResult.Degraded(description: statusMessage, tcs.Task.Exception)
                    });

                // TODO: Investigate being more efficient by using one subscription per resource rather than one per Initialiser
                //TODO: Should this use AfterResourcesCreatedEvent instead?
                builder.Eventing.Subscribe<BeforeStartEvent>((evt, ct) =>
                {
                    ct.Register(() => tcs.TrySetCanceled());

                    var resourceNotificationService = evt.Services.GetRequiredService<ResourceNotificationService>();

                    // Now wait for the Initialiser to complete before marking the underlying resource's health check as complete
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await WaitUntilInitialisersCanRun(resourceNotificationService, resource, ct);
                            statusMessage = "Initialising";

                            // This works, but the resource state goes to "Running (Unhealthy)" for a second or so beforehand which is a bit meh
                            await resourceNotificationService.PublishUpdateAsync(resource, s => s with { State = new("Initialising", KnownResourceStateStyles.Info) });
                            var context = new InitialisationContext(evt.Services, ct);
                            await Task.WhenAll(Initialisers.Select(x => x.Initialiser(context)));

                            await resourceNotificationService.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.Running, StartTimeStamp = DateTime.Now });

                            tcs.TrySetResult();
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    });

                    return Task.CompletedTask;
                });
            }
        }

        return builder;
    }


    /// <summary>
    /// Waits for all non Initialiser health checks to be healthy.
    /// </summary>
    /// <remarks>
    /// Can't use the built in `WaitForResourceHealthyAsync` as that introduces a race condition - our resource isn't healthy until
    /// the initialisers have run, but we don't want to run Initialisers until all other health checks are passed
    /// </remarks>
    private static async Task WaitUntilInitialisersCanRun(ResourceNotificationService resourceNotificationService, IResource resource, CancellationToken ct)
    {
        //TODO: This doesn't always take effect - sometimes another update gets in and overwrites it.
        await resourceNotificationService.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.Waiting });

        // Wait for all OTHER health checks to pass before starting our Initialisers
        await resourceNotificationService.WaitForResourceAsync(resource.Name, IsOtherwiseHealthy, ct);

        bool IsOtherwiseHealthy(ResourceEvent evt)
        {
            //HORRIBLE HACK: This logic feels brittle - probably something smarter can be done
            var InitialiserCount = resource.Annotations.OfType<InitialiserAnnotation>().Count();
            var requiredHealthyReports = evt.Snapshot.HealthReports.Length - InitialiserCount;
            var healthyReports = evt.Snapshot.HealthReports.Count(x => x.Status == HealthStatus.Healthy);

            return healthyReports >= requiredHealthyReports;
        }
    }

    public static IResourceBuilder<TResource> WithInitialiser<TResource, TInitialiser>(this IResourceBuilder<TResource> builder, IResourceBuilder<TInitialiser> initialiser)
        where TResource : IResource
        where TInitialiser : IResourceWithWaitSupport
    {
        var targetResource = builder.Resource;
        var initialiserResource = initialiser.Resource;

        // Wait for the target resource to be 
        builder.ApplicationBuilder.Eventing.Subscribe<BeforeResourceStartedEvent>(initialiserResource, async (evt, ct) =>
        {
            var resourceNotificationService = evt.Services.GetRequiredService<ResourceNotificationService>();
            await WaitUntilInitialisersCanRun(resourceNotificationService, targetResource, ct);
        });

        return WithInitialiser(builder, initialiserResource.Name, async context =>
        {
            var resourceNotificationService = context.Services.GetRequiredService<ResourceNotificationService>();

            await resourceNotificationService.WaitForResourceAsync(initialiserResource.Name, KnownResourceStates.TerminalStates, context.CancellationToken);
            //TODO: Handle failures / exit codes etc.
        });
    }


    public static IResourceBuilder<T> WithInitialiser<T>(this IResourceBuilder<T> builder, string name, Func<InitialisationContext, Task> initialiser)
        where T : IResource
    {
        builder.Resource.Annotations.Add(new InitialiserAnnotation(name, initialiser));
        return builder;
    }
}
