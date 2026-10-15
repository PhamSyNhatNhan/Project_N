using System;
using UnityEngine;

namespace EveEngine
{
    // EN: Rigidbody2D movement on the XY plane, retaining the original EveMove behavior.
    // VI: Di chuyển bằng Rigidbody2D trên mặt phẳng XY, giữ hành vi EveMove ban đầu.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class EveMove2D : EveMove
    {
        protected Rigidbody2D body;
        public virtual Rigidbody2D Body => body;
        protected override bool HasBody => body != null;

        protected override void LoadBody()
        {
            body = GetComponent<Rigidbody2D>();
            if (body == null) throw new InvalidOperationException("EveMove2D requires Rigidbody2D.");
        }

        protected override void UnloadBody()
        {
            if (body != null) body.linearVelocity = Vector2.zero;
            body = null;
        }

        protected override Vector2 PlanarPosition
        {
            get => body.position;
            set => body.position = value;
        }

        protected override Vector2 PlanarVelocity
        {
            get => body.linearVelocity;
            set => body.linearVelocity = value;
        }

        public virtual int MoveToXY(Vector2 target, float speed, float maxDuration) =>
            MoveToPoint(target, speed, maxDuration);

        public virtual int MoveToXYLimitDistance(Vector2 target, float speed, float maxDuration, float distance = 0.5f) =>
            MoveToPointWithStopDistance(target, speed, maxDuration, distance);

        public virtual int TransformToXY(Vector2 target, float delay) => TeleportToPoint(target, delay);

        protected override void Flip()
        {
            base.Flip();
            transform.Rotate(0f, 180f, 0f);
        }
    }
}
