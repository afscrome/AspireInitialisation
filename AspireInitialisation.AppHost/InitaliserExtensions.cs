using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspireInitialisation.AppHost;

public static class InitialiserExtensions
{
    public static IResourceBuilder<T> WithInitialiser<T>(this IResourceBuilder<T> builder, string name, Func<InitialisationContext, Task> initialiser)
        where T : IResource
    {
        builder.ApplicationBuilder.Services.TryAddLifecycleHook<InitialiserHealthCheckDistributedApplicationLifecycleHook>();
        builder.Resource.Annotations.Add(new InitialiserAnnotation(name, initialiser));
        return builder;
    }

    public static IResourceBuilder<TResource> WithInitialiser<TResource, TInitialiser>(this IResourceBuilder<TResource> builder, IResourceBuilder<TInitialiser> initialiser)
        where TResource : IResource
        where TInitialiser : IResourceWithWaitSupport
    {
        var targetResource = builder.Resource;
        var initialiserResource = initialiser.Resource;

        builder.ApplicationBuilder.Eventing.Subscribe<BeforeResourceStartedEvent>(initialiserResource, BlockUntilTargetIsInitialiseable);

        //TODO: can this be replaced with initaliser.WaitForCompletion(builder);
        // Would still need BlockUntilTargetIsInitialiseable
        return WithInitialiser(builder, initialiserResource.Name, WaitForInitialiserToComplete);


        async Task BlockUntilTargetIsInitialiseable(BeforeResourceStartedEvent evt, CancellationToken cancellationToken)
        {
            var logger = evt.Services.GetRequiredService<ResourceLoggerService>().GetLogger(evt.Resource);
            logger.LogInformation("Waiting for resource '{Resource}' to become initialisable", targetResource.Name);

            var resourceNotificationService = evt.Services.GetRequiredService<ResourceNotificationService>();
            await resourceNotificationService.PublishUpdateAsync(evt.Resource, s => s with { State = KnownResourceStates.Waiting });

            await InitialiserHealthCheckDistributedApplicationLifecycleHook.WaitUntilInitialisersCanRun(resourceNotificationService, targetResource, cancellationToken);
            logger.LogInformation("Finished waiting for resource '{Resource}'", targetResource.Name);
            await resourceNotificationService.PublishUpdateAsync(evt.Resource, s => s with { State = KnownResourceStates.Starting });
        }

        async Task WaitForInitialiserToComplete(InitialisationContext context)
        {
            var resourceNotificationService = context.Services.GetRequiredService<ResourceNotificationService>();

            var evt = await resourceNotificationService.WaitForResourceAsync(initialiserResource.Name, evt => KnownResourceStates.TerminalStates.Contains(evt.Snapshot.State?.Text), context.CancellationToken);

            if (evt.Snapshot.State == KnownResourceStates.FailedToStart)
            {
                throw new Exception($"Resource '{initialiserResource.Name}' failed to start");
            }

            if (evt.Snapshot.ExitCode != 0)
            {
                throw new Exception($"Resource '{initialiserResource.Name}' failed with exit code '{evt.Snapshot.ExitCode}'");
            }
        }
    }

}
