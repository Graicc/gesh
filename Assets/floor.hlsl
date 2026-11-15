#include <stereokit.hlsli>

//--name = app/floor

//--color:color = 0,0,0,1
float4 color;
// Where the grid starts fading, and over how far: gone 5m out.
//--radius      = 2,3,0,0
float4 radius;
// The floor is drawn as a unit cube scaled to this size, so the grid can be
// laid out in the cube's own space and move with it.
//--size        = 30
float size;

struct vsIn {
	float4 pos : SV_POSITION;
};
struct psIn {
	float4 pos   : SV_POSITION;
	float2 grid  : TEXCOORD0;
};

psIn vs(vsIn input, sk_ids_t ids) {
	psIn o;
	float4 world = mul(float4(input.pos.xyz, 1), sk_inst[ids.inst].world);
	o.pos  = mul(world, sk_viewproj[ids.view]);
	o.grid = input.pos.xz * size;

	return o;
}
float4 ps(psIn input) : SV_TARGET{
	// This line algorithm is inspired by :
	// http://madebyevan.com/shaders/grid/

	// Minor grid lines
	float2 step = 1 / fwidth(input.grid);
	float2 grid = abs(frac(input.grid - 0.5) - 0.5) * step; // minor grid lines
	float  dist = sqrt(dot(input.grid, input.grid)); // transparency falloff
	float  lineDist = min(grid.x, grid.y);
	float  val = 1.0 - min(lineDist, 1.0);
	val *= saturate(1 - ((dist - radius.x)/radius.y));

	// Major axis lines
	const float extraThickness = 0.5;
	float2 axes = (abs(input.grid)) * step - extraThickness;
	lineDist = min(axes.x, axes.y);
	float  axisVal = 1.0 - min(lineDist, 1.0);
	axisVal *= saturate(1 - ((dist - radius.x)/radius.y));

	// combine, and drop transparent pixels
	val = max(val*0.6, axisVal);
	if (val <= 0) discard;

	return float4(color.rgb, val);
}