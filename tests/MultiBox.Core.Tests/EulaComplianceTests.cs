using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace MultiBox.Core.Tests;

/// <summary>
/// Enforces the EULA boundary in CI rather than by memory.
///
/// EVE's EULA prohibits reading client memory, capturing the screen, injecting code and
/// automating input. Those are absent today; these tests make a future change that
/// introduces one fail the build instead of shipping silently.
///
/// See docs/EULA-COMPLIANCE.md.
/// </summary>
public class EulaComplianceTests
{
    private readonly ITestOutputHelper _output;

    public EulaComplianceTests(ITestOutputHelper output) => _output = output;

    /// <summary>Shipped source only - test files may legitimately mention these names.</summary>
    private static IEnumerable<string> ShippedSourceFiles()
    {
        var src = Path.Combine(SampleLogs.Root, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    public static TheoryData<string, string> ProhibitedApis() => new()
    {
        // Reading the game's memory.
        { "ReadProcessMemory", "reads another process's memory" },
        { "WriteProcessMemory", "writes another process's memory" },
        { "OpenProcess", "opens a handle to another process" },
        { "NtReadVirtualMemory", "reads another process's memory" },
        { "VirtualQueryEx", "inspects another process's memory layout" },
        { "CreateRemoteThread", "injects code into another process" },

        // Capturing the screen.
        { "BitBlt", "captures screen content" },
        { "PrintWindow", "captures a window's rendered content" },
        { "CopyFromScreen", "captures screen content" },
        { "GetWindowDC", "obtains a device context for reading pixels" },
        { "GetPixel", "samples screen pixels" },

        // Sending input to the game, including multi-client broadcasting.
        { "SendInput", "synthesises input events" },
        { "keybd_event", "synthesises keyboard input" },
        { "mouse_event", "synthesises mouse input" },
        { "SendMessage", "can deliver input to another window" },
        { "PostMessage", "can deliver input to another window" },
        { "SendKeys", "synthesises keystrokes" },
        { "SetWindowsHookEx", "installs a system-wide hook" },
        { "SetWindowPos", "moves, resizes or reorders another window" },
    };

    // SetForegroundWindow and ShowWindow were deliberately removed from the list above when
    // thumbnail click-to-focus was added. They raise one window that the user just clicked,
    // which is what eve-o-preview does and is not what the EULA prohibits: the prohibition is
    // on automating gameplay and broadcasting input, and every input API remains banned. The
    // allow-list below is what keeps that decision from silently widening.

    [Theory]
    [MemberData(nameof(ProhibitedApis))]
    public void ShippedCodeNeverReferencesAProhibitedApi(string api, string why)
    {
        var offenders = ShippedSourceFiles()
            .Where(path => File.ReadAllText(path).Contains(api, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(SampleLogs.Root, path))
            // NativeMethods.cs names them in a comment listing what must stay out.
            .Where(rel => !rel.EndsWith("NativeMethods.cs", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"'{api}' ({why}) appears in: {string.Join(", ", offenders)}. " +
            "This would breach the EVE EULA - see docs/EULA-COMPLIANCE.md.");
    }

    /// <summary>
    /// The native surface is an allow-list. A new DllImport outside it fails here, which
    /// forces a deliberate decision rather than an accidental capability.
    /// </summary>
    [Fact]
    public void NativeApiSurfaceIsExactlyTheApprovedReadOnlySet()
    {
        var approved = new HashSet<string>(StringComparer.Ordinal)
        {
            "EnumWindows",
            "IsWindowVisible",
            "GetWindowTextLength",
            "GetWindowText",
            "GetWindowRect",
            "GetForegroundWindow",

            // Raises the single client whose thumbnail was clicked. See the note above.
            "SetForegroundWindow",
            "ShowWindow",
            "IsIconic",

            // Fast Screen Switcher hotkeys. RegisterHotKey reserves specific combinations and
            // reports only those; unlike a keyboard hook it cannot observe other keystrokes,
            // and it cannot send any. SetWindowsHookEx stays banned above for that reason.
            "RegisterHotKey",
            "UnregisterHotKey",

            // DWM thumbnails. These are the one group that is not a query: they tell the
            // compositor where to draw a preview. They are still incapable of affecting the
            // source window, and they return no image data - see docs/EULA-COMPLIANCE.md.
            "DwmRegisterThumbnail",
            "DwmUnregisterThumbnail",
            "DwmUpdateThumbnailProperties",
            "DwmQueryThumbnailSourceSize"
        };

        var declared = new List<string>();
        var pattern = new Regex(@"static\s+extern\s+[\w\.\<\>\[\]\?]+\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

        foreach (var path in ShippedSourceFiles())
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(path)))
                declared.Add(m.Groups["name"].Value);
        }

        foreach (var name in declared.Distinct().OrderBy(n => n))
            _output.WriteLine($"declared native call: {name}");

        var unexpected = declared.Where(n => !approved.Contains(n)).Distinct().ToList();

        Assert.True(unexpected.Count == 0,
            $"Unapproved native call(s): {string.Join(", ", unexpected)}. " +
            "Every native call must be a read-only window query - see docs/EULA-COMPLIANCE.md.");
    }

    /// <summary>
    /// Files the EVE client owns must only ever be opened for reading. FileShare.ReadWrite
    /// is required (EVE holds a write lock) but must never be paired with write access.
    /// </summary>
    [Fact]
    public void EveOwnedFilesAreOpenedReadOnly()
    {
        foreach (var path in ShippedSourceFiles())
        {
            var text = File.ReadAllText(path);
            var rel = Path.GetRelativePath(SampleLogs.Root, path);

            foreach (Match m in Regex.Matches(text, @"new FileStream\((?<args>[^;]*?)\)"))
            {
                var args = m.Groups["args"].Value;
                Assert.False(args.Contains("FileAccess.Write") || args.Contains("FileAccess.ReadWrite"),
                    $"{rel} opens a FileStream with write access: {args.Trim()}");
            }
        }
    }

    /// <summary>
    /// The only network destination is CCP's public API. Anything else would mean log
    /// contents leaving the machine.
    /// </summary>
    [Fact]
    public void TheOnlyOutboundHostsAreCcpPublicServices()
    {
        var allowedHosts = new[] { "esi.evetech.net", "images.evetech.net" };

        foreach (var path in ShippedSourceFiles())
        {
            var rel = Path.GetRelativePath(SampleLogs.Root, path);
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"https?://(?<host>[\w\.\-]+)"))
            {
                var host = m.Groups["host"].Value;
                // Documentation links in comments are not requests.
                if (host is "dotnet.microsoft.com" or "github.com" or "schemas.microsoft.com"
                         or "learn.microsoft.com" or "dot.net")
                    continue;

                Assert.True(allowedHosts.Contains(host),
                    $"{rel} references unexpected host '{host}'.");
            }
        }
    }
}
