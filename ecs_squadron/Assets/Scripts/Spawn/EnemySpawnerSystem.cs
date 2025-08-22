using Config;
using Player;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

namespace Spawn
{
    [BurstCompile]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct EnemySpawnerSystem : ISystem
    {
        private EntityQuery _enemyQuery;
        private uint _lastSpawnTime;
        private bool _initialSpawnComplete;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldConfig>();
            state.RequireForUpdate<PlayerTag>();
            _enemyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAny<Ship.Ship>()
                .WithNone<PlayerTag>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            WorldConfig config = SystemAPI.GetSingleton<WorldConfig>();
            int currentEnemyCount = _enemyQuery.CalculateEntityCount();

            if (currentEnemyCount >= config.EnemyConfig.MaxShipCount)
            {
                _initialSpawnComplete = true;
                return;
            }

            float currentTime = (float)SystemAPI.Time.ElapsedTime;
            if (!_initialSpawnComplete || currentTime - _lastSpawnTime >= config.EnemyConfig.WaveCooldown)
            {
                SpawnEnemies(ref state, currentEnemyCount, currentTime, config.EnemyConfig);
                _initialSpawnComplete = true;
                _lastSpawnTime = (uint)currentTime;
            }
        }

        [BurstCompile]
        private void SpawnEnemies(ref SystemState state, int currentEnemyCount, float currentTime,
            EnemyConfig config)
        {
            float3 playerPos = float3.zero;
            foreach (var playerTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                playerPos = playerTransform.ValueRO.Position;
                break;
            }

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            Random random = Random.CreateFromIndex(1234); //Random.CreateFromIndex((uint)(currentTime * 1000));
            int enemiesToSpawn = math.min(config.MaxShipCount - currentEnemyCount, config.WaveSize);

            for (int i = 0; i < enemiesToSpawn; i++)
            {
                float angle = random.NextFloat(0, 2 * math.PI);
                float distance = random.NextFloat(config.ShipSpawnMinRadius, config.ShipSpawnMaxRadius);

                float3 spawnPos = new float3(
                    playerPos.x + math.cos(angle) * distance,
                    playerPos.y + math.sin(angle) * distance,
                    0f
                );

                Entity enemy = ecb.Instantiate(config.EnemyPrefab);
                ecb.SetComponent(enemy, new LocalTransform
                {
                    Position = spawnPos,
                    Scale = 1,
                    Rotation = quaternion.identity
                });
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}