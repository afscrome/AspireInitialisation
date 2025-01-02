using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace AspireInitialisation.AppHost
{
    internal class InitialiserHealthCheck(InitialiserAnnotation annotation, string name) : IHealthCheck
    {
        public string Name => name;

        private InitialiserStatus _status = InitialiserStatus.WaitingToStart;
        private Exception? _exception;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var result = _status switch
            {
                InitialiserStatus.WaitingToStart => HealthCheckResult.Unhealthy("Waiting for Initialiser to start"),
                InitialiserStatus.Running => HealthCheckResult.Degraded("Waiting for Initialiser to complete"),
                InitialiserStatus.Failed => HealthCheckResult.Unhealthy(exception: _exception),
                InitialiserStatus.Complete => HealthCheckResult.Healthy(),
                _ => throw new ArgumentOutOfRangeException(nameof(_status), _status, "Unexpected InitialiserStatus value")
            };

            return Task.FromResult(result);
        }

        public async Task Initialise(InitialisationContext context)
        {
            _status = InitialiserStatus.Running;
            _exception = null;
            context.Logger.LogInformation("Running initialiser '{Initialiser}'", annotation.Name);

            try
            {
                await annotation.Initialiser(context);
                context.Logger.LogInformation("Completed initialiser '{Initialiser}'", annotation.Name);
                _status = InitialiserStatus.Complete;
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Error running initialiser '{Initialiser}'", annotation.Name);
                _exception = ex;
                _status = InitialiserStatus.Failed;
                throw;
            }
        }

        private enum InitialiserStatus
        {
            WaitingToStart = 0,
            Running,
            Complete,
            Failed
        }

    }
}
