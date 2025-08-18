using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ShipAuthoring : MonoBehaviour
{
    public float MoveSpeed = 10;
    public float EffectiveDistance = 2;

    class Baker : Baker<ShipAuthoring>
    {
        public override void Bake(ShipAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Ship()
            {
                MoveSpeed = authoring.MoveSpeed,
                EffectiveDistance = authoring.EffectiveDistance,
            });
            AddComponent(entity, new ShipMovement());
        }
    }
}


public struct Ship : IComponentData
{
    public float MoveSpeed;
    public float EffectiveDistance;
}

public struct ShipMovement : IComponentData
{
    public float3 LinearVelocity;
}