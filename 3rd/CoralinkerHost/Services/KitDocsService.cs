using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace CoralinkerHost.Services;

public sealed class KitDocsService
{
    private static readonly string[] PublishedSourceExtensions = [".md", ".cs", ".txt", ".json", ".py"];
    private readonly HostRuntimePaths _paths;
    private readonly HostAboutService _about;

    public KitDocsService(HostRuntimePaths paths, HostAboutService about)
    {
        _paths = paths;
        _about = about;
    }

    public KitDocsIndex GetIndex()
    {
        var root = ResolveMarkdownRoot();
        var version = GetVersion();
        var files = Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsPublishedDocFile)
                .Select(path => BuildDocFile(root, path, version))
                .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<KitDocFile>();

        return new KitDocsIndex(
            Version: version,
            MarkdownBaseUrl: "/api/docs/kit/md/",
            HtmlBaseUrl: "/docs/kit/",
            Files: files,
            Entry: files.FirstOrDefault(f => string.Equals(f.Path, "README.md", StringComparison.OrdinalIgnoreCase))?.Path
        );
    }

    public KitDocsResources GetResources()
    {
        var index = GetIndex();
        return new KitDocsResources(
            Version: index.Version,
            BundleUrl: $"/api/docs/kit/bundle.zip?v={Uri.EscapeDataString(index.Version)}",
            Entry: index.Entry ?? "README.md",
            RecommendedReadOrder:
            [
                "README.md",
                "00-system-overview.md",
                "01-quickstart.md",
                "09-agent-workflows.md",
                "10-agent-api.md",
                "11-multinode-system-design-reference.md",
                "runtime/fact-template.md",
                "tools/README.md",
                "02-logic-api.md",
                "04-variables-and-io.md",
                "06-remote-control.md"
            ],
            Files: index.Files.Select(file => new KitDocsResourceFile(
                file.Path,
                ResourceKindOf(file.Path),
                file.MarkdownUrl,
                file.HtmlUrl,
                file.SizeBytes,
                file.LastModifiedUtc
            )).ToArray()
        );
    }

    public KitDocsBundle BuildBundle()
    {
        var root = ResolveMarkdownRoot();
        var resources = GetResources();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in resources.Files)
            {
                var fullPath = ResolveUnderRoot(root, file.Path);
                if (fullPath == null || !File.Exists(fullPath) || !IsPublishedDocFile(fullPath))
                {
                    continue;
                }

                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTimeUtc(fullPath);
                using var entryStream = entry.Open();
                using var input = File.OpenRead(fullPath);
                input.CopyTo(entryStream);
            }

            var resourcesEntry = archive.CreateEntry("resources.json", CompressionLevel.Optimal);
            using var resourcesStream = resourcesEntry.Open();
            JsonSerializer.Serialize(resourcesStream, resources, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }

        var bytes = stream.ToArray();
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16] + "\"";
        var lastModified = resources.Files.Length == 0 ? DateTime.UtcNow : resources.Files.Max(f => f.LastModifiedUtc);
        return new KitDocsBundle("kit-docs-bundle.zip", "application/zip", bytes, etag, lastModified);
    }

    public KitDocContent? ReadMarkdown(string? path)
    {
        var relativePath = NormalizeDocPath(path, defaultPath: "README.md");
        var root = ResolveMarkdownRoot();
        var fullPath = ResolveUnderRoot(root, relativePath);
        if (fullPath == null || !File.Exists(fullPath) || !IsPublishedDocFile(fullPath))
        {
            return null;
        }

        var text = File.ReadAllText(fullPath, Encoding.UTF8);
        var etag = BuildEtag(text);
        return new KitDocContent(relativePath, ContentTypeOf(relativePath), text, etag, File.GetLastWriteTimeUtc(fullPath));
    }

    public KitDocContent? ReadHtml(string? path)
    {
        var relativePath = NormalizeDocPath(path, defaultPath: "README.html");
        if (relativePath.EndsWith("/", StringComparison.Ordinal))
        {
            relativePath += "README.html";
        }

        var requestedExtension = Path.GetExtension(relativePath);
        if (!string.IsNullOrWhiteSpace(requestedExtension)
            && !requestedExtension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            && !requestedExtension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return ReadPublishedSource(relativePath);
        }

        if (!relativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            relativePath += ".html";
        }

        var htmlRoot = ResolveHtmlRoot();
        var htmlPath = ResolveUnderRoot(htmlRoot, relativePath);
        if (htmlPath != null && File.Exists(htmlPath))
        {
            var html = File.ReadAllText(htmlPath, Encoding.UTF8);
            return new KitDocContent(relativePath, "text/html; charset=utf-8", html, BuildEtag(html), File.GetLastWriteTimeUtc(htmlPath));
        }

        var markdownPath = relativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^".html".Length] + ".md"
            : relativePath + ".md";
        var markdown = ReadMarkdown(markdownPath);
        if (markdown == null)
        {
            return null;
        }

        var htmlText = RenderHtml(markdown.Path, markdown.Text, GetIndex());
        return new KitDocContent(relativePath, "text/html; charset=utf-8", htmlText, BuildEtag(htmlText), markdown.LastModifiedUtc);
    }

    private KitDocContent? ReadPublishedSource(string relativePath)
    {
        var htmlRoot = ResolveHtmlRoot();
        var htmlPath = ResolveUnderRoot(htmlRoot, relativePath);
        if (htmlPath != null && File.Exists(htmlPath) && IsPublishedDocFile(htmlPath))
        {
            var text = File.ReadAllText(htmlPath, Encoding.UTF8);
            return new KitDocContent(relativePath, ContentTypeOf(relativePath), text, BuildEtag(text), File.GetLastWriteTimeUtc(htmlPath));
        }

        return ReadMarkdown(relativePath);
    }

    public string ResolveMarkdownRoot()
    {
        var published = Path.Combine(_paths.ContentRoot, "res", "docs", "kit", "md");
        if (Directory.Exists(published))
        {
            return published;
        }

        return Path.GetFullPath(Path.Combine(_paths.ContentRoot, "..", "CoralinkerKitDocs"));
    }

    public string ResolveHtmlRoot()
    {
        return Path.Combine(_paths.ContentRoot, "wwwroot", "docs", "kit");
    }

    public string GetVersion()
    {
        var about = _about.GetAbout().Backend;
        var commit = string.IsNullOrWhiteSpace(about.Commit) ? "dev" : about.Commit;
        var tag = string.IsNullOrWhiteSpace(about.Tag) ? null : about.Tag;
        return tag == null ? commit : $"{tag}-{commit}";
    }

    public string RenderHtml(string markdownPath, string markdown, KitDocsIndex index)
    {
        var title = ExtractTitle(markdown) ?? Path.GetFileNameWithoutExtension(markdownPath);
        var body = MarkdownToHtml(markdown);
        body = RewriteMarkdownLinksToHtml(body);
        var nav = BuildNav(index, markdownPath);
        var version = WebUtility.HtmlEncode(index.Version);
        var safeTitle = WebUtility.HtmlEncode(title);

        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{safeTitle}} - Coralinker Kit Docs</title>
              <style>
                :root { color-scheme: light dark; font-family: "Segoe UI", Arial, sans-serif; }
                body { margin: 0; line-height: 1.65; }
                header { padding: 16px 24px; border-bottom: 1px solid #8884; }
                main { display: grid; grid-template-columns: minmax(220px, 280px) minmax(0, 1fr); gap: 24px; max-width: 1280px; margin: 0 auto; padding: 24px; }
                nav { position: sticky; top: 16px; align-self: start; font-size: 14px; }
                nav a { display: block; padding: 4px 0; text-decoration: none; }
                article { max-width: 920px; }
                code { padding: 0.1em 0.25em; border-radius: 4px; background: #8882; }
                pre { overflow: auto; padding: 14px; border-radius: 8px; background: #8882; }
                pre code { padding: 0; background: transparent; }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #8885; padding: 6px 8px; vertical-align: top; }
                blockquote { margin-left: 0; padding-left: 16px; border-left: 4px solid #8886; color: #777; }
                .version { color: #777; font-size: 13px; }
                @media (max-width: 800px) { main { display: block; } nav { position: static; margin-bottom: 24px; } }
              </style>
            </head>
            <body>
              <header>
                <strong>Coralinker Kit Docs</strong>
                <span class="version">version={{version}}</span>
              </header>
              <main>
                <nav>
                  {{nav}}
                </nav>
                <article>
                  {{body}}
                </article>
              </main>
            </body>
            </html>
            """;
    }

    private KitDocFile BuildDocFile(string root, string fullPath, string version)
    {
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        return new KitDocFile(
            Path: relative,
            MarkdownUrl: $"/api/docs/kit/md/{relative}?v={Uri.EscapeDataString(version)}",
            HtmlUrl: $"/docs/kit/{HtmlPathOf(relative)}?v={Uri.EscapeDataString(version)}",
            SizeBytes: new FileInfo(fullPath).Length,
            LastModifiedUtc: File.GetLastWriteTimeUtc(fullPath)
        );
    }

    private static bool IsPublishedDocFile(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/');
        if (normalized.Contains("/_write_request/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return PublishedSourceExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase);
    }

    private static string ContentTypeOf(string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".md" => "text/markdown; charset=utf-8",
            ".cs" => "text/plain; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".py" => "text/x-python; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
    }

    private static string ResourceKindOf(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase)) return "tool";
        if (normalized.StartsWith("examples/", StringComparison.OrdinalIgnoreCase)) return "example";
        if (normalized.StartsWith("stubs/", StringComparison.OrdinalIgnoreCase)) return "stub";
        if (normalized.StartsWith("runtime/", StringComparison.OrdinalIgnoreCase)) return "runtime";
        return Path.GetExtension(normalized).Equals(".md", StringComparison.OrdinalIgnoreCase) ? "doc" : "resource";
    }

    private static string HtmlPathOf(string relativePath)
    {
        return Path.GetExtension(relativePath).Equals(".md", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(relativePath, ".html").Replace('\\', '/')
            : relativePath.Replace('\\', '/');
    }

    private static string? ResolveUnderRoot(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return full;
    }

    private static string NormalizeDocPath(string? path, string defaultPath)
    {
        path = string.IsNullOrWhiteSpace(path) ? defaultPath : path;
        path = path.Replace('\\', '/').TrimStart('/');
        if (path.Contains("..", StringComparison.Ordinal))
        {
            return defaultPath;
        }
        return path;
    }

    private static string BuildEtag(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return "\"" + Convert.ToHexString(hash).ToLowerInvariant()[..16] + "\"";
    }

    private static string? ExtractTitle(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }
        return null;
    }

    private static string BuildNav(KitDocsIndex index, string currentPath)
    {
        var sb = new StringBuilder();
        foreach (var file in index.Files.Where(f => !f.Path.StartsWith("examples/", StringComparison.OrdinalIgnoreCase)))
        {
            var label = WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(file.Path));
            var href = WebUtility.HtmlEncode(file.HtmlUrl);
            var current = string.Equals(file.Path, currentPath, StringComparison.OrdinalIgnoreCase) ? " aria-current=\"page\"" : "";
            sb.Append("<a href=\"").Append(href).Append('"').Append(current).Append('>').Append(label).AppendLine("</a>");
        }
        return sb.ToString();
    }

    private static string MarkdownToHtml(string markdown)
    {
        var sb = new StringBuilder();
        var inCode = false;
        var inList = false;
        var inTable = false;
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            sb.Append("<p>").Append(InlineMarkdown(paragraph.ToString().Trim())).AppendLine("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (!inList) return;
            sb.AppendLine("</ul>");
            inList = false;
        }

        void CloseTable()
        {
            if (!inTable) return;
            sb.AppendLine("</tbody></table>");
            inTable = false;
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                CloseTable();
                if (inCode)
                {
                    sb.AppendLine("</code></pre>");
                    inCode = false;
                }
                else
                {
                    sb.Append("<pre><code>");
                    inCode = true;
                }
                continue;
            }

            if (inCode)
            {
                sb.Append(WebUtility.HtmlEncode(line)).Append('\n');
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                CloseList();
                CloseTable();
                continue;
            }

            if (line.StartsWith("|", StringComparison.Ordinal) && line.EndsWith("|", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                if (line.Contains("---", StringComparison.Ordinal))
                {
                    continue;
                }
                var cells = line.Trim('|').Split('|').Select(c => InlineMarkdown(c.Trim())).ToArray();
                if (!inTable)
                {
                    sb.AppendLine("<table><tbody>");
                    inTable = true;
                }
                sb.Append("<tr>");
                foreach (var cell in cells)
                {
                    sb.Append("<td>").Append(cell).Append("</td>");
                }
                sb.AppendLine("</tr>");
                continue;
            }

            CloseTable();
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                var level = line.TakeWhile(c => c == '#').Count();
                if (level is >= 1 and <= 6 && line.Length > level && line[level] == ' ')
                {
                    var text = InlineMarkdown(line[(level + 1)..].Trim());
                    sb.Append("<h").Append(level).Append('>').Append(text).Append("</h").Append(level).AppendLine(">");
                    continue;
                }
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                if (!inList)
                {
                    sb.AppendLine("<ul>");
                    inList = true;
                }
                sb.Append("<li>").Append(InlineMarkdown(line[2..].Trim())).AppendLine("</li>");
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                sb.Append("<blockquote>").Append(InlineMarkdown(line[2..].Trim())).AppendLine("</blockquote>");
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }
            paragraph.Append(line.Trim());
        }

        FlushParagraph();
        CloseList();
        CloseTable();
        if (inCode)
        {
            sb.AppendLine("</code></pre>");
        }
        return sb.ToString();
    }

    private static string InlineMarkdown(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = Regex.Replace(encoded, @"`([^`]+)`", "<code>$1</code>");
        encoded = Regex.Replace(encoded, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
        encoded = Regex.Replace(encoded, @"\[([^\]]+)\]\(([^)]+)\)", match =>
        {
            var label = match.Groups[1].Value;
            var href = match.Groups[2].Value;
            return $"<a href=\"{href}\">{label}</a>";
        });
        return encoded;
    }

    private static string RewriteMarkdownLinksToHtml(string html)
    {
        return Regex.Replace(html, "href=\"([^\"]+?)\\.md(#[^\"]*)?(\\?[^\"]*)?\"", match =>
        {
            var path = match.Groups[1].Value;
            var hash = match.Groups[2].Value;
            var query = match.Groups[3].Value;
            return $"href=\"{path}.html{hash}{query}\"";
        }, RegexOptions.IgnoreCase);
    }
}

public sealed record KitDocsIndex(string Version, string MarkdownBaseUrl, string HtmlBaseUrl, KitDocFile[] Files, string? Entry);
public sealed record KitDocFile(string Path, string MarkdownUrl, string HtmlUrl, long SizeBytes, DateTime LastModifiedUtc);
public sealed record KitDocContent(string Path, string ContentType, string Text, string ETag, DateTime LastModifiedUtc);
public sealed record KitDocsResources(string Version, string BundleUrl, string Entry, string[] RecommendedReadOrder, KitDocsResourceFile[] Files);
public sealed record KitDocsResourceFile(string Path, string Kind, string MarkdownUrl, string HtmlUrl, long SizeBytes, DateTime LastModifiedUtc);
public sealed record KitDocsBundle(string FileName, string ContentType, byte[] Bytes, string ETag, DateTime LastModifiedUtc);
