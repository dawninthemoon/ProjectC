using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Utils
{
    public class SimplePool<T> where T : new()
    {
        Queue<T> _queue = new Queue<T>();

        public SimplePool()
        {
            enqueue();
        }

        public SimplePool(int count)
        {
            enqueueCount(count);
        }

        public void enqueue(T t)
        {
            _queue.Enqueue(t);
        }

        private void enqueue()
        {
            _queue.Enqueue(new T());
        }

        private void enqueueCount(int count)
        {
            for(int i = 0; i < count; ++i)
            {
                _queue.Enqueue(new T());
            }
        }

        public T dequeue()
        {
            if(_queue.Count == 0)
                enqueue();

            return _queue.Dequeue();
        }
    }
}
