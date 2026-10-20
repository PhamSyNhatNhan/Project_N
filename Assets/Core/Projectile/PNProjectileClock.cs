#if UNITY_5_3_OR_NEWER
using EveEngine;

// EN: A projectile's own clock (a standard EveTimeScale, minScale 0) driven explicitly by the projectile manager, so a time-stop
//     modifier on it freezes movement and lifetime. The base Update would advance it by itself; here it must not.
// VI: Đồng hồ riêng của đạn (EveTimeScale chuẩn, minScale 0) do manager điều khiển; Update mặc định không được tự tiến.
public sealed class PNProjectileClock : EveTimeScale
{
    protected override void Update() { }
}
#endif
