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
    [UpdateAfter(typeof(BoidsSimulationSystem))]
    public partial struct TargetingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //TODO: алгоритм сомннительной сложности, мб обратиться вновь к хэшмапам
            foreach (var (weapon, transform)in SystemAPI
                         .Query<RefRW<ProjectileWeapon>, RefRO<LocalTransform>>()
                         .WithAll<PlayerTag>())
            {
                Entity closestEnemy = Entity.Null;
                float closestDistanceSq = weapon.ValueRO.AttackRange * weapon.ValueRO.AttackRange;

                foreach (var (enemyTransform, enemyEntity) in SystemAPI.Query<RefRO<LocalTransform>>()
                             .WithEntityAccess().WithAll<Ship.Ship>().WithNone<PlayerTag>())
                {
                    float distanceSq = math.distancesq(transform.ValueRO.Position, enemyTransform.ValueRO.Position);
                    if (distanceSq < closestDistanceSq)
                    {
                        closestDistanceSq = distanceSq;
                        closestEnemy = enemyEntity;
                    }
                }

                weapon.ValueRW.Target = closestEnemy;
            }

            Entity playerEntity = Entity.Null;
            float3 playerPosition = float3.zero;

            foreach (var (playerTransform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithEntityAccess()
                         .WithAll<PlayerTag>())
            {
                playerEntity = entity;
                playerPosition = playerTransform.ValueRO.Position;
                break;
            }


            //TODO: переиспользование кода выше с определением "команды" юнита
            foreach (var (weapon, transform) in SystemAPI
                         .Query<RefRW<ProjectileWeapon>, RefRO<LocalTransform>>()
                         .WithAll<Ship.Ship>()
                         .WithNone<PlayerTag>())
            {
                if (playerEntity != Entity.Null)
                {
                    float distanceSq = math.distancesq(transform.ValueRO.Position, playerPosition);
                    if (distanceSq <= weapon.ValueRO.AttackRange * weapon.ValueRO.AttackRange)
                    {
                        weapon.ValueRW.Target = playerEntity;
                    }
                    else
                    {
                        weapon.ValueRW.Target = Entity.Null;
                    }
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
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float currentTime = (float)SystemAPI.Time.ElapsedTime;
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (weapon, transform, faction, entity) in SystemAPI
                         .Query<RefRW<ProjectileWeapon>, RefRO<LocalTransform>, RefRO<Faction>>()
                         .WithEntityAccess())
            {
                if (weapon.ValueRO.Target == Entity.Null)
                    continue;

                if (currentTime - weapon.ValueRO.LastAttackTime < weapon.ValueRO.AttackCooldown)
                    continue;

                // Есть ли смысл в проверке, или стоит обнулять таргет?
                if (!SystemAPI.Exists(weapon.ValueRO.Target))
                {
                    weapon.ValueRW.Target = Entity.Null;
                    continue;
                }

                //TODO: вероятно, достаточно обновления таргета в TargetSystem
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
    public partial struct ProjectileSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            //TODO: мб сбор данных вынести из систем?
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

            //Хэш для целей
            float cellSize = 10f;
            NativeParallelMultiHashMap<int, int> targetSpatialHashMap =
                new NativeParallelMultiHashMap<int, int>(targetCount, world.UpdateAllocator.ToAllocator);

            NativeQueue<Entity> hitProjectileEntities = new NativeQueue<Entity>(world.UpdateAllocator.ToAllocator);
            NativeQueue<DamageEvent> damageEvents = new NativeQueue<DamageEvent>(world.UpdateAllocator.ToAllocator);

            GatherProjectileDataJob gatherProjectileJob = new GatherProjectileDataJob
            {
                Entities = projectileEntities,
                Positions = projectilePositions,
                ProjectileData = projectileData,
                DeltaTime = deltaTime
            };
            JobHandle gatherProjectileHandle = gatherProjectileJob.ScheduleParallel(projectileQuery, state.Dependency);

            GatherTargetDataJob gatherTargetJob = new GatherTargetDataJob
            {
                Entities = targetEntities,
                Positions = targetPositions,
                Factions = targetFactions,
                SpatialHashMap = targetSpatialHashMap.AsParallelWriter(),
                CellSize = cellSize
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
                CellSize = cellSize,
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
            
            state.Dependency = applyHandle;
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
            [ReadOnly] public float DeltaTime;

            void Execute([EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                ref LocalTransform transform,
                in Projectile projectile)
            {
                //TODO: вообще другая джоба
                float3 velocity = projectile.Direction * projectile.Speed * DeltaTime;
                transform.Position += velocity;

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

            public NativeQueue<Entity>.ParallelWriter HitProjectileEntities;
            public NativeQueue<DamageEvent>.ParallelWriter DamageEvents;

            public void Execute(int projectileIndex)
            {
                Entity projectileEntity = ProjectileEntities[projectileIndex];
                float3 projectilePos = ProjectilePositions[projectileIndex];
                Projectile projectile = ProjectileData[projectileIndex];

                NativeList<int> nearbyTargets = new NativeList<int>(Allocator.Temp);
                SpatialHashUtils.AddNeighborIndexes(ref TargetSpatialHashMap, ref projectilePos, CellSize, 1,
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
                        break;//мб не всего одного попадание?
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
    [UpdateAfter(typeof(ProjectileSystem))]
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