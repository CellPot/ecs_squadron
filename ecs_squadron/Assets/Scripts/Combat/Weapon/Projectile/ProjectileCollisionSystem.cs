using Boids;
using Config;
using Ship;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Weapon.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    public partial struct ProjectileCollisionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            WorldConfig worldConfig = SystemAPI.GetSingleton<WorldConfig>();

            EntityQuery projectileQuery = SystemAPI.QueryBuilder()
                .WithAll<Projectile, LocalTransform>()
                .Build();

            EntityQuery targetQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Health.Health, Faction>()
                .WithAbsent<Projectile>()
                .Build();

            int projectileCount = projectileQuery.CalculateEntityCount();
            int targetCount = targetQuery.CalculateEntityCount();

            if (projectileCount == 0 || targetCount == 0) return;

            WorldUnmanaged world = state.WorldUnmanaged;

            NativeArray<Entity> projectileEntities =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(projectileCount,
                    ref world.UpdateAllocator);
            NativeArray<float3> projectilePositions =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(projectileCount,
                    ref world.UpdateAllocator);
            NativeArray<Projectile> projectileData =
                CollectionHelper.CreateNativeArray<Projectile, RewindableAllocator>(projectileCount,
                    ref world.UpdateAllocator);

            NativeArray<Entity> targetEntities =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(targetCount, ref world.UpdateAllocator);
            NativeArray<float3> targetPositions =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(targetCount, ref world.UpdateAllocator);
            NativeArray<int> targetFactions =
                CollectionHelper.CreateNativeArray<int, RewindableAllocator>(targetCount, ref world.UpdateAllocator);

            NativeParallelMultiHashMap<int, int> targetSpatialHashMap =
                new NativeParallelMultiHashMap<int, int>(targetCount, world.UpdateAllocator.ToAllocator);

            NativeQueue<Entity> hitProjectileEntities = new NativeQueue<Entity>(world.UpdateAllocator.ToAllocator);
            NativeQueue<DamageEvent> damageEvents = new NativeQueue<DamageEvent>(world.UpdateAllocator.ToAllocator);

            GatherProjectileDataJob gatherProjectileJob = new GatherProjectileDataJob
            {
                Entities = projectileEntities,
                Positions = projectilePositions,
                ProjectileData = projectileData,
            };
            JobHandle gatherProjectileHandle = gatherProjectileJob.ScheduleParallel(projectileQuery, state.Dependency);

            GatherTargetDataJob gatherTargetJob = new GatherTargetDataJob
            {
                Entities = targetEntities,
                Positions = targetPositions,
                Factions = targetFactions,
                SpatialHashMap = targetSpatialHashMap.AsParallelWriter(),
                CellSize = worldConfig.CombatConfig.ProjectileSearchCellSize,
            };
            JobHandle gatherTargetHandle = gatherTargetJob.ScheduleParallel(targetQuery, gatherProjectileHandle);


            ProcessProjectileCollisionsJob collisionJob = new ProcessProjectileCollisionsJob
            {
                ProjectileEntities = projectileEntities,
                ProjectilePositions = projectilePositions,
                ProjectileData = projectileData,
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetFactions = targetFactions,
                TargetSpatialHashMap = targetSpatialHashMap,
                CellSize = worldConfig.CombatConfig.ProjectileSearchCellSize,
                SearchRadius = worldConfig.CombatConfig.ProjectileCellCheckRadius,
                HitProjectileEntities = hitProjectileEntities.AsParallelWriter(),
                DamageEvents = damageEvents.AsParallelWriter()
            };
            JobHandle collisionHandle = collisionJob.Schedule(projectileCount, 32, gatherTargetHandle);

            ApplyProjectileResultsJob applyJob = new ApplyProjectileResultsJob
            {
                DamageEvents = damageEvents,
                HealthLookup = SystemAPI.GetComponentLookup<Health.Health>(false)
            };
            JobHandle applyHandle = applyJob.Schedule(collisionHandle);

            EntityCommandBuffer destructionECB = new EntityCommandBuffer(world.UpdateAllocator.ToAllocator);
            DestroyProjectilesJob destroyJob = new DestroyProjectilesJob
            {
                ProjectilesToDestroy = hitProjectileEntities,
                EntityCommandBuffer = destructionECB
            };
            JobHandle destroyHandle = destroyJob.Schedule(collisionHandle);

            JobHandle combinedHandle = JobHandle.CombineDependencies(applyHandle, destroyHandle);
            combinedHandle.Complete();

            destructionECB.Playback(state.EntityManager);
            destructionECB.Dispose();

            state.Dependency = combinedHandle;
        }

        [BurstCompile]
        private struct DestroyProjectilesJob : IJob
        {
            public NativeQueue<Entity> ProjectilesToDestroy;
            public EntityCommandBuffer EntityCommandBuffer;

            public void Execute()
            {
                while (ProjectilesToDestroy.TryDequeue(out Entity projectileEntity))
                {
                    EntityCommandBuffer.DestroyEntity(projectileEntity);
                }
            }
        }

        [BurstCompile]
        private partial struct GatherProjectileDataJob : IJobEntity
        {
            public NativeArray<Entity> Entities;
            public NativeArray<float3> Positions;
            public NativeArray<Projectile> ProjectileData;

            void Execute([EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                ref LocalTransform transform,
                in Projectile projectile)
            {
                Entities[entityIndexInQuery] = entity;
                Positions[entityIndexInQuery] = transform.Position;
                ProjectileData[entityIndexInQuery] = projectile;
            }
        }

        [BurstCompile]
        private partial struct GatherTargetDataJob : IJobEntity
        {
            public NativeArray<Entity> Entities;
            public NativeArray<float3> Positions;
            public NativeArray<int> Factions;
            public NativeParallelMultiHashMap<int, int>.ParallelWriter SpatialHashMap;
            [ReadOnly] public float CellSize;

            void Execute([EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in LocalTransform transform,
                in Faction faction)
            {
                Entities[entityIndexInQuery] = entity;
                Positions[entityIndexInQuery] = transform.Position;
                Factions[entityIndexInQuery] = faction.FactionId;

                int hash = SpatialHashUtils.GetSpatialHash(transform.Position, CellSize);
                SpatialHashMap.Add(hash, entityIndexInQuery);
            }
        }

        [BurstCompile]
        private struct ProcessProjectileCollisionsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> ProjectileEntities;
            [ReadOnly] public NativeArray<float3> ProjectilePositions;
            [ReadOnly] public NativeArray<Projectile> ProjectileData;

            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<float3> TargetPositions;
            [ReadOnly] public NativeArray<int> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<int, int> TargetSpatialHashMap;
            [ReadOnly] public float CellSize;
            [ReadOnly] public int SearchRadius;

            public NativeQueue<Entity>.ParallelWriter HitProjectileEntities;
            public NativeQueue<DamageEvent>.ParallelWriter DamageEvents;

            public void Execute(int projectileIndex)
            {
                Entity projectileEntity = ProjectileEntities[projectileIndex];
                float3 projectilePos = ProjectilePositions[projectileIndex];
                Projectile projectile = ProjectileData[projectileIndex];

                NativeList<int> nearbyTargets = new NativeList<int>(Allocator.Temp);
                SpatialHashUtils.AddNeighborIndexes(ref TargetSpatialHashMap, ref projectilePos, CellSize, SearchRadius,
                    ref nearbyTargets);

                for (int i = 0; i < nearbyTargets.Length; i++)
                {
                    int targetIndex = nearbyTargets[i];
                    Entity targetEntity = TargetEntities[targetIndex];

                    if (projectileEntity == targetEntity) continue;

                    if (ProjectileData[projectileIndex].FactionId == TargetFactions[targetIndex]) continue;

                    if (ProjectileData[projectileIndex].FiredByEntity == targetEntity) continue;

                    float3 targetPos = TargetPositions[targetIndex];
                    float distanceSq = math.distancesq(projectilePos, targetPos);

                    if (distanceSq < projectile.CollisionRadius * projectile.CollisionRadius)
                    {
                        DamageEvents.Enqueue(new DamageEvent
                        {
                            TargetEntity = targetEntity,
                            Damage = projectile.Damage
                        });

                        HitProjectileEntities.Enqueue(projectileEntity);
                        break;
                    }
                }

                nearbyTargets.Dispose();
            }
        }

        [BurstCompile]
        private struct ApplyProjectileResultsJob : IJob
        {
            public NativeQueue<DamageEvent> DamageEvents;
            public ComponentLookup<Health.Health> HealthLookup;

            public void Execute()
            {
                while (DamageEvents.TryDequeue(out DamageEvent damageEvent))
                {
                    if (HealthLookup.HasComponent(damageEvent.TargetEntity))
                    {
                        RefRW<Health.Health> health = HealthLookup.GetRefRW(damageEvent.TargetEntity);
                        health.ValueRW.CurrentHealth -= damageEvent.Damage;
                    }
                }
            }
        }

        private struct DamageEvent
        {
            public Entity TargetEntity;
            public float Damage;
        }
    }
}