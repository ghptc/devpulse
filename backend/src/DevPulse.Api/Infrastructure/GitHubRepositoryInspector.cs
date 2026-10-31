using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevPulse.Api.Application;

namespace DevPulse.Api.Infrastructure;

public sealed class GitHubOptions
{
    public string? Token { get; set; }
    public string[] AllowedPrivateRepositories { get; set; } = [];
}
public sealed class GitHubRepositoryInspector(HttpClient client, GitHubOptions options) : IGitHubRepositoryInspector
{
    public async Task<GitHubIdentity> ValidateAsync(string owner, string name, CancellationToken cancellationToken)
    {
        using var document = await Read(owner, name, "", cancellationToken);
        try
        {
            var root = document.RootElement;
            var identity = new GitHubIdentity(root.GetProperty("id").GetInt64(), root.GetProperty("owner").GetProperty("login").GetString()!, root.GetProperty("name").GetString()!, root.GetProperty("default_branch").GetString()!, root.GetProperty("private").GetBoolean());
            if (identity.Id <= 0 || !RepositoryApplicationService.ValidIdentity(identity.Owner, 39) || !RepositoryApplicationService.ValidIdentity(identity.Name, 100) || string.IsNullOrEmpty(identity.DefaultBranch) || identity.DefaultBranch.Length > 255 || identity.DefaultBranch.Any(char.IsControl)) throw Invalid();
            if (identity.IsPrivate && (!Allowed(owner, name) || string.IsNullOrWhiteSpace(options.Token))) throw new RepositoryFailure(403, "private_repository_not_authorized", "This private repository has no configured server authorization.");
            return identity;
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException) { throw Invalid(); }
    }
    public async Task<IReadOnlyList<CommitDto>> ReadHistoryAsync(GitHubIdentity repository, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(limit));
        var current = await ValidateAsync(repository.Owner, repository.Name, cancellationToken);
        if (current.Id != repository.Id) throw new RepositoryFailure(409, "repository_identity_changed", "The upstream repository identity changed. Ask the owner to reconcile its registration.");
        using var document = await Read(repository.Owner, repository.Name, "/commits?per_page=" + limit, cancellationToken);
        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > limit) throw Invalid();
            var commits = new List<CommitDto>();
            foreach (var item in root.EnumerateArray())
            {
                var sha = item.GetProperty("sha").GetString()!;
                var commit = item.GetProperty("commit");
                var author = commit.GetProperty("author");
                var authorName = author.GetProperty("name").GetString();
                var message = commit.GetProperty("message").GetString();
                if (sha is not { Length: 40 } || !sha.All(char.IsAsciiHexDigit) || authorName == null || message == null || !author.GetProperty("date").TryGetDateTimeOffset(out var date)) throw Invalid();
                commits.Add(new(sha, authorName, date, message.Split('\n')[0].TrimEnd('\r')));
            }
            return commits;
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException) { throw Invalid(); }
    }
    private bool Allowed(string owner, string name) => options.AllowedPrivateRepositories.Contains(owner + "/" + name, StringComparer.OrdinalIgnoreCase);
    private async Task<JsonDocument> Read(string owner, string name, string suffix, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Uri.EscapeDataString(owner) + "/" + Uri.EscapeDataString(name) + suffix);
        request.Headers.UserAgent.ParseAdd("DevPulse-M01/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (Allowed(owner, name) && !string.IsNullOrWhiteSpace(options.Token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.Forbidden &&
                    (response.Headers.RetryAfter != null || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"));
                var status = rateLimited ? 429 : response.StatusCode switch { HttpStatusCode.Unauthorized => 401, HttpStatusCode.Forbidden => 403, HttpStatusCode.NotFound => 404, _ => 502 };
                var retry = rateLimited ? Math.Clamp((int)(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60), 1, 3600) : (int?)null;
                throw new RepositoryFailure(status, rateLimited ? "github_rate_limited" : "github_unavailable", rateLimited ? "GitHub rate limit reached. Retry later." : "GitHub could not provide this repository. Check its identity and server authorization.", retry);
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (memory.Length + count > 2097152) throw Invalid();
                memory.Write(buffer, 0, count);
            }
            return JsonDocument.Parse(memory.ToArray());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new RepositoryFailure(504, "github_timeout", "GitHub did not respond within the allowed time."); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException) { throw Invalid(); }
    }
    private static RepositoryFailure Invalid() => new(502, "invalid_github_response", "GitHub returned an unavailable or invalid response.");
}
