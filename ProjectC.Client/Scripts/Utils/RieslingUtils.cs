using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Linq;

namespace Game.Utils 
{
    public static class ExVector {
        public static float GetRadian(Vector2 u, Vector2 v) {
            Vector2 diff = v - u;
            return Mathf.Atan2(diff.y, diff.x);
        }

        public static float GetDegree(Vector2 u, Vector2 v) {
            return GetRadian(u, v) * Mathf.Rad2Deg;
        }

        public static Vector2 AddDegree(this Vector2 dir, float degree)
        {
            float radian = degree * Mathf.Deg2Rad;
            radian += Mathf.Atan2(dir.y, dir.x);
            Vector2 newDir = new Vector2(Mathf.Cos(radian), Mathf.Sin(radian)).normalized;
            return newDir;
        }

        public static Vector3 AddDegree(this Vector3 dir, float degree)
        {
            float radian = degree * Mathf.Deg2Rad;
            radian += Mathf.Atan2(dir.y, dir.x);
            Vector3 newDir = new Vector3(Mathf.Cos(radian), Mathf.Sin(radian)).normalized;
            return newDir;
        }

        public static Vector2 ChangeXPos(this Vector2 origin, float xValue) {
            Vector2 newVector = new Vector2(xValue, origin.y);
            return newVector;
        }

        public static Vector2 ChangeYPos(this Vector2 origin, float yValue) {
            Vector2 newVector = new Vector2(origin.x, yValue);
            return newVector;
        }

        public static Vector3 ChangeXPos(this Vector3 origin, float xValue) {
            Vector3 newVector = new Vector3(xValue, origin.y, origin.z);
            return newVector;
        }

        public static Vector3 ChangeYPos(this Vector3 origin, float yValue) {
            Vector3 newVector = new Vector3(origin.x, yValue, origin.z);
            return newVector;
        }

        public static Vector3 ChangeZPos(this Vector3 origin, float zValue) {
            Vector3 newVector = new Vector3(origin.x, origin.y, zValue);
            return newVector;
        }
    }

    public static class Bezier {
        public static Vector3 GetPoint(Vector3 p0, Vector3 p1, Vector3 p2, float t) {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            return
                oneMinusT * oneMinusT * p0 +
                2f * oneMinusT * t * p1 +
                t * t * p2;
        }

        public static Vector3 GetFirstDerivative(Vector3 p0, Vector3 p1, Vector3 p2, float t) {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            return
                2f * oneMinusT * (p1 - p0) +
                2f * t * (p2 - p1);
        }

        public static Vector3 GetPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            return
                oneMinusT * oneMinusT * oneMinusT * p0 +
                3f * oneMinusT * oneMinusT * t * p1 +
                3f * oneMinusT * t * t * p2 +
                t * t * t * p3;
        }

        public static Vector3 GetFirstDerivative(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            return
                3f * oneMinusT * oneMinusT * (p1 - p0) +
                6f * oneMinusT * t * (p2 - p1) +
                3f * t * t * (p3 - p2);
        }
    }

    public static class ExMath 
    {
        public static float GetDegreeBetween(Vector2 from, Vector2 to) 
        {
            Vector2 diff = to - from;
            float radian = Mathf.Atan2(diff.y, diff.x);
            return radian * Mathf.Rad2Deg;
        }

        public static Vector2 GetRotatedPos(Vector2 origin, Vector2 point, float radian) 
        {
            float rotatedX = (Mathf.Cos(radian) * (point.x - origin.x)) + (Mathf.Sin(radian) * (point.y - origin.y)) + origin.x;
            float rotatedY = (Mathf.Cos(radian) * (point.y - origin.y)) - (Mathf.Sin(radian) * (point.x - origin.x)) + origin.y;
            return new Vector2(rotatedX, rotatedY);
        }

        public static int AddBitMask(this int mask, int targetMask) 
        {
            int newmMask = mask | (1 << targetMask);
            return newmMask;
        }

        public static bool BitmaskContains(this int mask, int other) 
        {
            return ((mask & (1 << other)) != 0);
        }

        public static float Cross(Vector2 v1, Vector2 v2)
        {
            float crossProduct = v1.x * v2.y - v1.y * v2.x;
            return crossProduct;
        }
    }

    public static class ExEnum 
    {
        public static T Parse<T>(string str) where T : System.Enum 
        {
            T enumValue = (T)System.Enum.Parse(typeof(T), str);
            return enumValue;
        }
    }

    public static class ExKey 
    {
        public static KeyCode GetPressedKey() 
        {
            KeyCode result = KeyCode.None;
            for (KeyCode key = KeyCode.None; key < KeyCode.Joystick8Button19; key++) 
            {
                if (Input.GetKeyDown(key)) 
                {
                    result = key;
                    break;
                }
            }
            return result;
        }
    }

    public static class ExString 
    {
        private static readonly string RegexContainsPrefix = "^.*(";
        private static readonly string RegexContainsSuffix = ").*";
        public static bool Contains(string str, string pattern) 
        {
            return Regex.Match(str, RegexContainsPrefix + pattern + RegexContainsSuffix).Success;
        }
    }

    public static class ExMouse 
    {
        public static Vector3 GetMouseWorldPosition() 
        {
            Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            worldPosition.z = 0f;
            return worldPosition;
        }

        public static bool IsMouseOverCollider(Collider2D collider) 
        {
            Vector3 mousePosition = GetMouseWorldPosition();
            bool? isOverlaped = Physics2D.OverlapPoint(mousePosition)?.Equals(collider);
            return isOverlaped.HasValue ? isOverlaped.Value : false;
        }

        public static Collider2D GetOverlapedCollider(string layerName) 
        {
            int layerMask = LayerMask.NameToLayer(layerName);
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit2D hit = Physics2D.GetRayIntersection(ray, 100f, (1 << layerMask));
            return hit.collider;
        }
    }

    public static class ExUnity 
    {
        public static void ToggleGameObject(this GameObject obj) 
        {
            obj.SetActive(!obj.activeSelf);
        }

        public static void SetLocalScaleX(this Transform transform, float scaleX)
        {
            transform.localScale = transform.localScale.ChangeXPos(scaleX);
        }
    }

    public static class ExCollection 
    {
        public static T SelectOne<T>(this IList<T> list) 
        {
            T result = default(T);
            int numOfElements = list.Count;
            if (numOfElements > 0) 
            {
                int randomIndex = Random.Range(0, numOfElements);
                result = list[randomIndex];
            }
            return result;
        }

        public static T1 GetRandomKey<T1, T2>(this Dictionary<T1, T2> dict) 
        {
            T1 result = default(T1);
            int numOfElements = dict.Count;
            if (numOfElements > 0) 
            {
                int randomIndex = Random.Range(0, numOfElements);
                result = dict.ElementAt(randomIndex).Key;
            }
            return result;
        }

        public static T2 SelectOne<T1, T2>(this Dictionary<T1, T2> dict)
         {
            T2 result = default(T2);
            int numOfElements = dict.Count;
            if (numOfElements > 0) 
            {
                int randomIndex = Random.Range(0, numOfElements);
                result = dict.ElementAt(randomIndex).Value;
            }
            return result;
        }

        public static IList<T> Shuffle<T>(this IList<T> source)
        {
            List<T> list = new List<T>(source);
            int n = list.Count;
            
            while (n > 1)
            {
                n--;
                int k = Random.Range(0, n + 1);
                T temp = list[k];
                list[k] = list[n];
                list[n] = temp;
            }
            return list;
        }
    }
    public static class ExParser {
        public static bool ParseBoolOrDefault(string value) 
        {
            if (!bool.TryParse(value, out bool result)) {
                result = default(bool);
            }
            return result;
        }

        public static int ParseIntOrDefault(string value) 
        {
            if (!int.TryParse(value, out int result)) {
                result = default(int);
            }
            return result;
        }

        public static float ParseFloatOrDefault(string value) 
        {
            if (!float.TryParse(value, out float result)) {
                result = default(float);
            }
            return result;
        }

        public static T ParseEnumOrDefault<T>(string value) where T : System.Enum 
        {
            if ((value != null) && System.Enum.IsDefined(typeof(T), value))
            {
                return ExEnum.Parse<T>(value);
            }
            return default(T);
        }
    }

    public static class ExColor {
        public static Color ToColor(this string colorText)
        {
            if (!ColorUtility.TryParseHtmlString(colorText, out Color color))
            {
                color = Color.white;
            }
            return color;
        }
    }
}