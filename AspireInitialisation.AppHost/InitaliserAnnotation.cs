namespace AspireInitialisation.AppHost
{
    public class InitialiserAnnotation(string name, Func<InitialisationContext, Task> initialiser) : IResourceAnnotation
    {
        public string Name => name;

        internal Func<InitialisationContext, Task> Initialiser => initialiser;
    }
}
