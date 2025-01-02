using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AspireInitialisation.AppHost
{
    public class InitialiserHealthCheckDistributedApplicationLifecycleHook(IOptions<HealthCheckServiceOptions> healthCheckServiceOptions, ResourceNotificationService resourceNotificationService, IServiceProvider serviceProvider) : IDistributedApplicationLifecycleHook
    {
        Task IDistributedApplicationLifecycleHook.BeforeStartAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken)
        {
            foreach (var resource in appModel.Resources)
            {
                AddInitialiserHealthChecksToResource(resource, cancellationToken);
            }

            return Task.CompletedTask;
        }

        internal static async Task WaitUntilInitialisersCanRun(ResourceNotificationService resourceNotificationService, IResource resource, CancellationToken cancellationToken)
        {
            await resourceNotificationService.WaitForResourceAsync(resource.Name, KnownResourceStates.Running);

            if (!resource.TryGetAnnotationsOfType<InitialiserAnnotation>(out var initialisers))
            {
                return;
            }

            var initialiserHealthCheckNames = initialisers.Select(x => x.Name).ToList();

            await resourceNotificationService.WaitForResourceAsync(resource.Name, IsHealthyApartFromInitialisers, cancellationToken);

            bool IsHealthyApartFromInitialisers(ResourceEvent evt)
            {
                var nonInitialiserHealthReports = evt.Snapshot.HealthReports
                    .Where(x => !initialiserHealthCheckNames.Contains(x.Name));

                return nonInitialiserHealthReports.All(x => x.Status == HealthStatus.Healthy);
            }
        }

        private void AddInitialiserHealthChecksToResource(IResource resource, CancellationToken cancellationToken)
        {
            if (!resource.TryGetAnnotationsOfType<InitialiserAnnotation>(out var initialisers))
            {
                return;
            }

            var healthChecks = new List<InitialiserHealthCheck>();

            foreach(var initialiser in initialisers)
            {
                var healthCheck = new InitialiserHealthCheck(initialiser, initialiser.Name);
                healthChecks.Add(healthCheck);

                var registration = new HealthCheckRegistration(healthCheck.Name, healthCheck, HealthStatus.Degraded, null);
                healthCheckServiceOptions.Value.Registrations.Add(registration);
                resource.Annotations.Add(new HealthCheckAnnotation(healthCheck.Name));
            }

            _ = Task.Run(async () =>
            {
                await WaitUntilInitialisersCanRun(resourceNotificationService, resource, cancellationToken);
                await RunInitialisers();
            });

            async Task RunInitialisers()
            {
                ResourceStateSnapshot? originalResourceStateSnapshot = null;

                await resourceNotificationService.PublishUpdateAsync(resource, s => {
                    originalResourceStateSnapshot = s.State;
                    return s with { State = new("Initialising", KnownResourceStateStyles.Warn) };
                });

                try
                {
                    await Task.WhenAll(healthChecks.Select(healthCheck =>
                    {
                        var context = new InitialisationContext(serviceProvider, cancellationToken, resource);
                        return healthCheck.Initialise(context);
                    }));


                    // TODO: This produces some slightly confusing UX as the resoruce state chagnes from Initalising --> Running
                    // But health state takes a few seconds to propogate due to the polling model
                    // Could be solved by using operators rather than health checks
                    await resourceNotificationService.PublishUpdateAsync(resource, s => s with
                    {
                        State = originalResourceStateSnapshot,
                        StartTimeStamp = DateTime.Now,
                    });
                }
                catch
                {
                    await resourceNotificationService.PublishUpdateAsync(resource, s => s with
                    {
                        State = new("FailedToInitialise", KnownResourceStateStyles.Error),
                    });
                }
            }

        }
    }
}
