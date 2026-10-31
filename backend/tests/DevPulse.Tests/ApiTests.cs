using System.Net;
using System.Net.Http.Json;
using DevPulse.Api.Application;
using DevPulse.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevPulse.Tests;
public sealed class ApiFactory(GitFixture fixture) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        services.RemoveAll<RepositoryOptions>(); services.AddSingleton(fixture.Options);
    });
}
public sealed class ApiTests
{
    [Fact] public async Task RegistrationHistoryDuplicatesAndValidationUseRealLocalGit()
    {
        using var git = new GitFixture(); git.Git("commit", "--allow-empty", "-m", "API-backed commit");
        using var factory = new ApiFactory(git); using var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/v1/repositories", new { provider = "LocalGit", alias = "sample" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var repository = (await registration.Content.ReadFromJsonAsync<RepositoryDto>())!;
        var history = (await client.GetFromJsonAsync<HistoryDto>($"/api/v1/repositories/{repository.Id}/commits"))!;
        Assert.Equal("LocalGit", history.Provider); Assert.Equal("API-backed commit", Assert.Single(history.Commits).Message);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/repositories", new { provider = "LocalGit", alias = "sample" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/repositories/{repository.Id}/commits?limit=21")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/repositories/{Guid.NewGuid()}/commits")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/repositories", new { provider = "LocalGit", alias = git.Root })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/repositories", new { provider = "LocalGit", path = git.Root })).StatusCode);
    }
    [Fact] public async Task RejectsRemoteHostsAndCrossOriginRequests()
    {
        using var git = new GitFixture(); using var factory = new ApiFactory(git); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/repository-aliases")).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Host = "untrusted.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/repository-aliases")).StatusCode);
    }
    [Fact] public async Task AliasEndpointNeverExposesPaths()
    {
        using var git = new GitFixture(); using var factory = new ApiFactory(git); using var client = factory.CreateClient();
        var response = await client.GetStringAsync("/api/v1/repository-aliases");
        Assert.Contains("sample", response); Assert.DoesNotContain(git.Root, response);
    }
    [Fact] public async Task GitHubRegistrationAndHistoryUseTypedHttpProvider()
    {
        using var git = new GitFixture();
        using var baseFactory = new ApiFactory(git);
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient<IGitHubRepositoryInspector, GitHubRepositoryInspector>().ConfigurePrimaryHttpMessageHandler(() => new FixtureHandler(request => FixtureHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("/commits") ? "[]" : GitHubTests.Metadata)))));
        using var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/v1/repositories", new { provider = "GitHub", owner = "owner", name = "sample" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var repository = (await registration.Content.ReadFromJsonAsync<RepositoryDto>())!;
        Assert.Equal("GitHub", repository.Provider);
        var history = (await client.GetFromJsonAsync<HistoryDto>($"/api/v1/repositories/{repository.Id}/commits?limit=1"))!;
        Assert.Empty(history.Commits);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/repositories", new { provider = "GitHub", owner = "owner", name = "sample" })).StatusCode);
    }

}
