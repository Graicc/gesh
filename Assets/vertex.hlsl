#include "stereokit.hlsli"

// Renders mesh vertices as spheres: one camera facing quad per vertex, with a
// sphere's normals, and pixels outside the circle discarded. One mesh for all
// vertices is much cheaper on the CPU than a draw call per sphere. Like
// edge.hlsl, the quad is pushed toward the camera by the radius, to where the
// sphere's front would be, so faces through the vertex don't clip it.
//
// Vertex layout, 4 verts per vertex:
//   pos  = the vertex's position
//   uv   = -1 or 1 on each axis, which corner of the quad

//--name = app/vertex

// The point between the eyes. Camera facing geometry has to face this, not
// each eye's own camera: facing each eye builds slightly different geometry
// per eye, which shows as edges twisting differently in each eye.
float3 head_pos() {
	float3 sum = 0;
	for (uint i = 0; i < sk_view_count; i++) sum += sk_camera_pos[i].xyz;
	return sum / max(sk_view_count, 1u);
}

//--color:color = 1,1,1,1
float4 color;
//--radius = 0.015
float radius; // world space, in meters

struct vsIn {
	float4 pos  : SV_Position;
	float3 norm : NORMAL0;
	float2 uv   : TEXCOORD0;
	float4 col  : COLOR0;
};
struct psIn {
	float4 pos   : SV_Position;
	float2 uv    : TEXCOORD0;
	float3 right : TEXCOORD1;
	float3 up    : TEXCOORD2;
	float3 front : TEXCOORD3;
	float4 color : COLOR0;
};

psIn vs(vsIn input, sk_ids_t ids) {
	psIn o;
	float3 center = mul(float4(input.pos.xyz, 1), sk_inst[ids.inst].world).xyz;
	float3 to_cam = head_pos() - center;
	float  dist   = length(to_cam);
	o.front = dist > 1e-6 ? to_cam / dist : float3(0, 0, 1);
	// Any up that isn't along the view direction works for a sphere.
	float3 up_hint = abs(o.front.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
	o.right = normalize(cross(up_hint, o.front));
	o.up    = cross(o.front, o.right);

	float3 corner = center + (o.right * input.uv.x + o.up * input.uv.y + o.front) * radius;
	o.pos   = mul(float4(corner, 1), sk_viewproj[ids.view]);
	o.uv    = input.uv;
	o.color = input.col * color * sk_inst[ids.inst].color;
	return o;
}

float4 ps(psIn input) : SV_TARGET {
	float r2 = dot(input.uv, input.uv);
	if (r2 > 1) discard;
	float3 normal = input.right * input.uv.x + input.up * input.uv.y + input.front * sqrt(1 - r2);
	return float4(input.color.rgb * sk_lighting(normal), input.color.a);
}
