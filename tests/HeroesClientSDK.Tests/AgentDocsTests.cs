using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HeroesClientSDK.Tests;

/// <summary>
/// Keeps the agent docs in step with the tree: every folder under <c>.agents/skills</c> is a
/// first-party skill named in <c>AGENTS.md</c> or a vendored one pinned in <c>vendored.json</c>,
/// and every <c>SKILL.md</c> has front matter with its folder's name and a description.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class AgentDocsTests
{
    private static readonly string Root = RepoRoot();
    private static readonly string Skills = Path.Combine(Root, ".agents", "skills");

    [Fact]
    public void Skills_HaveFrontMatterWithTheirFolderNameAndADescription()
    {
        foreach (string folder in Directory.GetDirectories(Skills))
        {
            string file = Path.Combine(folder, "SKILL.md");
            Assert.True(File.Exists(file), folder + " has no SKILL.md.");
            string text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
            Match frontMatter = Regex.Match(text, @"\A---\n(.*?)\n---\n", RegexOptions.Singleline);
            Assert.True(frontMatter.Success, file + " does not start with --- front matter.");

            string yaml = frontMatter.Groups[1].Value;
            Match name = Regex.Match(yaml, @"^name:\s*(\S+)\s*$", RegexOptions.Multiline);
            Assert.True(name.Success, file + " has no name.");
            Assert.Equal(Path.GetFileName(folder), name.Groups[1].Value);
            Assert.Matches(new Regex(@"^description:\s*\S", RegexOptions.Multiline), yaml);
        }
    }

    /// <summary>
    /// Names that agents accept (lowercase letters, digits and hyphens, 2 to 64 characters), and a
    /// SKILL.md small enough to load inline (100 KB, as in HeroesReplay).
    /// </summary>
    [Fact]
    public void Skills_HaveLowercaseHyphenatedNamesAndAModestSize()
    {
        var name = new Regex("^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$");
        foreach (string folder in Directory.GetDirectories(Skills))
        {
            Assert.Matches(name, Path.GetFileName(folder));
            long size = new FileInfo(Path.Combine(folder, "SKILL.md")).Length;
            Assert.True(size <= 100_000, folder + "/SKILL.md is " + size + " bytes.");
        }
    }

    [Fact]
    public void Skills_AreFirstPartyOrPinnedInTheVendoredManifest()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Skills, "vendored.json"))
        );
        JsonElement root = manifest.RootElement;
        Assert.Matches("^[0-9a-f]{12,40}$", root.GetProperty("commit").GetString());
        Assert.True(
            File.Exists(Path.Combine(Skills, root.GetProperty("license").GetString())),
            "The vendored skills' license file is missing."
        );
        var vendored = root.GetProperty("skills")
            .EnumerateArray()
            .Select(skill => skill.GetString())
            .ToHashSet(StringComparer.Ordinal);

        string agents = File.ReadAllText(Path.Combine(Root, "AGENTS.md"));
        var firstParty = Regex
            .Matches(agents, @"\| `\.agents/skills/([a-z0-9-]+)` \|")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var folders = Directory
            .GetDirectories(Skills)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(vendored.Intersect(firstParty));
        Assert.Equal(
            folders.Order(StringComparer.Ordinal),
            vendored.Union(firstParty).Order(StringComparer.Ordinal)
        );

        // AGENTS.md lists the same vendored skills as the manifest.
        string section = agents.Substring(
            agents.IndexOf("## Vendored .NET skills", StringComparison.Ordinal)
        );
        var listed = Regex
            .Matches(section, "`([a-z0-9-]+)`")
            .Select(match => match.Groups[1].Value)
            .Where(folders.Contains)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(vendored.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Readme_LinksAgentsMdAndEveryRepoSkill()
    {
        string readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        string agents = File.ReadAllText(Path.Combine(Root, "AGENTS.md"));

        Assert.Contains("(AGENTS.md)", readme, StringComparison.Ordinal);
        foreach (Match skill in Regex.Matches(agents, @"\| `\.agents/skills/([a-z0-9-]+)` \|"))
        {
            string link = "(.agents/skills/" + skill.Groups[1].Value + "/SKILL.md)";
            Assert.True(
                readme.Contains(link, StringComparison.Ordinal),
                "README.md does not link " + link
            );
        }
    }

    /// <summary>
    /// Nothing from the client is committed: the reverse-engineering output stays ignored, and the
    /// skills hold only text (docs and scripts), never a binary.
    /// </summary>
    [Fact]
    public void ClientFiles_StayOutOfTheRepo()
    {
        string ignore = File.ReadAllText(Path.Combine(Root, ".gitignore"));
        foreach (string pattern in new[] { "re/", "*.dmp", "*.gpr", "*.rep/", "*.nupkg" })
        {
            Assert.True(
                Regex.IsMatch(
                    ignore,
                    "^" + Regex.Escape(pattern) + @"\s*$",
                    RegexOptions.Multiline
                ),
                ".gitignore does not ignore " + pattern
            );
        }

        var text = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".md",
            ".txt",
            ".json",
            ".java",
            ".ps1",
            ".py",
            ".sh",
            ".cs",
            ".xml",
            ".yml",
            ".yaml",
        };
        List<string> other = Directory
            .EnumerateFiles(Path.Combine(Root, ".agents"), "*", SearchOption.AllDirectories)
            .Where(file => !text.Contains(Path.GetExtension(file)))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToList();
        Assert.True(other.Count == 0, "Not a text file: " + string.Join(", ", other));
    }

    /// <summary>
    /// A Windows path written through an escaping layer loses its backslashes (HeroesReplay once
    /// had <c>C:\heroesreplay\app</c> turn into <c>C:heroesreplay</c>, a BEL character and
    /// <c>pp</c>). No doc keeps a control character or a drive letter without a separator.
    /// </summary>
    [Fact]
    public void Docs_KeepTheirWindowsPathSeparators()
    {
        var control = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]");
        var driveWithoutSeparator = new Regex(@"(?<![A-Za-z])[A-Z]:(?![\\/])[A-Za-z]");
        IEnumerable<string> docs = Directory
            .EnumerateFiles(Skills, "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(Root, "AGENTS.md"))
            .Append(Path.Combine(Root, "README.md"));

        var broken = new List<string>();
        foreach (string doc in docs)
        {
            string[] lines = File.ReadAllLines(doc);
            for (int i = 0; i < lines.Length; i++)
            {
                if (control.IsMatch(lines[i]) || driveWithoutSeparator.IsMatch(lines[i]))
                {
                    broken.Add(Path.GetRelativePath(Root, doc) + ":" + (i + 1));
                }
            }
        }

        Assert.True(
            broken.Count == 0,
            "A Windows path lost its separators, or a control character is in: "
                + string.Join(", ", broken)
        );
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HeroesClientSDK.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("HeroesClientSDK.slnx was not found.");
    }
}
