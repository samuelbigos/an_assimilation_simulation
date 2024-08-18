shader_type spatial;
render_mode unshaded;

uniform sampler2D _sdf;

void vertex() {
	// Called for every vertex the material is visible on.
}

void fragment() {
	vec4 colour = texture(_sdf, UV);
	float value = step(0.3f, colour.r);
	ALBEDO = vec3(colour.rgb);
	ALPHA = 1.0f;
}