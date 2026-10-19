using System.Numerics;

namespace AutoHuntGrinder.Core.Travel;

// Inside is within RadiusMeters of Floor over the ground and no more than HeightMeters above it, so a character
// pressed against the roof from outside does not count as in.
internal readonly record struct Enclosure(uint TerritoryId, string Name, Vector3 Floor, float RadiusMeters, float HeightMeters, Vector3 Entrance)
{
    public bool Contains(Vector3 position)
        => GroundDistance.Between(position, Floor) <= RadiusMeters && position.Y <= Floor.Y + HeightMeters;
}

// A spot under a roof the pathfinder routes straight through has to be entered through its one opening.
internal static class Enclosures
{
    public const float EntranceArriveMeters = 5f;

    private const uint RubySeaTerritoryId = 613;
    // A character that stopped at the entrance drifts a little before the next leg starts, and is still there.
    private const float AtEntranceMeters = 8f;

    // The Exile's entrance is map X 25.4, Y 23.7, Z -1.6 (issue #10); the shown Z is the height in hundreds of yalms.
    // Dives from above wedged on its roof 42 to 67 m over the floor, so inside reaches 20 m up. The radius takes in the
    // Yumemi spawns around map (25, 26) and (25.5, 24.8) and stops well short of the entrance, 105 m out.
    private static readonly Enclosure[] all =
    [
        new(RubySeaTerritoryId, "The Exile", new Vector3(180f, -172f, 215f), 75f, 20f, new Vector3(196f, -160f, 111f)),
    ];

    // The enclosure a way from `from` to `destination` has to go in through: the destination lies inside it, and the
    // character is neither inside nor at its entrance yet.
    public static bool TryFindEntranceAhead(uint territoryId, Vector3 from, Vector3 destination, out Enclosure enclosure)
    {
        for (var index = 0; index < all.Length; index++)
        {
            var candidate = all[index];
            if (candidate.TerritoryId != territoryId
                || !candidate.Contains(destination)
                || candidate.Contains(from)
                || Vector3.Distance(from, candidate.Entrance) <= AtEntranceMeters)
            {
                continue;
            }

            enclosure = candidate;
            return true;
        }

        enclosure = default;
        return false;
    }

    public static float RouteMeters(uint territoryId, Vector3 from, Vector3 destination)
        => TryFindEntranceAhead(territoryId, from, destination, out var enclosure)
            ? Vector3.Distance(from, enclosure.Entrance) + Vector3.Distance(enclosure.Entrance, destination)
            : Vector3.Distance(from, destination);
}
