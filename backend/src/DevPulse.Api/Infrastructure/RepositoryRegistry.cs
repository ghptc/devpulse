using DevPulse.Api.Application;
using System.Security.Cryptography;
using System.Text;
namespace DevPulse.Api.Infrastructure;

// Single-owner, bounded, process-local registrations. IDs are stable identity hashes, not authorization tokens.
public sealed class RepositoryRegistry : IRepositoryRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, RegisteredRepository> entries = [];
    public RegisteredRepository Register(string identity, RepositoryProvider provider, string name, string scope, string? alias, GitHubIdentity? github)
    {
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
        lock (gate)
        {
            if (entries.ContainsKey(id)) throw new RepositoryFailure(409, "repository_already_registered", "This repository is already registered.");
            if (entries.Count >= 1024) throw new RepositoryFailure(429, "registration_limit", "Registration limit reached.");
            var entry = new RegisteredRepository(id, identity, provider, name, scope, alias, github);
            entries.Add(id, entry); return entry;
        }
    }
    public RegisteredRepository? Find(Guid id) { lock (gate) return entries.GetValueOrDefault(id); }
}
