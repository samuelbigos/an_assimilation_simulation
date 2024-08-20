shader_type spatial;
render_mode unshaded;

uniform vec4 _terrainCol1 : source_color = vec4(1.0);
uniform vec4 _terrainCol2 : source_color = vec4(1.0);
uniform vec4 _terrainCol3 : source_color = vec4(1.0);
uniform sampler2D _sdf;

void vertex() {
	// Called for every vertex the material is visible on.
}

float sRGB(float x) {
    if (x <= 0.00031308)
        return 12.92 * x;
    else
        return 1.055*pow(x,(1.0 / 2.4) ) - 0.055;
}
vec3 sRGB_v3(vec3 c) {
    return vec3(sRGB(c.x),sRGB(c.y),sRGB(c.z));
}

void fragment() {
	float distMod = 50.0;
	float dist = texture(_sdf, UV).r * distMod;
	
	float threshold1 = 1.0;
	float threshold2 = 10.0;
	
	vec3 col;
	if (dist < threshold1)
	{
		col = _terrainCol1.rgb;
	}
	else if (dist < threshold2)
	{
		float v = (dist - threshold1) / (threshold2 - threshold1);
		col = mix(_terrainCol2, _terrainCol3, 1.0 - pow(1.0 - v, 2.0)).rgb;
	}
	else
	{
		col = _terrainCol3.rgb;
	}
	
	ALBEDO = col;
	ALPHA = 1.0;
}