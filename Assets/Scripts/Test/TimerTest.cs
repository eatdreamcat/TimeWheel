using System.Collections;
using System.Collections.Generic;
using Framework.Timer;
using UnityEngine;

namespace Test.Timer
{
    public class TimerTest
    {
        public static void TestWheelIndexCalculate()
        {
           
            // TimerManager.SetIntervalSync(0, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            //
            // TimerManager.SetIntervalSync(63, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            
            // TimerManager.SetIntervalSync(64, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            //
            // TimerManager.SetIntervalSync(65, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            //
            // TimerManager.SetIntervalSync(66, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            //
            // TimerManager.SetIntervalSync(72, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            //
            // TimerManager.SetIntervalSync(73, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            
            // TimerManager.SetIntervalSync(511, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            
            TimerManager.SetIntervalSync(512, (o1, o2) =>
            {
            
            }, 1, 2);
            //
            // TimerManager.SetIntervalSync(4095, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            //
            TimerManager.SetIntervalSync(4096, (o1, o2) =>
            {
            
            }, 1, 2);
            //
            // TimerManager.SetIntervalSync(134217728, (o1, o2) =>
            // {
            //
            // }, 1, 2);
            //
            // TimerManager.SetIntervalSync(1073741823, (o1, o2) =>
            // {
            //
            // }, 1, 2);
        }
    }
}

