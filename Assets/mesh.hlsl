#include "stereokit.hlsli"

//--name = app/mesh

//--color:color = 1,1,1,1
//--tex_trans   = 0,0,1,1
//--diffuse     = white

float4       color;
float4       tex_trans;
Texture2D    diffuse   : register(t0);
SamplerState diffuse_s : register(s0);

struct vsIn {
	float4 pos  : SV_Position;
	float3 norm : NORMAL0;   // unused now, but left to match StereoKit's vertex layout
	float2 uv   : TEXCOORD0;
	float4 col  : COLOR0;
};
struct psIn {
	float4 pos   : SV_Position;
	float2 uv    : TEXCOORD0;
	float4 color : COLOR0;
	float3 world : TEXCOORD1;   // world-space position, interpolated
};

psIn vs(vsIn input, sk_ids_t ids) {
	psIn o;
	float4 world = mul(float4(input.pos.xyz, 1), sk_inst[ids.inst].world);
	o.pos        = mul(world, sk_viewproj[ids.view]);
	o.world      = world.xyz;

	o.uv    = (input.uv * tex_trans.zw) + tex_trans.xy;
	o.color = color * input.col * sk_inst[ids.inst].color;
	return o;
}

float4 ps(psIn input) : SV_TARGET {
	// Two vectors lying in the triangle's plane -> their cross product is the face normal
	float3 normal = normalize(cross(ddy(input.world), ddx(input.world)));

	float4 col = diffuse.Sample(diffuse_s, input.uv) * input.color;
	col.rgb   *= sk_lighting(normal);
	return col;
}
