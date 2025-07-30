using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

public class UVCCameraManager : MonoBehaviour
{

    AndroidJavaObject plugin;
    AndroidJavaObject activity;
    AndroidJavaClass unityPlayer;


    public int AttachedCameraCount { get; private set; }


    void Start()
    {
        Debug.Log("Start");
    }

}