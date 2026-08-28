using System.Text;

namespace MultiBox.Core.Tailing;

/// <summary>
/// Follows a log file as EVE appends to it.
///
/// Two constraints shape this: the client keeps the file open with a write lock, so it must
/// be opened with <see cref="FileShare.ReadWrite"/> or every read throws; and the logs live
/// under OneDrive, whose sync can make change notifications unreliable, so this polls on an
/// interval rather than trusting FileSystemWatcher alone.
/// </summary>
public sealed class LogTailer : IDisposable
{
    private readonly FileStream _stream;
    private readonly StreamReader _reader;
    private StringBuilder _partial = new();

    public LogTailer(string path, Encoding? encoding = null, bool fromStart = true)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // detectEncodingFromByteOrderMarks handles game logs (UTF-8) and chat logs (UTF-16LE)
        // without the caller having to know which kind of file this is.
        _reader = new StreamReader(_stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: encoding is null);

        if (!fromStart)
            _stream.Seek(0, SeekOrigin.End);
    }

    public string Path { get; }

    /// <summary>
    /// Returns whole lines appended since the last call. A trailing partial line is held
    /// back until its newline arrives, so a half-flushed line is never parsed.
    /// </summary>
    public IReadOnlyList<string> ReadNewLines()
    {
        var lines = new List<string>();

        int ch;
        while ((ch = _reader.Read()) >= 0)
        {
            var c = (char)ch;
            if (c == '\n')
            {
                lines.Add(_partial.ToString().TrimEnd('\r'));
                _partial.Clear();
            }
            else
            {
                _partial.Append(c);
            }
        }

        return lines;
    }

    /// <summary>Any buffered text not yet terminated by a newline.</summary>
    public string PendingText => _partial.ToString();

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
