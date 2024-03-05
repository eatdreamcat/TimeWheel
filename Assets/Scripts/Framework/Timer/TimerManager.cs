using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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
     */
    public class TimerManager
    {
        /// <summary>
        /// 频率，即每秒钟Tick多少次
        /// </summary>
        private const ulong k_HZ = 1000;
        
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
        private const ulong k_LevelSize = (1 << k_LevelBits);
        
        /// <summary>
        /// 每一级槽位的范围Mask，比如64个槽位，则范围是0~63
        /// </summary>
        private const ulong k_LevelMask = k_LevelSize - 1;
        
        /// <summary>
        /// 所有级别轮盘的槽位总数
        /// </summary>
        private const ulong k_WheelSize = k_LevelSize * k_Depth;

        /// <summary>
        /// 每一个级别的进位倍数位
        /// 比如第一级每个Bucket对应一个Jiff，当这个Bits为3时，则第二级为每个Bucket对应8(1<<3)个Jiff
        /// </summary>
        private const int k_LevelClockShiftBits = 3;
        
        /// <summary>
        /// 当前能表示的最大Steps
        /// </summary>
        private const ulong k_WheelTimeoutCutoff = ((k_LevelSize) << ((k_Depth - 1) * k_LevelClockShiftBits)) - 1;

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

    // TODO：移除测试代码
        public static ulong Jiffies => s_Jiffies;

        

        // Task对象池
        private static ObjectPool<TimerTask> s_TaskPool = new ();
        
        // 用于保存当前所有in queue的Task
        private static ConcurrentDictionary<int, TimerTask> s_TaskMap = new ();

        /// <summary>
        /// 时间轮
        /// </summary>
        private static List<ConcurrentQueue<TimerTask>> s_TaskWheelList = new((int)k_WheelSize);
        
        // private static ReaderWriterLockSlim s_LockSlim = new ();

        static TimerManager()
        {
            for (int i = 0; i < (int)k_WheelSize; ++i)
            {
                s_TaskWheelList.Add(new ConcurrentQueue<TimerTask>());
            }
        }
        
        private static void AddToScheduler(TimerTask task, uint delay = 0)
        {
            
            var index = (int)CalculateWheelIndex(task.Expires);
            
            // TODO: 考虑到用户是否可能在子线程调用 SetInterval 来添加Task
            // TODO:  是否需要加锁？
            s_TaskWheelList[index].Enqueue(task);
            
        }

        private static void UpdateTaskExpires(TimerTask task, uint delay = 0)
        {
            task.Expires = 
                MillisecondsToJiffies(delay) + task.Interval + s_Jiffies;
        }
        
        private static bool AddTask(TimerTask task, uint delay = 0)
        {
            if (s_TaskMap.TryAdd(task.ID, task))
            {
                UpdateTaskExpires(task, delay);
                
                AddToScheduler(task, delay);
                
                return true;
            }
            else
            {
                return false;
            }
        }
        
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
            // 把计算出来的次数除以2，因为运行callback也需要时间
            totalJiffCount = (totalJiffCount >> 1) + 1;
            
            // 执行
            PushJiffies(totalJiffCount);
               
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
                if ((s_Jiffies & (ulong)((1 << levels * k_LevelClockShiftBits) - 1)) == 0)
                {
                    var index = CalculateIndex(s_Jiffies - 1, (uint)levels, 0ul);
                    var taskBucket = s_TaskWheelList[(int)index];
                 
                    while (taskBucket.Count > 0)
                    {
                        var count = taskBucket.Count;
                        if (taskBucket.TryDequeue(out var task))
                        {
                            AddToScheduler(task);

                            if (count == taskBucket.Count)
                            {
                                throw new Exception(" error !!! ");
                            }
                        }
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
                
                ExecuteTasks(currClock);
                
                ++s_Jiffies;
            }
        }

        private static void ExecuteTasks(int clock)
        {
            var expiredTaskQueue = s_TaskWheelList[clock];
            // TODO： 是否有必要加锁， 本身ConcurrentQueue就是线程安全，但是外部的时间轮List不是线程安全
            // TODO:  如果加锁，那其实应该用Try包裹起来，因为无法确定用户的Callback是否会抛异常
            while (expiredTaskQueue.Count > 0)
            {
                if (expiredTaskQueue.TryDequeue(out var task))
                {
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
                            RemoveTask(task.ID);
                        }
                    }
                    else
                    {
                        RemoveTask(task.ID);
                    }
                }
            }
        }

        #region 工具方法

        /// <summary>
        /// 把毫秒数转换成Jiff数
        /// </summary>
        /// <param name="millisecond"></param>
        /// <returns></returns>
        private static ulong MillisecondsToJiffies(uint millisecond)
        {
            return (ulong)Mathf.CeilToInt(millisecond / k_MillisecondInOneJiff);
        }
        
        /// <summary>
        /// 计算每一个级别的Jiffies起点
        /// </summary>
        /// <param name="level">时间轮的级别</param>
        /// <returns>对应级别的Jiffies起点</returns>
        private static ulong LevelStartJiffies(uint level)
        {
            return k_LevelSize << (int)((level - 1u) * k_LevelClockShiftBits);
        }
        //
        // private static ulong LevelGranularity(int level)
        // {
        //     int shift = level * k_LevelClockShiftBits;
        //     return 1UL << shift;
        // }
        
        /// <summary>
        /// 获取每一级的bucket下标起点
        /// </summary>
        /// <returns></returns>
        private static uint GetLevelOff(uint level)
        {
            return level * (int)k_LevelSize;
        }
        
        /// <summary>
        /// 计算对应的bucket下标
        /// </summary>
        /// <param name="expires"></param>
        /// <returns></returns>
        private static uint CalculateWheelIndex(ulong expires)
        {
            if (expires <= s_Jiffies)
            {
                // 到期任务
                return (uint)(s_Jiffies + 1 & k_LevelMask);
            }
            
            var delta = expires - s_Jiffies;
            
            // 从 1 到 8 个level
            for (uint level = 1u; level < k_Depth; ++level)
            {
                if (delta < LevelStartJiffies(level))
                {
                    return CalculateIndex(expires, level - 1u, LevelStartJiffies(level - 1));
                }
            }

            // 第 9 个Level
            if (delta >= k_WheelTimeoutCutoff)
            {
                expires = s_Jiffies + k_WheelTimeoutMax;
                return CalculateIndex(expires, k_Depth - 1, LevelStartJiffies(k_Depth - 1));
            }

            throw new Exception($"WheelIndex计算出错, 非法的delta:{delta}");
        }

        /// <summary>
        /// 获取bucketList的下标
        /// </summary>
        /// <param name="expires">到期时间</param>
        /// <param name="level">时间轮等级</param>
        /// <param name="levelStartJiffies">当前level下的jiffies起点</param>
        /// <returns></returns>
        private static uint CalculateIndex(ulong expires, uint level, ulong levelStartJiffies)
        {
            // ReSharper disable once InvalidXmlDocComment
            /**
             *  这里右移当前级对应的精度位，代表对expires降精度。
             * 例如：
             *  ① 假设 expires = 123， clk是60
             *     level = 0时，精度是1，则expires无需移位
             *     LevelOff是0，LevelMask是63，用 124 & 63 可以求出index = 60
             *     当前clk是60，要跑到此任务，需要再运行63个clk
             *
             *  ② 假设 expires = 124，clk是60
             *     level = 1时，精度是8，则expires右移3位，得到15，
             *     LevelOff是64，LevelMask是63，用 16 & 63 可以得出index = 16
             *     最终index = 64 + 16 = 80
             *     当前clk是60，要跑到此任务，需要在运行64个clk
             *      NOTE:
             *     但是需要注意，此时clk已经跑了60，
             *     代表第level 2 跑了8格，所以index放在第二级的16格，代表需要再跑8格，正好是64clk
             *
             *     [0-63][64-127]
             */
            int levelShiftBits = (int)(k_LevelClockShiftBits * level);
            expires -= levelStartJiffies;
            expires = (expires >> levelShiftBits);
            return GetLevelOff(level) + (uint)(expires & k_LevelMask);
        }
        
        #endregion

        private static int SetInterval<T1, T2>(uint interval, Action<object, object> callback,
            T1 param1, T2 param2, int loopTimes, uint delay, TaskType type)
        {
            if (interval <= 0)
            {
                Debug.LogWarning("Interval 应该是一个大于0的值");
            }

            if (callback == null)
            {
                Debug.LogError("Callback 为空");

                return -1;
            }

            loopTimes = Math.Max(-1, loopTimes);
            
            var task = s_TaskPool.Get();
            task.LoopTimes = loopTimes;
            task.Interval = MillisecondsToJiffies(interval);
            task.SetCallback(callback, param1, param2);
            task.SetType(type);
            
            if (AddTask(task))
            {
                return task.ID;
            }

            Debug.LogError($"Task 添加失败: {task.ID}");
            s_TaskPool.Recycle(task);
            
            return -1;
        }
        
        private static int SetTimeout<T1, T2>(uint delay, Action<object, object> callback, T1 param1, T2 param2,
            TaskType type)
        {
            if (callback == null)
            {
                Debug.LogError("Callback 为空");

                return -1;
            }
            
            var task = s_TaskPool.Get();
            task.SetCallback(callback, param1, param2);
            task.SetType(type);
            task.SetOnce();
            
            if (AddTask(task, delay))
            {
                return task.ID;
            }
            
            Debug.LogError($"Task 添加失败:{task.ID}");
            s_TaskPool.Recycle(task);
            
            return -1;
        }
        
        #region 开放接口

        /// <summary>
        /// 设置一个异步延迟回调
        /// </summary>
        /// <param name="delay">延迟的毫秒数</param> 
        /// <param name="callback">回调方法</param> 
        /// <param name="param1">回调参数1</param> 
        /// <param name="param2">回调参数2</param> 
        /// <typeparam name="T1">参数1类型</typeparam>
        /// <typeparam name="T2">参数2类型</typeparam>
        /// <returns>返回 Task ID</returns>
        public static int SetTimeoutAsync<T1, T2>(uint delay, Action<object, object> callback, T1 param1, T2 param2)
        {
            return SetTimeout(delay, callback, param1, param2, TaskType.Async);
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
        public static int SetTimeoutSync<T1, T2>(uint delay, Action<object, object> callback, T1 param1, T2 param2)
        {
            return SetTimeout(delay, callback, param1, param2, TaskType.Sync);
        }

        /// <summary>
        /// 设置一个重复执行的异步任务
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
        public static int SetIntervalAsync<T1, T2>(uint interval, Action<object, object> callback,
            T1 param1, T2 param2, int loopTimes = -1, uint delay = 0)
        {
            return SetInterval(interval, callback, param1, param2, loopTimes, delay, TaskType.Async);
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
        public static int SetIntervalSync<T1, T2>(uint interval, Action<object, object> callback,
            T1 param1, T2 param2, int loopTimes = -1, uint delay = 0)
        {
            return SetInterval(interval, callback, param1, param2, loopTimes, delay, TaskType.Sync);
        }
        
        /// <summary>
        /// 修改任务的Interval属性
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="interval">间隔毫秒数</param>
        public static void ModifyInterval(uint taskId, uint interval)
        {
            if (interval <= 0)
            {
                Debug.LogWarning("Interval 应该是一个大于0的值");
            }
            
            // TODO
        }

        /// <summary>
        /// 修改任务的延迟
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="delay">延迟毫秒数</param>
        public static void ModifyDelay(uint taskId, uint delay)
        {
            
        }

        public static void ModifyCallback<T1, T2>(uint taskId, Action<object, object> callback, 
            T1 param1, T2 param2, TaskType type)
        {
            
        }
        
        public static void ModifyCallback<T1, T2>(uint taskId, Action<object, object> callback, T1 param1, T2 param2)
        {
            
        }
        
        public static void ModifyCallback(uint taskId, Action<object, object> callback, TaskType type)
        {
            
        }

        public static void ModifyCallback(uint taskId, Action<object, object> callback)
        {
            
        }
        
        public static void ModifyParameters<T1, T2>(uint taskId, T1 param1, T2 param2)
        {
            
        }

        public static void ModifyToSyncTask(uint taskId)
        {
            
        }

        public static void ModifyToAsyncTask(uint taskId)
        {
            
        }
        
        public static void RemoveTask(int taskId)
        {
            if (s_TaskMap.TryRemove(taskId, out var task))
            {
                // Debug.Log($"Task Remove:{task.ID}, task expires:{task.Expires}");
                s_TaskPool.Recycle(task);
            }
            
        }
        
        #endregion
    }
}
