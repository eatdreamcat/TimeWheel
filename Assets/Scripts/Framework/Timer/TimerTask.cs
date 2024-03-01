using System.Collections;
using System.Collections.Generic;
using Framework.ObjectPool;
using UnityEngine;

namespace Framework.Timer
{
    public class TimerTask : IPoolable
    {


        #region 对象池接口

        public void Recycle()
        {
            throw new System.NotImplementedException();
        }

        public void Fetch()
        {
            throw new System.NotImplementedException();
        }

        #endregion
        
    }
}
