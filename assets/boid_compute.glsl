#[compute]
#version 450

/* Layouts */
layout(set = 0, binding = 0, std430) restrict buffer Position {
	vec2 data[];
} boidPositions;
layout(set = 1, binding = 0, std430) restrict buffer Velocity {
	vec2 data[];
} boidVelocities;
layout(set = 2, binding = 0, std430) restrict buffer Radius {
	float data[];
} boidRadii;
layout(set = 3, binding = 0, std430) restrict buffer DebugOut {
	vec4 data[];
} debugOut;

layout(set = 4, binding = 0) uniform sampler2D _distanceField;

layout(push_constant, std430) uniform Params {
	float numBoids;	
	float imageSizeX;
	float imageSizeY;
	float sdfDistMod;
	float boidMaxSpeed;
	float boidMaxForce;
	float boidSeparationRadius;
	float boidCoherenceRadius;
	float boidAlignmentRadius;
} params;

layout(local_size_x = 1024, local_size_y = 1, local_size_z = 1) in;

/* Shared functions */
float sdf(vec2 p) {
	vec2 uv = vec2(p.x, p.y);
	uv.x = uv.x / params.imageSizeX;
	uv.y = uv.y / params.imageSizeY;
	return clamp(texture(_distanceField, uv).r - 0.01, 0.000001, 1.0) * params.sdfDistMod;
}
vec2 calcNormal(vec2 p) {
	float h = 1;
	return normalize(vec2(sdf(p + vec2(h, 0)) - sdf(p - vec2(h, 0)),
					sdf(p + vec2(0, h)) - sdf(p - vec2(0, h))));
}
vec2 projectUonV(vec2 u, vec2 v) {
	vec2 r;
	r = v * (dot(u, v) / dot(v, v));
	return r;
}
float lengthSq(vec2 v) {
	return dot(v, v);
}
float sq(float v) {
	return v * v;
}
vec2 limit(vec2 v, float l) {
	float length = length(v);
	if (length == 0.0f) return v;
	float i = l / length;
	i = min(i, 1.0f);
	return v * i;
}

// Converts from world space into terrain space and back again.
vec2 encodePos(vec2 p) {
	return p - vec2(params.imageSizeX, params.imageSizeY) * 0.5;
}
vec2 decodePos(vec2 p) {
	return p + vec2(params.imageSizeX, params.imageSizeY) * 0.5;
}

/* Steering behaviours */
vec2 steeringSeparation(vec2 v0, vec2 p0, vec2 p1, float r0, float r1) {
	float dist = max(0.0, length(p0 - p1) - (r0 + r1));
	vec2 dir = normalize(p1 - p0);
	vec2 desired = max(0.0, params.boidSeparationRadius - dist) * dir;
	return desired - v0;
}
vec2 steeringCoherence(vec2 p0, vec2 p1) {
	return vec2(0, 0);
}
vec2 steeringAlignment(vec2 p0, vec2 p1, vec2 v0, vec2 v1) {
	return vec2(0, 0);
}

/* Main */
void main() {
	uint id = gl_GlobalInvocationID.x;

	vec2 boidPos = decodePos(boidPositions.data[id]);
	vec2 boidVel = boidVelocities.data[id];
	float boidRadius = boidRadii.data[id];

	/* Steering behaviours */
	vec2 separationForce = vec2(0,0);
	vec2 coherenceForce = vec2(0,0);
	vec2 alignmentForce = vec2(0,0);
	vec2 totalForce = vec2(0,0);

    for (int i = 0; i < params.numBoids; i++)
    {
        if (i == id)
            continue;

        vec2 p0 = boidPos;
        vec2 p1 = decodePos(boidPositions.data[i]);
        vec2 v0 = boidVel;
        vec2 v1 = boidVelocities.data[i];
        float r0 = boidRadius;
        float r1 = boidRadii.data[i];

		separationForce += steeringSeparation(v0, p0, p1, r0, r1);
		coherenceForce += steeringCoherence(p0, p1);
		alignmentForce += steeringAlignment(p0, p1, v0, v1);

		// Collide with other boids
		{
			float separation = distance(p0, p1);
			float r = r0 + r1;
			float diff = separation - r;
			if (diff <= 0.0)
			{
				boidPos += diff * 0.5 * normalize(p1 - p0);

				vec2 nv0 = v0;
				nv0 += projectUonV(v1, p1 - p0);
				nv0 -= projectUonV(v0, p0 - p1);
				boidVel = nv0 * 1.0;
			}
		}
    }

	// Collide with terrain
	float terrainDist = sdf(boidPos);
	{
		vec2 toSurface = -calcNormal(boidPos);

		vec2 p0 = boidPos;
		vec2 p1 = boidPos + toSurface * terrainDist;
		vec2 v0 = boidVel;
		vec2 v1 = vec2(0.0, 0.0);
		float r0 = boidRadius;
		float r1 = 0.0;

		float separation = distance(p0, p1);
		float r = r0 + r1;
		float diff = separation - r;
		if (diff <= 0.0) // hit
		{
			boidPos += diff * 0.5 * normalize(p1 - p0);

			vec2 nv0 = v0;
			nv0 += projectUonV(v1, p1 - p0);
			nv0 -= projectUonV(v0, p0 - p1);
			boidVel = nv0 * 1.0;
		}
	}

	totalForce += separationForce;
	totalForce += coherenceForce;
	totalForce += alignmentForce;
	totalForce = limit(totalForce, params.boidMaxForce);

	//boidVel += totalForce;
	boidVel = limit(boidVel, params.boidMaxSpeed);

	boidPos = boidPos + boidVel;

	boidPositions.data[id] = encodePos(boidPos);
	boidVelocities.data[id] = boidVel;

	debugOut.data[id] = vec4(id, boidMaxSpeed, 0, 0);
}
