using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ErGameManager : GameManager
{
    public GameMode gameMode = GameMode.ER;

    protected override void Start()
    {
        base.Start();
    }

    protected override void Update()
    {
        base.Update();
    }


    public GameMode GameMode
    {
        get => gameMode;
        set => gameMode = value;
    }
}
