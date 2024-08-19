#[compute]
#version 450

/* Layouts */
layout(set = 0, binding = 0, std430) restrict buffer Position {
	vec2 data[];
} boidPositions;
layout(set = 1, binding = 0, std430) restrict buffer Velocity {
	vec2 data[];
} boidVelocities;
layout(set = 2, binding = 0, std430) restrict buffer BoidData {
	float radius[];
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
	float boidCohesionRadius;
	float boidAlignmentRadius;
	float boidSdfAvoidDistance;
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
	r = v * (dot(u, v) / max(0.000001, dot(v, v)));
	return r;
}
float lengthSq(vec2 v) {
	return dot(v, v);
}
float sq(float v) {
	return v * v;
}
vec2 limit(vec2 v, float l) {
	float len = length(v);
	if (len == 0.0f) return v;
	float i = l / len;
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
vec2 steeringSeparation(vec2 v0, vec2 p0, vec2 p1, float r0, float r1, float c) {
	float distSq = lengthSq(p0 - p1);
	if (distSq == 0.0) return vec2(0,0);
	distSq = max(0.00001, distSq - (r0 + r1));
	float scale = clamp(1.0 - (distSq / sq(c)), 0.00001, 1.0);
	vec2 dir = normalize(p0 - p1);
	return limit(dir, scale * params.boidMaxForce);
	// float dist = length(p0 - p1);
	// if (dist > params.boidSeparationRadius) return vec2(0,0);
	// float scale = pow(clamp(1.0 - (dist / params.boidSeparationRadius), 0.0, 1.0), 3.0);
	// vec2 dir = normalize(p0 - p1);
	// return limit(dir, scale * params.boidMaxForce);
}
vec2 steeringCohesion(vec2 p0, vec2 p1, vec2 v0) {
	vec2 desired = p1 - p0;
	vec2 force = desired - v0;
	return force * params.boidMaxForce / params.boidMaxSpeed;
}
vec2 steeringAlignment(vec2 v0, vec2 v1) {
	vec2 desired = v1;
	vec2 force = desired - v0;
	return force * params.boidMaxForce / params.boidMaxSpeed;
}
vec2 steeringMaintainSpeed(vec2 v0) {
	vec2 desired = normalize(v0) * params.boidMaxSpeed;
	vec2 force = desired - v0;
	return force * params.boidMaxForce / params.boidMaxSpeed;
}
vec2 steeringAvoid(vec2 v0, vec2 p0, vec2 p1, float r0, float r1, float c) {
	if (dot(normalize(p0 - p1), normalize(v0)) > 0.0) return vec2(0,0);

	float dist = length(p0 - p1);
	if (dist > c) return vec2(0,0);

	float scale = pow(clamp(1.0 - (dist / c), 0.0, 1.0), 1.0);

	vec2 dir = normalize(p0 - p1);
	vec2 desired = dot(v0, dir) * dir;
	desired = normalize(v0 - desired) * params.boidMaxSpeed;
	return desired;
}

/* Main */
void main() {
	uint id = gl_GlobalInvocationID.x;

	vec2 boidPos = decodePos(boidPositions.data[id]);
	vec2 boidVel = boidVelocities.data[id];
	float boidRadius = boidRadii.data[id];

	/* Steering behaviours */
	vec2 separationForce = vec2(0,0);
	vec2 cohesionForce = vec2(0,0);
	vec2 alignmentForce = vec2(0,0);
	vec2 avoidForce = vec2(0,0);
	vec2 totalForce = vec2(0,0);

	int cohesionCount = 0;
	vec2 cohesionPosition = vec2(0,0);
	int alignmentCount = 0;
	vec2 alignmentVelocity = vec2(0,0);

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

		separationForce += steeringSeparation(v0, p0, p1, r0, r1, params.boidSeparationRadius);
		
		if (lengthSq(p0 - p1) < sq(params.boidCohesionRadius))
		{
			cohesionPosition += p1;
			cohesionCount++;
		}
		if (lengthSq(p0 - p1) < sq(params.boidAlignmentRadius))
		{
			alignmentVelocity += v1;
			alignmentCount++;
		}

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

	if (cohesionCount > 0)
		cohesionForce += steeringCohesion(boidPos, cohesionPosition / cohesionCount, boidVel);
	if (alignmentCount > 0)
		alignmentForce += steeringAlignment(boidVel, alignmentVelocity / alignmentCount);

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

		avoidForce += steeringAvoid(v0, p0, p1, r0, r1, params.boidSdfAvoidDistance);

		float separation = distance(p0, p1);
		float r = r0 + r1;
		float diff = separation - r;
		if (diff <= 0.0) // hit
		{
			boidPos += diff * 1.0 * normalize(p1 - p0);

			vec2 nv0 = v0;
			nv0 += projectUonV(v1, p1 - p0);
			nv0 -= projectUonV(v0, p0 - p1);
			boidVel = nv0 * 1.0;
		}
	}

	// Accumulate the flocking forces.
	totalForce += limit(separationForce, params.boidMaxForce / 3.0);
	totalForce += limit(cohesionForce, params.boidMaxForce / 3.0);
	totalForce += limit(alignmentForce, params.boidMaxForce / 3.0);

	// Give a little force to maintain the max speed, to keep boids moving.
	totalForce += steeringMaintainSpeed(boidVel) * 0.25;

	// Avoid terrain.
	totalForce += limit(avoidForce, params.boidMaxForce) * 2.0;

	totalForce = limit(totalForce, params.boidMaxForce);

	boidVel += totalForce;
	boidVel = limit(boidVel, params.boidMaxSpeed);

	boidPos = boidPos + boidVel;

	boidPositions.data[id] = encodePos(boidPos);
	boidVelocities.data[id] = boidVel;

	debugOut.data[id] = vec4(id, cohesionForce.x, cohesionForce.y, 0);
}
