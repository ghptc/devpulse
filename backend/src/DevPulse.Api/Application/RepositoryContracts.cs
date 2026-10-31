namespace DevPulse.Api.Application;

public enum RepositoryProvider { LocalGit, GitHub }
public sealed record RepositoryRequest(string? Provider, string? Alias = null, string? Owner = null, string? Name = null);
public sealed record RepositoryDto(Guid Id, string Name, string Provider, string Scope);
public sealed record CommitDto(string Sha, string Author, DateTimeOffset AuthoredAt, string Message);
public sealed record HistoryDto(Guid RepositoryId, string Provider, IReadOnlyList<CommitDto> Commits, int Count);
public sealed record AuthorizedRepository(string Path, string Name);
public sealed record GitHubIdentity(long Id, string Owner, string Name, string DefaultBranch, bool IsPrivate);
public sealed record RegisteredRepository(Guid Id, string Identity, RepositoryProvider Provider, string Name, string Scope, string? Alias, GitHubIdentity? GitHub);
public sealed record RepositoryAlias(string Alias, string Name);

public sealed class RepositoryFailure(int status, string code, string safeMessage, int? retryAfterSeconds = null) : Exception(safeMessage)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
public interface IAuthorizedPathPolicy { AuthorizedRepository Authorize(string? path, CancellationToken cancellationToken); }
public interface IApprovedLocalRepositories
{
    IReadOnlyList<RepositoryAlias> List();
    AuthorizedRepository Resolve(string alias, CancellationToken cancellationToken);
}
public interface IRepositoryRegistry
{
    RegisteredRepository Register(string identity, RepositoryProvider provider, string name, string scope, string? alias, GitHubIdentity? github);
    RegisteredRepository? Find(Guid id);
}
public interface IGitRepositoryInspector
{
    Task ValidateAsync(AuthorizedRepository repository, CancellationToken cancellationToken);
    Task<IReadOnlyList<CommitDto>> ReadHistoryAsync(AuthorizedRepository repository, int limit, CancellationToken cancellationToken);
}
public interface IGitHubRepositoryInspector
{
    Task<GitHubIdentity> ValidateAsync(string owner, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<CommitDto>> ReadHistoryAsync(GitHubIdentity repository, int limit, CancellationToken cancellationToken);
}
public sealed class RepositoryApplicationService(IApprovedLocalRepositories local, IRepositoryRegistry registry, IGitRepositoryInspector git, IGitHubRepositoryInspector github)
{
    public async Task<RepositoryDto> RegisterAsync(RepositoryRequest request, CancellationToken ct)
    {
        if (request.Provider is not ("LocalGit" or "GitHub")) throw Invalid();
        var provider = Enum.Parse<RepositoryProvider>(request.Provider);
        RegisteredRepository registered;
        if (provider == RepositoryProvider.LocalGit)
        {
            if (string.IsNullOrWhiteSpace(request.Alias) || request.Alias.Length > 80 || request.Owner != null || request.Name != null) throw Invalid();
            var repository = local.Resolve(request.Alias, ct);
            await git.ValidateAsync(repository, ct);
            var identity = OperatingSystem.IsWindows() ? repository.Path.ToUpperInvariant() : repository.Path;
            registered = registry.Register("LocalGit:" + identity, provider, repository.Name, "Registered local HEAD", request.Alias, null);
        }
        else
        {
            if (request.Alias != null || !ValidIdentity(request.Owner, 39) || !ValidIdentity(request.Name, 100)) throw Invalid();
            var repository = await github.ValidateAsync(request.Owner!, request.Name!, ct);
            registered = registry.Register("GitHub:" + repository.Id, provider, repository.Owner + "/" + repository.Name, "Default branch: " + repository.DefaultBranch, null, repository);
        }
        return Describe(registered);
    }
    public async Task<HistoryDto> GetHistoryAsync(Guid id, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 20) throw Invalid();
        var registration = registry.Find(id) ?? throw new RepositoryFailure(404, "repository_not_registered", "Repository is not registered. Register it again after a server restart.");
        IReadOnlyList<CommitDto> commits;
        if (registration.Provider == RepositoryProvider.LocalGit)
        {
            var repository = local.Resolve(registration.Alias!, ct);
            var identity = "LocalGit:" + (OperatingSystem.IsWindows() ? repository.Path.ToUpperInvariant() : repository.Path);
            if (identity != registration.Identity) throw new RepositoryFailure(403, "repository_authorization_changed", "The approved repository mapping changed. Register it again.");
            commits = await git.ReadHistoryAsync(repository, limit, ct);
        }
        else commits = await github.ReadHistoryAsync(registration.GitHub!, limit, ct);
        if (commits.Count > limit) throw new RepositoryFailure(502, "invalid_provider_response", "The provider returned an invalid commit count.");
        return new(id, registration.Provider.ToString(), commits, commits.Count);
    }
    public static bool ValidIdentity(string? value, int max) => value is { Length: > 0 } && value.Length <= max && value is not ("." or "..") && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && value[0] != '-';
    private static RepositoryDto Describe(RegisteredRepository r) => new(r.Id, r.Name, r.Provider.ToString(), r.Scope);
    private static RepositoryFailure Invalid() => new(400, "invalid_request", "Choose a supported provider and its required identity fields. Limit must be between 1 and 20.");
}
