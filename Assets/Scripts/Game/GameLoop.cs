using System.Collections;
using Framework.Timer;
using UnityEngine;

public class GameLoop : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(AddTask());
    }

    IEnumerator AddTask()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        Test.Timer.TimerTest.TestWheelIndexCalculate();
    }
    
    // Update is called once per frame
    void Update()
    {
        TimerManager.Tick(Time.deltaTime * 1000);
    }
}
