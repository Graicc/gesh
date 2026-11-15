#include "stereokit.hlsli"

// Draws the back side of mesh faces as a see-through red grid, so they're
// visible while editing, but obviously not "real" surface. Drawn as a second
// pass with front faces culled, blended and without depth writes.

//--name = app/mesh_back

//--color:color = 1,0.1,0.1,1
float4 color;
//--grid_size   = 0.02
float  grid_size;
//--fill_alpha  = 0.12
float  fill_alpha;

struct vsIn {
	float4 pos : SV_Position;
};
struct psIn {
	float4 pos   : SV_Position;
	float3 local : TEXCOORD0; // model-space position, so the grid sticks to the mesh
	float3 world : TEXCOORD1;
};

psIn vs(vsIn input, sk_ids_t ids) {
	psIn o;
	float4 world = mul(float4(input.pos.xyz, 1), sk_inst[ids.inst].world);
	o.pos   = mul(world, sk_viewproj[ids.view]);
	o.world = world.xyz;
	o.local = input.pos.xyz;
	return o;
}

float4 ps(psIn input) : SV_TARGET {
	// Project the grid along the face normal's dominant axis.
	float3 n  = abs(cross(ddy(input.local), ddx(input.local)));
	float2 uv = (n.x > n.y && n.x > n.z) ? input.local.yz
	          : (n.y > n.z)              ? input.local.xz
	          :                            input.local.xy;
	uv /= grid_size;

	// Anti-aliased grid lines, see floor.hlsl
	float2 grid = abs(frac(uv - 0.5) - 0.5) / fwidth(uv);
	float  grid_line = 1.0 - min(min(grid.x, grid.y), 1.0);

	return float4(color.rgb, lerp(fill_alpha, color.a, grid_line));
}
