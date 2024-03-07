using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Framework.ObjectPool;
using UnityEngine;

namespace Framework.Timer
{
    /**
     *
     *
     * HZ 1000 steps
     * Level Offset  Granularity            Range
     *  0      0         1 ms                0 steps -         63 steps
     *  1     64         8 ms               64 steps -        511 steps
     *  2    128        64 ms              512 steps -       4095 steps (512ms - ~4s)
     *  3    192       512 ms             4096 steps -      32767 steps (~4s - ~32s)
     *  4    256      4096 ms (~4s)      32768 steps -     262143 steps (~32s - ~4m)
     *  5    320     32768 ms (~32s)    262144 steps -    2097151 steps (~4m - ~34m)
     *  6    384    262144 ms (~4m)    2097152 steps -   16777215 steps (~34m - ~4h)
     *  7    448   2097152 ms (~34m)  16777216 steps -  134217727 steps (~4h - ~1d)
     *  8    512  16777216 ms (~4h)  134217728 steps - 1073741823 steps (~1d - ~12d)
     *
     * HZ  250
     * Level Offset  Granularity            Range
     *  0	   0         4 ms                0 ms -        255 ms
     *  1	  64        32 ms              256 ms -       2047 ms (256ms - ~2s)
     *  2	 128       256 ms             2048 ms -      16383 ms (~2s - ~16s)
     *  3	 192      2048 ms (~2s)      16384 ms -     131071 ms (~16s - ~2m)
     *  4	 256     16384 ms (~16s)    131072 ms -    1048575 ms (~2m - ~17m)
     *  5	 320    131072 ms (~2m)    1048576 ms -    8388607 ms (~17m - ~2h)
     *  6	 384   1048576 ms (~17m)   8388608 ms -   67108863 ms (~2h - ~18h)
     *  7	 448   8388608 ms (~2h)   67108864 ms -  536870911 ms (~18h - ~6d)
     *  8    512  67108864 ms (~18h) 536870912 ms - 4294967288 ms (~6d - ~49d)
     *
     *
     */
    public class TimerManager
    {
        private static readonly string s_Modular = "Framework.Timer.TimerManager";
        
        /// <summary>
        /// 频率，即每秒钟Tick多少次
        /// </summary>
        private const ulong k_HZ = 60;
        
        /// <summary>
        /// 时间轮的级数
        /// </summary>
        private const int k_Depth = 9;

        /// <summary>
        /// 每一级槽位数用多少位来表示
        /// </summary>
        private const int k_LevelBits = 6;
        
        /// <summary>
        /// 与LevelBits对应，代表每一级的槽位个数
        /// 如：默认LevelBits是6位，则代表的槽位个数就是 64 个。
        /// </summary>
        private const int k_LevelSize = (1 << k_LevelBits);
        
        /// <summary>
        /// 每一级槽位的范围Mask，比如64个槽位，则范围是0~63
        /// </summary>
        private const int k_LevelMask = k_LevelSize - 1;
        
        /// <summary>
        /// 所有级别轮盘的槽位总数
        /// </summary>
        private const int k_WheelSize = k_LevelSize * k_Depth;

        /// <summary>
        /// 每一个级别的进位倍数位
        /// 比如第一级每个Bucket对应一个Jiff，当这个Bits为3时，则第二级为每个Bucket对应8(1<<3)个Jiff
        /// </summary>
        private const int k_LevelClockShiftBits = 3;
        
        /// <summary>
        /// 当前能表示的最大Steps
        /// </summary>
        private const ulong k_WheelTimeoutCutoff = ((k_LevelSize) << ((k_Depth - 1) * k_LevelClockShiftBits)) - 1;

        /// <summary>
        /// 最大轮的粒度
        /// </summary>
        private const ulong k_LastLevelGranularity = (1UL << (k_Depth - 1) * k_LevelClockShiftBits);
        
        /// <summary>
        /// 最大的到期时间, 最后一个bucket
        /// </summary>
        private const ulong k_WheelTimeoutMax = k_WheelTimeoutCutoff - k_LastLevelGranularity;
        
        /// <summary>
        /// 一秒钟多少毫秒
        /// </summary>
        private const float k_Milliseconds = 1000f;
        
        /// <summary>
        /// 每一次Jiff多少毫秒数
        /// </summary>
        private const float k_MillisecondInOneJiff =  k_Milliseconds / k_HZ;
        
        // 从启动到当前的jiff数
        private static ulong s_Jiffies = 0;
        
        // Task对象池
        private static ObjectPool<TimerTask> s_TaskPool = new ();
        
        // 用于保存当前所有in queue的Task
        private static Dictionary<int, TimerTask> s_TaskMap = new ();

        /// <summary>
        /// 时间轮
        /// </summary>
        /// TODO: 用LinkedList频繁增删可能会导致大量GC，因为内部维护LinkedListNode对象？
        private static List<LinkedList<TimerTask>> s_TaskWheelList = new(k_WheelSize);
       
        static TimerManager()
        {
            for (int i = 0; i < k_WheelSize; ++i)
            {
                s_TaskWheelList.Add(new LinkedList<TimerTask>());
            }
        }
        
        private static void AddToScheduler(TimerTask task)
        {
            
            var index = CalculateWheelIndex(task.Expires);
            
            s_TaskWheelList[index].AddLast(task);
            
            task.BucketIndex = index;

        }

        private static void UpdateTaskExpires(TimerTask task, int delay = 0)
        {
            task.Expires = 
                MillisecondsToJiffies(delay) + task.Interval + s_Jiffies;
        }
        
        private static bool AddTask(TimerTask task, int delay = 0)
        {
            if (s_TaskMap.TryAdd(task.ID, task))
            {
                UpdateTaskExpires(task, delay);
                
                AddToScheduler(task);
                
                return true;
            }
            else
            {
                return false;
            }
        }
        
        private static void TaskShift()
        {
            if (s_Jiffies <= 0)
            {
                return;
            }
            
            var levels = k_Depth;
            while (--levels >= 1)
            {
                if ((s_Jiffies & ((1ul << levels * k_LevelClockShiftBits) - 1)) == 0)
                {
                    var index = CalculateIndex(s_Jiffies - 1, levels, 0ul);
                    var taskLinkedList = s_TaskWheelList[(int)index];
                 
                    while (taskLinkedList.Count > 0)
                    {
                        var first = taskLinkedList.First;
                        taskLinkedList.RemoveFirst();
                        AddToScheduler(first.Value);
                    }
                }
            }
        }
        
        private static void PushJiffies(int totalJiffCount)
        {
            while (totalJiffCount-- > 0)
            {
                int currClock = (int)(s_Jiffies & k_LevelMask);
                
                ExecuteTasks(currClock);
                
                TaskShift();
                
                ++s_Jiffies;
            }
        }

        private static void ExecuteTasks(int clock)
        {
            var expiredTaskLinkedList = s_TaskWheelList[clock];
          
            while (expiredTaskLinkedList.Count > 0)
            {
                var first = expiredTaskLinkedList.First;
                expiredTaskLinkedList.RemoveFirst();
                
                var task = first.Value;
                if (task.Valid())
                {
                    task.Execute();

                    if (task.CanLoop())
                    {
                        UpdateTaskExpires(task);
                        AddToScheduler(task);
                    }
                    else
                    {
                        DoRecycle(task.ID);
                    }
                }
                else
                {
                    DoRecycle(task.ID);
                }
                
            }
        }

        private static void DoRecycle(int taskId)
        {
            if (s_TaskMap.Remove(taskId, out var task))
            {
                s_TaskPool.Recycle(task);
            }
        }
        
        #region 工具方法

        /// <summary>
        /// 把毫秒数转换成Jiff数
        /// </summary>
        /// <param name="millisecond"></param>
        /// <returns></returns>
        private static ulong MillisecondsToJiffies(float millisecond)
        {
            return (ulong)Mathf.CeilToInt(millisecond / k_MillisecondInOneJiff);
        }
        
        /// <summary>
        /// 计算每一个级别的Jiffies起点
        /// </summary>
        /// <param name="level">时间轮的级别</param>
        /// <returns>对应级别的Jiffies起点</returns>
        private static ulong LevelStartJiffies(int level)
        {
            return (ulong)k_LevelSize << ((level - 1) * k_LevelClockShiftBits);
        }
     
        /// <summary>
        /// 获取每一级的bucket下标起点
        /// </summary>
        /// <returns></returns>
        private static int GetLevelOff(int level)
        {
            return level * k_LevelSize;
        }
        
        /// <summary>
        /// 计算对应的bucket下标
        /// </summary>
        /// <param name="expires"></param>
        /// <returns></returns>
        private static int CalculateWheelIndex(ulong expires)
        {
            if (expires <= s_Jiffies)
            {
                // 到期任务, 直接执行
                return (int)(s_Jiffies & k_LevelMask);
            }
            
            var delta = expires - s_Jiffies;
            
            // 从 1 到 8 个level
            for (int level = 1; level < k_Depth; ++level)
            {
                if (delta < LevelStartJiffies(level))
                {
                    return CalculateIndex(expires, level - 1, LevelStartJiffies(level - 1));
                }
            }

            // 第 9 个Level
            if (delta >= k_WheelTimeoutCutoff)
            {
                expires = s_Jiffies + k_WheelTimeoutMax;
                
            }

            return CalculateIndex(expires, k_Depth - 1, LevelStartJiffies(k_Depth - 1));
        }

        /// <summary>
        /// 获取bucketList的下标
        /// </summary>
        /// <param name="expires">到期时间</param>
        /// <param name="level">时间轮等级</param>
        /// <param name="levelStartJiffies">当前level下的jiffies起点</param>
        /// <returns></returns>
        private static int CalculateIndex(ulong expires, int level, ulong levelStartJiffies)
        {
            // ReSharper disable once InvalidXmlDocComment
            /**
             *  这里右移当前级对应的精度位，代表对expires降精度。
             *
             *     [0-63][64-127]
             */
            int levelShiftBits = k_LevelClockShiftBits * level;
            expires -= levelStartJiffies;
            expires = (expires >> levelShiftBits);
            return GetLevelOff(level) + (int)(expires & k_LevelMask);
        }
        
        #endregion
        
        private static int AddIntervalTask<T1, T2>(float interval, Action<object, object> callback,
            T1 param1, T2 param2, int loopTimes, int delay)
        {
            if (interval < 0)
            {
                Debug.LogWarning($"[{s_Modular}] AddIntervalTask : Interval 应该是一个非负的值");
            }
            
            if (delay < 0)
            {
                Debug.LogWarning($"[{s_Modular}] AddIntervalTask : Delay 应该是一个非负的值");
            }

            if (callback == null)
            {
                
                Debug.LogError($"[{s_Modular}] AddIntervalTask : Callback 为空");
                
                return -1;
            }

            if (loopTimes < -1)
            {
                Debug.LogWarning($"[{s_Modular}] AddIntervalTask : loopTimes 应该是一个大于-1的值， -1代表无限循环");
            }
            
            interval = Math.Max(0, interval);
            delay = Math.Max(0, delay);
            loopTimes = Math.Max(-1, loopTimes);
            
            var task = s_TaskPool.Get();
            task.LoopTimes = loopTimes;
            task.Interval = MillisecondsToJiffies(interval);
            task.SetCallback(callback, param1, param2);
       
            if (AddTask(task, delay))
            {
                return task.ID;
            }

            Debug.LogError($"[{s_Modular}] AddIntervalTask : Task 添加失败: {task.ID}");
            s_TaskPool.Recycle(task);
            
            return -1;
        }
        
        private static int AddTimeoutTask<T1, T2>(int delay, Action<object, object> callback, T1 param1, T2 param2)
        {
            if (delay < 0)
            {
                Debug.LogWarning($"[{s_Modular}] AddIntervalTask : Delay 应该是一个非负的值");
            }
            
            
            if (callback == null)
            {
                Debug.LogError($"[{s_Modular}] AddTimeoutTask : Callback 为空");

                return -1;
            }
            
            var task = s_TaskPool.Get();
            task.SetCallback(callback, param1, param2);
            delay = Math.Max(0, delay);
            task.SetOnce();
            
            if (AddTask(task, delay))
            {
                return task.ID;
            }
            
            Debug.LogError($"[{s_Modular}] AddIntervalTask : Task 添加失败: {task.ID}");
            s_TaskPool.Recycle(task);
            
            return -1;
        }
        
        #region 开放接口

        /// <summary>
        /// TimerManager的步进，需要外部驱动，比如在Mono中的Update驱动
        /// </summary>
        /// <param name="deltaInMillisecond">上一次Tick跟这一次的时间间隔，注意这里应该传毫秒数</param>
        public static void Tick(float deltaInMillisecond)
        {
            if (s_TaskMap.Count <= 0)
            {
                s_Jiffies = 0;
                return;
            }
            
            // 计算当前delta time内，可以跑几个jiff
            int totalJiffCount = Mathf.FloorToInt(deltaInMillisecond / k_MillisecondInOneJiff);
            // Debug.Log("totalJiffCount:" + totalJiffCount+ ",  totalJiffCount:" + deltaInMillisecond);
            
            /**
             * 
             *  Note:  把计算出来的次数除以2，因为运行callback也需要时间。 
             *         在Review会上，反馈说需要去掉，不需要留时间片给callback
             *         但是实测下来，留时间片的精准度更好一些
             * 
             */
                      
            totalJiffCount = (totalJiffCount >> 1) + 1;
            
            // 执行
            PushJiffies(totalJiffCount);
            
        }
        
        /// <summary>
        /// 设置一个同步延迟回调
        /// </summary>
        /// <param name="delay">延迟的毫秒数</param> 
        /// <param name="callback">回调方法</param> 
        /// <param name="param1">回调参数1</param> 
        /// <param name="param2">回调参数2</param> 
        /// <typeparam name="T1">参数1类型</typeparam>
        /// <typeparam name="T2">参数2类型</typeparam>
        /// <returns>返回 Task ID</returns>
        public static int AddDelayTask<T1, T2>(int delay, Action<object, object> callback, T1 param1, T2 param2)
        {
            return AddTimeoutTask(delay, callback, param1, param2);
        }
        
        /// <summary>
        /// 设置一个重复执行的同步任务
        /// </summary>
        /// <param name="interval">每次间隔毫秒数</param>
        /// <param name="callback">回调函数</param>
        /// <param name="param1">回调参数1</param>
        /// <param name="param2">回调参数2</param>
        /// <param name="loopTimes">重复次数，默认-1代表无限重复</param>
        /// <param name="delay">首次执行的延迟毫秒数</param>
        /// <typeparam name="T1">参数类型1</typeparam>
        /// <typeparam name="T2">参数类型2</typeparam>
        /// <returns>返回 Task ID</returns>
        public static int AddLoopTask<T1, T2>(float interval, Action<object, object> callback,
            T1 param1, T2 param2, int loopTimes = -1, int delay = 0)
        {
            return AddIntervalTask(interval, callback, param1, param2, loopTimes, delay);
        }
        
        /// <summary>
        /// 修改任务的Interval属性
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="interval">间隔毫秒数</param>
        public static bool ModifyInterval(int taskId, float interval)
        {
            if (interval <= 0)
            {
                Debug.LogError($"[{s_Modular}] ModifyInterval : Interval 应该是一个非负的值");
                return false;
            }
            
            if (s_TaskMap.TryGetValue(taskId, out var task))
            {
                var oldBucket = task.BucketIndex;

                if (oldBucket >= 0 && oldBucket < k_WheelSize)
                {
                    task.Interval = MillisecondsToJiffies(interval);
                    
                    UpdateTaskExpires(task);

                    s_TaskWheelList[oldBucket].Remove(task);
                    
                    AddToScheduler(task);

                    return true;
                }
                
                Debug.LogError($"[{s_Modular}] ModifyInterval : task：{taskId} 对应的bucket 不存在：{oldBucket}");
                

                return false;
            }
            
            Debug.LogError($"[{s_Modular}] ModifyInterval : task 不存在：{taskId}");
            return false;
        }

        /// <summary>
        /// 修改任务的Loop次数，-1代表无限循环
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="loopTimes"></param>
        /// <returns></returns>
        public static bool ModifyLoopTimes(int taskId, int loopTimes)
        {
            if (loopTimes < -1)
            {
                Debug.LogError($"[{s_Modular}] ModifyLoopTimes : loopTimes 应该是一个大于-1的值， -1代表无限循环");
                return false;
            }

            if (s_TaskMap.TryGetValue(taskId, out var task))
            {
                task.LoopTimes = loopTimes;
                return true;
            }
            
            Debug.LogError($"[{s_Modular}] ModifyLoopTimes : task 不存在：{taskId}");
            return false;
        }

        /// <summary>
        /// 修改任务的delay值
        /// </summary>
        /// <param name="taskId">非负的id</param>
        /// <param name="delay">延迟毫秒数</param>
        /// <returns></returns>
        public static bool ModifyDelay(int taskId, int delay)
        {
            if (delay <= 0)
            {
                Debug.LogError($"[{s_Modular}] ModifyDelay : Delay 应该是一个非负的值");

                return false;
            }
            
            
            if (s_TaskMap.TryGetValue(taskId, out var task))
            {
                var oldBucket = task.BucketIndex;

                if (oldBucket >= 0 && oldBucket < k_WheelSize)
                {
                    UpdateTaskExpires(task, delay);

                    s_TaskWheelList[oldBucket].Remove(task);
                    
                    AddToScheduler(task);

                    return true;
                }
                
                Debug.LogError($"[{s_Modular}] ModifyDelay : task：{taskId} 对应的bucket 不存在：{oldBucket}");
                

                return false;
            }
            
            Debug.LogError($"[{s_Modular}] ModifyDelay : task 不存在：{taskId}");
            return false;
        }
        
        /// <summary>
        /// 修改任务的callback跟对应的参数
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="callback"></param>
        /// <param name="param1"></param>
        /// <param name="param2"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <returns></returns>
        public static bool ModifyCallback<T1, T2>(int taskId, Action<object, object> callback, T1 param1, T2 param2)
        {
            if (s_TaskMap.TryGetValue(taskId, out var task))
            {
                task.SetCallback(callback, param1, param2);
                return true;
            }
            
            Debug.LogError($"[{s_Modular}] ModifyCallback : task 不存在：{taskId}");
            return false;
        }
       
        /// <summary>
        /// 修改任务的callback
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        public static bool ModifyCallback(int taskId, Action<object, object> callback)
        {
            if (s_TaskMap.TryGetValue(taskId, out var task))
            {
                task.SetCallback(callback);
                return true;
            }
            
            Debug.LogError($"[{s_Modular}] ModifyCallback : task 不存在：{taskId}");
            return false;
            
        }
        
        /// <summary>
        /// 修改任务回调的参数
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="param1"></param>
        /// <param name="param2"></param>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <returns></returns>
        public static bool ModifyParameters<T1, T2>(uint taskId, T1 param1, T2 param2)
        {
            if (s_TaskMap.TryGetValue((int)taskId, out var task))
            {
                task.SetParameters(param1, param2);
                return true;
            }
            
            Debug.LogError($"[{s_Modular}] ModifyParameters : task 不存在：{taskId}");
            return false;
        }
       
        public static bool RemoveTask(int taskId)
        {
            if (s_TaskMap.TryGetValue(taskId, out var task))
            {
                task.SetInvalid();
                return true;
            }

            Debug.LogError($"[{s_Modular}] RemoveTask : task 不存在：{taskId}");
            
            return false;

        }
        
        #endregion
    }
}