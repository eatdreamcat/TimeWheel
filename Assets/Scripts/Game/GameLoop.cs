using System.Collections;
using Framework.Timer;
using UnityEngine;
using UnityEngine.Profiling;

public class GameLoop : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Application.targetFrameRate = 60;
        StartCoroutine(AddTask());
    }

    IEnumerator AddTask()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        // TimerManager.SetIntervalSync(973630528, (o, o1) => { }, 1, 2);

        // 时间轮进位准确度测试
        // Test.Timer.TimerTest.TestWheelIndexCalculate(1, 56, 512);
        // Test.Timer.TimerTest.TestWheelIndexCalculate(1, 57, 512);
        // Test.Timer.TimerTest.TestWheelIndexCalculate(262143, 33, 0);
        // Test.Timer.TimerTest.TestWheelIndexCalculate(262143, 1, 0);

        // 测试时间轮的分级
        // Test.Timer.TimerTest.TestIndexInLevelsWithJiffies();

        // 测试延迟任务
        // Test.Timer.TimerTest.TestDelayTask(1000, 100);

        // 测试循环任务
        // Test.Timer.TimerTest.TestIntervalTask(1000, 100);
        
        // 测试修改时间间隔
        // Test.Timer.TimerTest.TestModifyInterval();
        
        // 测试删除定时器
        Test.Timer.TimerTest.TestRemoveTask();
        
        // 压力测试
        // yield return Test.Timer.TimerTest.PressureTest(1000000, 100, 1000000000);
        
    }
    
    // Update is called once per frame
    void Update()
    {
        
#if UNITY_EDITOR && DEBUG
        Profiler.BeginSample("TimerManager.Tick");
#endif
        
        TimerManager.Tick(Time.deltaTime * 1000);
        
#if UNITY_EDITOR && DEBUG   
        Profiler.EndSample();
#endif
        
    }
}
