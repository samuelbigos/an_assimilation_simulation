shader_type spatial;
render_mode unshaded;

uniform sampler2D _noiseTexture;

void vertex() {
	// Called for every vertex the material is visible on.
}

void fragment() {
	vec4 colour = texture(_noiseTexture, UV);
	float value = step(colour.r, 0.6f);
	ALBEDO = vec3(value);
	ALPHA = 1.0f;
}