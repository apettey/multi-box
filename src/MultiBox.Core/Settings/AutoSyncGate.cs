namespace MultiBox.Core.Settings;

/// <summary>
/// Decides when an automatic settings sync is safe and worth doing.
///
/// EVE reads its settings files at login and writes them back at logout, so a copy only
/// sticks if it lands while no client is open - and it is only useful if it lands before the
/// next one opens. Closing the last client is the moment that satisfies both. MultiBox does
/// not launch clients, so it cannot hook "before launch" directly; syncing as soon as the
/// fleet is down means the files are already in place whenever the next launch happens.
///
/// A sync is due when all of these hold:
/// <list type="bullet">
/// <item>no client window has existed for <see cref="Settle"/>;</item>
/// <item>no settings file has been written for <see cref="Settle"/>, because a closing client
/// can flush its files a moment after its window goes;</item>
/// <item>some settings file changed since the last sync, so an idle machine does not rewrite
/// identical files every tick.</item>
/// </list>
/// </summary>
public sealed class AutoSyncGate
{
    private DateTime? _clearSince;

    public AutoSyncGate(TimeSpan? settle = null) => Settle = settle ?? TimeSpan.FromSeconds(10);

    public TimeSpan Settle { get; }

    /// <param name="nowUtc">Current time.</param>
    /// <param name="openClients">EVE windows open right now, including the character-select screen.</param>
    /// <param name="newestWriteUtc">Latest write time across the source and target files.</param>
    /// <param name="lastSyncUtc">When the last sync ran, or null if it never has.</param>
    public bool ShouldSync(DateTime nowUtc, int openClients, DateTime newestWriteUtc, DateTime? lastSyncUtc)
    {
        if (openClients > 0)
        {
            _clearSince = null;
            return false;
        }

        _clearSince ??= nowUtc;
        if (nowUtc - _clearSince.Value < Settle)
            return false;

        if (nowUtc - newestWriteUtc < Settle)
            return false;

        return lastSyncUtc is null || newestWriteUtc > lastSyncUtc.Value;
    }
}
