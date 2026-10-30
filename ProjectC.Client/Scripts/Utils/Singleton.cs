using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Utils
{
    public class Singleton<T> where T : new()
    {
        private static T instance;

        public static T Instance => instance ??= new T();

        public static void DeleteSingleton()
        {
            instance = default(T);
        }
    }
}
