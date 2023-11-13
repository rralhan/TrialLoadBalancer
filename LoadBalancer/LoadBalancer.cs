using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class LoadBalancer
{
    private readonly List<string> _backendServers;
    private readonly Stopwatch _stopwatch;
    private string _fastestServer;

    public LoadBalancer(List<string> backendServers)
    {
        _backendServers = backendServers ?? throw new ArgumentNullException(nameof(backendServers));
        _stopwatch = new Stopwatch();
    }

    public async Task StartAsync()
    {
        //Get the fastest server amongst the ones supplied
        _fastestServer = await GetFastestServerAsync();
       
       //Create the webapplication builder
        var builder = WebApplication.CreateBuilder();     

        //Add the routing to the builder
        builder.Services.AddRouting();

        //Configure and build the application
        var app = builder.Build();

        //Map the root to the load balancing logic
        app.Map("",
            //Define the delegate logic
            app => app.Run(async context =>
            {
               
                //Load balancing proxy configuration
                var proxyRequest = new HttpRequestMessage
                {
                    Method = new HttpMethod(context.Request.Method),
                    RequestUri = new Uri(_fastestServer + context.Request.Path + context.Request.QueryString),
                    Content = new StreamContent(context.Request.Body)
                };

                foreach (var (key, value) in context.Request.Headers)
                {
                    proxyRequest.Headers.TryAddWithoutValidation(key, value.ToArray());
                }

                var retryCount = 3; // You can adjust the number of retries

                while (retryCount > 0)
                {
                    try
                    {
                        //Make the http call
                        using (var responseMessage = await new HttpClient().SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead))
                        {
                            context.Response.StatusCode = (int)responseMessage.StatusCode;

                            foreach (var (key, value) in responseMessage.Headers)
                            {
                                context.Response.Headers[key] = value.ToArray();
                            }

                            await responseMessage.Content.CopyToAsync(context.Response.Body);

                            return; // Request succeeded, exit the retry loop
                        }
                    }
                    catch (Exception)
                    {
                        // Log or handle the exception
                        retryCount--;

                        if (retryCount > 0)
                        {
                            // Wait for a short time before retrying
                            await Task.Delay(500);
                        }
                    }
                }

                context.Response.StatusCode = 500; // Internal Server Error
                await context.Response.WriteAsync("Failed to process the request.");
            }));

        await app.RunAsync();
    }

    private async Task<string> GetFastestServerAsync()
    {
        var tasks = new List<Task<long>>();

        foreach (var server in _backendServers)
        {
            tasks.Add(MeasureResponseTime(server));
        }

        var responseTimes = await Task.WhenAll(tasks);

        if (string.IsNullOrEmpty(_fastestServer) || responseTimes.Min() < _stopwatch.ElapsedMilliseconds)
        {
            _stopwatch.Restart();
            return _backendServers[Array.IndexOf(responseTimes, responseTimes.Min())];
        }

        return _fastestServer;
    }

    private static async Task<long> MeasureResponseTime(string url)
    {
        var stopwatch = Stopwatch.StartNew();
        using (var httpClient = new HttpClient())
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                return stopwatch.ElapsedMilliseconds;
            }
            catch (Exception)
            {
                // Handle exception if the request to the server fails
                return long.MaxValue;
            }
        }
    }
}


