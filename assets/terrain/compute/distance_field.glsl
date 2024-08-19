#[compute]
#version 450

layout(set = 0, binding = 0, rgba8) uniform image2D _input;
layout(set = 1, binding = 0, rgba8) uniform image2D _output;
layout(set = 2, binding = 0, std430) restrict buffer SafePositions {
	int data[];
} _safe;

layout(push_constant, std430) uniform Params {
	float sdfDistMod;
} params;

layout(local_size_x = 32, local_size_y = 32, local_size_z = 1) in;
void main() 
{
    ivec2 uv;
    uv.x = int(gl_WorkGroupID.x) * int(gl_WorkGroupSize.x) + int(gl_LocalInvocationID.x);
    uv.y = int(gl_WorkGroupID.y) * int(gl_WorkGroupSize.y) + int(gl_LocalInvocationID.y);
    
    ivec2 imageSize = ivec2(int(gl_NumWorkGroups.x) * int(gl_WorkGroupSize.x), int(gl_NumWorkGroups.y) * int(gl_WorkGroupSize.y));
    
    vec2 pos = imageLoad(_input, uv).rg;
    pos.x *= imageSize.x;
    pos.y *= imageSize.y;
    float dist = distance(pos, uv);

	// Fill out the safe positions buffer so we know we can spawn a boid here.
	if (dist > 25.0 && (uv.x % 5) == 0 && (uv.y % 5) == 0)
		_safe.data[uv.x * imageSize.y + uv.y] = 1;

    dist = clamp(dist / params.sdfDistMod, 0.0, 1.0);
    imageStore(_output, uv, vec4(dist, dist, dist, 1.0));	
}