using Godot;
using System;
using System.Diagnostics;
using Vector2 = System.Numerics.Vector2;

public partial class SteeringManager
{
    public enum Behaviours // in order of importance
    {
        DesiredVelocityOverride,
        Separation,
        AvoidObstacles,
        AvoidAllies,
        AvoidEnemies,
        Flee,
        MaintainSpeed,
        Cohesion,
        Alignment,
        Arrive,
        Pursuit,
        Wander,
        FlowFieldFollow,
        MaintainDistance,
        MaintainOffset,
        Stop,
        MaintainBroadside,
        COUNT,
    }
    
    private static Vector2 Steering_Seek(in Boid boid, Vector2 position, float limit = -1.0f)
    {
        Vector2 desired = position - boid.Position;
        desired.SetMag(boid.Velocity.Length() + boid.MaxForce);
        
        Vector2 force = desired - boid.Velocity;
        return force;
    }
    
    private static Vector2 Steering_Arrive(in Boid boid, Vector2 position)
    {
        float dist = (boid.Position - position).Length();
        float reqDist = boid.Velocity.LengthSquared() / (2.0f * boid.MaxForce);
        boid.ArriveRadius = reqDist;

        return reqDist > dist ? Steering_Stop(boid) : Steering_Seek(boid, position);
    }

    private static Vector2 Steering_Cohesion(in Boid boid, in ReadOnlySpan<Boid> boids)
    {
        Vector2 centre = Vector2.Zero;
        int count = 0;
        foreach (ref readonly Boid other in boids)
        {
            if (boid.Id == other.Id) continue;
            if (boid.Alignment != other.Alignment) continue;
            if ((boid.Position - other.Position).LengthSquared() > Sq(boid.ViewRange)) continue;
            if (!InView(boid, other, boid.ViewAngle)) continue;

            centre += other.Position;
            count++;
        }

        if (count == 0)
            return Vector2.Zero;

        centre /= count;
        return Steering_Seek(boid, centre);
    }

    private static Vector2 Steering_Align(in Boid boid, in ReadOnlySpan<Boid> boids)
    {
        Vector2 desired = Vector2.Zero;
        int count = 0;
        foreach (ref readonly Boid other in boids)
        {
            if (boid.Id == other.Id) continue;
            if (boid.Alignment != other.Alignment) continue;
            if ((boid.Position - other.Position).LengthSquared() > Sq(boid.ViewRange)) continue;
            if (!InView(boid, other, boid.ViewAngle)) continue;

            desired += other.Velocity;
            count++;
        }

        if (count == 0)
            return Vector2.Zero;

        desired /= count;
        Vector2 force = desired - boid.Velocity;
        return force;
    }

    private static Vector2 Steering_Separate(in Boid boid, in ReadOnlySpan<Boid> boids, in ReadOnlySpan<Obstacle> obstacles, double delta)
    {
        Vector2 forceSum = Vector2.Zero;
        int count = 0;
        Vector2 pos = boid.Position;
        float radius = boid.Radius;
        
        // boids
        int n = boids.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 otherPos = boids[i].Position;
            float x = pos.X - otherPos.X;
            float y = pos.Y - otherPos.Y;
            float distSq = x * x + y * y;
            float radiusSq = Sq(radius + boids[i].Radius);
            if (distSq > radiusSq || distSq == 0.0f)
                continue;
            
            ref readonly Boid other = ref boids[i];
            if (other.Ignore) continue;
            if (boid.Id == other.Id) continue;

            Vector2 desired = pos - otherPos;
            desired.SetMag(boid.MaxSpeed);
            float t = 1.0f;//- Mathf.Pow(distSq / radiusSq, 2.0f);
            Vector2 force = desired.Limit(boid.MaxSpeed * t);
            forceSum += force;
            count++;
        }

        // obstacles
        for (int i = 0; i < obstacles.Length; i++)
        {
            Obstacle other = obstacles[i];
            float distSq = (boid.Position - other.Position).LengthSquared();
            float radiusSq = boid.Radius + other.Size;
            if (distSq > radiusSq)
                continue;
        
            Vector2 desired = boid.Position - other.Position;
            desired.SetMag(boid.MaxSpeed);
            float t = 1.0f - Mathf.Pow(distSq / radiusSq, 2.0f);
            Vector2 force = desired.Limit(boid.MaxForce * (float)delta * t);
            forceSum += force;
            count++;
        }

        return count == 0 ? Vector2.Zero : forceSum;
    }

    private static Vector2 Steering_Pursuit(in Boid boid)
    {
        return Steering_Seek(boid, boid.Target);
    }
    
    private static Vector2 Steering_Flee(in Boid boid)
    {
        return -Steering_Seek(boid, boid.Target);
    }

    private static Vector2 Steering_AvoidObstacles(ref Boid boid, in ReadOnlySpan<Obstacle> obstacles)
    {
        Intersection intersection = default;
        float nearestDistance = 999999.0f;
        float range = boid.Speed * boid.LookAhead;
        
        // obstacles
        for (int i = 0; i < obstacles.Length; i++)
        {
            Obstacle obstacle = obstacles[i];
            switch (obstacle.Shape3D)
            {
                case ObstacleShape.Circle:
                {
                    if ((obstacle.Position - boid.Position).Length() > range + obstacle.Size + boid.Radius)
                        continue;

                    bool collision = CollisionDetection(boid.Position, obstacle.Position, boid.Velocity, Vector2.Zero,
                        boid.Radius, obstacle.Size,
                        out Vector2 collisionPos, out Vector2 collisionNormal, out float collisionTime);

                    if (!collision)
                        continue;

                    float dist = (collisionPos - boid.Position).LengthSquared();
                    if (dist < nearestDistance)
                    {
                        nearestDistance = dist;
                        intersection.SurfaceNormal = collisionNormal;
                        intersection.IntersectTime = collisionTime;
                        intersection.Intersect = true;
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // if nearby intersection found, steer away from it, otherwise no steering
#if !FINAL
        boid.Intersection = intersection;
#endif
        if (intersection.Intersect && intersection.IntersectTime < boid.LookAhead)
        {
            Vector2 force = intersection.SurfaceNormal.PerpendicularComponent(boid.Heading);
            force.SetMag(boid.MaxForce);
            return force;
        }
        return Vector2.Zero;
    }

    private static Vector2 Steering_AvoidAllies(ref Boid boid, int index, in ReadOnlySpan<Boid> boids)
    {
        return Steering_AvoidBoids(ref boid, index, boids, true);
    }
    
    private static Vector2 Steering_AvoidEnemies(ref Boid boid, int index, in ReadOnlySpan<Boid> boids)
    {
        return Steering_AvoidBoids(ref boid, index, boids, false);
    }

    private static Vector2 Steering_AvoidBoids(ref Boid boid, int index, in ReadOnlySpan<Boid> boids, bool avoidAllies)
    {
        Intersection intersection = default;
        float nearestDistance = 999999.0f;
        float range = boid.Speed * boid.LookAhead;
        byte alignment = boid.Alignment;
        Vector2 pos = boid.Position;
        float radius = boid.Radius;
        
        // boids
        int n = boids.Length;
        for (int i = 0; i < n; i++)
        {
            if (index == i) continue;

            byte otherAlignment = boids[i].Alignment;
            if (!avoidAllies && alignment == otherAlignment) continue;
            if (avoidAllies && alignment != otherAlignment) continue;

            Vector2 otherPos = boids[i].Position;
            float x = pos.X - otherPos.X;
            float y = pos.Y - otherPos.Y;
            float distSq = x * x + y * y;
            float radiusSq = Sq(range + radius + boids[i].Radius);
            if (distSq > radiusSq || distSq == 0.0f) continue;
            
            ref readonly Boid other = ref boids[i];
            if (other.Ignore) continue;
            if (!InView(boid, other, boid.ViewAngle)) continue;

            bool collision = CollisionDetection(boid.Position, other.Position, boid.Velocity, other.Velocity,
                boid.Radius, other.Radius,
                out Vector2 collisionPos, out Vector2 collisionNormal, out float collisionTime);

            if (!collision)
                continue;

            float dist = (collisionPos - boid.Position).LengthSquared();
            if (dist < nearestDistance)
            {
                nearestDistance = dist;
                intersection.SurfaceNormal = collisionNormal;
                intersection.SurfacePoint = collisionPos;
                intersection.IntersectTime = collisionTime;
                intersection.Intersect = true;
            }
        }

        // if nearby intersection found, steer away from it, otherwise no steering
#if !FINAL
        boid.Intersection = intersection;
#endif
        if (intersection.Intersect && intersection.IntersectTime < boid.LookAhead)
        {
            Vector2 force = intersection.SurfaceNormal.PerpendicularComponent(boid.Heading);
            force.SetMag(boid.MaxForce);
            return force;
        }
        return Vector2.Zero;
    }

    private static Vector2 Steering_MaintainSpeed(in Boid boid)
    {
        Vector2 desired = boid.Heading * boid.DesiredSpeed;
        Vector2 force = desired - boid.Velocity;
        return force;
    }
    
    private static Vector2 Steering_Wander(ref Boid boid, double delta)
    {
        Vector2 circleCentre = boid.Position + boid.Heading * boid.WanderCircleDist;
        float angle = -boid.Heading.AngleTo(Vector2.UnitX) + boid.WanderAngle;
        Vector2 displacement = Vector2.Normalize(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))) * boid.WanderCircleRadius;
        boid.WanderAngle += (Utils.Rng.Randf() - 0.5f) * (float)delta * boid.WanderVariance;

        Vector2 desired = (circleCentre + displacement) - boid.Position;
        Vector2 force = desired - boid.Velocity;
        return force;
    }
    
    private static Vector2 Steering_MaintainDistance(in Boid boid)
    {
        Vector2 targetToSelf = (boid.Position - boid.Target).NormalizeSafe();
        float currentDist = (boid.Position - boid.Target).Length();
        if (currentDist > boid.DesiredDistFromTargetMin && currentDist < boid.DesiredDistFromTargetMax)
            return Vector2.Zero;
        Vector2 targetPos = boid.Target + targetToSelf * Mathf.Clamp(currentDist, boid.DesiredDistFromTargetMin, boid.DesiredDistFromTargetMax);
        return Steering_Arrive(boid, targetPos);
    }
    
    private static Vector2 Steering_MaintainOffset(in Boid boid)
    {
        Vector2 targetPos = boid.Target + boid.DesiredOffsetFromTarget;
        return Steering_Arrive(boid, targetPos);
    }

    private static Vector2 Steering_Stop(in Boid boid)
    {
        Vector2 desired = Vector2.Zero;
        return desired - boid.Velocity;
    }
    
    private static Vector2 Steering_MaintainBroadside(in Boid boid)
    {
        Vector2 targetToSelf = (boid.Position - boid.Target).NormalizeSafe();
        Vector2 perp = boid.Velocity.PerpendicularComponent(targetToSelf).NormalizeSafe();
        if (perp == Vector2.Zero)
            perp = targetToSelf.Rot90();
        Vector2 force = perp - boid.Velocity;
        return force.Limit(boid.MaxForce);
    }
    
    private static Vector2 Steering_DesiredVelocityOverride(in Boid boid)
    {
        return boid.DesiredVelocityOverride - boid.Velocity;
    }
}
