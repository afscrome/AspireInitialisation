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

        var taskCompletionSource = new TaskCompletionSource();

        builder.ApplicationBuilder.Eventing.Subscribe<BeforeResourceStartedEvent>(initialiserResource, async (evt, ct) =>
        {
            var logger = evt.Services.GetRequiredService<ResourceLoggerService>().GetLogger(initialiserResource);
            logger.LogInformation("Waiting for {Resource} to be initialisable", targetResource.Name);
            await taskCompletionSource.Task.WaitAsync(ct);
        });


        return WithInitialiser(builder, initialiserResource.Name, WaitForInitialiserToComplete);


        async Task WaitForInitialiserToComplete(InitialisationContext context)
        {
            taskCompletionSource.TrySetResult();
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
