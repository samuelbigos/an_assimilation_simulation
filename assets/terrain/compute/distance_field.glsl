#[compute]
#version 450

layout(set = 0, binding = 0, rgba8) uniform image2D _input;
layout(set = 1, binding = 0, rgba8) uniform image2D _output;

layout(local_size_x = 32, local_size_y = 32, local_size_z = 1) in;
void main() 
{
    ivec2 uv;
    uv.x = int(gl_WorkGroupID.x) * int(gl_WorkGroupSize.x) + int(gl_LocalInvocationID.x);
    uv.y = int(gl_WorkGroupID.y) * int(gl_WorkGroupSize.y) + int(gl_LocalInvocationID.y);
    
    ivec2 imageSize = ivec2(int(gl_NumWorkGroups.x) * int(gl_WorkGroupSize.x), int(gl_NumWorkGroups.y) * int(gl_WorkGroupSize.y));
    
    float dist_mod = 2.0;
    
    vec2 pos = imageLoad(_input, uv).rg;
    vec2 uv01 = vec2(float(uv.x) / float(imageSize.x), float(uv.y) / float(imageSize.y));
    float dist = clamp(distance(pos, uv01) * dist_mod, 0.0, 1.0);
    imageStore(_output, uv, vec4(dist, dist, dist, 1.0));
}