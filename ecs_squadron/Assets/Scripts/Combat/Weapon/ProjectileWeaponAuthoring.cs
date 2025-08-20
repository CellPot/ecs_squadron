using Boids;
using Config;
using Player;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
    }

    public struct Projectile : IComponentData
    {
        public Entity Target;
        public float Damage;
        public float Speed;

        public float3 Direction;
        // public float Lifetime;
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

            foreach (var (weapon, transform) in SystemAPI.Query<RefRW<ProjectileWeapon>, RefRO<LocalTransform>>())
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
                    Target = weapon.ValueRO.Target,
                    Damage = weapon.ValueRO.Damage,
                    Speed = weapon.ValueRO.ProjectileSpeed,
                    Direction = direction,
                    // Lifetime = weapon.ValueRO.ProjectileLifetime,
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
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            //TODO: алгоритм высокой сложности
            foreach (var (projectile, projectileTransform, entity) in SystemAPI
                         .Query<RefRW<Projectile>, RefRW<LocalTransform>>()
                         .WithEntityAccess())
            {
                float3 velocity = projectile.ValueRO.Direction * projectile.ValueRO.Speed * deltaTime;
                projectileTransform.ValueRW.Position += velocity;

                foreach (var (targetTransform, health) in SystemAPI
                             .Query<RefRO<LocalTransform>, RefRW<Health>>())
                {
                    float distanceSq = math.distancesq(projectileTransform.ValueRO.Position,
                        targetTransform.ValueRO.Position);
                    //TODO: селф-харм
                    if (distanceSq < 1f)
                    {
                        health.ValueRW.CurrentHealth -= projectile.ValueRO.Damage;
                        ecb.DestroyEntity(entity);
                        break;
                    }
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
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