using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Optim.Core.Tests;

/// <summary>
/// Hard gate on repository authorship: this project has exactly one contributor,
/// so every commit must be authored by the owner and must not hand credit to
/// anyone else. A stray "Co-authored-by" trailer (added by an editor, a template,
/// or a tool) is enough to make GitHub list a second contributor, and once that
/// attribution is in shared history it cannot be removed without a rewrite.
///
/// The sibling hook (scripts/hooks/commit-msg) rejects the same things before a
/// commit is even created; this test is the backstop that also catches commits
/// made with hooks disabled or from another machine. It runs in CI because
/// build.yml invokes the test project.
/// </summary>
public class CommitAuthorshipTests
{
    /// <summary>
    /// The owner's commit identity: GitHub's noreply address for the account, in
    /// both the plain form and the numeric-prefixed form GitHub issues. Only the
    /// email is matched because that is what GitHub maps to an account; the
    /// display name on a commit does not create a contributor.
    /// </summary>
    private static readonly Regex OwnerEmail = new(
        @"^(\d+\+)?soupashh-ship-it@users\.noreply\.github\.com$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Trailers that credit a second author, which GitHub turns into a second
    /// contributor on the repo's contributor graph.
    /// </summary>
    private static readonly Regex CoAuthorTrailer = new(
        @"^\s*co-authored-by\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>One commit as the gate needs to see it.</summary>
    private sealed record CommitInfo(string Hash, string AuthorName, string AuthorEmail, string Message);

    private static readonly Lazy<IReadOnlyList<CommitInfo>> CommitLog = new(ReadCommits);

    [Fact]
    public void Every_commit_is_authored_by_the_repo_owner()
    {
        var offenders = CommitLog.Value
            .Where(c => !OwnerEmail.IsMatch(c.AuthorEmail))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Commits authored by someone other than the repo owner found. This project is "
            + "single-author: fix the commit identity (git config user.name / user.email) and "
            + "rewrite the offending commits before pushing, otherwise GitHub lists a second "
            + "contributor:\n  "
            + string.Join("\n  ", offenders.Select(c =>
                $"{c.Hash[..Math.Min(8, c.Hash.Length)]}  {c.AuthorName} <{c.AuthorEmail}>")));
    }

    [Fact]
    public void No_commit_carries_a_co_author_trailer()
    {
        var offenders = CommitLog.Value
            .Where(c => CoAuthorTrailer.IsMatch(c.Message))
            .Select(c => (Commit: c, Trailer: CoAuthorTrailer.Match(c.Message).Value.Trim()))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Commit messages credit another author. This project is single-author, so the "
            + "trailer must be removed (amend the commit if it was not pushed yet):\n  "
            + string.Join("\n  ", offenders.Select(o =>
                $"{o.Commit.Hash[..Math.Min(8, o.Commit.Hash.Length)]}  \"{o.Trailer}\"")));
    }

    /// <summary>
    /// Reads the local history through the git CLI. An environment without git, or
    /// without history (a source export), is not something this gate can judge, so
    /// it reports nothing and the tests pass there rather than failing spuriously.
    /// </summary>
    private static IReadOnlyList<CommitInfo> ReadCommits()
    {
        if (!Directory.Exists(Path.Combine(RepoPaths.Root(), ".git")))
        {
            return Array.Empty<CommitInfo>();
        }

        // Field separator \x1f, record separator \x1e: the message body contains
        // newlines, so line-oriented parsing would mis-split it.
        const string format = "%H%x1f%an%x1f%ae%x1f%B%x1e";
        if (!TryGit($"log --no-color --format={format}", out var output))
        {
            return Array.Empty<CommitInfo>();
        }

        var commits = new List<CommitInfo>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\n', '\r').Split('\x1f');
            if (fields.Length < 4)
            {
                continue;
            }

            commits.Add(new CommitInfo(fields[0].Trim(), fields[1], fields[2], fields[3]));
        }

        return commits;
    }

    private static bool TryGit(string arguments, out string output)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = RepoPaths.Root(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            });

            if (process is null)
            {
                output = string.Empty;
                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15_000);
            return process.ExitCode == 0;
        }
        catch
        {
            // git missing or unrunnable: nothing to assert against.
            output = string.Empty;
            return false;
        }
    }
}
