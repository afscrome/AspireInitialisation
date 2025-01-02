using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspireInitialisation.AppHost
{
    public class InitialisationContext(IServiceProvider services, CancellationToken cancellationToken, IResource resource)
    {
        public IServiceProvider Services => services;
        public CancellationToken CancellationToken => cancellationToken;
        public IResource Resource => resource;
        public ILogger Logger => Services.GetRequiredService<ResourceLoggerService>().GetLogger(Resource);
    };
}
