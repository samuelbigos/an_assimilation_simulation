#[compute]
#version 450

layout(set = 0, binding = 0, rgba8) uniform image2D _input;
layout(set = 1, binding = 0, rgba8) uniform image2D _output;
layout(push_constant, std430) uniform Params {
	float offset;
} params;

layout(local_size_x = 32, local_size_y = 32, local_size_z = 1) in;
void main() 
{
	ivec2 uv;
	uv.x = int(gl_WorkGroupID.x) * int(gl_WorkGroupSize.x) + int(gl_LocalInvocationID.x);
	uv.y = int(gl_WorkGroupID.y) * int(gl_WorkGroupSize.y) + int(gl_LocalInvocationID.y);

	ivec2 imageSize = ivec2(int(gl_NumWorkGroups.x) * int(gl_WorkGroupSize.x), int(gl_NumWorkGroups.y) * int(gl_WorkGroupSize.y));

	float closest_dist = 9999999.9;
	vec2 closest_pos = vec2(0.0, 0.0);

	// uses Jump Flood Algorithm to do a fast voronoi generation.
	for(int x = -1; x <= 1; x += 1)
	{
		for(int y = -1; y <= 1; y += 1)
		{
			ivec2 voffset = uv;
			voffset += ivec2(x * int(params.offset), y * int(params.offset));

			vec2 pos = imageLoad(_input, voffset).rg;

			vec2 uv01 = vec2(float(uv.x) / float(imageSize.x), float(uv.y) / float(imageSize.y));
			float dist = distance(pos.xy, uv01.xy);

			if(pos.x != 0.0 && pos.y != 0.0 && dist < closest_dist)
			{
				closest_dist = dist;
				closest_pos = pos;
			}
		}
	}
	vec4 col = vec4(1.0, 1.0, 0.0, 1.0);
	col = vec4(closest_pos.x, closest_pos.y, 0.0, 1.0);
	imageStore(_output, uv, col);
}
