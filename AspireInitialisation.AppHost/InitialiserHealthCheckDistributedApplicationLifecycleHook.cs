using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Logging;

namespace AspireInitialisation.AppHost
{
    public class InitialiserHealthCheckDistributedApplicationLifecycleHook(
        IDistributedApplicationEventing distributedApplicationEventing,
        ResourceLoggerService resourceLoggerService,
        ResourceNotificationService resourceNotificationService) : IDistributedApplicationLifecycleHook
    {
        private static readonly ResourceStateSnapshot InitalisingState = new("Initialising", KnownResourceStateStyles.Info);

        Task IDistributedApplicationLifecycleHook.BeforeStartAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken)
        {
            foreach (var resource in appModel.Resources)
            {
                if (!resource.TryGetAnnotationsOfType<InitialiserAnnotation>(out var initialiserAnnotations))
                {
                    continue;
                }

                distributedApplicationEventing.Subscribe<ResourceReadyEvent>(resource, async (evt, ct) =>
                {
                    var context = new InitialisationContext(evt.Services, ct, resource);

                    var logger = resourceLoggerService.GetLogger(resource);
                    logger.LogInformation("Running Initialisers");

                    ResourceStateSnapshot? originalState = null;
                    await resourceNotificationService.PublishUpdateAsync(resource, x =>
                    {
                        originalState = x.State;
                        return x with { State = InitalisingState };
                    });

                    try
                    {
                        await Task.WhenAll(initialiserAnnotations.Select(RunInitaliser))
                            .WaitAsync(ct);
                    }
                    finally
                    {
                        await resourceNotificationService.PublishUpdateAsync(resource, x =>
                        {
                            // Only revert state if something else hasn't taken over
                            return x with { State = x.State == InitalisingState ? originalState : x.State };
                        });
                    }

                    async Task RunInitaliser(InitialiserAnnotation annotation)
                    {
                        try
                        {
                            await annotation.Initialiser(context);
                            logger.LogInformation("Completed '{Name}' Initialiser", annotation.Name);
                        }
                        catch(Exception ex)
                        {
                            logger.LogError(ex, "Error running '{Name}' Initialiser", annotation.Name);
                            throw;
                        }
                    }
                });
            }

            return Task.CompletedTask;
        }
    }
}
