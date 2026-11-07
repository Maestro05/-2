namespace КГ_2.Rendering
{
    /// <summary>
    /// Точка/вектор в 3D-пространстве
    /// </summary>
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
