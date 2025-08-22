using Ship;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Combat.Weapon.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct ProjectileWeaponAttackSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ProjectileWeapon>();
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

                ecb.SetComponent(projectile, LocalTransform.FromPositionRotationScale(
                
                    transform.ValueRO.Position + direction * 0.5f,
                    quaternion.LookRotationSafe(direction, math.up()),
                    1f
                ));

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
}