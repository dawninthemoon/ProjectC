using System;
using System.Diagnostics;
using System.Numerics;
using UnityEngine.UIElements;
namespace Game.Utils
{
    public static class MathEx
    {
        public static UnityEngine.Vector3 deleteZ(UnityEngine.Vector3 vector3) {return new UnityEngine.Vector3(vector3.x,vector3.y,0f);}

        public static int abs(int a){return a < 0 ? -a : a;}
        public static float abs(float a){return a < 0 ? -a : a;}
        
        public static float easeOutCubic(float delta) {return 1f - (float)UnityEngine.Mathf.Pow(1.0f - delta, 3.0f);}

        public static float lerpf(float a, float b, float time){return a + (b - a) * time;}
        public static UnityEngine.Vector2 lerpV2(UnityEngine.Vector2 a, UnityEngine.Vector2 b, float time) {return new UnityEngine.Vector2(lerpf(a.x,b.x,time),lerpf(a.y,b.y,time));}
        public static UnityEngine.Vector3 lerpV3WithoutZ(UnityEngine.Vector3 a, UnityEngine.Vector3 b, float time) {return new UnityEngine.Vector3(lerpf(a.x,b.x,time),lerpf(a.y,b.y,time));}
        public static int mini(int a, int b){return a < b ? a : b;}
        public static int maxi(int a, int b){return a > b ? a : b;}
        public static float minf(float a, float b){return a < b ? a : b;}
        public static float maxf(float a, float b){return a > b ? a : b;}
        public static int clampi(int value, int a, int b){return mini(maxi(value,a), b);}
        public static float clampf(float value, float a, float b){return minf(maxf(value,a), b);}
        public static float clamp01f(float value){return value < 0f ? 0f : (value > 1f ? 1f : value);}
        public static float clampDegree(float degree)
        {
            while(degree < 0f) {degree += 360f;}
            while(degree > 360f) {degree -= 360f;}
            
            return degree;
        }

        public static float distancef(float a, float b) {return abs(a - b);}

        public static UnityEngine.Vector3 convergence0(UnityEngine.Vector3 value, float a)
        {
            if(MathEx.equals(a,0f,float.Epsilon))
                return value;
                
            if(value.sqrMagnitude < float.Epsilon)
                return UnityEngine.Vector3.zero;
                
            UnityEngine.Vector3 vector = value.normalized * a;
            float result = value.sqrMagnitude - vector.sqrMagnitude;

            return result <= 0f ? UnityEngine.Vector3.zero : value - vector;
        }

        public static UnityEngine.Vector3 convergenceTarget(UnityEngine.Vector3 value, UnityEngine.Vector3 target, float a)
        {
            if(MathEx.equals(a,0f,float.Epsilon))
                return value;
                
            UnityEngine.Vector3 direction = (target - value).normalized;

            UnityEngine.Vector3 result = value + (direction * a);
            UnityEngine.Vector3 resultDirection = (target - result).normalized;

            return UnityEngine.Vector3.Angle(direction,resultDirection) > 100f ? target : result;
        }

        public static float convergence0(float value, float a)
        {
            if(equals(value,0f,float.Epsilon))
                return 0f;
                
            float mark = normalize(value);
            float result = abs(value) - abs(a);

            return result <= 0f ? 0f : result * mark;
        }

        public static float convergenceTarget(float value, float target, float a)
        {
            if(equals(value,target,float.Epsilon))
                return target;
                
            float mark = target > value ? 1f : -1f;
            float result = value + (abs(a) * mark);

            return (abs(target) - abs(result)) * mark <= 0f ? target : result;
        }

        public static float normalize(float value) {return value < 0f ? -1f : 1f;}

        public static bool equals(int a, int b){return a==b;}
        public static bool equals(float a, float b, float epsilon){return abs(a-b) < epsilon;}
        public static bool equals(UnityEngine.Color a, UnityEngine.Color b, float epsilon)
        {
            return equals(a.r,b.r,epsilon) && equals(a.g,b.g,epsilon) && equals(a.b,b.b,epsilon) && equals(a.a,b.a,epsilon);
        }

        public static bool equals(UnityEngine.Vector3 a, UnityEngine.Vector3 b, float epsilon)
        {
            return equals(a.x,b.x,epsilon) && equals(a.y,b.y,epsilon) && equals(a.z,b.z,epsilon);
        }

        public static bool equals(UnityEngine.Vector2 a, UnityEngine.Vector2 b, float epsilon)
        {
            return equals(a.x,b.x,epsilon) && equals(a.y,b.y,epsilon);
        }


        public static float directionToAngle(UnityEngine.Vector2 dir) 
        {
            float val = UnityEngine.Mathf.Atan2(dir.y, dir.x) * UnityEngine.Mathf.Rad2Deg;

            return clamp360Degree(val);
        }
        public static UnityEngine.Vector3 angleToDirection(float angle) {return new UnityEngine.Vector3(UnityEngine.Mathf.Cos(angle),UnityEngine.Mathf.Sin(angle));}
        public static float clamp360Degree(float eulerAngle)
        {
            //  float val = eulerAngle - Mathf.CeilToInt(eulerAngle / 360f) * 360f;
            //  val = val < 0 ? val + 360f : val;
            float val = eulerAngle + ((float)((int)-eulerAngle / 360) * 360f);
            val = val < 0 ? val + 360f : val;
            return val;
        }


        public static void makeTriangle(UnityEngine.Vector3 centerPosition,float radius, float theta, float directionAngle, out UnityEngine.Vector3 triangleA, out UnityEngine.Vector3 triangleB, out UnityEngine.Vector3 triangleC)
        {
            UnityEngine.Vector3 directionVector = UnityEngine.Quaternion.Euler(0f,0f,directionAngle) * UnityEngine.Vector3.right * radius;
            
            triangleA = centerPosition;
            triangleB = triangleA + UnityEngine.Quaternion.Euler(0f,0f,theta * .5f) * directionVector;
            triangleC = triangleA + UnityEngine.Quaternion.Euler(0f,0f,-theta * .5f) * directionVector;
        }

        public static bool isInTriangle(UnityEngine.Vector3 target, UnityEngine.Vector3 a, UnityEngine.Vector3 b, UnityEngine.Vector3 c, out float t, out float s, out float ots)
        {
            getBarycentricWeight(target, a, b, c, out t, out s, out ots);

            return t >= 0 && t <= 1 && s >= 0 && s <= 1 && ots >= 0 && ots <= 1;
        }

        public static void getBarycentricWeight(UnityEngine.Vector3 target, UnityEngine.Vector3 a, UnityEngine.Vector3 b, UnityEngine.Vector3 c, out float t, out float s, out float ots)
        {
            UnityEngine.Vector3 u = c - b;
            UnityEngine.Vector3 v = a - b;

            UnityEngine.Vector3 w = target - b;

            float uu = UnityEngine.Vector3.Dot(u, u);
            float vv = UnityEngine.Vector3.Dot(v, v);
            float uv = UnityEngine.Vector3.Dot(u, v);
            float wu = UnityEngine.Vector3.Dot(w, u);
            float wv = UnityEngine.Vector3.Dot(w, v);

            float denominator = uv * uv - uu * vv;
            t = (wu * uv - wv * uu) / denominator;
            s = (wv * uv - wu * vv) / denominator;
            ots = 1f - s - t;
        }

        public static bool pointSphereRayCast(UnityEngine.Vector3 currentPosition,UnityEngine.Vector3 targetPosition, UnityEngine.Vector3 direction, float radius)
        {
            UnityEngine.Vector3 perpendicularPoint = getPerpendicularPointOnLineSegment(currentPosition,currentPosition + direction,targetPosition);
            return UnityEngine.Vector3.Distance(perpendicularPoint, targetPosition) < radius;
        }

        public static UnityEngine.Vector3 getPerpendicularPointOnLineSegment(UnityEngine.Vector3 lineA, UnityEngine.Vector3 lineB, UnityEngine.Vector3 point)
        {
            UnityEngine.Vector3 pa = point - lineA;
            UnityEngine.Vector3 ab = lineB - lineA;

            float len = ab.sqrMagnitude;
            float dot = UnityEngine.Vector3.Dot(ab, pa);
            float distance = dot / len;

            if (distance < 0)
                return lineA;
            else if (distance > 1)
                return lineB;
            else
                return lineA + ab * distance;

        }

        public static UnityEngine.Vector3 getPerpendicularPointOnLine(UnityEngine.Vector3 lineA, UnityEngine.Vector3 lineB, UnityEngine.Vector3 point)
        {
            UnityEngine.Vector3 direction = lineB - lineA;
            float distance = UnityEngine.Vector3.Dot(direction, point - lineA) / direction.magnitude;
            float dot = UnityEngine.Vector3.Dot(direction, point - lineA);
            float t = dot / direction.sqrMagnitude;

            return lineA + t * direction;
        }

        public static bool findNearestPointOnTriangle(UnityEngine.Vector3 target, UnityEngine.Vector3 a, UnityEngine.Vector3 b, UnityEngine.Vector3 c, out UnityEngine.Vector3 result, out float nearDistance )
        {
            float t = 0f;
            float s = 0f;
            float ots = 0f;

            nearDistance = 0f;

            result = UnityEngine.Vector3.zero;

            if (isInTriangle(target, a, b, c, out t, out s, out ots))
                return false;

            result = target;
            nearDistance = float.MaxValue;
            if (s < 0)
            {
                result = getPerpendicularPointOnLineSegment(a, b, target);

                nearDistance = UnityEngine.Vector3.Distance(result, target);
            }

            if (t < 0)
            {
                UnityEngine.Vector3 point = getPerpendicularPointOnLineSegment(b, c, target);

                float distance = UnityEngine.Vector3.Distance(point, target);

                if (nearDistance > distance)
                {
                    nearDistance = distance;
                    result = point;
                }
            }

            if (ots < 0)
            {
                UnityEngine.Vector3 point = getPerpendicularPointOnLineSegment(c, a, target);

                float distance = UnityEngine.Vector3.Distance(point, target);

                if (nearDistance > distance)
                {
                    nearDistance = distance;
                    result = point;
                }
            }

            getBarycentricWeight(result, a, b, c, out t, out s, out ots);
            return true;
        }

        public static float damp(float a, float b, float lambda, float dt)
        {
            return UnityEngine.Mathf.Lerp(a, b, 1.0f - UnityEngine.Mathf.Exp(-lambda * dt));
        }

        public static UnityEngine.Vector3 damp(UnityEngine.Vector3 a, UnityEngine.Vector3 b, float lambda, float deltaTime)
        {
            return UnityEngine.Vector3.Lerp(a, b, 1.0f - UnityEngine.Mathf.Exp(-lambda * deltaTime));
        }

        public static UnityEngine.Vector3 round(UnityEngine.Vector3 value, int digits)
        {
            return new UnityEngine.Vector3(round(value.x, digits), round(value.y, digits), round(value.z, digits));
        }

        public static UnityEngine.Vector3 ceiling(UnityEngine.Vector3 value, int digits)
        {
            return new UnityEngine.Vector3(ceiling(value.x, digits), ceiling(value.y, digits), ceiling(value.z, digits));
        }

        public static UnityEngine.Vector3 floor(UnityEngine.Vector3 value, int digits)
        {
            return new UnityEngine.Vector3(floor(value.x, digits), floor(value.y, digits), floor(value.z, digits));
        }

        public static UnityEngine.Vector3 floorNoSign(UnityEngine.Vector3 value, int digits)
        {
            return new UnityEngine.Vector3(floorNoSign(value.x, digits), floorNoSign(value.y, digits), floorNoSign(value.z, digits));
        }

        public static float round(float value, int digits)
        {
            return (float)System.Math.Round(value,digits);
        }

        public static float floorNoSign(float value, int digits)
        {
            float factor = (float)Math.Pow(10.0,(double)digits);
            float sign = value < 0f ? -1f : 1f;
            return (float)Math.Floor(MathEx.abs(value) * factor) * (1f / factor) * sign;
        }

        public static float ceiling(float value, int digits)
        {
            float factor = (float)Math.Pow(10.0,(double)digits);
            return (float)Math.Ceiling(value * factor) / factor;
        }

        public static float floor(float value, int digits)
        {
            float factor = (float)Math.Pow(10.0,(double)digits);
            return (float)Math.Floor(value * factor) / factor;
        }

        public static UnityEngine.Vector3 lemniscate(float time)
        {
            float scale = 2f / (3f - UnityEngine.Mathf.Cos(2f * time)); 
            float x = scale * UnityEngine.Mathf.Cos(time); 
            float y = scale * UnityEngine.Mathf.Sin(2f * time) / 2f;

            return new UnityEngine.Vector3(x, y, 0f);
        }

        public static bool intersectRect(UnityEngine.Vector3 point, UnityEngine.Vector3 center, float width, float height)
        {
            float l,r,b,t;

            l = center.x - width * 0.5f;
            r = center.x + width * 0.5f;

            b = center.y - height * 0.5f;
            t = center.y + height * 0.5f;

            return (l < point.x && r > point.x) && (b < point.y && t > point.y);
        }

        public static bool findLineIntersection(UnityEngine.Vector2 p1, UnityEngine.Vector2 p2, UnityEngine.Vector2 p3, UnityEngine.Vector2 p4, out UnityEngine.Vector2 intersection)
        {
            intersection = UnityEngine.Vector2.zero;

            UnityEngine.Vector2 dirA = p2 - p1;
            float lenA = dirA.magnitude;
            if(equals(lenA,0f,float.Epsilon))
                return false;
                
            dirA *= 1f / lenA;

            UnityEngine.Vector2 dirB = p4 - p3;
            float lenB = dirB.magnitude;
            if(equals(lenB,0f,float.Epsilon))
                return false;

            dirB *= 1f / lenB;

            float det = dirA.x * dirB.y - dirA.y * dirB.x;
            if(equals(det,0f,float.Epsilon))
                return false;

            UnityEngine.Vector2 delta = p3 - p1;
            float t = (delta.x * dirB.y - delta.y * dirB.x) / det;
            float u = (delta.x * dirA.y - delta.y * dirA.x) / det;

            if(t < 0f || t > lenA || u < 0f || u > lenB)
                return false;

            intersection = p1 + dirA * t;
            return true;
        }


        public enum EaseType{
            Linear,
            EaseInQuad,
            EaseOutQuad,
            EaseInOutQuad,
            EaseInCubic,
            EaseOutCubic,
            EaseInOutCubic,
            End,
        }
        public static UnityEngine.Vector2 GetEaseFormula(EaseType type, UnityEngine.Vector2 start, UnityEngine.Vector2 end, float time)
        {
            if(type == EaseType.Linear)
            {
                return LinearVector2(start,end,time);
            }
            else if(type == EaseType.EaseInQuad)
            {
                return EaseInQuadVector2(start,end,time);
            }
            else if(type == EaseType.EaseOutQuad)
            {
                return EaseOutQuadVector2(start,end,time);
            }
            else if(type == EaseType.EaseInOutQuad)
            {
                return EaseInOutQuadVector2(start,end,time);
            }
            else if(type == EaseType.EaseInCubic)
            {
                return EaseInCubicVector2(start,end,time);
            }
            else if(type == EaseType.EaseOutCubic)
            {
                return EaseOutCubicVector2(start,end,time);
            }
            else if(type == EaseType.EaseInOutCubic)
            {
                return EaseInOutCubicVector2(start,end,time);
            }

            return new UnityEngine.Vector2();
        }

        public static float getEaseFormula(EaseType type, float start, float end, float time)
        {
            if(type == EaseType.Linear)
            {
                return linear(start,end,time);
            }
            else if(type == EaseType.EaseInQuad)
            {
                return easeInOutQuad(start,end,time);
            }
            else if(type == EaseType.EaseOutQuad)
            {
                return easeOutQuad(start,end,time);
            }
            else if(type == EaseType.EaseInOutQuad)
            {
                return easeInOutQuad(start,end,time);
            }
            else if(type == EaseType.EaseInCubic)
            {
                return easeInCubic(start,end,time);
            }
            else if(type == EaseType.EaseOutCubic)
            {
                return easeOutCubic(start,end,time);
            }
            else if(type == EaseType.EaseInOutCubic)
            {
                return easeInOutCubic(start,end,time);
            }
            else
            {
                Debug.Assert(false, "잘못된 EaseType " + type);
            }

            return 0f;
        }

        public static UnityEngine.Vector2 LinearVector2(UnityEngine.Vector2 start, UnityEngine.Vector2 end ,float time)
        {
            return UnityEngine.Vector2.Lerp(start,end,time);
        }

        public static UnityEngine.Vector2 getPointOnBezierCurve(UnityEngine.Vector2 p0, UnityEngine.Vector2 p1, UnityEngine.Vector2 p2, UnityEngine.Vector2 p3, float t)
        {
            float u = 1f - t;
            float t2 = t * t;
            float u2 = u * u;
            float u3 = u2 * u;
            float t3 = t2 * t;
    
            UnityEngine.Vector2 result =
                (u3) * p0 +
                (3f * u2 * t) * p1 +
                (3f * u * t2) * p2 +
                (t3) * p3;
    
            return result;
        }

        public static float approximateBezierCurveLength(UnityEngine.Vector2 p0, UnityEngine.Vector2 p1, UnityEngine.Vector2 p2, UnityEngine.Vector2 p3, float sampleRate = 0.01f)
        {
            float rate = 0f;
            float resultDistance = 0f;
            for(; rate + sampleRate < 1f; rate += sampleRate)
            {
                UnityEngine.Vector2 first = getPointOnBezierCurve(p0,p1,p2,p3, rate);

                float secondRate = MathEx.minf(1f, rate + sampleRate);
                UnityEngine.Vector2 second = getPointOnBezierCurve(p0,p1,p2,p3, secondRate);

                resultDistance += UnityEngine.Vector2.Distance(first,second);
            }

            return resultDistance;
        }

        public static UnityEngine.Vector2 EaseInQuadVector2(UnityEngine.Vector2 start, UnityEngine.Vector2 end, float time)
        {
            return new UnityEngine.Vector2(easeInQuad(start.x,end.x,time),easeInQuad(start.y,end.y,time));
        }

        public static UnityEngine.Vector2 EaseOutQuadVector2(UnityEngine.Vector2 start, UnityEngine.Vector2 end ,float time)
        {
            return new UnityEngine.Vector2(easeOutQuad(start.x,end.x,time),easeOutQuad(start.y,end.y,time));
        }

        public static UnityEngine.Vector2 EaseInOutQuadVector2(UnityEngine.Vector2 start, UnityEngine.Vector2 end ,float time)
        {
            return new UnityEngine.Vector2(easeInOutQuad(start.x,end.x,time),easeInOutQuad(start.y,end.y,time));
        }

        public static UnityEngine.Vector2 EaseInCubicVector2(UnityEngine.Vector2 start, UnityEngine.Vector2 end ,float time)
        {
            return new UnityEngine.Vector2(easeInCubic(start.x,end.x,time),easeInCubic(start.y,end.y,time));
        }

        public static UnityEngine.Vector2 EaseOutCubicVector2(UnityEngine.Vector2 start, UnityEngine.Vector2 end ,float time)
        {
            return new UnityEngine.Vector2(easeOutCubic(start.x,end.x,time),easeOutCubic(start.y,end.y,time));
        }
        
        public static UnityEngine.Vector2 EaseInOutCubicVector2(UnityEngine.Vector2 start, UnityEngine.Vector2 end ,float time)
        {
            return new UnityEngine.Vector2(easeInOutCubic(start.x,end.x,time),easeInOutCubic(start.y,end.y,time));
        }

        public static float linear(float start, float end, float value)
        {
            return lerpf(start, end, value);
        }

        public static float easeInQuad(float start, float end, float value)
        {
            end -= start;
            return end * value * value + start;
        }

        public static float easeOutQuad(float start, float end, float value)
        {
            end -= start;
            return -end * value * (value - 2) + start;
        }

        public static float easeInOutQuad(float start, float end, float value)
        {
            value /= .5f;
            end -= start;
            if (value < 1) return end * 0.5f * value * value + start;
            value--;
            return -end * 0.5f * (value * (value - 2) - 1) + start;
        }

        public static float easeInCubic(float start, float end, float value)
        {
            end -= start;
            return end * value * value * value + start;
        }

        public static float easeOutCubic(float start, float end, float value)
        {
            value--;
            end -= start;
            return end * (value * value * value + 1) + start;
        }

        public static float easeInOutCubic(float start, float end, float value)
        {
            value /= .5f;
            end -= start;
            if (value < 1) return end * 0.5f * value * value * value + start;
            value -= 2;
            return end * 0.5f * (value * value * value + 2) + start;
        }

    //움직임 목록에 포함 안되있는 식들===============================================
        public static float easeInBack(float start, float end, float value)
        {
            end -= start;
            value /= 1;
            float s = 1.70158f;
            return end * (value) * value * ((s + 1) * value - s) + start;
        }
    //============================================================================
    }
}
