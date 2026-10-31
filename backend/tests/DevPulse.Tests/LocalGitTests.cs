using System.Diagnostics;
using DevPulse.Api.Application;
using DevPulse.Api.Infrastructure;
using Xunit;

namespace DevPulse.Tests;

public sealed class GitFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "devpulse-" + Guid.NewGuid().ToString("N"));
    public RepositoryOptions Options { get; }
    public AuthorizedPathPolicy Policy { get; }
    public ControlledGitRepositoryInspector Inspector { get; }
    public AuthorizedRepository Repository => Policy.Authorize(Root, default);
    public GitFixture()
    {
        Directory.CreateDirectory(Root);
        Options = new() { AuthorizedRoots = [Root], Aliases = new() { ["sample"] = Root } };
        Policy = new(Options);
        Inspector = new(Options, Policy);
        Git("init", "--initial-branch=main");
        Git("config", "user.name", "Test Author");
        Git("config", "user.email", "test@example.invalid");
    }
    public string Git(params string[] args)
    {
        var start = new ProcessStartInfo("/usr/bin/git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr);
        return stdout.Trim();
    }
    public void Dispose() => Directory.Delete(Root, true);
}
public sealed class LocalGitTests
{
    [Fact] public async Task ReadsRealHistoryWithBoundedCountAndMetadata()
    {
        using var fixture = new GitFixture();
        for (var i = 0; i < 23; i++) fixture.Git("commit", "--allow-empty", "-m", "Commit " + i);
        var commits = await fixture.Inspector.ReadHistoryAsync(fixture.Repository, 20, default);
        Assert.Equal(20, commits.Count);
        Assert.Equal(fixture.Git("rev-parse", "HEAD"), commits[0].Sha);
        Assert.Equal("Commit 22", commits[0].Message);
        Assert.Equal("Test Author", commits[0].Author);
        Assert.Single(await fixture.Inspector.ReadHistoryAsync(fixture.Repository, 1, default));
    }
    [Fact] public async Task UnbornRepositoryReturnsEmpty()
    {
        using var fixture = new GitFixture();
        Assert.Empty(await fixture.Inspector.ReadHistoryAsync(fixture.Repository, 20, default));
    }
    [Fact] public async Task BrokenDetachedHeadDoesNotMasqueradeAsEmpty()
    {
        using var fixture = new GitFixture();
        File.WriteAllText(Path.Combine(fixture.Root, ".git", "HEAD"), new string('a', 40) + "\n");
        Assert.Equal(502, (await Assert.ThrowsAsync<RepositoryFailure>(() => fixture.Inspector.ReadHistoryAsync(fixture.Repository, 20, default))).Status);
    }
    [Fact] public void OnlyApprovedAliasesAndOrdinaryRepositoryLayoutsAreAllowed()
    {
        using var fixture = new GitFixture();
        var local = new ApprovedLocalRepositories(fixture.Options, fixture.Policy);
        Assert.Equal(fixture.Root, local.Resolve("sample", default).Path);
        Assert.Throws<RepositoryFailure>(() => local.Resolve(fixture.Root, default));
        fixture.Options.AuthorizedRoots = [];
        Assert.Throws<RepositoryFailure>(() => local.Resolve("sample", default));
    }
    [Fact] public void RejectsLinkedMetadataAndConfigIncludes()
    {
        using var fixture = new GitFixture();
        File.AppendAllText(Path.Combine(fixture.Root, ".git", "config"), "\n[include]\npath = /tmp/other\n");
        Assert.Throws<RepositoryFailure>(() => fixture.Policy.Authorize(fixture.Root, default));
    }
    [Theory]
    [InlineData("malformed")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\u0000Author\u0000bad-date\u0000Subject\u0000")]
    public void MalformedOutputIsRejected(string output) => Assert.Throws<RepositoryFailure>(() => ControlledGitRepositoryInspector.Parse(output, 20));
    [Fact] public async Task CancellationPropagates()
    {
        using var fixture = new GitFixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Inspector.ReadHistoryAsync(fixture.Repository, 20, cancelled.Token));
    }
    [Fact] public async Task HangingGitIsTimedOut()
    {
        using var fixture = new GitFixture();
        var binary = Path.Combine(Directory.GetCurrentDirectory(), "slow-git-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(binary, "#!/bin/sh\nexec /bin/sleep 30\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        fixture.Options.GitExecutable = binary;
        fixture.Options.TimeoutMilliseconds = 100;
        try
        {
            var error = await Assert.ThrowsAsync<RepositoryFailure>(() => fixture.Inspector.ValidateAsync(fixture.Repository, default));
            Assert.Equal(504, error.Status);
        }
        finally { File.Delete(binary); }
    }
}
