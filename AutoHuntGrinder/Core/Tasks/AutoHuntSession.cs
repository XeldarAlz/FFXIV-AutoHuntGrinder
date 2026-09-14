using AutoHuntGrinder.Core.Custom;
using AutoHuntGrinder.Core.HuntingLog;
using AutoHuntGrinder.Core.Hunts;
using ECommons.DalamudServices;
using System.Threading;

namespace AutoHuntGrinder.Core.Tasks;

public sealed class AutoHuntSession
{
    private readonly BillLedger[] ledgers;
    private readonly LogLedger[] logLedgers;
    private readonly MobLedger[] mobLedgers;
    private readonly string[] billNames;
    private readonly HashSet<uint> givenUpObjectives = [];
    private readonly HashSet<uint> notices = [];

    private HuntWallet lastWallet;
    private bool walletKnown;

    private long pausedMs;
    private long pauseStartedAtMs;
    private DateTime? endedAt;

    public AutoHuntSession(IReadOnlyList<HuntBill> bills)
    {
        Mode = HuntMode.MarkBills;
        ledgers = new BillLedger[bills.Count];
        billNames = new string[bills.Count];
        for (var index = 0; index < bills.Count; index++)
        {
            ledgers[index] = new BillLedger(bills[index].MarkIndex);
            billNames[index] = bills[index].Name;
        }

        logLedgers = [];
        mobLedgers = [];
        JobAbbreviation = CurrentJobAbbreviation();
        Rebaseline();
    }

    public AutoHuntSession(IReadOnlyList<byte> huntingLogSlots)
    {
        Mode = HuntMode.HuntingLog;
        logLedgers = new LogLedger[huntingLogSlots.Count];
        billNames = new string[huntingLogSlots.Count];
        for (var index = 0; index < huntingLogSlots.Count; index++)
        {
            logLedgers[index] = new LogLedger(huntingLogSlots[index]);
            billNames[index] = HuntingLogRegistry.BookName(huntingLogSlots[index]);
        }

        ledgers = [];
        mobLedgers = [];
        JobAbbreviation = CurrentJobAbbreviation();
        Rebaseline();
    }

    // Every listed mob gets a ledger, so one switched on mid-run still counts; the names are the mobs that still needed
    // kills when the run started.
    public AutoHuntSession(IReadOnlyList<CustomMobEntry> customMobs)
    {
        Mode = HuntMode.CustomList;
        var tracked = new List<MobLedger>(customMobs.Count);
        var names = new List<string>(customMobs.Count);
        for (var index = 0; index < customMobs.Count; index++)
        {
            var entry = customMobs[index];
            if (entry.NameId == 0)
            {
                continue;
            }

            tracked.Add(new MobLedger(entry.NameId));
            if (entry.Enabled && entry.Killed < entry.Needed)
            {
                names.Add(CustomMobCatalog.NameOf(entry.NameId));
            }
        }

        mobLedgers = [.. tracked];
        billNames = [.. names];
        ledgers = [];
        logLedgers = [];
        JobAbbreviation = CurrentJobAbbreviation();
        Rebaseline();
    }

    public HuntMode Mode { get; }

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public string JobAbbreviation { get; private set; }

    // The bills, Hunting Logs or custom mobs the run works, by name.
    public IReadOnlyList<string> BillNames => billNames;

    // Every credited kill, whatever the mode: marks on bills, Hunting Log targets or custom mobs.
    public int MarksKilled { get; private set; }

    public int BillsCompleted { get; private set; }

    public int AlliedSeals { get; private set; }

    public int CenturioSeals { get; private set; }

    public int Nuts { get; private set; }

    public bool EndedWithFault { get; private set; }

    public bool CompletedByStopCondition;

    internal bool Recorded;
    internal bool AfterActionDispatched;

    // Run-scoped rather than kept on the hunt task, because every Resume and fault restart builds a new task.
    internal HashSet<uint> GivenUpNameIds { get; } = [];
    internal bool UnhuntableReported;
    internal int HuntPassesCompleted;

    public bool DidNothing
        => MarksKilled == 0 && BillsCompleted == 0 && AlliedSeals == 0 && CenturioSeals == 0 && Nuts == 0;

    public int Seals => AlliedSeals + CenturioSeals;

    public TimeSpan Elapsed => (endedAt ?? DateTime.UtcNow) - StartedAt - TimeSpan.FromMilliseconds(PausedTotalMs);

    public double MarksPerHour => Elapsed.TotalHours > 0 ? MarksKilled / Elapsed.TotalHours : 0;

    private long PausedTotalMs
        => pausedMs + (pauseStartedAtMs == 0 ? 0 : Environment.TickCount64 - pauseStartedAtMs);

    // Keyed per objective, so a Hunting Log target given up in one log does not hide the same mob in another.
    internal bool IsGivenUp(in HuntObjective objective) => givenUpObjectives.Contains(ObjectiveProgress.Key(objective));

    internal void GiveUp(in HuntObjective objective) => givenUpObjectives.Add(ObjectiveProgress.Key(objective));

    // True the first time a key comes up in this run, so a notice is not repeated after a Resume or a fault restart.
    internal bool Notice(uint key) => notices.Add(key);

    public void Sample()
    {
        SampleSources(credit: true);
        SampleWallet(credit: true);
        if (JobAbbreviation.Length == 0)
        {
            JobAbbreviation = CurrentJobAbbreviation();
        }
    }

    // Makes the current game state the new zero point, so progress made while the run was paused is not credited to it.
    public void Rebaseline()
    {
        SampleSources(credit: false);
        SampleWallet(credit: false);
    }

    // A Stop unwinds the run loop through the same catch as a genuine fault, and only a genuine fault may resume the run.
    public void RecordFault(Exception exception, CancellationToken cancelToken)
    {
        if (cancelToken.IsCancellationRequested || exception is OperationCanceledException)
        {
            return;
        }

        EndedWithFault = true;
    }

    internal void ClearFault() => EndedWithFault = false;

    public void BeginPause()
    {
        if (pauseStartedAtMs != 0)
        {
            return;
        }

        pauseStartedAtMs = Environment.TickCount64;
    }

    public void EndPause()
    {
        if (pauseStartedAtMs == 0)
        {
            return;
        }

        pausedMs += Environment.TickCount64 - pauseStartedAtMs;
        pauseStartedAtMs = 0;
    }

    // Freezes the clock, so a finished run left on screen during its after-run action stops counting.
    internal void End()
    {
        if (endedAt is not null)
        {
            return;
        }

        EndPause();
        endedAt = DateTime.UtcNow;
    }

    private void SampleSources(bool credit)
    {
        switch (Mode)
        {
            case HuntMode.HuntingLog:
                HuntingLogReader.Refresh(force: true);
                for (var index = 0; index < logLedgers.Length; index++)
                {
                    SampleLog(ref logLedgers[index], credit);
                }

                break;
            case HuntMode.CustomList:
                for (var index = 0; index < mobLedgers.Length; index++)
                {
                    SampleMob(ref mobLedgers[index], credit);
                }

                break;
            default:
                MarkBillReader.Refresh(force: true);
                for (var index = 0; index < ledgers.Length; index++)
                {
                    SampleBill(ref ledgers[index], credit);
                }

                break;
        }
    }

    private void SampleBill(ref BillLedger ledger, bool credit)
    {
        var status = MarkBillReader.Status(ledger.MarkIndex);
        // The reader reports every bill as locked while the character is not loaded, so the last known state stands.
        if (status == BillStatus.Locked)
        {
            return;
        }

        var targets = MarkBillReader.Targets(ledger.MarkIndex);
        if (credit && ledger.Known)
        {
            Credit(ref ledger, status, targets);
        }
        else
        {
            ledger.Settled = !IsHeld(status) || AllCleared(targets);
        }

        ledger.Known = true;
        ledger.Status = status;
        ledger.TargetCount = targets.Length;
        if (ledger.Targets.Length < targets.Length)
        {
            ledger.Targets = new HuntTarget[targets.Length];
        }

        targets.CopyTo(ledger.Targets);
    }

    // The reader reports a log as unavailable while the character is not loaded, or when it belongs to another Grand
    // Company, so the last known total stands. Only rises count, as with currency.
    private void SampleLog(ref LogLedger ledger, bool credit)
    {
        if (HuntingLogReader.Status(ledger.Slot) == HuntingLogStatus.Unavailable)
        {
            return;
        }

        var total = BookKills(ledger.Slot);
        if (credit && ledger.Known && total > ledger.Total)
        {
            MarksKilled += total - ledger.Total;
        }

        ledger.Total = total;
        ledger.Known = true;
    }

    // A reset from the list editor drops the count, which re-baselines the ledger instead of crediting anything.
    private void SampleMob(ref MobLedger ledger, bool credit)
    {
        var killed = CustomKilled(ledger.NameId);
        if (killed < 0)
        {
            return;
        }

        if (credit && ledger.Known && killed > ledger.Killed)
        {
            MarksKilled += killed - ledger.Killed;
        }

        ledger.Killed = killed;
        ledger.Known = true;
    }

    private void Credit(ref BillLedger ledger, BillStatus status, ReadOnlySpan<HuntTarget> targets)
    {
        var held = IsHeld(status);
        if (IsHeld(ledger.Status))
        {
            if (held && IsSameBill(in ledger, targets))
            {
                MarksKilled += KillsGained(in ledger, targets);
                SettleIfCleared(ref ledger, targets);
                return;
            }

            // A held bill only leaves the held set, or swaps its marks, once its last mark falls.
            SettleFinished(ref ledger);
        }

        if (!held)
        {
            return;
        }

        // A bill is picked up with no kills on it, so every kill it already shows came after the pickup.
        ledger.Settled = false;
        MarksKilled += KillsCredited(targets);
        SettleIfCleared(ref ledger, targets);
    }

    private void SettleFinished(ref BillLedger ledger)
    {
        if (ledger.Settled)
        {
            return;
        }

        MarksKilled += KillsRemaining(in ledger);
        BillsCompleted++;
        ledger.Settled = true;
    }

    private void SettleIfCleared(ref BillLedger ledger, ReadOnlySpan<HuntTarget> targets)
    {
        if (ledger.Settled || !AllCleared(targets))
        {
            return;
        }

        BillsCompleted++;
        ledger.Settled = true;
    }

    private void SampleWallet(bool credit)
    {
        if (!HuntWallet.TryRead(out var wallet))
        {
            return;
        }

        if (credit && walletKnown)
        {
            AlliedSeals += CurrencyGained(lastWallet.AlliedSeals, wallet.AlliedSeals);
            CenturioSeals += CurrencyGained(lastWallet.CenturioSeals, wallet.CenturioSeals);
            Nuts += CurrencyGained(lastWallet.Nuts, wallet.Nuts);
        }

        lastWallet = wallet;
        walletKnown = true;
    }

    // Only rises count, because a spend between two samples would otherwise cancel out currency the run earned.
    private static int CurrencyGained(int before, int after) => after > before ? after - before : 0;

    // A finished rank reads full, so the sum only grows as the log advances even though the game resets its counts
    // whenever a rank opens.
    private static int BookKills(byte slot)
    {
        if (!HuntingLogRegistry.TryGetBook(slot, out var book))
        {
            return 0;
        }

        var total = 0;
        for (byte rank = 0; rank < book.RankCount; rank++)
        {
            total += HuntingLogReader.RankProgress(slot, rank).Killed;
        }

        return total;
    }

    // -1 when the mob is no longer on the list.
    private static int CustomKilled(uint nameId)
    {
        var entries = Plugin.Instance.Configuration.CustomMobs;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].NameId == nameId)
            {
                return entries[index].Killed;
            }
        }

        return -1;
    }

    private static bool IsHeld(BillStatus status) => status is BillStatus.Held or BillStatus.Stale;

    private static bool IsSameBill(in BillLedger ledger, ReadOnlySpan<HuntTarget> targets)
    {
        if (ledger.TargetCount != targets.Length)
        {
            return false;
        }

        for (var slot = 0; slot < targets.Length; slot++)
        {
            var before = ledger.Targets[slot];
            var now = targets[slot];
            // Kill counts only grow on one bill, so a drop means a new bill took the slot.
            if (before.TargetRowId != now.TargetRowId || now.Killed < before.Killed)
            {
                return false;
            }
        }

        return true;
    }

    private static int KillsGained(in BillLedger ledger, ReadOnlySpan<HuntTarget> targets)
    {
        var gained = 0;
        for (var slot = 0; slot < targets.Length; slot++)
        {
            gained += Math.Max(0, Credited(targets[slot]) - Credited(ledger.Targets[slot]));
        }

        return gained;
    }

    private static int KillsCredited(ReadOnlySpan<HuntTarget> targets)
    {
        var credited = 0;
        for (var slot = 0; slot < targets.Length; slot++)
        {
            credited += Credited(targets[slot]);
        }

        return credited;
    }

    private static int KillsRemaining(in BillLedger ledger)
    {
        var remaining = 0;
        for (var slot = 0; slot < ledger.TargetCount; slot++)
        {
            remaining += ledger.Targets[slot].Remaining;
        }

        return remaining;
    }

    private static bool AllCleared(ReadOnlySpan<HuntTarget> targets)
    {
        if (targets.Length == 0)
        {
            return false;
        }

        for (var slot = 0; slot < targets.Length; slot++)
        {
            if (!targets[slot].Done)
            {
                return false;
            }
        }

        return true;
    }

    private static int Credited(in HuntTarget target) => Math.Min(target.Killed, target.Needed);

    private static string CurrentJobAbbreviation()
        => Svc.Objects.LocalPlayer?.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? string.Empty;

    private struct BillLedger(byte markIndex)
    {
        public readonly byte MarkIndex = markIndex;
        public bool Known;
        public bool Settled;
        public BillStatus Status;
        public int TargetCount;
        public HuntTarget[] Targets = [];
    }

    private struct LogLedger(byte slot)
    {
        public readonly byte Slot = slot;
        public bool Known;
        public int Total;
    }

    private struct MobLedger(uint nameId)
    {
        public readonly uint NameId = nameId;
        public bool Known;
        public int Killed;
    }
}
