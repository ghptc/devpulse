using System.Diagnostics;
using System.Globalization;
using System.Text;
using DevPulse.Api.Application;

namespace DevPulse.Api.Infrastructure;

public sealed class ControlledGitRepositoryInspector(RepositoryOptions options, IAuthorizedPathPolicy paths) : IGitRepositoryInspector
{
    private int cleanupFaulted;
    public async Task ValidateAsync(AuthorizedRepository repository, CancellationToken cancellationToken)
    {
        var result = await Run(repository, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (result.Code != 0 || result.Output.Trim() != "true") throw Failed();
    }

    public async Task<IReadOnlyList<CommitDto>> ReadHistoryAsync(AuthorizedRepository repository, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(limit));
        await ValidateAsync(repository, cancellationToken);
        var head = await Run(repository, ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], cancellationToken);
        if (head.Code != 0)
        {
            var symbolic = await Run(repository, ["symbolic-ref", "--quiet", "HEAD"], cancellationToken);
            var reference = symbolic.Output.Trim();
            if (symbolic.Code == 0 && reference.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                var exists = await Run(repository, ["show-ref", "--verify", "--quiet", reference], cancellationToken);
                if (exists.Code == 1) return [];
            }
            throw Failed();
        }
        var result = await Run(repository, ["log", "--max-count=" + limit.ToString(CultureInfo.InvariantCulture), "--topo-order", "--no-decorate", "--no-notes", "--format=%H%x00%an%x00%aI%x00%s", "-z", "HEAD", "--"], cancellationToken);
        if (result.Code != 0) throw Failed();
        return Parse(result.Output, limit);
    }

    public static IReadOnlyList<CommitDto> Parse(string output, int limit)
    {
        if (output.Length == 0) throw Failed();
        var fields = output.Split('\0');
        if (fields[^1] != "" || (fields.Length - 1) % 4 != 0 || (fields.Length - 1) / 4 > limit) throw Failed();
        var commits = new List<CommitDto>();
        for (var i = 0; i < fields.Length - 1; i += 4)
        {
            if (fields[i].Length is not (40 or 64) || !fields[i].All(char.IsAsciiHexDigit) ||
                !DateTimeOffset.TryParseExact(fields[i + 2], "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw Failed();
            commits.Add(new(fields[i], fields[i + 1], date, fields[i + 3]));
        }
        return commits;
    }

    private async Task<(int Code, string Output)> Run(AuthorizedRepository repository, string[] arguments, CancellationToken ct)
    {
        if (Volatile.Read(ref cleanupFaulted) != 0) throw new RepositoryFailure(503, "git_cleanup_failed", "Inspection is unavailable until the administrator verifies process cleanup.");
        paths.Authorize(repository.Path, ct);
        if (options.TimeoutMilliseconds is < 1 or > 30000 || options.OutputLimitBytes is < 1024 or > 2097152 || !Path.IsPathFullyQualified(options.GitExecutable)) throw Failed();
        using var timeout = new CancellationTokenSource(options.TimeoutMilliseconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var start = new ProcessStartInfo(options.GitExecutable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.Environment.Clear();
        foreach (var pair in new Dictionary<string, string> { ["PATH"] = "/usr/bin:/bin", ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null", ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_OPTIONAL_LOCKS"] = "0", ["GIT_NO_REPLACE_OBJECTS"] = "1", ["LC_ALL"] = "C.UTF-8" }) start.Environment[pair.Key] = pair.Value;
        foreach (var arg in new[] { "--no-pager", "--no-optional-locks", "-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null", "-c", "log.showSignature=false", "-C", repository.Path }.Concat(arguments)) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        var started = false;
        try
        {
            started = process.Start();
            if (!started) throw Failed();
            var consumed = 0;
            async Task<byte[]> Drain(Stream stream)
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[4096];
                int count;
                while ((count = await stream.ReadAsync(chunk, linked.Token)) != 0)
                {
                    if (Interlocked.Add(ref consumed, count) > options.OutputLimitBytes) { linked.Cancel(); throw Failed(); }
                    buffer.Write(chunk, 0, count);
                }
                return buffer.ToArray();
            }
            var stdout = Drain(process.StandardOutput.BaseStream);
            var stderr = Drain(process.StandardError.BaseStream);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(linked.Token));
            return (process.ExitCode, new UTF8Encoding(false, true).GetString(await stdout));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new RepositoryFailure(504, "git_timeout", "Repository inspection exceeded its execution limit."); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or DecoderFallbackException or IOException) { throw Failed(); }
        finally
        {
            try
            {
                if (started && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                Interlocked.Exchange(ref cleanupFaulted, 1);
                throw new RepositoryFailure(503, "git_cleanup_failed", "Inspection is unavailable until the administrator verifies process cleanup.");
            }
        }
    }
    private static RepositoryFailure Failed() => new(502, "git_inspection_failed", "The authorized repository could not be inspected safely.");
}
