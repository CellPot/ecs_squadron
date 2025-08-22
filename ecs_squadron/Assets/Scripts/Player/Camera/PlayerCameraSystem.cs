using Config;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Player.Camera
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [BurstCompile]
    public partial struct PlayerCameraSystem : ISystem
    {
        private float2 _cameraPosition;
        private bool _cameraInitialized;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<WorldConfig>();
            _cameraInitialized = false;
        }

        public void OnUpdate(ref SystemState state)
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null) return;

            float deltaTime = SystemAPI.Time.DeltaTime;

            float2 playerPosition = float2.zero;
            bool playerFound = false;

            foreach (var localTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                playerPosition = new float2(localTransform.ValueRO.Position.x, localTransform.ValueRO.Position.y);
                playerFound = true;
                break;
            }

            if (!playerFound) return;

            if (!_cameraInitialized)
            {
                _cameraPosition = playerPosition;
                _cameraInitialized = true;
                camera.transform.position =
                    new Vector3(playerPosition.x, playerPosition.y, camera.transform.position.z);
                return;
            }

            WorldConfig worldConfig = SystemAPI.GetSingleton<WorldConfig>();

            float orthographicSize = camera.orthographicSize;
            float cameraHeight = orthographicSize * 2f;
            float cameraWidth = cameraHeight * camera.aspect;

            float boundaryWidth = cameraWidth * worldConfig.CameraConfig.BoundaryPercent * 0.5f;
            float boundaryHeight = cameraHeight * worldConfig.CameraConfig.BoundaryPercent * 0.5f;

            float leftBoundary = _cameraPosition.x - cameraWidth * 0.5f + boundaryWidth;
            float rightBoundary = _cameraPosition.x + cameraWidth * 0.5f - boundaryWidth;
            float bottomBoundary = _cameraPosition.y - cameraHeight * 0.5f + boundaryHeight;
            float topBoundary = _cameraPosition.y + cameraHeight * 0.5f - boundaryHeight;

            float2 targetPosition = _cameraPosition;

            if (playerPosition.x < leftBoundary)
            {
                targetPosition.x = playerPosition.x - (-cameraWidth * 0.5f + boundaryWidth);
            }
            else if (playerPosition.x > rightBoundary)
            {
                targetPosition.x = playerPosition.x - (cameraWidth * 0.5f - boundaryWidth);
            }

            if (playerPosition.y < bottomBoundary)
            {
                targetPosition.y = playerPosition.y - (-cameraHeight * 0.5f + boundaryHeight);
            }
            else if (playerPosition.y > topBoundary)
            {
                targetPosition.y = playerPosition.y - (cameraHeight * 0.5f - boundaryHeight);
            }

            _cameraPosition = math.lerp(_cameraPosition, targetPosition,
                deltaTime * worldConfig.CameraConfig.FollowSpeed);

            camera.transform.position = new Vector3(_cameraPosition.x, _cameraPosition.y, camera.transform.position.z);
        }
    }
}