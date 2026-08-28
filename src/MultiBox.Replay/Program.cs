using MultiBox.Core.Chat;
using MultiBox.Core.Model;
using MultiBox.Core.Parsing;
using MultiBox.Core.Stats;
using MultiBox.Core.Tailing;

namespace MultiBox.Replay;

/// <summary>
/// Command line companion to the dashboard.
///
/// "doctor" answers "will this work on my machine?" without launching the UI: it finds the
/// log folders, lists the characters it can see and reports what it can and cannot detect.
/// "replay" runs a saved session through the same parser the live app uses, which is how a
/// parsing problem gets diagnosed against a real log rather than guessed at.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "doctor";

        return command switch
        {
            "doctor" => Doctor(args.FirstOrDefault(a => !a.StartsWith('-') && a != command),
                               args.Contains("--esi")),
            "replay" => Replay(args.Length > 1 ? args[1] : null),
            "chat" => ReplayChat(args.Length > 1 ? args[1] : null),
            _ => Usage()
        };
    }

    private static int Usage()
    {
        Console.WriteLine("""
            multibox-replay <command> [path]

              doctor [logsRoot] [--esi]
                                  check log folders, list characters, report detectability;
                                  --esi also verifies each character id against ESI
              replay <folder>     parse every gamelog in a folder and summarise events
              chat   <folder>     parse chat logs and show the deduplicated stream
            """);
        return 1;
    }

    private static int Doctor(string? root, bool useEsi)
    {
        Console.WriteLine("MultiBox doctor");
        Console.WriteLine("===============\n");

        var gamelogs = root is not null ? Path.Combine(root, "Gamelogs") : LogDirectory.FindGamelogFolder();
        var chatlogs = root is not null ? Path.Combine(root, "Chatlogs") : LogDirectory.FindChatlogFolder();

        Console.WriteLine($"Gamelogs : {Describe(gamelogs)}");
        Console.WriteLine($"Chatlogs : {Describe(chatlogs)}");

        if (gamelogs is null || !Directory.Exists(gamelogs))
        {
            Console.WriteLine("\nCould not find EVE's Gamelogs folder. Checked:");
            foreach (var candidate in LogDirectory.CandidateRoots())
                Console.WriteLine("  " + candidate);
            Console.WriteLine("\nSet GamelogPath in multibox.json if your logs live elsewhere.");
            return 2;
        }

        var files = LogDirectory.LatestPerCharacter(gamelogs);
        var nameToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"\nCharacters found ({files.Count}):");
        foreach (var file in files.OrderBy(f => f.CharacterId))
        {
            var header = LogHeader.Parse(File.ReadLines(file.Path));
            Console.WriteLine($"  {header.Listener,-22} id={file.CharacterId,-12} session={file.SessionStart:u}");
            Console.WriteLine($"    window title expected: \"EVE - {header.Listener}\"");
            if (header.Listener is not null)
                nameToId[header.Listener] = file.CharacterId;
        }

        if (useEsi && nameToId.Count > 0)
        {
            Console.WriteLine("\nVerifying character ids against ESI...");
            try
            {
                using var esi = new MultiBox.Core.Esi.EsiClient();
                var mismatches = esi.VerifyAsync(nameToId).GetAwaiter().GetResult();
                if (mismatches.Count == 0)
                    Console.WriteLine("  all ids match the names in the log headers.");
                else
                    foreach (var mismatch in mismatches)
                        Console.WriteLine("  MISMATCH: " + mismatch);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ESI lookup failed (offline?): " + ex.Message);
            }
        }

        if (chatlogs is not null && Directory.Exists(chatlogs))
        {
            var channels = LogDirectory.LatestPerCharacter(chatlogs)
                .Select(f => f.Channel).Where(c => c is not null).Distinct().OrderBy(c => c);
            Console.WriteLine("\nChat channels seen: " + string.Join(", ", channels));
        }

        Console.WriteLine("\nDetectable from logs:");
        foreach (var type in Enum.GetValues<EwarType>())
            Console.WriteLine($"  {(EwarTypeInfo.IsLogged(type) ? "yes" : "NO "),-4} {EwarTypeInfo.DisplayName(type)}");

        Console.WriteLine("""

            Note: EVE writes no game-log line when a stasis web, target painter, sensor
            dampener or tracking disruptor is applied to you, so those cannot be detected
            from logs by any tool. They are shown greyed out in the dashboard.
            """);

        return 0;
    }

    private static int Replay(string? folder)
    {
        folder ??= LogDirectory.FindGamelogFolder();
        if (folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Give me a folder of gamelogs.");
            return 2;
        }

        var monitors = new Dictionary<string, CharacterMonitor>();
        var ewarCounts = new Dictionary<(string Victim, EwarType Type), int>();
        var seen = new HashSet<string>();
        var total = 0;
        var duplicates = 0;

        foreach (var path in Directory.EnumerateFiles(folder, "*.txt").OrderBy(p => p))
        {
            var parsed = LogFileName.TryParse(path);
            if (parsed is null || parsed.IsChatLog)
                continue;

            var header = LogHeader.Parse(File.ReadLines(path));
            if (header.Listener is null)
                continue;

            var parser = new GamelogParser(header.Listener);
            if (!monitors.TryGetValue(header.Listener, out var monitor))
                monitors[header.Listener] = monitor = new CharacterMonitor(header.Listener, parsed.CharacterId);

            foreach (var line in File.ReadLines(path))
            {
                if (parser.ParseLine(line) is not { } e)
                    continue;

                total++;
                if (!seen.Add(e.DedupKey()))
                {
                    duplicates++;
                    continue;
                }

                foreach (var m in monitors.Values)
                    m.Apply(e);

                if (e.Direction == Direction.Incoming && e.Ewar is { } type && e.Victim is { } victim)
                    ewarCounts[(victim, type)] = ewarCounts.GetValueOrDefault((victim, type)) + 1;
            }
        }

        Console.WriteLine($"parsed events   : {total}");
        Console.WriteLine($"cross-client dupes dropped: {duplicates} ({(total == 0 ? 0 : 100.0 * duplicates / total):F1}%)");
        Console.WriteLine($"distinct events : {seen.Count}\n");

        Console.WriteLine("Incoming EWAR by character:");
        if (ewarCounts.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var ((victim, type), count) in ewarCounts.OrderBy(kv => kv.Key.Victim).ThenBy(kv => kv.Key.Type))
                Console.WriteLine($"  {victim,-22} {EwarTypeInfo.DisplayName(type),-12} x{count}");
        }

        return 0;
    }

    private static int ReplayChat(string? folder)
    {
        folder ??= LogDirectory.FindChatlogFolder();
        if (folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Give me a folder of chat logs.");
            return 2;
        }

        var aggregator = new ChatAggregator(100000);
        var raw = 0;

        foreach (var path in Directory.EnumerateFiles(folder, "*.txt").OrderBy(p => p))
        {
            var parsed = LogFileName.TryParse(path);
            if (parsed?.Channel is null)
                continue;

            var header = LogHeader.Parse(File.ReadLines(path).Select(ChatlogParser.Clean));
            if (header.Listener is null)
                continue;

            var parser = new ChatlogParser(header.ChannelName ?? parsed.Channel, header.Listener);
            foreach (var line in File.ReadLines(path))
            {
                if (parser.ParseLine(line) is not { } message)
                    continue;
                raw++;
                aggregator.Add(message);
            }
        }

        var unified = aggregator.Messages.OrderBy(m => m.Timestamp).ToList();
        Console.WriteLine($"raw chat lines across all clients : {raw}");
        Console.WriteLine($"unified messages after dedup      : {unified.Count}");
        if (raw > 0)
            Console.WriteLine($"duplication removed               : {100.0 * (raw - unified.Count) / raw:F1}%\n");

        foreach (var message in unified.TakeLast(40))
            Console.WriteLine($"  [{message.Timestamp:HH:mm:ss}] {message.Channel,-10} {message.Sender,-18} > {message.Text}  (seen by {message.Witnesses.Count})");

        return 0;
    }

    private static string Describe(string? path) =>
        path is null ? "not found" : Directory.Exists(path) ? path : $"{path} (missing)";
}
