using System.Net;
using System.Text.Json.Serialization;
using DevPulse.Api.Application;
using DevPulse.Api.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5090");
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
var repositories = builder.Configuration.GetSection("Repositories").Get<RepositoryOptions>() ?? new();
var github = builder.Configuration.GetSection("GitHub").Get<GitHubOptions>() ?? new();
builder.Services.AddSingleton(repositories);
builder.Services.AddSingleton(github);
builder.Services.AddSingleton<IAuthorizedPathPolicy, AuthorizedPathPolicy>();
builder.Services.AddSingleton<IApprovedLocalRepositories, ApprovedLocalRepositories>();
builder.Services.AddSingleton<IRepositoryRegistry, RepositoryRegistry>();
builder.Services.AddSingleton<IGitRepositoryInspector, ControlledGitRepositoryInspector>();
builder.Services.AddHttpClient<IGitHubRepositoryInspector, GitHubRepositoryInspector>().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
builder.Services.AddScoped<RepositoryApplicationService>();
var ownerOrigins = builder.Configuration.GetSection("OwnerOrigins").Get<string[]>() ?? ["http://127.0.0.1:5090", "http://localhost:5090"];
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var failure = exception as RepositoryFailure;
    var status = failure?.Status ?? (exception is BadHttpRequestException ? 400 : 500);
    if (failure?.RetryAfterSeconds is int retry) context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
    await Results.Problem(statusCode: status, title: failure?.Message ?? (status == 400 ? "Invalid request." : "The request could not be completed."), extensions: new Dictionary<string, object?> { ["code"] = failure?.Code ?? "request_failed", ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
}));
// M01 is a single-owner, loopback-only service. Never expose it through a reverse proxy.
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    var host = context.Request.Host.Host;
    var origin = context.Request.Headers.Origin.ToString();
    var allowedOrigin = origin.Length == 0 || ownerOrigins.Contains(origin, StringComparer.Ordinal);
    if (remote != null && !IPAddress.IsLoopback(remote) || host is not ("localhost" or "127.0.0.1" or "[::1]") || !allowedOrigin)
    {
        await Results.Problem(statusCode: 403, title: "Only the local owner interface is authorized.").ExecuteAsync(context);
        return;
    }
    await next(context);
});
app.MapGet("/api/v1/repository-aliases", (IApprovedLocalRepositories local) => Results.Ok(local.List()));
app.MapPost("/api/v1/repositories", async (RepositoryRequest request, RepositoryApplicationService service, CancellationToken ct) =>
{
    var repository = await service.RegisterAsync(request, ct);
    return Results.Created($"/api/v1/repositories/{repository.Id}/commits", repository);
});
app.MapGet("/api/v1/repositories/{repositoryId}/commits", async (string repositoryId, int? limit, RepositoryApplicationService service, CancellationToken ct) =>
{
    if (!Guid.TryParse(repositoryId, out var id)) throw new RepositoryFailure(400, "invalid_repository_id", "Repository ID must be a GUID.");
    return Results.Ok(await service.GetHistoryAsync(id, limit ?? 20, ct));
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();
public partial class Program;
