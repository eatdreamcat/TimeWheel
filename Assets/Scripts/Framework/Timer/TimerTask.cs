using System;
using System.Threading.Tasks;
using Framework.ObjectPool;

namespace Framework.Timer
{
    internal class TimerTask : IPoolable
    {
        private static int s_ID = 0;

        private int m_ID;
        internal int ID => m_ID;

        /// <summary>
        /// 用于记录间隔，用于每次更新Expire的值, 单位也是jiff
        /// </summary>
        private ulong m_Interval;

        internal ulong Interval
        {
            get => m_Interval;
            set
            {
                m_Interval = value;
            }
        }

        /// <summary>
        /// 用于暂存对应的bucket下标，方便移动和删除
        /// </summary>
        private int m_BucketIndex;

        internal int BucketIndex
        {
            get => m_BucketIndex;
            set
            {
                m_BucketIndex = value;
            }
        }
        
        /// <summary>
        /// 任务超时时间，单位是Jiff
        /// </summary>
        private ulong m_Expires;

        internal ulong Expires
        {
            get => m_Expires;
            set
            {
                m_Expires = value;
            }
        }
        
        /// <summary>
        /// 重复次数，-1代表无限重复
        /// </summary>
        private int m_LoopTimes;

        internal int LoopTimes
        {
            get => m_LoopTimes;
            set
            {
                m_LoopTimes = value;
            }
        }

        private Action<object, object> m_Action;
        private object m_Param1;
        private object m_Param2;
        
        public void SetParameters<T1, T2>(T1 param1, T2 param2)
        {
            m_Param1 = param1;
            m_Param2 = param2;
        }
        
        public void SetCallback<T1, T2>(Action<object, object> callback, T1 param1, T2 param2)
        {
            m_Param1 = param1;
            m_Param2 = param2;
            m_Action = callback;
        }
        
        public void SetCallback(Action<object, object> callback)
        {
            m_Action = callback;
        }
        
        public TimerTask()
        {
            m_ID = s_ID++;
        }
        
        private void Reset()
        {
            m_Action = null;
            m_Param1 = null;
            m_Param2 = null;

            m_LoopTimes = -1;
            m_Expires = 0;
            m_Interval = 0;
            
        }
        
        internal void SetInvalid()
        {
            this.Reset();
        }
        
        internal bool Valid()
        {
            if (m_Action == null)
            {
                return false;
            }

            if (m_LoopTimes == 0)
            {
                return false;
            }
            
            return true;
        }

        internal bool CanLoop()
        {
            return m_LoopTimes == -1 || m_LoopTimes > 0;
        }

        internal void SetOnce()
        {
            this.m_LoopTimes = 1;
        }
        
        internal void Execute()
        {
            if (m_LoopTimes > 0)
            {
                --m_LoopTimes;
            }

            m_Action.Invoke(m_Param1, m_Param2);
            
        }
        
        #region 对象池接口

        public void OnRecycle()
        {
            Reset();
        }

        public void OnReuse()
        {
            Reset();
        }

        public void OnCreate()
        {
            Reset();
        }

        #endregion
        
    }
}
