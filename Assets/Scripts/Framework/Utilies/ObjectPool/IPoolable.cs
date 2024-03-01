using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.ObjectPool
{
    interface IPoolable
    {
        public void Recycle();
        public void Fetch();
        
    }
}
