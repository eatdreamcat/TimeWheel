using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Framework.ObjectPool
{
    internal class ObjectPool<T> where T : IPoolable, new()
    {
        private List<T> m_Pool = new();
        public T Get()
        {
            if (m_Pool.Count > 0)
            {
                var obj = m_Pool[0];
                m_Pool.RemoveAt(0);
                obj.OnReuse();
                return obj;
            }
            else
            {
                var obj = new T();
                obj.OnCreate();
                return obj;
            }
        }

        public void Recycle(T obj)
        {
            obj.OnRecycle();
            this.m_Pool.Add(obj);
        }
    }
}
