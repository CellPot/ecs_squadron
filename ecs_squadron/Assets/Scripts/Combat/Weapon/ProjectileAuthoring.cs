using Unity.Entities;
using UnityEngine;

namespace Combat.Weapon
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
}