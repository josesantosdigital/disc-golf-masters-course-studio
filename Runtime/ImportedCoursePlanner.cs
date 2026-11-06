namespace DgmCustomCourses;

// Pure distance and variety selection; Unity supplies grounded candidate points.
internal static class ImportedCoursePlanner
{
    internal readonly record struct Point(float X, float Y, float Z);
    internal readonly record struct Hole(Point Tee, Point Basket, float Distance);
    private static readonly int[] DefaultTargets = { 120, 200, 300, 155, 240, 320, 135, 265, 300 };

    internal static List<Hole> Plan(IReadOnlyList<Point> points, int count)
        => Plan(points, count, DefaultTargets, out _);

    internal static List<Hole> Plan(IReadOnlyList<Point> points, int count, out bool usedRelaxedPairs)
        => Plan(points, count, DefaultTargets, out usedRelaxedPairs);

    internal static List<Hole> Plan(IReadOnlyList<Point> points, int count, IReadOnlyList<int> targetDistances, out bool usedRelaxedPairs)
    {
        usedRelaxedPairs = false;
        if (targetDistances == null || targetDistances.Count == 0)
            targetDistances = DefaultTargets;
        var result = new List<Hole>();
        // A sparse island may expose only one usable tee/basket pair. Reusing
        // that grounded pair is safer than returning no holes and leaving the
        // player at the base-course spawn outside the imported play area.
        if (points.Count < 2 || count <= 0) return result;
        var maxDistance = 0f;
        for (var a = 0; a < points.Count; a++)
        for (var b = a + 1; b < points.Count; b++)
            maxDistance = MathF.Max(maxDistance, HorizontalDistance(points[a], points[b]));
        // Tiny imported islands still need a playable layout. Do not fall back
        // to arbitrary map bounds just because the scene cannot fit a full-size
        // 120 m opening hole.
        if (maxDistance < 8f) return result;

        // Prefer gentle elevation changes so a generated basket is reachable
        // from its tee and both game objects sit on usable ground. Loosen the
        // cap only when a map does not provide enough viable pairs.
        var best = result;
        foreach (var elevationCap in new[] { 14f, 22f, 32f, 48f })
        {
            var candidate = PlanWithElevationCap(points, count, maxDistance, elevationCap, targetDistances);
            if (candidate.Count > best.Count)
                best = candidate;
            if (candidate.Count == count)
                return candidate;
        }

        // Some imported maps expose land in disconnected clusters. The normal
        // target windows then find no usable pairs even though raycasts found
        // safe ground (for example, two tiny islands separated by water). Fill
        // the remaining holes from the best grounded pairs available instead
        // of aborting the map setup and leaving the player at the base-course
        // spawn. Distances may be shorter or longer than the requested target.
        var completed = CompleteWithRelaxedPairs(points, count, maxDistance, best, targetDistances);
        usedRelaxedPairs = completed.Count > best.Count;
        return completed;
    }

    private static List<Hole> CompleteWithRelaxedPairs(IReadOnlyList<Point> points, int count, float maxDistance, List<Hole> existing, IReadOnlyList<int> targetDistances)
    {
        var result = new List<Hole>(existing);
        if (points.Count < 2 || result.Count >= count)
            return result;

        var scale = MathF.Min(1f, maxDistance * 0.9f / 320f);
        var useCount = new int[points.Count];
        var previous = -1;
        var lastDx = 0f;
        var lastDz = 0f;
        for (var i = 0; i < result.Count; i++)
        {
            var teeIndex = FindPointIndex(points, result[i].Tee);
            var basketIndex = FindPointIndex(points, result[i].Basket);
            if (teeIndex >= 0) useCount[teeIndex]++;
            if (basketIndex >= 0) useCount[basketIndex]++;
            if (basketIndex >= 0) previous = basketIndex;
            var directionLength = HorizontalDistance(result[i].Tee, result[i].Basket);
            if (directionLength > 0f)
            {
                lastDx = (result[i].Basket.X - result[i].Tee.X) / directionLength;
                lastDz = (result[i].Basket.Z - result[i].Tee.Z) / directionLength;
            }
        }

        for (var hole = result.Count; hole < count; hole++)
        {
            var target = Math.Clamp(targetDistances[hole % targetDistances.Count], 8, 2000) * scale;
            var bestScore = float.PositiveInfinity;
            var bestTee = -1;
            var bestBasket = -1;
            for (var a = 0; a < points.Count; a++)
            for (var b = 0; b < points.Count; b++)
            {
                if (a == b) continue;
                var distance = HorizontalDistance(points[a], points[b]);
                if (distance < 8f) continue;

                var elevationDelta = MathF.Abs(points[a].Y - points[b].Y);
                var dx = (points[b].X - points[a].X) / distance;
                var dz = (points[b].Z - points[a].Z) / distance;
                var score = MathF.Abs(distance - target) / MathF.Max(30f, target);
                score += elevationDelta / MathF.Max(8f, target * 0.15f) * 0.4f;
                score += (useCount[a] + useCount[b]) * 0.4f;
                if (hole > 0) score += MathF.Max(0f, dx * lastDx + dz * lastDz) * 0.25f;
                if (previous >= 0) score += HorizontalDistance(points[previous], points[a]) / MathF.Max(1f, maxDistance) * 0.08f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestTee = a;
                bestBasket = b;
            }

            if (bestTee < 0) break;
            var tee = points[bestTee];
            var basket = points[bestBasket];
            var length = HorizontalDistance(tee, basket);
            result.Add(new Hole(tee, basket, length));
            useCount[bestTee]++;
            useCount[bestBasket]++;
            previous = bestBasket;
            lastDx = (basket.X - tee.X) / length;
            lastDz = (basket.Z - tee.Z) / length;
        }

        return result;
    }

    private static int FindPointIndex(IReadOnlyList<Point> points, Point target)
    {
        for (var i = 0; i < points.Count; i++)
            if (points[i] == target)
                return i;
        return -1;
    }

    private static List<Hole> PlanWithElevationCap(IReadOnlyList<Point> points, int count, float maxDistance, float elevationCap, IReadOnlyList<int> targetDistances)
    {
        var result = new List<Hole>();
        var scale = MathF.Min(1f, maxDistance * 0.9f / 320f);
        var useCount = new int[points.Count];
        var previous = -1;
        var lastDx = 0f;
        var lastDz = 0f;
        for (var hole = 0; hole < count; hole++)
        {
            var target = Math.Clamp(targetDistances[hole % targetDistances.Count], 8, 2000) * scale;
            var bestScore = float.PositiveInfinity;
            var bestTee = -1;
            var bestBasket = -1;
            for (var a = 0; a < points.Count; a++)
            for (var b = 0; b < points.Count; b++)
            {
                if (a == b) continue;
                var distance = HorizontalDistance(points[a], points[b]);
                if (distance < MathF.Max(8f, target * 0.55f) || distance > MathF.Max(target * 1.5f, target + 30f)) continue;
                var elevationDelta = MathF.Abs(points[a].Y - points[b].Y);
                var allowedElevationDelta = MathF.Min(elevationCap, MathF.Max(8f, target * 0.15f));
                if (elevationDelta > allowedElevationDelta) continue;
                var dx = (points[b].X - points[a].X) / distance;
                var dz = (points[b].Z - points[a].Z) / distance;
                var score = MathF.Abs(distance - target) / MathF.Max(30f, target);
                score += elevationDelta / MathF.Max(1f, allowedElevationDelta) * 0.4f;
                score += (useCount[a] + useCount[b]) * 0.4f;
                if (hole > 0) score += MathF.Max(0f, dx * lastDx + dz * lastDz) * 0.25f;
                if (previous >= 0) score += HorizontalDistance(points[previous], points[a]) / maxDistance * 0.08f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestTee = a;
                bestBasket = b;
            }
            if (bestTee < 0) break;
            var tee = points[bestTee];
            var basket = points[bestBasket];
            var length = HorizontalDistance(tee, basket);
            result.Add(new Hole(tee, basket, length));
            useCount[bestTee]++;
            useCount[bestBasket]++;
            previous = bestBasket;
            lastDx = (basket.X - tee.X) / length;
            lastDz = (basket.Z - tee.Z) / length;
        }
        return result;
    }

    internal static int ParFor(float distance, int holeIndex) =>
        distance >= 330f || (distance >= 300f && holeIndex % 3 == 2) ? 6 : distance >= 240f || (distance >= 160f && holeIndex % 3 == 2) ? 5 : distance >= 105f ? 4 : 3;

    private static float HorizontalDistance(Point a, Point b)
    {
        var x = a.X - b.X;
        var z = a.Z - b.Z;
        return MathF.Sqrt(x * x + z * z);
    }
}
