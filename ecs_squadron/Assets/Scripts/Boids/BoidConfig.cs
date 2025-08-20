namespace Boids
{
    public struct BoidConfig
    {
        public float CellSize;
        public int CellCheckRadius;
        public float SeparationRadius;
        public float AlignmentWeight;
        public float CohesionWeight;
        public float SeparationWeight;
        public float TargetSeekWeight;
        public float MaxSteerForce;
    }
}