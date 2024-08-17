#[compute]
#version 450

layout(set = 0, binding = 0, rgba8) uniform image2D colorImage;
layout(set = 0, binding = 1, std430) restrict readonly buffer Color {
  vec4 fillColor;
};
layout(set = 0, binding = 2, rgba8) uniform image2D sampleImage;

layout(local_size_x = 32, local_size_y = 32, local_size_z = 1) in;
void main() 
{
    ivec2 uv;
    uv.x = int(gl_WorkGroupID.x) * int(gl_WorkGroupSize.x) + int(gl_LocalInvocationID.x);
    uv.y = int(gl_WorkGroupID.y) * int(gl_WorkGroupSize.y) + int(gl_LocalInvocationID.y);
    
    float threshold = 0.6;
    
    float val = imageLoad(sampleImage, uv).r;
    val = step(val, threshold);
    
    vec4 col = vec4(val, val, val, 1.0);
    imageStore(colorImage, uv, col);
}
