using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ShipAuthoring : MonoBehaviour
{
    public float MoveSpeed = 10;
    // public float RotationSpeed = 10;

    class Baker : Baker<ShipAuthoring>
    {
        public override void Bake(ShipAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Ship()
            {
                MoveSpeed = authoring.MoveSpeed,
                // RotationSpeed = authoring.RotationSpeed
            });
            AddComponent(entity, new ShipMovement());
        }
    }
}

public struct Ship : IComponentData
{
    public float MoveSpeed;
    // public float RotationSpeed;
}

public struct ShipMovement : IComponentData
{
    public float3 LinearVelocity;
    // public float AngularVelocity;
}