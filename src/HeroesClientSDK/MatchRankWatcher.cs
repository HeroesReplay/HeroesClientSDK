using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesClientSDK;

/// <summary>A new Storm League result, raised by <see cref="MatchRankWatcher"/>.</summary>
public sealed class MatchRankEventArgs : EventArgs
{
    /// <summary>A result of <paramref name="processId"/>, seen at <paramref name="observedAt"/>.</summary>
    public MatchRankEventArgs(
        int processId,
        RankResult result,
        HeroesClientVersion clientVersion,
        DateTimeOffset observedAt
    )
    {
        ProcessId = processId;
        Result = result;
        ClientVersion = clientVersion;
        ObservedAt = observedAt;
    }

    /// <summary>The client process.</summary>
    public int ProcessId { get; }

    /// <summary>The result: the rank before and after, and the points change.</summary>
    public RankResult Result { get; }

    /// <summary>The client's build, or null when unknown.</summary>
    public HeroesClientVersion ClientVersion { get; }

    /// <summary>When the watcher first saw this result (UTC).</summary>
    public DateTimeOffset ObservedAt { get; }
}

/// <summary>
/// Watches every running Heroes of the Storm client and raises <see cref="ResultAvailable"/> once
/// for each new Storm League result (<see cref="MatchRank"/>), with one reader per client process.
/// A result is new when its end-of-game record or its values differ from the last one raised for
/// that process, so a score screen read many times raises once, and so does a record that stays
/// after the player leaves the score screen. A result that is already there when the watcher
/// first sees a client is raised too, so a caller that starts late still gets the last game.
/// <para>
/// Call <see cref="Poll()"/> from your own loop, or <see cref="RunAsync"/> to poll every
/// <see cref="Interval"/> until cancelled. Read-only, like every reader.
/// </para>
/// </summary>
public sealed class MatchRankWatcher : IDisposable
{
    /// <summary>The client's process name.</summary>
    public const string ProcessName = "HeroesOfTheStorm_x64";

    /// <summary>The default polling interval.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    private readonly object gate = new();
    private readonly HeroesClientOptions options;
    private readonly TimeProvider time;
    private readonly Dictionary<int, Watched> clients = new();

    /// <summary>
    /// A watcher that polls every <paramref name="interval"/> (<see cref="DefaultInterval"/> when
    /// null) with readers built from <paramref name="options"/>.
    /// </summary>
    public MatchRankWatcher(TimeSpan? interval = null, HeroesClientOptions options = null)
    {
        Interval = interval ?? DefaultInterval;
        if (Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "The interval must be positive."
            );
        }

        this.options = options;
        time = options?.TimeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised once for each new result, on the thread that polled.</summary>
    public event EventHandler<MatchRankEventArgs> ResultAvailable;

    /// <summary>How often <see cref="RunAsync"/> polls.</summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// Reads every running client once, raises <see cref="ResultAvailable"/> for each new result
    /// and returns them. Clients that have exited are forgotten.
    /// </summary>
    public IReadOnlyList<MatchRankEventArgs> Poll()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return Poll(
                processes
                    .Select(process =>
                        (process.Id, (Func<MatchRank, MatchRankSample>)(rank => rank.Read(process)))
                    )
                    .ToList()
            );
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Polls every <see cref="Interval"/> until <paramref name="cancellationToken"/> is cancelled,
    /// then returns. An exception from a <see cref="ResultAvailable"/> handler ends the run.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Poll();
            try
            {
                await Task.Delay(Interval, time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One pass over <paramref name="running"/>: each client's id and how its reader reads it.
    /// </summary>
    internal IReadOnlyList<MatchRankEventArgs> Poll(
        IReadOnlyList<(int ProcessId, Func<MatchRank, MatchRankSample> Read)> running
    )
    {
        var raised = new List<MatchRankEventArgs>();
        lock (gate)
        {
            foreach ((int processId, Func<MatchRank, MatchRankSample> read) in running)
            {
                if (!clients.TryGetValue(processId, out Watched watched))
                {
                    watched = new Watched(new MatchRank(options));
                    clients[processId] = watched;
                }

                MatchRankSample sample = read(watched.Rank);
                if (watched.Start != watched.Rank.ProcessStart)
                {
                    // The pid was reused by a new client: its results are its own.
                    watched.Start = watched.Rank.ProcessStart;
                    watched.Record = 0;
                    watched.Result = null;
                }

                if (
                    !sample.Ok
                    || (
                        watched.Record == watched.Rank.LastRecord && watched.Result == sample.Result
                    )
                )
                {
                    continue;
                }

                watched.Record = watched.Rank.LastRecord;
                watched.Result = sample.Result;
                raised.Add(
                    new MatchRankEventArgs(
                        processId,
                        sample.Result,
                        sample.ClientVersion,
                        time.GetUtcNow()
                    )
                );
            }

            var ids = new HashSet<int>(running.Select(client => client.ProcessId));
            foreach (int gone in clients.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                clients[gone].Rank.Dispose();
                clients.Remove(gone);
            }
        }

        foreach (MatchRankEventArgs result in raised)
        {
            ResultAvailable?.Invoke(this, result);
        }

        return raised;
    }

    /// <summary>Closes every reader's process handle.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            foreach (Watched watched in clients.Values)
            {
                watched.Rank.Dispose();
            }

            clients.Clear();
        }
    }

    private sealed class Watched
    {
        public Watched(MatchRank rank) => Rank = rank;

        public MatchRank Rank { get; }

        public long Start { get; set; }

        public long Record { get; set; }

        public RankResult Result { get; set; }
    }
}
