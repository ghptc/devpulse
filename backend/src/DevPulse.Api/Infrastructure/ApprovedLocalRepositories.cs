using DevPulse.Api.Application;
namespace DevPulse.Api.Infrastructure;
public sealed class ApprovedLocalRepositories(RepositoryOptions options, IAuthorizedPathPolicy paths) : IApprovedLocalRepositories
{
    public IReadOnlyList<RepositoryAlias> List() => options.Aliases.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new RepositoryAlias(x.Key, x.Key)).ToArray();
    public AuthorizedRepository Resolve(string alias, CancellationToken ct)
    {
        if (!options.Aliases.TryGetValue(alias, out var path)) throw new RepositoryFailure(403, "alias_not_authorized", "Select an administrator-approved repository alias.");
        return paths.Authorize(path, ct);
    }
}
