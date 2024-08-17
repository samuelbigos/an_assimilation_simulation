#[compute]
#version 450

layout(set = 0, binding = 0, rgba8) uniform image2D colorImage;
layout(set = 0, binding = 1, std430) restrict readonly buffer Color {
  float u_offset;
  float u_level;
  float u_max_steps;
};
layout(set = 0, binding = 2, rgba8) uniform image2D sampleImage;

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
	for(int x = -imageSize.x; x <= imageSize.y; x += imageSize.x)
	{
		for(int y = -imageSize.y; y <= imageSize.x; y += imageSize.y)
		{
			ivec2 voffset = uv;
			voffset += ivec2(x * int(u_offset), y * int(u_offset));

			vec2 pos = imageLoad(sampleImage, voffset).rg;
      pos.x *= imageSize.x;
      pos.y *= imageSize.y;
			float dist = distance(pos.xy, uv.xy);
			
			if(pos.x != 0.0 && pos.y != 0.0 && dist < closest_dist)
			{
				closest_dist = dist;
				closest_pos = pos;
			}
		}
	}
  vec4 col = vec4(1.0, 1.0, 0.0, 1.0);
  //col = vec4(closest_pos.x, closest_pos.y, 0.0, 1.0);
	imageStore(colorImage, uv, col);
}
