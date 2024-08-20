shader_type spatial;
render_mode unshaded;

uniform vec4 _terrainCol1 : source_color = vec4(1.0);
uniform vec4 _terrainCol2 : source_color = vec4(1.0);
uniform vec4 _terrainCol3 : source_color = vec4(1.0);
uniform sampler2D _sdf;

void vertex() {
	// Called for every vertex the material is visible on.
}

void fragment() {
	float distMod = 50.0;
	float dist = texture(_sdf, UV).r * distMod;
	
	float threshold1 = 1.0;
	float threshold2 = 10.0;
	
	if (dist < threshold1)
	{
		ALBEDO = _terrainCol1.rgb;
	}
	else if (dist < threshold2)
	{
		float v = (dist - threshold1) / (threshold2 - threshold1);
		ALBEDO = mix(_terrainCol2, _terrainCol3, 1.0 - pow(1.0 - v, 2.0)).rgb;
	}
	else
	{
		ALBEDO = _terrainCol3.rgb;
	}
	
	ALPHA = 1.0;
}