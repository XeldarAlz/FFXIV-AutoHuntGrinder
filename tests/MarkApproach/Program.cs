using AutoHuntGrinder.Core.Hunts;
using AutoHuntGrinder.Core.Ipc;
using AutoHuntGrinder.Core.Tasks;
using ECommons.DalamudServices;

void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}
TestHunt Reset()
{
    Environment.TickCount64 = 1_000;
    MarkFinder.Alive = true;
    MarkFinder.Visible = true;
    MarkFinder.Mark = new(1, new(10, 0, 0), 1, 9, 100, 0);
    Svc.Objects.LocalPlayer = new();
    Svc.Targets.Target = null;
    Svc.Condition.Mounted = false;
    BossModIPC.Instance.Parked = false;
    return new();
}

var hunt = Reset();
Check(await hunt.Approach() == "Reached" && hunt.Moves == 0, "clear target within ranged approach needs no movement");

hunt = Reset();
MarkFinder.Visible = false;
hunt.OnMove = move =>
{
    Check(move.Tolerance == 1.5f && hunt.PlannedReach == 1.5f, "obstructed approach plans close to the target");
    Check(move.Stop?.Invoke() == false, "target behind a tree cannot stop the approach at casting range");
    MarkFinder.Visible = true;
    Environment.TickCount64 += 100;
    Check(move.Stop?.Invoke() == true, "approach stops when pathing restores line of sight");
};
Check(await hunt.Approach() == "Reached" && hunt.Moves == 1, "nearby obstructed target is approached before engagement");
Check(hunt.Status == "Approaching Quarry", "approach replaces the searching status");

hunt = Reset();
MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 29 };
hunt.OnMove = move =>
{
    Check(move.Tolerance == 1.5f, "initially clear ground approach also uses a close destination tolerance");
    MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 9 };
    MarkFinder.Visible = false;
    Check(move.Stop?.Invoke() == false, "mob roaming behind cover during approach keeps movement running");
    MarkFinder.Visible = true;
};
Check(await hunt.Approach() == "Reached", "moving target can be reached after passing the obstacle");

hunt = Reset();
Svc.Objects.LocalPlayer!.ClassJob.ValueNullable!.Role = 2;
MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 2 };
MarkFinder.Visible = false;
hunt.OnMove = move =>
{
    Check(move.Stop?.Invoke() == false, "melee range does not bypass cover");
    MarkFinder.Visible = true;
};
Check(await hunt.Approach() == "Reached" && hunt.Moves == 1, "melee approach routes around cover too");

hunt = Reset();
Svc.Condition.Mounted = true;
MarkFinder.Visible = false;
hunt.OnMove = move =>
{
    Check(!Svc.Condition.Mounted && move.Stop?.Invoke() == false, "mounted approach dismounts then walks around cover");
    MarkFinder.Visible = true;
};
Check(await hunt.Approach() == "Reached" && hunt.Moves == 1, "dismounting within range does not imply line of sight");

hunt = Reset();
hunt.Ride = true;
MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 60 };
MarkFinder.Visible = false;
hunt.OnMove = move =>
{
    if (hunt.Moves == 1)
    {
        MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 9 };
        Svc.Condition.Mounted = true;
        Check(move.Stop?.Invoke() == true, "ride stops at landing distance even with cover");
        hunt.Ride = false;
    }
    else
    {
        Check(!Svc.Condition.Mounted && move.Tolerance == 1.5f && move.Stop?.Invoke() == false, "landing transitions into ground pathing around cover");
        MarkFinder.Visible = true;
    }
};
Check(await hunt.Approach() == "Reached" && hunt.Moves == 2, "ride and dismount preserve line-of-sight recovery");

hunt = Reset();
MarkFinder.Visible = false;
Check(await hunt.Approach() == "Unreachable" && hunt.Moves == 4, "permanently blocked approach gives up after bounded attempts");

hunt = Reset();
MarkFinder.Visible = false;
hunt.RouteReachable = false;
Check(await hunt.Approach() == "Unreachable" && hunt.Moves == 0, "unreachable path does not enter combat");

hunt = Reset();
hunt.EntranceAhead = new(new(0, 0, -100));
MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 29 };
hunt.OnMove = move =>
{
    Check(hunt.EntranceTrips == 1, "a mark inside an enclosure is approached from its entrance");
    MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 9 };
};
Check(await hunt.Approach() == "Reached" && hunt.Moves == 1 && hunt.EntranceTrips == 1, "the entrance trip is not charged as an approach leg");

hunt = Reset();
hunt.EntranceAhead = new(new(0, 0, -100));
hunt.EntranceReachable = false;
MarkFinder.Mark = MarkFinder.Mark with { DistanceToHitbox = 29 };
Check(await hunt.Approach() == "Unreachable" && hunt.Moves == 0 && hunt.EntranceTrips == 1, "an unreachable entrance ends the approach without diving at the roof");

hunt = Reset();
MarkFinder.Visible = false;
var began = Environment.TickCount64;
hunt.OnMove = move =>
{
    Check(Environment.TickCount64 - began is >= 8_000 and < 45_000, "lost sight triggers short recovery before the no-damage timeout");
    Check(move.Tolerance == 1.5f && move.Stop?.Invoke() == false, "combat recovery moves even while target is inside casting range");
    Check(hunt.MovementHeld && BossModIPC.Instance.Parked, "pathfinding owns movement during recovery");
    MarkFinder.Visible = true;
    hunt.Done = true;
};
Check(await hunt.Engage() == "Counted" && hunt.Moves == 1, "engagement recovers from cover and counts the kill");
Check(hunt.MovementHeld && !BossModIPC.Instance.Parked, "completed fight restores movement ownership and clears temporary override");

hunt = Reset();
MarkFinder.Visible = false;
Check(await hunt.Engage() == "Unreachable" && hunt.Moves == 3, "combat recovery is bounded when cover cannot be cleared");

hunt = Reset();
MarkFinder.Visible = false;
hunt.OnFrame = () => { MarkFinder.Visible = true; hunt.Done = true; };
Check(await hunt.Engage() == "Counted" && hunt.Moves == 0, "brief obstruction does not interrupt combat with a reposition");

hunt = Reset();
MarkFinder.Visible = false;
hunt.OnMove = _ => throw new OperationCanceledException();
try
{
    await hunt.Reposition();
    throw new Exception("Expected cancellation");
}
catch (OperationCanceledException)
{
    Check(!hunt.MovementHeld && !BossModIPC.Instance.Parked, "cancelled recovery releases movement and removes the preset override");
}

hunt = Reset();
MarkFinder.Visible = false;
Check(!hunt.StopCheck()(), "blocked nearby mark is not an arrival");
Check(hunt.StopCheck(requireSight: false)(), "mounted landing leg can stop before line of sight is restored");
MarkFinder.Alive = false;
Check(hunt.StopCheck()(), "despawned mark ends movement");
MarkFinder.Alive = true;
MarkFinder.Mark = MarkFinder.Mark with { Position = new(30, 0, 0) };
Check(hunt.StopCheck()(), "mark drifting away requests a fresh route");

hunt = Reset();
MarkFinder.Visible = false;
hunt.Cancellation.Cancel();
Check(await hunt.Approach() == "Cancelled" && hunt.Moves == 0, "cancelled hunt does not start an obstructed approach");
hunt = Reset();
hunt.Dead = true;
Check(await hunt.Approach() == "KnockedOut" && hunt.Moves == 0, "knockout stops approaching");
