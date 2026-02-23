namespace HyPrism.Hosts;

public interface IRuntimeHost
{
    string Id { get; }
    string Name { get; }

    Task RunAsync(IServiceProvider services);
}
