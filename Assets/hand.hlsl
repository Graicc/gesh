#include "stereokit.hlsli"

// Meta's hand mesh, drawn like the system's own hands: a dark, nearly solid
// fill, and a thin light outline around it. Our own shader:
// XR_FB_hand_tracking_mesh gives the shape only, no material.
//
// GMetaHandMesh draws the mesh three times with this shader. A depth only
// pass records the nearest surface, so the fill after it draws just that
// one layer: overlapping fingers don't stack their opacity. The fill pushes
// the surface out along its normals by `extrude`, a little bigger than the
// tracked hand. The outline is a shell pushed out further, drawn with only
// its back faces after the fill, so all that shows is the band around the
// fill's edge.
//
// Per vertex, GMetaHandMesh puts in uv.x how much the index finger moves it
// (its skin weights on the index joints), and in uv.y how near the wrist it
// is, 0 out at the fingers to 1 at the wrist. The tool's colors tint the
// outline fully and the fill a little; the index finger takes index_color,
// the rest hand_color, blended smoothly between them.

//--name = app/hand

//--hand_color:color = 1,1,1,1
float4 hand_color;
//--index_color:color = 1,1,1,1
float4 index_color;
// Measured off a headset screenshot of the system hands: a dark warm grey
// fill (about sRGB 45,40,38) that mostly hides what's behind, and a light
// grey outline (about sRGB 150).
//--fill = 0.026,0.021,0.019,0.85
float4 fill;
//--outline = 0.3
float outline;
// 0 draws the fill, 1 the outline shell, 2 nothing but depth (the pass
// before the fill, so the fill only draws the nearest surface).
//--pass = 0
float pass;
// How far the surface is pushed out along its normals, in meters.
//--extrude = 0.0015
float extrude;
// How much the tool's color tints the fill.
//--fill_tint = 0.6
float fill_tint;

struct vsIn {
	float4 pos  : SV_Position;
	float3 norm : NORMAL0;
	float2 uv   : TEXCOORD0;
};
struct psIn {
	float4 pos  : SV_Position;
	float2 uv   : TEXCOORD0;
	float3 tint : COLOR0;
};

psIn vs(vsIn input, sk_ids_t ids) {
	psIn o;
	float3 world  = mul(float4(input.pos.xyz, 1), sk_inst[ids.inst].world).xyz;
	float3 normal = normalize(mul(input.norm, (float3x3)sk_inst[ids.inst].world));
	world  += normal * extrude;
	o.pos   = mul(float4(world, 1), sk_viewproj[ids.view]);
	o.uv    = input.uv;
	// The instance color brightens the outline while the hand pinches.
	o.tint  = sk_inst[ids.inst].color.rgb;
	return o;
}

float4 ps(psIn input) : SV_TARGET {
	float3 tool = lerp(hand_color.rgb, index_color.rgb, saturate(input.uv.x));
	// Fading out toward the wrist.
	float  fade = saturate((0.45 - saturate(input.uv.y)) / 0.15);

	// Depth for the whole hand, the faded wrist too: the mesh is open at the
	// wrist, and without depth there the outline shell's inside showed
	// through the opening as a solid grey patch. The hand draws after the
	// scene, so this invisible depth hides nothing of the model.
	if (pass > 1.5)
		return float4(0, 0, 0, 0);
	// The outline fades out ahead of the fill, so no light ring is left
	// around a wrist that's already gone.
	if (pass > 0.5)
		return float4(tool * outline * input.tint, fade * fade);

	// The tool's hue at full brightness, so white leaves the fill as it is.
	float3 hue = tool / max(max(tool.r, tool.g), max(tool.b, 0.001));
	return float4(fill.rgb * lerp(float3(1, 1, 1), hue, fill_tint), fade * fill.a);
}
