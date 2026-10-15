using System;

namespace EveEngine
{
    // EN: Independent contributions to time rate or reduction of its protection floor.
    // VI: Đóng góp độc lập vào hệ số thời gian hoặc lượng giảm ngưỡng bảo vệ.
    public enum EveTimeModifierKind { Scale, MinScaleReduction }

    // EN: Immutable view of one modifier; null remaining time means explicitly owned lifetime.
    // VI: Bản đọc bất biến của modifier; thời hạn null nghĩa là vòng đời do nguồn sở hữu quản lý.
    public readonly struct EveTimeModifier
    {
        public long Id { get; }
        public EveId Source { get; }
        public EveTimeModifierKind Kind { get; }
        public float Value { get; }
        public double? RemainingTime { get; }

        internal EveTimeModifier(long id, EveId source, EveTimeModifierKind kind, float value, double? remaining)
        { Id = id; Source = source; Kind = kind; Value = value; RemainingTime = remaining; }
    }
}
