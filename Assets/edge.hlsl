#include "stereokit.hlsli"

// Renders mesh edges as cylinders: a camera facing quad per edge, shaded with
// a cylinder's normals. Plain quad lines sit on the edge's axis, so half the
// quad ends up inside any face touching the edge. This quad is instead pushed
// toward the camera by the radius, to where a cylinder's front surface would
// be, so the faces it's attached to don't clip it.
//
// This doesn't write per-pixel depth (SV_Depth), that kills early-z on mobile
// GPUs, and currently crashes StereoKit's Vulkan renderer.
//
// Vertex layout, 4 verts per edge:
//   pos  = edge start, norm = edge end
//   uv.x = -1 or 1, which side of the quad
//   uv.y = 0 or 1, which end of the edge this vertex is on

//--name = app/edge

// The point between the eyes. Camera facing geometry has to face this, not
// each eye's own camera: facing each eye builds slightly different geometry
// per eye, which shows as edges twisting differently in each eye.
float3 head_pos() {
	float3 sum = 0;
	for (uint i = 0; i < sk_view_count; i++) sum += sk_camera_pos[i].xyz;
	return sum / max(sk_view_count, 1u);
}

//--radius = 0.005
float radius; // world space, in meters

struct vsIn {
	float4 pos  : SV_Position;
	float3 norm : NORMAL0;
	float2 uv   : TEXCOORD0;
	float4 col  : COLOR0;
};
struct psIn {
	float4 pos   : SV_Position;
	float3 side  : TEXCOORD0;
	float3 front : TEXCOORD1;
	float  s     : TEXCOORD2; // -1..1 across the cylinder
	float4 color : COLOR0;
};

psIn vs(vsIn input, sk_ids_t ids) {
	psIn o;
	float4x4 world = sk_inst[ids.inst].world;
	float3   a     = mul(float4(input.pos.xyz, 1), world).xyz;
	float3   b     = mul(float4(input.norm,    1), world).xyz;
	float3   p     = lerp(a, b, input.uv.y);

	// Zero length edges, and looking straight down an edge, have no proper
	// direction or side. Pick any, so normalize doesn't make NaNs.
	float3 ab     = b - a;
	float3 dir    = dot(ab, ab) > 1e-12 ? ab * rsqrt(dot(ab, ab)) : float3(0, 1, 0);
	float3 to_cam = head_pos() - p;
	float3 side   = cross(dir, to_cam);
	if (dot(side, side) < 1e-12)
		side = cross(dir, abs(dir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0));
	o.side  = normalize(side);
	o.front = cross(o.side, dir); // perpendicular to the edge, toward the camera

	float3 corner = p + (o.side * input.uv.x + o.front) * radius;
	o.pos   = mul(float4(corner, 1), sk_viewproj[ids.view]);
	o.s     = input.uv.x;
	o.color = input.col * sk_inst[ids.inst].color;
	return o;
}

float4 ps(psIn input) : SV_TARGET {
	float  s      = clamp(input.s, -1, 1);
	float3 normal = normalize(input.side) * s + normalize(input.front) * sqrt(1 - s * s);
	return float4(input.color.rgb * sk_lighting(normal), input.color.a);
}
