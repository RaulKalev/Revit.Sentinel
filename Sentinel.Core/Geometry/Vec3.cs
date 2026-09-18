using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Sentinel.Core.Geometry
{
    /// <summary>
    /// Immutable 3D vector in millimetres (host project coordinates). Revit-free stand-in for XYZ so that
    /// domain objects stay serializable and testable. The Revit layer converts feet &lt;-&gt; millimetres.
    /// </summary>
    public struct Vec3 : IEquatable<Vec3>
    {
        [JsonProperty] public readonly double X;
        [JsonProperty] public readonly double Y;
        [JsonProperty] public readonly double Z;

        [JsonConstructor]
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
        public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
        public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);

        [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        [JsonIgnore] public bool IsZero => Length < 1e-9;

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

        public Vec3 Cross(Vec3 o) => new Vec3(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

        public Vec3 Normalize()
        {
            var len = Length;
            return len < 1e-12 ? Zero : new Vec3(X / len, Y / len, Z / len);
        }

        /// <summary>Projection onto the XY plane (Z = 0).</summary>
        public Vec3 Flatten() => new Vec3(X, Y, 0);

        public double DistanceTo(Vec3 o) => (this - o).Length;

        /// <summary>Rotates the vector about the global Z axis (counter-clockwise, degrees).</summary>
        public Vec3 RotateAboutZ(double degrees)
        {
            var r = degrees * Math.PI / 180.0;
            var c = Math.Cos(r);
            var s = Math.Sin(r);
            return new Vec3(X * c - Y * s, X * s + Y * c, Z);
        }

        /// <summary>Plan angle of the vector in degrees, normalised to [0, 360).</summary>
        public double PlanAngleDeg()
        {
            if (Math.Abs(X) < 1e-12 && Math.Abs(Y) < 1e-12) return 0.0;
            return Angles.Normalize360(Math.Atan2(Y, X) * 180.0 / Math.PI);
        }

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vec3 && Equals((Vec3)obj);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = X.GetHashCode();
                h = (h * 397) ^ Y.GetHashCode();
                return (h * 397) ^ Z.GetHashCode();
            }
        }

        public bool AlmostEquals(Vec3 other, double tolerance) => DistanceTo(other) <= tolerance;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", X, Y, Z);
    }

    public static class Angles
    {
        public static double Normalize360(double deg)
        {
            var d = deg % 360.0;
            if (d < 0) d += 360.0;
            if (d >= 360.0 - 1e-9) d = 0.0;
            return d;
        }

        /// <summary>Smallest absolute difference between two angles in degrees (0..180).</summary>
        public static double Difference(double a, double b)
        {
            var d = Math.Abs(Normalize360(a) - Normalize360(b));
            return d > 180.0 ? 360.0 - d : d;
        }
    }
}
