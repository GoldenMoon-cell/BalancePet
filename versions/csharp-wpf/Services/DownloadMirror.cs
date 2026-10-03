using System.IO;

namespace BalancePet.Wpf.Services;

/// <summary>
/// The domestic copy of the program's own release assets, when there is one.
/// </summary>
/// <remarks>
/// The update package is fifty to eighty megabytes and it is the one download that has no
/// other way round the network. Measured here: three public mirrors cut every connection at
/// the same three-second mark, jsDelivr does not serve release assets at all and caps a file
/// at twenty megabytes, and GitHub's own asset endpoint answers 403 without a token because
/// the unauthenticated allowance is sixty requests an hour. What is left is GitHub itself,
/// which works but drops often enough that a七十 megabyte download is a matter of patience,
/// and a copy somewhere domestic.
///
/// The mirror is asked for <em>first</em> rather than as a fallback, which is the opposite
/// of how the repository files are treated: those are small and GitHub's copy is the
/// authoritative one, while this exists precisely because GitHub is the slow path here. If
/// the mirror does not answer, GitHub still does, and the digest decides whether what
/// arrived is the package either way.
///
/// Empty until the mirror repository exists. Everything below is inert while it is, so the
/// only change needed to turn it on is the one line.
/// </remarks>
public static class DownloadMirror
{
    /// <summary>
    /// Where the mirrored releases live, without a trailing slash and without the tag.
    /// </summary>
    /// <remarks>
    /// A Gitee repository's releases, which answer at
    /// <c>{base}/{tag}/{file}</c> — the same shape GitHub uses, deliberately, so a mirrored
    /// asset keeps the address it had.
    /// </remarks>
    public const string Base = "";

    /// <summary>Whether a mirror is configured at all.</summary>
    public static bool Configured => Base.Length > 0;

    /// <summary>
    /// The mirror's address for one asset, then GitHub's.
    /// </summary>
    /// <param name="official">The address the release names.</param>
    /// <param name="tag">The release tag, which is part of the mirrored path.</param>
    /// <param name="mirrorBase">Overrides <see cref="Base"/>, so the shape can be tested.</param>
    /// <remarks>
    /// Both addresses name the same bytes, which is what lets one be abandoned for the
    /// other part way through a file: the download resumes from the length already on disk
    /// regardless of which host supplied it, and the caller's digest check is what decides
    /// whether the result is the package.
    /// </remarks>
    public static IReadOnlyList<Uri> Candidates(Uri official, string? tag, string? mirrorBase = null)
    {
        var candidates = new List<Uri>();
        var file = Path.GetFileName(official.AbsolutePath);
        var root = (mirrorBase ?? Base).Trim();
        if (root.Length > 0 && !string.IsNullOrWhiteSpace(tag) && file.Length > 0)
        {
            var mirrored = $"{root.TrimEnd('/')}/{Uri.EscapeDataString(tag.Trim())}/{Uri.EscapeDataString(file)}";
            if (Uri.TryCreate(mirrored, UriKind.Absolute, out var mirror) && mirror.Scheme == Uri.UriSchemeHttps)
                candidates.Add(mirror);
        }
        candidates.Add(official);
        return candidates;
    }
}
