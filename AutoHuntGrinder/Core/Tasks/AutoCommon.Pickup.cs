using AutoHuntGrinder.Core.Game.Ops;
using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Travel;
using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;
using PlayerHelpers = ECommons.GameHelpers.Player;

namespace AutoHuntGrinder.Core.Tasks;

public abstract partial class AutoCommon
{
    private const float BoardArriveWithinMeters = 4f;
    private const int BoardTravelAttempts = 2;
    private const int BillPickupAttempts = 3;
    private const int BoardObjectWaitMs = 10_000;
    private const int BoardApproachWatchdogMs = 20_000;
    private const float BoardApproachToleranceMeters = 2f;
    // A board hangs on a wall, so the walk aims at the reachable floor in front of it rather than at the board itself.
    private const float BoardStandSearchMeters = 5f;
    private const int BoardReadyTimeoutMs = 10_000;
    private const int BoardTargetSettleMs = 1_000;
    private const int BoardMenuOpenTimeoutMs = 5_000;
    private const int BoardWindowOpenTimeoutMs = 5_000;
    private const int BillAcceptClicks = 2;
    private const int BillAcceptSettleMs = 5_000;
    private const int BillHeldConfirmMs = 5_000;
    private const int BoardWindowsCloseTimeoutMs = 5_000;
    private const int BoardWindowPollMs = 250;
    private const int BoardRetryDelayMs = 1_000;
    private const int BoardUiCheckFrames = 2;
    private const int BillHeldCheckFrames = 10;

    private enum BillAcceptOutcome { Accepted, Declined, Failed }

    // The board in the current city goes first, since reaching it needs no teleport.
    protected async Task<int> PickUpBills(IReadOnlyList<HuntBill> bills)
    {
        MarkBillReader.Refresh(force: true);
        var boardOfBill = new int[bills.Count];
        var boardNeeded = new bool[HuntBoards.Count];
        var pending = 0;
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            boardOfBill[billIndex] = HuntBoards.NoBoard;
            var bill = bills[billIndex];
            var status = MarkBillReader.Status(bill.MarkIndex);
            if (status != BillStatus.Available)
            {
                Diag($"Pickup: {bill.Name} (mark {bill.MarkIndex}) is {status}; no board visit needed");
                continue;
            }

            if (!HuntBoards.TryChoose(bill.MarkIndex, out var boardIndex))
            {
                Warn($"Pickup: no hunt board posts {bill.Name} (mark {bill.MarkIndex}) for grand company {PlayerHelpers.GrandCompany}; skipping it");
                continue;
            }

            boardOfBill[billIndex] = boardIndex;
            boardNeeded[boardIndex] = true;
            pending++;
            Diag($"Pickup: {bill.Name} (mark {bill.MarkIndex}) is posted on the board in {TerritoryNames.Of(HuntBoards.Get(boardIndex).TerritoryId)}");
        }

        if (pending == 0)
        {
            Diag("Pickup: no selected bill is waiting on a board");
            return 0;
        }

        Diag($"Pickup: {pending} bill(s) to pick up, starting in territory {Svc.ClientState.TerritoryType}");
        var pickedUp = 0;
        var firstBoard = NeededBoardIn(boardNeeded, Svc.ClientState.TerritoryType);
        if (firstBoard != HuntBoards.NoBoard)
        {
            boardNeeded[firstBoard] = false;
            pickedUp += await PickUpAtBoard(firstBoard, bills, boardOfBill);
        }

        for (var boardIndex = 0; boardIndex < boardNeeded.Length; boardIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                break;
            }

            if (!boardNeeded[boardIndex])
            {
                continue;
            }

            pickedUp += await PickUpAtBoard(boardIndex, bills, boardOfBill);
        }

        MarkBillReader.Refresh(force: true);
        Diag($"Pickup: picked up {pickedUp} of {pending} bill(s)");
        return pickedUp;
    }

    private async Task<int> PickUpAtBoard(int boardIndex, IReadOnlyList<HuntBill> bills, int[] boardOfBill)
    {
        var board = HuntBoards.Get(boardIndex);
        var cityName = TerritoryNames.Of(board.TerritoryId);
        var scope = $"pickup-board-{board.ObjectId}";
        if (!HuntBoards.TryGetPosition(boardIndex, out var position))
        {
            Warn($"Pickup: the hunt board in {cityName} has no known position; skipping its bills");
            return 0;
        }

        Diag($"{scope}: the hunt board in {cityName} ({board.TerritoryId}) stands at {FormatPosition(position)}");
        if (!await ReachBoard(boardIndex, position, cityName, scope))
        {
            return 0;
        }

        var pickedUp = 0;
        for (var billIndex = 0; billIndex < bills.Count; billIndex++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                break;
            }

            if (boardOfBill[billIndex] != boardIndex)
            {
                continue;
            }

            if (await PickUpBill(bills[billIndex], boardIndex, cityName))
            {
                pickedUp++;
            }
        }

        return pickedUp;
    }

    private async Task<bool> ReachBoard(int boardIndex, Vector3 position, string cityName, string scope)
    {
        var board = HuntBoards.Get(boardIndex);
        if (board.ShardId != 0 && !ZoneAetherytes.IsAttuned(board.ShardId))
        {
            Diag($"{scope}: {HuntBoards.ShardName(boardIndex)} is not attuned, so the aethernet cannot shorten the walk to the board");
        }

        var arrived = false;
        for (var attempt = 1; attempt <= BoardTravelAttempts && !arrived; attempt++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            Status = $"Heading to the hunt board in {cityName}";
            Diag($"{scope}: travelling to the board (attempt {attempt}/{BoardTravelAttempts})");
            arrived = await TravelTo(board.TerritoryId, position, BoardArriveWithinMeters);
        }

        if (!arrived)
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"Pickup: could not reach the hunt board in {cityName}; skipping its bills");
            }

            return false;
        }

        Diag($"{scope}: at the board, {DistanceTo(position):F1}m away ({ConditionTag()})");
        return true;
    }

    private async Task<bool> PickUpBill(HuntBill bill, int boardIndex, string cityName)
    {
        var markIndex = bill.MarkIndex;
        for (var attempt = 1; attempt <= BillPickupAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            MarkBillReader.Refresh(force: true);
            var status = MarkBillReader.Status(markIndex);
            if (status == BillStatus.Held)
            {
                Diag($"Pickup: {bill.Name} is held");
                return true;
            }

            if (status != BillStatus.Available)
            {
                Diag($"Pickup: {bill.Name} reads {status} now; leaving it");
                return false;
            }

            var scope = $"pickup-mark-{markIndex}#{attempt}";
            Status = $"Picking up {bill.Name}";
            Diag($"{scope}: picking up {bill.Name} at the board in {cityName} (attempt {attempt}/{BillPickupAttempts})");
            var outcome = await TryPickUpBillOnce(bill, boardIndex, scope);
            await LeaveBoardWindows(boardIndex, scope);
            if (outcome == BillAcceptOutcome.Accepted)
            {
                return true;
            }

            if (outcome == BillAcceptOutcome.Declined)
            {
                Warn($"Pickup: taking {bill.Name} would abandon a bill still in progress; left it on the board");
                return false;
            }

            await DelayMs(BoardRetryDelayMs);
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"Pickup: could not pick up {bill.Name} after {BillPickupAttempts} attempts");
        }

        return false;
    }

    private async Task<BillAcceptOutcome> TryPickUpBillOnce(HuntBill bill, int boardIndex, string scope)
    {
        var board = await ApproachBoard(HuntBoards.Get(boardIndex).ObjectId, scope);
        if (board is null
            || !await ReadyToUseBoard(scope)
            || !await OpenBoardMenu(board, scope)
            || !await ChooseBillTier(bill, boardIndex, scope))
        {
            return BillAcceptOutcome.Failed;
        }

        return await AcceptBill(bill, boardIndex, scope);
    }

    private async Task<IGameObject?> ApproachBoard(uint objectId, string scope)
    {
        if (!await WaitUntilTimed(() => NpcInteraction.FindNearest(objectId) is not null, BoardObjectWaitMs, $"{scope}-object"))
        {
            Diag($"{scope}: the board ({objectId}) is not among the nearby objects");
            return null;
        }

        if (NpcInteraction.FindNearest(objectId) is not { } board)
        {
            return null;
        }

        if (board.IsInInteractRange())
        {
            return board;
        }

        var boardPosition = board.Position;
        var standPoint = NavmeshIPC.Instance.NearestPointReachable(boardPosition, BoardStandSearchMeters, BoardStandSearchMeters) ?? boardPosition;
        var approachScope = $"{scope}-approach";
        Diag($"{approachScope}: {DistanceTo(boardPosition):F1}m from the board, out of interact range; stepping to {FormatPosition(standPoint)}");
        var approach = new MoveOp(move => move.MoveInZone(
            standPoint,
            MovementConfig.InteractRange.WithTolerance(BoardApproachToleranceMeters),
            () => board.IsInInteractRange()));
        await RunCancellable(approach, BoardApproachWatchdogMs, approachScope, StuckDetector.MoveStallAbort(approachScope));
        if (approach.Fault is { } fault)
        {
            Diag($"{approachScope}: faulted: {fault.Message}");
        }

        if (board.IsInInteractRange())
        {
            return board;
        }

        Diag($"{approachScope}: still out of interact range, {DistanceTo(boardPosition):F1}m from the board");
        return null;
    }

    private async Task<bool> ReadyToUseBoard(string scope)
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            Diag($"{scope}: dismounting to use the board");
            await DismountViaOp($"{scope}-dismount");
        }

        if (await WaitUntilTimed(NpcInteraction.PlayerReady, BoardReadyTimeoutMs, $"{scope}-ready", BoardUiCheckFrames))
        {
            return true;
        }

        Diag($"{scope}: cannot use the board yet ({NpcInteraction.DescribeBlockers()})");
        return false;
    }

    private async Task<bool> OpenBoardMenu(IGameObject board, string scope)
    {
        NpcInteraction.Target(board);
        await WaitUntilTimed(() => NpcInteraction.IsTargeted(board), BoardTargetSettleMs, $"{scope}-target", checkFrames: 1);
        var result = NpcInteraction.Interact(board);
        if (result == 0)
        {
            Diag($"{scope}: the game rejected the interaction with the board ({NpcInteraction.DescribeBlockers()})");
            return false;
        }

        Diag($"{scope}: interacted with the board (result {result}); waiting for its menu");
        return await WaitForBoardMenu(scope);
    }

    private async Task<bool> WaitForBoardMenu(string scope)
    {
        var deadline = Environment.TickCount64 + BoardMenuOpenTimeoutMs;
        var sawMessage = false;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (NpcInteraction.SelectStringOpen())
            {
                Diag($"{scope}: the board menu is open with {NpcInteraction.SelectStringEntryCount()} entries");
                return true;
            }

            if (NpcInteraction.TalkOpen())
            {
                if (!sawMessage)
                {
                    Diag($"{scope}: the board shows a message; moving past it");
                    sawMessage = true;
                }

                NpcInteraction.ProgressTalk();
            }

            await NextFrame(BoardUiCheckFrames);
        }

        Diag($"{scope}: the board menu did not open within {BoardMenuOpenTimeoutMs / TimeUnits.MillisecondsPerSecond}s ({NpcInteraction.DescribeBlockers()})");
        return false;
    }

    // A menu missing a tier shifts every later entry, so an entry is taken by its place only when the menu is complete.
    private async Task<bool> ChooseBillTier(HuntBill bill, int boardIndex, string scope)
    {
        var markIndex = bill.MarkIndex;
        var wanted = HuntBoards.MenuText(markIndex);
        var entryIndex = NpcInteraction.FindSelectStringEntry(wanted);
        if (entryIndex != NpcInteraction.NoEntry)
        {
            Diag($"{scope}: menu entry {entryIndex} reads \"{wanted}\"");
        }
        else
        {
            var entryCount = NpcInteraction.SelectStringEntryCount();
            var expected = HuntBoards.MenuEntryCount(boardIndex);
            if (entryCount != expected)
            {
                Diag($"{scope}: no menu entry reads \"{wanted}\" and the menu lists {entryCount} entries instead of {expected}; not guessing the tier");
                return false;
            }

            entryIndex = HuntBoards.MenuIndex(markIndex);
            Diag($"{scope}: no menu entry reads \"{wanted}\"; taking entry {entryIndex} \"{NpcInteraction.SelectStringEntryText(entryIndex)}\" by its place");
        }

        if (!NpcInteraction.SelectEntry(entryIndex))
        {
            Diag($"{scope}: the menu was not ready to take the choice");
            return false;
        }

        var windowAddon = HuntBoards.WindowAddon(boardIndex);
        if (await WaitUntilTimed(() => NpcInteraction.IsAddonReady(windowAddon), BoardWindowOpenTimeoutMs, $"{scope}-window", BoardUiCheckFrames))
        {
            Diag($"{scope}: the {windowAddon} bill window is open");
            return true;
        }

        Diag($"{scope}: the {windowAddon} bill window did not open ({DescribeBoardWindows(windowAddon)})");
        return false;
    }

    // A second press covers a click the window dropped; if the first one landed after all, the game asks to abandon the
    // bill just taken, the answer is No, and the bill reads held.
    private async Task<BillAcceptOutcome> AcceptBill(HuntBill bill, int boardIndex, string scope)
    {
        var markIndex = bill.MarkIndex;
        var windowAddon = HuntBoards.WindowAddon(boardIndex);
        for (var click = 1; click <= BillAcceptClicks; click++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return BillAcceptOutcome.Failed;
            }

            if (!NpcInteraction.FireCallback(windowAddon, HuntBoards.AcceptCallbackValue))
            {
                Diag($"{scope}: {windowAddon} was not ready to take the accept (press {click}/{BillAcceptClicks})");
                break;
            }

            Diag($"{scope}: pressed accept in {windowAddon} (press {click}/{BillAcceptClicks})");
            await WaitUntilTimed(
                () => NpcInteraction.SelectYesnoOpen() || !NpcInteraction.IsAddonReady(windowAddon) || BillHeld(markIndex),
                BillAcceptSettleMs,
                $"{scope}-accept",
                BillHeldCheckFrames);
            if (NpcInteraction.SelectYesnoOpen())
            {
                Diag($"{scope}: the game asks \"{NpcInteraction.SelectYesnoText()}\"; answering No to keep the bill in progress");
                await DeclineBoardPrompt(scope);
                return BillHeld(markIndex) ? BillAcceptOutcome.Accepted : BillAcceptOutcome.Declined;
            }

            if (BillHeld(markIndex) || !NpcInteraction.IsAddonReady(windowAddon))
            {
                break;
            }
        }

        return await ConfirmBillHeld(bill, scope) ? BillAcceptOutcome.Accepted : BillAcceptOutcome.Failed;
    }

    private async Task<bool> ConfirmBillHeld(HuntBill bill, string scope)
    {
        if (await WaitUntilTimed(() => BillHeld(bill.MarkIndex), BillHeldConfirmMs, $"{scope}-held", BillHeldCheckFrames))
        {
            Diag($"{scope}: {bill.Name} is held");
            return true;
        }

        Diag($"{scope}: {bill.Name} still reads {MarkBillReader.Status(bill.MarkIndex)} after accepting");
        return false;
    }

    private async Task DeclineBoardPrompt(string scope)
    {
        var deadline = Environment.TickCount64 + BoardWindowsCloseTimeoutMs;
        while (NpcInteraction.SelectYesnoOpen() && Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            NpcInteraction.AnswerNo();
            await DelayMs(BoardWindowPollMs);
        }

        if (NpcInteraction.SelectYesnoOpen())
        {
            Diag($"{scope}: the prompt is still open after answering No");
        }
    }

    // Backs out through the prompt, the menu and the bill window the way a player would, so no event keeps holding the character.
    private async Task LeaveBoardWindows(int boardIndex, string scope)
    {
        var windowAddon = HuntBoards.WindowAddon(boardIndex);
        var deadline = Environment.TickCount64 + BoardWindowsCloseTimeoutMs;
        var reported = false;
        while (BoardWindowOpen(windowAddon))
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                Diag($"{scope}: board windows still open after {BoardWindowsCloseTimeoutMs / TimeUnits.MillisecondsPerSecond}s ({DescribeBoardWindows(windowAddon)})");
                return;
            }

            if (!reported)
            {
                Diag($"{scope}: closing the board windows ({DescribeBoardWindows(windowAddon)})");
                reported = true;
            }

            StepOutOfBoardWindows(windowAddon);
            await DelayMs(BoardWindowPollMs);
        }
    }

    private static void StepOutOfBoardWindows(string windowAddon)
    {
        if (NpcInteraction.SelectYesnoOpen())
        {
            NpcInteraction.AnswerNo();
            return;
        }

        if (NpcInteraction.SelectStringOpen())
        {
            NpcInteraction.SelectLastEntry();
            return;
        }

        if (NpcInteraction.IsAddonReady(windowAddon))
        {
            NpcInteraction.Close(windowAddon);
            return;
        }

        NpcInteraction.ProgressTalk();
    }

    private static bool BoardWindowOpen(string windowAddon)
        => NpcInteraction.DialogAddonOpen() || NpcInteraction.IsAddonReady(windowAddon);

    private static string DescribeBoardWindows(string windowAddon)
        => $"menu {NpcInteraction.SelectStringOpen()}, bill window {NpcInteraction.IsAddonReady(windowAddon)}, prompt {NpcInteraction.SelectYesnoOpen()}, message {NpcInteraction.TalkOpen()}";

    private static bool BillHeld(byte markIndex)
    {
        MarkBillReader.Refresh(force: true);
        return MarkBillReader.Status(markIndex) == BillStatus.Held;
    }

    private static int NeededBoardIn(bool[] boardNeeded, uint territoryId)
    {
        for (var boardIndex = 0; boardIndex < boardNeeded.Length; boardIndex++)
        {
            if (boardNeeded[boardIndex] && HuntBoards.Get(boardIndex).TerritoryId == territoryId)
            {
                return boardIndex;
            }
        }

        return HuntBoards.NoBoard;
    }
}
