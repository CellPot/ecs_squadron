using Unity.Entities;
using UnityEngine;

namespace Combat
{
    public class HealthAuthoring : MonoBehaviour
    {
        public float MaxHealth = 100f;

        class Baker : Baker<HealthAuthoring>
        {
            public override void Bake(HealthAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new Health
                {
                    CurrentHealth = authoring.MaxHealth,
                    MaxHealth = authoring.MaxHealth
                });
            }
        }
    }

    public struct Health : IComponentData
    {
        public float CurrentHealth;
        public float MaxHealth;
    }
}