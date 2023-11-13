class Program
{
    static async Task Main(string[] args)
    {
        var backendServers = new List<string> { "http://localhost:5001", "http://localhost:5002" };

        var loadBalancer = new LoadBalancer(backendServers);
        await loadBalancer.StartAsync();
    }
}