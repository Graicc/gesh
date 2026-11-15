#include "stereokit.hlsli"

//--name = sk/default_ui

float4 color = {1,1,1,1};
// gesh: a clip region, two world space planes (xyz normal, w offset); a
// pixel is kept where both are >= 0. The defaults keep everything. The
// renderer sets them on material copies while a clip is pushed, see
// render_push_clip.
float4 clip_top    = {0,0,0,1};
float4 clip_bottom = {0,0,0,1};

//--diffuse = white
Texture2D    diffuse   : register(t0);
SamplerState diffuse_s : register(s0);

struct vsIn {
	float4 pos  : SV_Position;
	float3 norm : NORMAL0;
	float2 uv   : TEXCOORD0;
	float4 color: COLOR0;
};
struct psIn {
	float4      pos   : SV_POSITION;
	float2      uv    : TEXCOORD0;
	float3      world : TEXCOORD1;
	min16float4 color : COLOR0;
	float2      clip  : TEXCOORD2;
};

psIn vs(vsIn input, sk_ids_t ids) {
	psIn o;
	float3x3 world3x3 = (float3x3)sk_inst[ids.inst].world;
	float3   normal   = normalize(mul(input.norm, world3x3));
	float3   world    = mul(input.pos.xyz, world3x3) + sk_inst[ids.inst].world[3].xyz;
	o.pos   = mul(float4(world, 1), sk_viewproj[ids.view]);
	o.world = world;
	o.clip = float2(dot(clip_top.xyz, world) + clip_top.w, dot(clip_bottom.xyz, world) + clip_bottom.w);
	o.uv    = input.uv;
	o.color.rgb = color.rgb * input.color.rgb * sk_inst[ids.inst].color.rgb * sk_lighting(normal);
	o.color.a   = input.color.a;
	return o;
}

min16float4 ps(psIn input) : SV_TARGET {
	clip(min(input.clip.x, input.clip.y));
	min16float  glow = sk_finger_glow(input.world);
	min16float4 col  = min16float4(lerp(input.color.rgb, min16float3(1, 1, 1), glow), input.color.a);

	return (min16float4)diffuse.Sample(diffuse_s, input.uv) * col;
}