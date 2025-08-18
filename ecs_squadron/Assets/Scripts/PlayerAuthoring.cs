using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<PlayerTag>(entity);
            AddComponent<PlayerInput>(entity);
        }
    }
}

public struct PlayerTag : IComponentData
{
}

public struct PlayerInput : IComponentData
{
    public float3 MoveAxis;
    // public float RotateAxis;
}