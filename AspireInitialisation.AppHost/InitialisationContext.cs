namespace AspireInitialisation.AppHost
{
    public class InitialisationContext(IServiceProvider services, CancellationToken cancellationToken)
    {
        public IServiceProvider Services => services;
        public CancellationToken CancellationToken => cancellationToken;
    };
}
