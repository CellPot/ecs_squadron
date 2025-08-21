using Boids;
using Config;
using Player;
using Ship;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Combat.Weapon
{
    public struct ProjectileWeapon : IComponentData
    {
        public float AttackRange;
        public float Damage;
        public float AttackCooldown;
        public float LastAttackTime;
        public Entity Target;
        public Entity ProjectilePrefab;
        public float ProjectileSpeed;
        public float ProjectileLifetime;
        public float ProjectileCollisionRadius;
    }

    public struct Projectile : IComponentData
    {
        public float3 Direction;
        public float Speed;
        public float Damage;
        public float CollisionRadius;
        public int FactionId;
        public Entity FiredByEntity;
    }

    public struct DestroyOnDelay : IComponentData
    {
        public float TimeToDestroy;
    }

    public class ProjectileWeaponAuthoring : MonoBehaviour
    {
        public float AttackRange = 8f;
        public float Damage = 25f;
        public float AttackCooldown = 1f;

        public float ProjectileLifetime = 6f;
        public float ProjectileSpeed = 1f;
        public float ProjectileCollisionRadius = 1f;

        public GameObject ProjectilePrefab;

        class Baker : Baker<ProjectileWeaponAuthoring>
        {
            public override void Bake(ProjectileWeaponAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new ProjectileWeapon
                {
                    AttackRange = authoring.AttackRange,
                    Damage = authoring.Damage,
                    AttackCooldown = authoring.AttackCooldown,
                    LastAttackTime = 0f,
                    Target = Entity.Null,
                    ProjectilePrefab = GetEntity(authoring.ProjectilePrefab, TransformUsageFlags.Dynamic),
                    ProjectileLifetime = authoring.ProjectileLifetime,
                    ProjectileSpeed = authoring.ProjectileSpeed,
                    ProjectileCollisionRadius = authoring.ProjectileCollisionRadius
                });
            }
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TargetingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            WorldConfig worldConfig = SystemAPI.GetSingleton<WorldConfig>();

            EntityQuery weaponQuery = SystemAPI.QueryBuilder()
                .WithAll<ProjectileWeapon, LocalTransform, Faction>()
                .Build();

            EntityQuery targetableQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Faction>()
                .WithAny<Ship.Ship>()
                .Build();

            int weaponCount = weaponQuery.CalculateEntityCount();
            int targetCount = targetableQuery.CalculateEntityCount();

            if (weaponCount == 0 || targetCount == 0) return;

            WorldUnmanaged world = state.WorldUnmanaged;

            NativeArray<Entity> weaponEntities =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);
            NativeArray<float3> weaponPositions =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);
            NativeArray<float> weaponRanges =
                CollectionHelper.CreateNativeArray<float, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);
            NativeArray<int> weaponFactions =
                CollectionHelper.CreateNativeArray<int, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);

            NativeArray<Entity> targetEntities =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(targetCount, ref world.UpdateAllocator);
            NativeArray<float3> targetPositions =
                CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(targetCount, ref world.UpdateAllocator);
            NativeArray<int> targetFactions =
                CollectionHelper.CreateNativeArray<int, RewindableAllocator>(targetCount, ref world.UpdateAllocator);

            NativeParallelMultiHashMap<int, int> targetSpatialHash =
                new NativeParallelMultiHashMap<int, int>(targetCount, world.UpdateAllocator.ToAllocator);

            NativeArray<Entity> targetResults =
                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(weaponCount, ref world.UpdateAllocator);

            GatherWeaponDataJob gatherWeaponsJob = new GatherWeaponDataJob
            {
                WeaponEntities = weaponEntities,
                WeaponPositions = weaponPositions,
                WeaponRanges = weaponRanges,
                WeaponFactions = weaponFactions
            };
            JobHandle gatherWeaponsHandle = gatherWeaponsJob.ScheduleParallel(weaponQuery, state.Dependency);

            GatherTargetDataJob gatherTargetsJob = new GatherTargetDataJob
            {
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetFactions = targetFactions,
                SpatialHash = targetSpatialHash.AsParallelWriter(),
                CellSize = worldConfig.CombatConfig.WeaponTargetSearchCellSize,
            };
            JobHandle gatherTargetsHandle = gatherTargetsJob.ScheduleParallel(targetableQuery, gatherWeaponsHandle);

            FindTargetsJob findTargetsJob = new FindTargetsJob
            {
                WeaponEntities = weaponEntities,
                WeaponPositions = weaponPositions,
                WeaponRanges = weaponRanges,
                WeaponFactions = weaponFactions,
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetFactions = targetFactions,
                TargetSpatialHash = targetSpatialHash,
                CellSize = worldConfig.CombatConfig.WeaponTargetSearchCellSize,
                TargetResults = targetResults
            };
            JobHandle findTargetsHandle = findTargetsJob.Schedule(weaponCount, 32, gatherTargetsHandle);

            ApplyTargetingResultsJob applyResultsJob = new ApplyTargetingResultsJob
            {
                WeaponEntities = weaponEntities,
                TargetResults = targetResults,
                WeaponLookup = SystemAPI.GetComponentLookup<ProjectileWeapon>(false)
            };
            JobHandle applyResultsHandle = applyResultsJob.Schedule(weaponCount, 64, findTargetsHandle);

            state.Dependency = applyResultsHandle;
        }

        [BurstCompile]
        private partial struct GatherWeaponDataJob : IJobEntity
        {
            public NativeArray<Entity> WeaponEntities;
            public NativeArray<float3> WeaponPositions;
            public NativeArray<float> WeaponRanges;
            public NativeArray<int> WeaponFactions;

            void Execute([EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in ProjectileWeapon weapon,
                in LocalTransform transform,
                in Faction faction)
            {
                WeaponEntities[entityIndexInQuery] = entity;
                WeaponPositions[entityIndexInQuery] = transform.Position;
                WeaponRanges[entityIndexInQuery] = weapon.AttackRange;
                WeaponFactions[entityIndexInQuery] = faction.FactionId;
            }
        }

        [BurstCompile]
        private partial struct GatherTargetDataJob : IJobEntity
        {
            public NativeArray<Entity> TargetEntities;
            public NativeArray<float3> TargetPositions;
            public NativeArray<int> TargetFactions;
            public NativeParallelMultiHashMap<int, int>.ParallelWriter SpatialHash;
            [ReadOnly] public float CellSize;

            void Execute([EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in LocalTransform transform,
                in Faction faction)
            {
                TargetEntities[entityIndexInQuery] = entity;
                TargetPositions[entityIndexInQuery] = transform.Position;
                TargetFactions[entityIndexInQuery] = faction.FactionId;

                int hash = SpatialHashUtils.GetSpatialHash(transform.Position, CellSize);
                SpatialHash.Add(hash, entityIndexInQuery);
            }
        }

        [BurstCompile]
        private struct FindTargetsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> WeaponEntities;
            [ReadOnly] public NativeArray<float3> WeaponPositions;
            [ReadOnly] public NativeArray<float> WeaponRanges;
            [ReadOnly] public NativeArray<int> WeaponFactions;

            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<float3> TargetPositions;
            [ReadOnly] public NativeArray<int> TargetFactions;
            [ReadOnly] public NativeParallelMultiHashMap<int, int> TargetSpatialHash;
            [ReadOnly] public float CellSize;

            [NativeDisableParallelForRestriction] public NativeArray<Entity> TargetResults;

            public void Execute(int weaponIndex)
            {
                Entity weaponEntity = WeaponEntities[weaponIndex];
                float3 weaponPos = WeaponPositions[weaponIndex];
                float weaponRange = WeaponRanges[weaponIndex];
                int weaponFaction = WeaponFactions[weaponIndex];

                Entity closestTarget = Entity.Null;
                float closestDistanceSq = weaponRange * weaponRange;

                NativeList<int> nearbyTargetIndices = new NativeList<int>(Allocator.Temp);

                //Instead of pre-defined cell radius weapon range is used
                int searchRadius = math.max(1, (int)math.ceil(weaponRange / CellSize));
                SpatialHashUtils.AddNeighborIndexes(ref TargetSpatialHash, ref weaponPos, CellSize, searchRadius,
                    ref nearbyTargetIndices);

                for (int i = 0; i < nearbyTargetIndices.Length; i++)
                {
                    int targetIndex = nearbyTargetIndices[i];
                    Entity targetEntity = TargetEntities[targetIndex];

                    if (targetEntity == weaponEntity)
                        continue;

                    if (TargetFactions[targetIndex] == weaponFaction)
                        continue;

                    float3 targetPos = TargetPositions[targetIndex];
                    float distanceSq = math.distancesq(weaponPos, targetPos);

                    if (distanceSq < closestDistanceSq)
                    {
                        closestDistanceSq = distanceSq;
                        closestTarget = targetEntity;
                    }
                }

                nearbyTargetIndices.Dispose();
                TargetResults[weaponIndex] = closestTarget;
            }
        }

        [BurstCompile]
        private struct ApplyTargetingResultsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> WeaponEntities;
            [ReadOnly] public NativeArray<Entity> TargetResults;
            [NativeDisableParallelForRestriction] public ComponentLookup<ProjectileWeapon> WeaponLookup;

            public void Execute(int index)
            {
                Entity weaponEntity = WeaponEntities[index];
                Entity target = TargetResults[index];

                if (WeaponLookup.HasComponent(weaponEntity))
                {
                    RefRW<ProjectileWeapon> weapon = WeaponLookup.GetRefRW(weaponEntity);
                    weapon.ValueRW.Target = target;
                }
            }
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct ProjectileAttackSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float currentTime = (float)SystemAPI.Time.ElapsedTime;
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (weapon, transform, faction, entity) in SystemAPI
                         .Query<RefRW<ProjectileWeapon>, RefRO<LocalTransform>, RefRO<Faction>>()
                         .WithEntityAccess())
            {
                if (weapon.ValueRO.Target != Entity.Null && !SystemAPI.Exists(weapon.ValueRO.Target))
                {
                    weapon.ValueRW.Target = Entity.Null;
                    continue;
                }

                if (weapon.ValueRO.Target == Entity.Null)
                    continue;

                if (currentTime - weapon.ValueRO.LastAttackTime < weapon.ValueRO.AttackCooldown)
                    continue;


                LocalTransform targetTransform = SystemAPI.GetComponent<LocalTransform>(weapon.ValueRO.Target);
                float distanceSq = math.distancesq(transform.ValueRO.Position, targetTransform.Position);

                if (distanceSq > weapon.ValueRO.AttackRange * weapon.ValueRO.AttackRange)
                {
                    weapon.ValueRW.Target = Entity.Null;
                    continue;
                }

                Entity projectile = ecb.Instantiate(weapon.ValueRO.ProjectilePrefab);
                float3 direction = math.normalizesafe(targetTransform.Position - transform.ValueRO.Position);

                ecb.SetComponent(projectile, new LocalTransform
                {
                    Position = transform.ValueRO.Position + direction * 0.5f,
                    Rotation = quaternion.LookRotationSafe(direction, math.up()),
                    Scale = 1f
                });

                ecb.SetComponent(projectile, new Projectile
                {
                    Damage = weapon.ValueRO.Damage,
                    Speed = weapon.ValueRO.ProjectileSpeed,
                    Direction = direction,
                    FactionId = faction.ValueRO.FactionId,
                    CollisionRadius = weapon.ValueRO.ProjectileCollisionRadius,
                    FiredByEntity = entity,
                });

                ecb.AddComponent(projectile, new DestroyOnDelay
                {
                    TimeToDestroy = weapon.ValueRO.ProjectileLifetime
                });

                weapon.ValueRW.LastAttackTime = currentTime;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ProjectileMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, projectile) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRO<Projectile>>())
            {
                float3 velocity = projectile.ValueRO.Direction * projectile.ValueRO.Speed * deltaTime;
                transform.ValueRW.Position += velocity;
            }
        }
    }

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
                .WithAll<LocalTransform, Health, Faction>()
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
                HealthLookup = SystemAPI.GetComponentLookup<Health>(false)
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
            public ComponentLookup<Health> HealthLookup;

            public void Execute()
            {
                while (DamageEvents.TryDequeue(out DamageEvent damageEvent))
                {
                    if (HealthLookup.HasComponent(damageEvent.TargetEntity))
                    {
                        RefRW<Health> health = HealthLookup.GetRefRW(damageEvent.TargetEntity);
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

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    public partial struct HealthSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (health, entity) in SystemAPI
                         .Query<RefRO<Health>>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.CurrentHealth <= 0)
                {
                    ecb.DestroyEntity(entity);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct DestroyOnDelaySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (destroyComponent, entity) in SystemAPI
                         .Query<RefRW<DestroyOnDelay>>()
                         .WithEntityAccess())
            {
                destroyComponent.ValueRW.TimeToDestroy -= SystemAPI.Time.DeltaTime;
                if (destroyComponent.ValueRW.TimeToDestroy <= 0)
                {
                    ecb.DestroyEntity(entity);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}