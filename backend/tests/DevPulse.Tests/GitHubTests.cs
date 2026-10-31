using System.Net;
using System.Text;
using DevPulse.Api.Application;
using DevPulse.Api.Infrastructure;
using Xunit;

namespace DevPulse.Tests;
public sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
public sealed class GitHubTests
{
    public const string Metadata = """{"id":123,"owner":{"login":"owner"},"name":"sample","private":false,"default_branch":"main"}""";
    [Fact] public async Task UsesActualHttpResponseAndDoesNotLeakCredentialsToPublicRepositories()
    {
        var requests = new List<string>();
        using var handler = new FixtureHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            requests.Add(request.RequestUri!.PathAndQuery);
            return FixtureHandler.Json(request.RequestUri.AbsolutePath.EndsWith("/commits") ? """[{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","commit":{"message":"Subject\nBody","author":{"name":"Author","date":"2026-10-30T12:00:00Z"}}}]""" : Metadata);
        });
        using var client = new HttpClient(handler);
        var inspector = new GitHubRepositoryInspector(client, new() { Token = "server-secret" });
        var identity = await inspector.ValidateAsync("owner", "sample", default);
        var result = await inspector.ReadHistoryAsync(identity, 20, default);
        Assert.Equal("Subject", Assert.Single(result).Message);
        Assert.Equal("/repos/owner/sample/commits?per_page=20", requests[^1]);
    }
    [Theory] [InlineData(401, 401)] [InlineData(403, 403)] [InlineData(404, 404)] [InlineData(429, 429)] [InlineData(500, 502)]
    public async Task SanitizesProviderFailures(int upstream, int expected)
    {
        using var handler = new FixtureHandler(_ => FixtureHandler.Json("secret upstream detail", (HttpStatusCode)upstream));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<RepositoryFailure>(() => new GitHubRepositoryInspector(client, new()).ValidateAsync("owner", "sample", default));
        Assert.Equal(expected, error.Status);
        Assert.DoesNotContain("secret", error.Message);
    }
    [Fact] public async Task PrivateRepositoryRequiresExplicitServerAllowlist()
    {
        using var handler = new FixtureHandler(_ => FixtureHandler.Json(Metadata.Replace("false", "true")));
        using var client = new HttpClient(handler);
        Assert.Equal(403, (await Assert.ThrowsAsync<RepositoryFailure>(() => new GitHubRepositoryInspector(client, new() { Token = "secret" }).ValidateAsync("owner", "sample", default))).Status);
    }
    [Fact] public async Task PrivateAllowlistedRepositoryUsesServerToken()
    {
        using var handler = new FixtureHandler(request => { Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); return FixtureHandler.Json(Metadata.Replace("false", "true")); });
        using var client = new HttpClient(handler);
        Assert.True((await new GitHubRepositoryInspector(client, new() { Token = "secret", AllowedPrivateRepositories = ["owner/sample"] }).ValidateAsync("owner", "sample", default)).IsPrivate);
    }
    [Fact] public async Task MalformedMetadataIsNotSuccessfulHistory()
    {
        using var handler = new FixtureHandler(_ => FixtureHandler.Json("{}")); using var client = new HttpClient(handler);
        Assert.Equal(502, (await Assert.ThrowsAsync<RepositoryFailure>(() => new GitHubRepositoryInspector(client, new()).ValidateAsync("owner", "sample", default))).Status);
    }
}
