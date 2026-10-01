namespace Rune.Engine;

/// <summary>
/// What one launch of Rune asked for, parsed from its arguments:
/// <c>Rune.exe [file.pdf ...] [--page N] [--zoom Z] [--new-window]</c>.
///
/// Explorer passes a file path; <c>--page</c> and <c>--zoom</c> exist for
/// scripted testing (tools/capture-demo.ps1); <c>--new-window</c> opts a launch
/// out of single-instancing, so it gets a window of its own instead of a tab
/// in the one already open.
///
/// Pure parsing, no file-system checks, so the test project can cover it. The
/// window decides what to do with a path that does not exist.
/// </summary>
public sealed record LaunchRequest(
    IReadOnlyList<string> Paths,
    int? Page,
    double? Zoom,
    bool NewWindow)
{
    public static readonly LaunchRequest Empty = new([], null, null, false);

    public bool HasPaths => Paths.Count > 0;

    /// <param name="args">Arguments without the executable (argv[1..]).</param>
    public static LaunchRequest Parse(IReadOnlyList<string> args)
    {
        var paths = new List<string>();
        int? page = null;
        double? zoom = null;
        bool newWindow = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.Equals("--page", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count && int.TryParse(args[i + 1], out int p))
                {
                    page = p;
                    i++;
                }
            }
            else if (arg.Equals("--zoom", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count &&
                    double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double z))
                {
                    zoom = z;
                    i++;
                }
            }
            else if (arg.Equals("--new-window", StringComparison.OrdinalIgnoreCase))
            {
                newWindow = true;
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                // Unknown switch: ignore rather than mistake it for a file name.
            }
            else
            {
                paths.Add(arg);
            }
        }

        return new LaunchRequest(paths, page, zoom, newWindow);
    }
}
