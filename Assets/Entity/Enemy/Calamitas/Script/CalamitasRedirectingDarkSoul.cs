using UnityEngine;

public class CalamitasRedirectingDarkSoul : EasyBulletObject
{
    [SerializeField]private float changeModeTime = 0.5f;
    private float subChangeModeTime = 0.0f;

    protected override void OnEnable()
    {
        base.OnEnable();
        subChangeModeTime = changeModeTime;
        moveMode = BulletMoveMode.Angle;
        speed = 50.0f;
    }
    protected override void Update()
    {
        base.Update();
        subChangeModeTime -= DeltaTime;
        if (subChangeModeTime <= 0.0f)
        {
            speed = 200f;
            EasyModeChange(BulletMoveMode.Target);
            subChangeModeTime = float.MaxValue; 
        }
    }
}
