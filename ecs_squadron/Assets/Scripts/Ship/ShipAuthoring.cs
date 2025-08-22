using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ship
{
    public class ShipAuthoring : MonoBehaviour
    {
        public float MoveSpeed = 5;
        public int FactionId;

        class Baker : Baker<ShipAuthoring>
        {
            public override void Bake(ShipAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new Ship()
                {
                    MoveSpeed = authoring.MoveSpeed,
                });
                AddComponent(entity, new ShipMovement());
                AddComponent(entity, new Faction()
                {
                    FactionId = authoring.FactionId,
                });
            }
        }
    }

    public struct Ship : IComponentData
    {
        public float MoveSpeed;
    }

    public struct Faction : IComponentData
    {
        public int FactionId;
    }

    public struct ShipMovement : IComponentData
    {
        public float3 LinearVelocity;
    }
}