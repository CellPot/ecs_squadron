using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Combat.Weapon.Projectile
{
    public class ProjectileAuthoring : MonoBehaviour
    {
        class Baker : Baker<ProjectileAuthoring>
        {
            public override void Bake(ProjectileAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<Projectile>(entity);
                AddComponent<DestroyOnDelay>(entity);
            }
        }
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
}