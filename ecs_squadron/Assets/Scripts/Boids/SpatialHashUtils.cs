using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Boids
{
    [BurstCompile]
    public static class SpatialHashUtils
    {
        [BurstCompile]
        public static int GetSpatialHash(in float3 position, float cellSize)
        {
            int3 gridPos = new int3(math.floor(position / cellSize));
            return GetUniqueHashKey(gridPos);

            int GetUniqueHashKey(int3 int3) =>
                (int)math.hash(int3);
        }

        [BurstCompile]
        public static void AddNeighborIndexes(
            [ReadOnly] ref NativeParallelMultiHashMap<int, int> spatialHashMap,
            [ReadOnly] ref float3 position,
            float cellSize,
            int searchRadius,
            [WriteOnly] ref NativeList<int> neighborIndexes)
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    var offsetPos = position + new float3(dx * cellSize, dy * cellSize, 0);
                    var hash = GetSpatialHash(offsetPos, cellSize);

                    if (spatialHashMap.TryGetFirstValue(hash, out int shipIndex, out var iterator))
                    {
                        do
                        {
                            neighborIndexes.Add(shipIndex);
                        } while (spatialHashMap.TryGetNextValue(out shipIndex, ref iterator));
                    }
                }
            }
        }
    }
}