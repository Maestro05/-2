namespace КГ_2.Rendering
{
    /// <summary>
    /// Многоугольник в 3D: набор вершин + цвет для отрисовки
    /// </summary>
    public class Polygon3D
    {
        public Vec3[] Vertices;
        public byte R, G, B;
        public Polygon3D(Vec3[] vertices, byte r, byte g, byte b)
        {
            Vertices = vertices;
            R = r; G = g; B = b;
        }
        public int EdgeCount => Vertices.Length;
    }
}