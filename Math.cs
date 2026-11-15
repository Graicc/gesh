using StereoKit;

namespace gesh;

public static class Math
{
	public static Vec3 ClosestPointToLine(Vec3 p, Vec3 a, Vec3 b)
	{
		if ((b - a).LengthSq == 0) throw new System.ArgumentException($"Zero length line at {a}");
		float t = Vec3.Dot(p - a, b - a) / (b - a).LengthSq;
		t = float.Clamp(t, 0, 1);
		return t * (b - a) + a;
	}

	public static float DistanceToLineSq(Vec3 p, Vec3 a, Vec3 b)
	{
		return (p - ClosestPointToLine(p, a, b)).LengthSq;
	}

	// https://stackoverflow.com/questions/2924795/fastest-way-to-compute-point-to-triangle-distance-in-3d
	public static Vec3 ClosestPointToTriangle(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
	{
		Vec3 ab = b - a;
		Vec3 ac = c - a;
		Vec3 ap = p - a;

		float d1 = Vec3.Dot(ab, ap);
		float d2 = Vec3.Dot(ac, ap);
		if (d1 <= 0 && d2 <= 0) return a;

		Vec3 bp = p - b;
		float d3 = Vec3.Dot(ab, bp);
		float d4 = Vec3.Dot(ac, bp);
		if (d3 >= 0 && d4 < d3) return b;

		Vec3 cp = p - c;
		float d5 = Vec3.Dot(ab, cp);
		float d6 = Vec3.Dot(ac, cp);
		if (d6 >= 0 && d5 <= d6) return c;

		float vc = d1 * d4 - d3 * d2;
		if (vc <= 0 && d1 >= 0 && d3 <= 0)
		{
			float v = d1 / (d1 - d3);
			return a + v * ab;
		}

		float vb = d5 * d2 - d1 * d6;
		if (vb <= 0 && d2 >= 0 && d6 <= 0)
		{
			float v = d2 / (d2 - d6);
			return a + v * ac;
		}

		float va = d3 * d6 - d5 * d4;
		if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
		{
			float v = (d4 - d3) / (d4 - d3 + (d5 - d6));
			return b + v * (c - b);
		}

		float denom = 1f / (va + vb + vc);
		float v_ = vb * denom;
		float w = vc * denom;
		return a + v_ * ab + w * ac;
	}

	public static float DistanceToTraingleSq(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
	{
		return (p - ClosestPointToTriangle(p, a, b, c)).LengthSq;
	}
}
