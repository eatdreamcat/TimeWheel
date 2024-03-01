using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.ObjectPool
{
    public interface IPoolable
    {
        public void OnRecycle();
        public void OnReuse();
        
        public void OnCreate();

    }
}
