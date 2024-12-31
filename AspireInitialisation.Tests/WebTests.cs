using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit.Sdk;

namespace AspireInitialisation.Tests;

public class WebTests
{
    [Fact]
    public async Task Hangs()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireInitialisation_AppHost>();

        foreach(var resource in appHost.Resources)
        {
            var lifetimeAnnotaitons = resource.Annotations.OfType<ContainerLifetimeAnnotation>().ToList();
            foreach (var lifetimeAnnotation in lifetimeAnnotaitons) {
                resource.Annotations.Remove(lifetimeAnnotation);
            }
        }

        await using var app = await appHost.BuildAsync();
        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync();

        // Act
        await resourceNotificationService.WaitForResourceHealthyAsync("fake");
    }

    [Fact]
    public async Task Passes()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireInitialisation_AppHost>();

        builder.Resources.Clear();
        builder.AddResource(new ContainerResource("fake"))
            .WithImage("mcr.microsoft.com/foo/bar/does-not-exist-at-all-for-realz");

        var app = await builder.BuildAsync();
        await app.StartAsync();

        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await resourceNotificationService.WaitForResourceHealthyAsync("fake");
    }
}
