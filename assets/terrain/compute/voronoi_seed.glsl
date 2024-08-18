#[compute]
#version 450

layout(set = 0, binding = 0, rgba8) uniform image2D _input;
layout(set = 1, binding = 0, rgba8) uniform image2D _output;
layout(set = 2, binding = 0, rgba8) uniform image2D _noise;

layout(local_size_x = 32, local_size_y = 32, local_size_z = 1) in;
void main() 
{
  ivec2 uv;
  uv.x = int(gl_WorkGroupID.x) * int(gl_WorkGroupSize.x) + int(gl_LocalInvocationID.x);
  uv.y = int(gl_WorkGroupID.y) * int(gl_WorkGroupSize.y) + int(gl_LocalInvocationID.y);
  
  float threshold = 0.6;
  
  float val = imageLoad(_noise, uv).r;
  val = step(threshold, val);
  
  vec2 imageSize = vec2(gl_NumWorkGroups.x * gl_WorkGroupSize.x, gl_NumWorkGroups.y * gl_WorkGroupSize.y);
  if (val > 0.5)
  {
    vec4 col = vec4(float(uv.x) / float(imageSize.x),  float(uv.y) / float(imageSize.y), 0.0, 1.0);
    imageStore(_output, uv, col);
  }
  else
  {
    imageStore(_output, uv, vec4(0.0, 0.0, 0.0, 1.0));
  }
}
