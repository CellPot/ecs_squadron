using Unity.Entities;
using UnityEngine;

namespace Combat.Weapon.Projectile
{
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
}