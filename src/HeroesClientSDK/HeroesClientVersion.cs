using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace HeroesClientSDK;

/// <summary>
/// A Heroes of the Storm client build, such as <c>2.57.0.98304</c>. It is always optional: every
/// API that takes one accepts null, which means "detect it from the process, or use the generic
/// path". A version is only used where reading genuinely has to branch by build.
/// </summary>
/// <param name="Major">The first number, for example 2.</param>
/// <param name="Minor">The second number, for example 57.</param>
/// <param name="Revision">The third number, for example 0.</param>
/// <param name="Build">The build number, for example 98304.</param>
public sealed record HeroesClientVersion(int Major, int Minor, int Revision, int Build)
    : IComparable<HeroesClientVersion>
{
    /// <summary>The main patch line, the first two numbers (for example <c>2.57</c>).</summary>
    public string PatchLine => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    /// <summary>
    /// Parses <c>2.57.0.98304</c> (also with commas or spaces, as Windows file versions may be
    /// written). Returns null, never throws, for anything else.
    /// </summary>
    public static HeroesClientVersion TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Trim()
            .Split(new[] { '.', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return null;
        }

        int[] numbers = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (
                !int.TryParse(
                    parts[i],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[i]
                )
            )
            {
                return null;
            }
        }

        return new HeroesClientVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>
    /// The file version of an exe (for example a <c>Versions\Base*\HeroesOfTheStorm_x64.exe</c>),
    /// or null when the file is missing or has no version.
    /// </summary>
    public static HeroesClientVersion FromFile(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return null;
            }

            return TryParse(FileVersionInfo.GetVersionInfo(exePath).FileVersion);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public int CompareTo(HeroesClientVersion other)
    {
        if (other is null)
        {
            return 1;
        }

        int compared = Major.CompareTo(other.Major);
        if (compared == 0)
        {
            compared = Minor.CompareTo(other.Minor);
        }

        if (compared == 0)
        {
            compared = Revision.CompareTo(other.Revision);
        }

        return compared == 0 ? Build.CompareTo(other.Build) : compared;
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Revision}.{Build}");
}
