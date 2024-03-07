using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Framework.Timer;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Test.Timer
{
    public class TimerTest
    {
        public static void TestWheelIndexCalculate(int count, int step, int offset = 0)
        {
           Debug.Log("##############测试下标计算#################");
           Debug.Log($"测试延迟任务，任务总个数 {count} 个, 每个任务延迟间隔 {step} ms");
           for (int i = 1; i <= count; ++i)
           {
               TimerManager.AddDelayTask(i * step + offset - 1 , (index, step) =>
               {
                   // Debug.Assert(TimerManager.Jiffies == (ulong)((int)index * (int)step + offset - 1), 
                   //     " 时刻不准确 ");
                  
                   Debug.Log($"Delay task {index} executed:{DateTime.Now}:{DateTime.Now.Millisecond}");
                
                   
               }, i, step);
           }
           
        }

        public static void TestIndexInLevelsWithJiffies()
        {
            Debug.Log("测试 进位");
            Task.Run((() =>
            {
                File.WriteAllTextAsync("./JiffTest.log","");
            
                var jiffies = 0;
            
                while (jiffies++ <= 262143)
                {
                    var levels = 9;
                
                    while (--levels >= 0)
                    { 
                        var currLevelIndex = (jiffies >> (levels * 3)) & 63;
                        using (var stream = File.AppendText("./JiffTest.log"))
                        {
                            stream.WriteAsync($"Level: {levels}, Jiffies:{jiffies}, Index:{currLevelIndex} \n");
                        }
                    }

                    using (var stream = File.AppendText("./JiffTest.log"))
                    {
                        stream.WriteAsync("\n \n \n");
                    }
                }
            }));
        }


        /// <summary>
        /// 测试delay任务
        /// </summary>
        /// <param name="count">任务个数</param>
        /// <param name="step">每个任务间的延迟间隔 ms</param>
        public static void TestDelayTask(int count, int step)
        {
            Debug.Log($"测试延迟任务，任务总个数 {count} 个, 每个任务延迟间隔 {step} ms");
            for (int i = 1; i <= count; ++i)
            {
                TimerManager.AddDelayTask(i * step, (index, o2) =>
                {
                    Debug.Log($"Delay task {index} executed:{DateTime.Now}:{DateTime.Now.Millisecond}");
                
                }, i, 1);
            }
            
        }

        public static void TestIntervalTask(int count, int step)
        {
            Debug.Log($"测试循环任务， 任务个数 {count}, 间隔递增： {step} ms");
            for (int i = 1; i <= count; ++i)
            {
                TimerManager.AddLoopTask((i * step), (index, o2) =>
                {
                    Debug.Log($"Interval task {index} executed:{DateTime.Now}:{DateTime.Now.Millisecond}");
                
                }, i, 1);
            }
        }

        public static IEnumerator PressureTest(int count, int minInterval, int maxInterval)
        {
            Debug.Log($"压力测试， 任务个数 {count}, interval范围: [{minInterval}, {maxInterval}]");
            while (count > 0)
            {
                var insideCount = 1000;
                while (insideCount-- > 0)
                {
                    count--;
                    
                    TimerManager.AddLoopTask((Random.Range(minInterval, maxInterval)), 
                        (index, o2) =>
                    {
                        
                    }, count, 1);
                }

                yield return new WaitForEndOfFrame();
            }
            
            Debug.Log($"### 压测任务添加完成 ###");
        }

        public static void TestModifyInterval()
        {
            var lastTime = DateTime.Now;
            var taskId = TimerManager.AddLoopTask(16.6f, (o1, o2) =>
            {
                Debug.Log($"测试修改任务间隔时间：{(DateTime.Now - lastTime).Milliseconds} ms");
                lastTime = DateTime.Now;

            }, 0, 1);

            TimerManager.AddDelayTask(1000, (o, o1) =>
            {
                Debug.Log("修改时间间隔");
                TimerManager.ModifyInterval(taskId, 33.333f);
                
            }, 0, 1);
        }

        public static void TestRemoveTask()
        {
            var taskId = TimerManager.AddLoopTask(1000f, (o, o1) =>
            {
                Debug.Log("Task 即将被删除。。。: " + DateTime.Now + " : " + DateTime.Now.Millisecond);
                
            }, 0, 0);

            TimerManager.AddDelayTask(5000f, (o1, o2) =>
            {
                Debug.Log($"删除Task:{TimerManager.RemoveTask(taskId)} " + DateTime.Now + " : " + DateTime.Now.Millisecond);
                
            }, 1, 1);
        }
    }
}