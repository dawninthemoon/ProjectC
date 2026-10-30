// Common Bind API from Scripts/Utils. Localization-dependent helpers intentionally remain in the original source.
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Utils
{
    public static class UnityBindingExtensions
    {
        public static bool Bind(this GameObject gameObject, string objectName, out GameObject target)
        {
            return Bind(gameObject.transform, objectName, out target);
        }

        public static bool Bind(this Transform transform, string objectName, out GameObject target)
        {
            Transform targetTransform = FindByName(transform, objectName);
            target = targetTransform != null ? targetTransform.gameObject : null;
            return target != null;
        }

        public static bool Bind(this MonoBehaviour behaviour, string objectName, out GameObject target)
        {
            return Bind(behaviour.transform, objectName, out target);
        }

        public static bool Bind<T>(this GameObject gameObject, string objectName, out T component)
            where T : Component
        {
            return Bind(gameObject.transform, objectName, out component);
        }

        public static bool Bind<T>(this Transform transform, string objectName, out T component)
            where T : Component
        {
            Transform target = FindByName(transform, objectName);
            component = target != null ? target.GetComponent<T>() : null;
            return component != null;
        }

        public static bool Bind<T>(this MonoBehaviour behaviour, string objectName, out T component)
            where T : Component
        {
            return Bind(behaviour.transform, objectName, out component);
        }

        public static bool BindButton(
            this MonoBehaviour behaviour,
            string objectName,
            out Button button,
            UnityAction callback)
        {
            if (!behaviour.Bind(objectName, out button))
            {
                return false;
            }

            button.onClick.RemoveAllListeners();
            if (callback != null)
            {
                button.onClick.AddListener(callback);
            }

            return true;
        }

        public static bool BindNoRecursive<T>(this GameObject gameObject, out T component)
            where T : Component
        {
            return BindNoRecursive(gameObject.transform, out component);
        }

        public static bool BindNoRecursive<T>(this Transform transform, out T component)
            where T : Component
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                component = transform.GetChild(i).GetComponent<T>();
                if (component != null)
                {
                    return true;
                }
            }

            component = null;
            return false;
        }

        public static bool BindNoRecursive<T>(this MonoBehaviour behaviour, out T component)
            where T : Component
        {
            return BindNoRecursive(behaviour.transform, out component);
        }

        private static Transform FindByName(Transform root, string objectName)
        {
            if (root.name == objectName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindByName(root.GetChild(i), objectName);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
